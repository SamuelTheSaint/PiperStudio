using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PiperStudio
{
    // Telegram bot: "@YourBot some text" in any chat -> a voice message made with Piper.
    public class TelegramBot
    {
        public string Token, Proxy, ApiBase = "https://api.telegram.org";
        public long OwnerId;
        public PiperRunner Runner;
        public Func<Job> GetJob;          // reads the current voice settings (called from the bot thread; must marshal to UI)
        public Func<string> GetFfmpeg;
        public Action<string> Log;
        public Action<long, string> OwnerClaimed;
        public Action<bool, string> StateChanged;
        public Action<string> Spoke;     // text that was turned into a voice message (for the history)
        public string BotUsername = "";

        HttpClient http;
        CancellationTokenSource cts;
        readonly object qLock = new object();
        readonly Dictionary<long, int> latestQuery = new Dictionary<long, int>();
        int queryCounter;
        readonly Dictionary<string, string> voiceCache = new Dictionary<string, string>();
        public bool Running { get { return cts != null && !cts.IsCancellationRequested; } }

        void L(string s) { if (Log != null) Log("[bot] " + s); }

        public void Start()
        {
            if (Running) return;
            if (string.IsNullOrWhiteSpace(Token)) throw new Exception("Paste your bot token first (from @BotFather).");
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(Proxy))
            {
                handler.Proxy = new WebProxy(Proxy.Trim());
                handler.UseProxy = true;
            }
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var th = new Thread(() => Loop(ct)) { IsBackground = true, Name = "TelegramBot" };
            th.Start();
        }

        public void Stop()
        {
            if (cts != null) cts.Cancel();
            try { if (http != null) http.CancelPendingRequests(); } catch { }
        }

        string Url(string method) { return ApiBase.TrimEnd('/') + "/bot" + Token.Trim() + "/" + method; }

        Dictionary<string, object> Call(string method, Dictionary<string, string> form, CancellationToken ct)
        {
            var content = new FormUrlEncodedContent(form ?? new Dictionary<string, string>());
            var resp = http.PostAsync(Url(method), content, ct).Result;
            return Parse(resp, method);
        }

        static Dictionary<string, object> Parse(HttpResponseMessage resp, string method)
        {
            string body = resp.Content.ReadAsStringAsync().Result;
            Dictionary<string, object> root = null;
            try { root = MiniJson.Parse(body) as Dictionary<string, object>; } catch { }
            if (root == null) throw new Exception(method + ": unexpected reply from Telegram (HTTP " + (int)resp.StatusCode + ")");
            object ok; root.TryGetValue("ok", out ok);
            if (!(ok is bool) || !(bool)ok)
            {
                object d; root.TryGetValue("description", out d);
                throw new Exception(method + ": " + (d ?? "error " + (int)resp.StatusCode));
            }
            return root;
        }

        static object G(object o, params string[] path)
        {
            foreach (var k in path)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }
        static long GL(object o, params string[] path) { var v = G(o, path); return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        static string GS(object o, params string[] path) { var v = G(o, path); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }

        static string J(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        void Loop(CancellationToken ct)
        {
            long offset = 0;
            int failures = 0;
            try
            {
                var me = Call("getMe", null, ct);
                BotUsername = GS(me, "result", "username") ?? "";
                L("Connected as @" + BotUsername);
                if (StateChanged != null) StateChanged(true, "Running as @" + BotUsername);
            }
            catch (Exception ex)
            {
                string msg = Root(ex);
                L("Couldn't connect: " + msg);
                if (StateChanged != null) StateChanged(false, "Couldn't connect: " + msg);
                cts.Cancel();
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var res = Call("getUpdates", new Dictionary<string, string> {
                        { "offset", offset.ToString(CultureInfo.InvariantCulture) }, { "timeout", "25" },
                        { "allowed_updates", "[\"message\",\"inline_query\"]" } }, ct);
                    failures = 0;
                    var list = G(res, "result") as List<object>;
                    if (list == null) continue;
                    foreach (var u in list)
                    {
                        offset = Math.Max(offset, GL(u, "update_id") + 1);
                        if (G(u, "inline_query") != null) OnInline(G(u, "inline_query"), ct);
                        else if (G(u, "message") != null) { var m = G(u, "message"); Task.Run(() => OnMessage(m, ct)); }
                    }
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    failures++;
                    string msg = Root(ex);
                    L("Connection problem: " + msg + " — retrying…");
                    if (StateChanged != null) StateChanged(true, "Reconnecting… (" + msg + ")");
                    Thread.Sleep(Math.Min(30000, 2000 * failures));
                }
            }
            if (StateChanged != null) StateChanged(false, "Stopped");
            L("Stopped");
        }

        static string Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            if (ex is TaskCanceledException) return "timed out — is your VPN/proxy on?";
            return ex.Message;
        }

        bool IsOwner(long uid, string name)
        {
            if (OwnerId == 0)
            {
                OwnerId = uid;
                L("Owner set to " + name + " (" + uid + ")");
                if (OwnerClaimed != null) OwnerClaimed(uid, name);
                return true;
            }
            return uid == OwnerId;
        }

        void OnMessage(object m, CancellationToken ct)
        {
            try
            {
                long chat = GL(m, "chat", "id"), uid = GL(m, "from", "id");
                string name = GS(m, "from", "first_name") ?? "";
                string text = GS(m, "text") ?? "";
                if (GS(m, "chat", "type") != "private") return;
                if (!IsOwner(uid, name))
                {
                    Call("sendMessage", new Dictionary<string, string> { { "chat_id", chat.ToString() }, { "text", "This is a private bot." } }, ct);
                    return;
                }
                if (text.StartsWith("/start") || text.StartsWith("/help"))
                {
                    Call("sendMessage", new Dictionary<string, string> { { "chat_id", chat.ToString() }, { "text",
                        "Hi " + name + "! You're the owner of this bot.\n\nIn any chat, type  @" + BotUsername + " your sentence  and tap the voice that appears.\n\nOr send me text here and I'll reply with a voice message you can forward." } }, ct);
                    return;
                }
                if (text.Trim().Length == 0) return;
                Call("sendChatAction", new Dictionary<string, string> { { "chat_id", chat.ToString() }, { "action", "record_voice" } }, ct);
                string ogg = MakeVoice(text, ct);
                try { SendVoice(chat, ogg, ct, false); } finally { try { File.Delete(ogg); } catch { } }
                if (Spoke != null) Spoke(text);
            }
            catch (Exception ex) { if (!ct.IsCancellationRequested) L("Message error: " + Root(ex)); }
        }

        void OnInline(object q, CancellationToken ct)
        {
            string id = GS(q, "id");
            long uid = GL(q, "from", "id");
            string name = GS(q, "from", "first_name") ?? "";
            string text = (GS(q, "query") ?? "").Trim();
            int mine;
            lock (qLock) { mine = ++queryCounter; latestQuery[uid] = mine; }
            Task.Run(() =>
            {
                try
                {
                    if (OwnerId != 0 && uid != OwnerId) { Answer(id, "[]", ct); return; }
                    if (OwnerId == 0)
                    {
                        Call("answerInlineQuery", new Dictionary<string, string> { { "inline_query_id", id }, { "results", "[]" }, { "cache_time", "0" }, { "is_personal", "true" },
                            { "button", "{\"text\":\"First time? Tap here to set up the bot\",\"start_parameter\":\"setup\"}" } }, ct);
                        return;
                    }
                    if (text.Length == 0) { Answer(id, "[]", ct); return; }
                    // wait until the user stops typing for a moment
                    Thread.Sleep(700);
                    lock (qLock) { int latest; if (latestQuery.TryGetValue(uid, out latest) && latest != mine) return; }
                    var job = GetJob();
                    if (job == null) throw new Exception("No voice selected in Piper Studio.");
                    string key = JobKey(job) + "|" + text;
                    string fileId;
                    lock (voiceCache) voiceCache.TryGetValue(key, out fileId);
                    var sw = Stopwatch.StartNew();
                    if (fileId == null)
                    {
                        string ogg = MakeVoice(text, ct, job);
                        lock (qLock) { int latest; if (latestQuery.TryGetValue(uid, out latest) && latest != mine) { try { File.Delete(ogg); } catch { } return; } }
                        try { fileId = SendVoice(OwnerId, ogg, ct, true); } finally { try { File.Delete(ogg); } catch { } }
                        lock (voiceCache) { if (voiceCache.Count > 200) voiceCache.Clear(); voiceCache[key] = fileId; }
                    }
                    string title = "🔊 " + (text.Length > 60 ? text.Substring(0, 60) + "…" : text);
                    string results = "[{\"type\":\"voice\",\"id\":" + J("v" + mine) + ",\"voice_file_id\":" + J(fileId) + ",\"title\":" + J(title) + "}]";
                    Answer(id, results, ct);
                    if (Spoke != null) Spoke(text);
                    L(string.Format("Inline voice ready in {0:0.0}s: «{1}»", sw.Elapsed.TotalSeconds, text.Length > 50 ? text.Substring(0, 50) + "…" : text));
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) return;
                    string msg = Root(ex);
                    if (msg.Contains("query is too old")) L("Telegram gave up waiting for this one — try again (the voice is now cached, so it'll be instant).");
                    else L("Inline error: " + msg);
                }
            });
        }

        void Answer(string queryId, string resultsJson, CancellationToken ct)
        {
            Call("answerInlineQuery", new Dictionary<string, string> {
                { "inline_query_id", queryId }, { "results", resultsJson }, { "cache_time", "0" }, { "is_personal", "true" } }, ct);
        }

        static string JobKey(Job j)
        {
            return string.Join("|", j.Main.FileName, j.MainSpeaker, j.Finglish, j.Mixed, j.Second != null ? j.Second.FileName : "", j.SecondSpeaker,
                j.Params.LengthScale.ToString("0.###", CultureInfo.InvariantCulture), j.Params.NoiseScale.ToString("0.###", CultureInfo.InvariantCulture),
                j.Params.NoiseW.ToString("0.###", CultureInfo.InvariantCulture), j.Gain.ToString("0.##", CultureInfo.InvariantCulture), j.Replacements.Count);
        }

        string MakeVoice(string text, CancellationToken ct, Job job = null)
        {
            job = job ?? GetJob();
            if (job == null) throw new Exception("No voice selected in Piper Studio.");
            string ff = GetFfmpeg();
            if (ff == null) throw new Exception("FFmpeg is needed to make Telegram voice messages. Set ffmpeg.exe on the Export tab.");
            var chunks = TextTools.MakeChunks(text, 0);
            var parts = new List<short[]>();
            for (int i = 0; i < chunks.Count; i++) parts.Add(Synth.Chunk(Runner, chunks[i].Text, job, i < chunks.Count - 1, ct));
            var pcm = Audio.Concat(parts);
            if (pcm.Length == 0) throw new Exception("Nothing to say.");
            string dir = Path.Combine(Path.GetTempPath(), "PiperStudio");
            Directory.CreateDirectory(dir);
            string wav = Path.Combine(dir, "bot_" + Guid.NewGuid().ToString("N") + ".wav");
            string ogg = Path.ChangeExtension(wav, ".ogg");
            Audio.WriteWav(wav, pcm, job.Rate);
            try
            {
                var psi = new ProcessStartInfo(ff, string.Format("-y -hide_banner -loglevel error -i \"{0}\" -ar 48000 -ac 1 -c:a libopus -b:a 48k -application voip \"{1}\"", wav, ogg))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0 || !File.Exists(ogg)) throw new Exception("FFmpeg couldn't make the voice file: " + err);
                }
            }
            finally { try { File.Delete(wav); } catch { } }
            return ogg;
        }

        // Uploads the voice. For inline use it's sent to the owner's chat with the bot, and deleted right away
        // (we only need Telegram's file_id). Returns the file_id.
        string SendVoice(long chatId, string ogg, CancellationToken ct, bool deleteAfter)
        {
            if (chatId == 0) throw new Exception("Send /start to your bot once first, so it knows who you are.");
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new StringContent(chatId.ToString(CultureInfo.InvariantCulture)), "chat_id");
                form.Add(new StringContent(deleteAfter ? "true" : "false"), "disable_notification");
                var file = new ByteArrayContent(File.ReadAllBytes(ogg));
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/ogg");
                form.Add(file, "voice", "voice.ogg");
                var resp = http.PostAsync(Url("sendVoice"), form, ct).Result;
                var root = Parse(resp, "sendVoice");
                string fileId = GS(root, "result", "voice", "file_id");
                long msgId = GL(root, "result", "message_id");
                if (deleteAfter && msgId != 0)
                    try { Call("deleteMessage", new Dictionary<string, string> { { "chat_id", chatId.ToString(CultureInfo.InvariantCulture) }, { "message_id", msgId.ToString(CultureInfo.InvariantCulture) } }, ct); } catch { }
                if (fileId == null) throw new Exception("Telegram didn't return the voice file.");
                return fileId;
            }
        }
    }
}
