using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PiperStudio
{
    public partial class MainForm
    {
        // ================================================================== voice presets
        // A preset is a saved set of voice + sound settings ("Calm", "Fast chat" …) you can switch with one click
        // or Ctrl+Shift+1 … 9. Built-in presets only change the sound (speed, pauses …); your own also keep the voice.
        public class VoicePreset
        {
            public string Name;
            public bool BuiltIn;
            public Dictionary<string, string> Values = new Dictionary<string, string>();
            public override string ToString() { return (BuiltIn ? "" : "★ ") + Name; }
        }

        static readonly string[] PresetKeys = { "voice", "speaker", "voice2", "speaker2", "mixed", "finglish", "speed", "noise", "noisew", "volume", "sentence", "paragraph" };
        readonly List<VoicePreset> presets = new List<VoicePreset>();
        ComboBox cboPreset;
        bool applyingPreset;
        string PresetsPath { get { return AppPaths.In("presets.txt"); } }

        static VoicePreset Built(string name, int speed, int noise, int noisew, string sentence, string paragraph)
        {
            var p = new VoicePreset { Name = name, BuiltIn = true };
            p.Values["speed"] = speed.ToString(); p.Values["noise"] = noise.ToString(); p.Values["noisew"] = noisew.ToString();
            p.Values["sentence"] = sentence; p.Values["paragraph"] = paragraph;
            return p;
        }

        List<VoicePreset> BuiltInPresets()
        {
            return new List<VoicePreset>
            {
                Built(Lang.T("Normal"), 100, 67, 80, "0.2", "0.5"),
                Built(Lang.T("Calm"), 85, 50, 60, "0.45", "0.9"),
                Built(Lang.T("Fast chat"), 125, 70, 80, "0.08", "0.25"),
                Built(Lang.T("Clear and slow"), 75, 40, 50, "0.6", "1"),
                Built(Lang.T("Lively"), 105, 95, 95, "0.2", "0.5"),
            };
        }

        Control PresetRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            cboPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(3, 3, 3, 3) };
            cboPreset.SelectedIndexChanged += (s, e) => { if (!applyingPreset && cboPreset.SelectedItem is VoicePreset) ApplyPreset((VoicePreset)cboPreset.SelectedItem); };
            row.Controls.Add(cboPreset);
            row.Controls.Add(SmallButton("Save as…", (s, e) => SavePresetAs()));
            row.Controls.Add(SmallButton("Delete", (s, e) => DeletePreset()));
            return row;
        }

        void LoadPresets()
        {
            presets.Clear();
            presets.AddRange(BuiltInPresets());
            try
            {
                if (File.Exists(PresetsPath))
                {
                    VoicePreset cur = null;
                    foreach (var raw in File.ReadAllLines(PresetsPath, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("## ")) { cur = new VoicePreset { Name = line.Substring(3).Trim() }; if (cur.Name.Length > 0) presets.Add(cur); else cur = null; continue; }
                        int eq = line.IndexOf('=');
                        if (cur != null && eq > 0) cur.Values[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }
            }
            catch { }
            FillPresetBox(Ini("preset", ""));
        }

        void SavePresets()
        {
            try
            {
                var sb = new StringBuilder("# Piper Studio voice presets\r\n");
                foreach (var p in presets.Where(x => !x.BuiltIn))
                {
                    sb.Append("## ").Append(p.Name).Append("\r\n");
                    foreach (var kv in p.Values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                }
                File.WriteAllText(PresetsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        void FillPresetBox(string select)
        {
            if (cboPreset == null) return;
            applyingPreset = true;
            cboPreset.BeginUpdate(); cboPreset.Items.Clear();
            foreach (var p in presets) cboPreset.Items.Add(p);
            cboPreset.EndUpdate();
            int i = presets.FindIndex(p => p.Name == select);
            cboPreset.SelectedIndex = i >= 0 ? i : 0;
            applyingPreset = false;
        }

        Dictionary<string, string> CurrentVoiceSettings()
        {
            var d = new Dictionary<string, string>();
            var v1 = SelectedVoice(cboVoice); var v2 = SelectedVoice(cboVoice2);
            d["voice"] = v1 != null ? v1.FileName : "";
            d["speaker"] = (cboSpeaker.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker.SelectedItem).Id : 0).ToString();
            d["voice2"] = v2 != null ? v2.FileName : "";
            d["speaker2"] = (cboSpeaker2.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker2.SelectedItem).Id : 0).ToString();
            d["mixed"] = chkMixed.Checked ? "1" : "0"; d["finglish"] = chkFinglish.Checked ? "1" : "0";
            d["speed"] = trkSpeed.Value.ToString(); d["noise"] = trkNoise.Value.ToString(); d["noisew"] = trkNoiseW.Value.ToString(); d["volume"] = trkVolume.Value.ToString();
            d["sentence"] = numSentence.Value.ToString(CultureInfo.InvariantCulture); d["paragraph"] = numParagraph.Value.ToString(CultureInfo.InvariantCulture);
            return d;
        }

        void ApplyPreset(VoicePreset p)
        {
            var v = p.Values; string x; int n; decimal dm;
            Func<string, int, int, int?> I = (k, lo, hi) => v.TryGetValue(k, out x) && int.TryParse(x, out n) ? (int?)Math.Max(lo, Math.Min(hi, n)) : null;
            Func<string, decimal?> D = k => v.TryGetValue(k, out x) && decimal.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out dm) ? (decimal?)dm : null;
            if (v.TryGetValue("voice", out x) && x != "") { SelectVoice(cboVoice, x, null); var sp = I("speaker", 0, 100000); if (sp.HasValue) FillSpeakers(cboVoice, cboSpeaker, sp.Value); }
            if (v.TryGetValue("voice2", out x) && x != "") { SelectVoice(cboVoice2, x, "en"); var sp = I("speaker2", 0, 100000); if (sp.HasValue) FillSpeakers(cboVoice2, cboSpeaker2, sp.Value); }
            if (v.TryGetValue("mixed", out x)) chkMixed.Checked = x == "1";
            if (v.TryGetValue("finglish", out x)) chkFinglish.Checked = x == "1";
            var s1 = I("speed", trkSpeed.Minimum, trkSpeed.Maximum); if (s1.HasValue) trkSpeed.Value = s1.Value;
            var s2 = I("noise", trkNoise.Minimum, trkNoise.Maximum); if (s2.HasValue) trkNoise.Value = s2.Value;
            var s3 = I("noisew", trkNoiseW.Minimum, trkNoiseW.Maximum); if (s3.HasValue) trkNoiseW.Value = s3.Value;
            var s4 = I("volume", trkVolume.Minimum, trkVolume.Maximum); if (s4.HasValue) trkVolume.Value = s4.Value;
            var d1 = D("sentence"); if (d1.HasValue) numSentence.Value = Math.Max(numSentence.Minimum, Math.Min(numSentence.Maximum, d1.Value));
            var d2 = D("paragraph"); if (d2.HasValue) numParagraph.Value = Math.Max(numParagraph.Minimum, Math.Min(numParagraph.Maximum, d2.Value));
            ini["preset"] = p.Name;
            SetStatus(string.Format(Lang.T("Preset: {0}"), p.Name));
            WarmUp();
        }

        void SavePresetAs()
        {
            string name = Dialogs.Prompt(this, Lang.T("Save preset"), Lang.T("Name for this voice and these sound settings (for example \"Calm\" or \"Fast chat\"):"), "", false);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            var existing = presets.FirstOrDefault(p => !p.BuiltIn && p.Name == name);
            if (existing == null) { existing = new VoicePreset { Name = name }; presets.Add(existing); }
            existing.Values = CurrentVoiceSettings();
            SavePresets();
            ini["preset"] = name;
            FillPresetBox(name);
            SetStatus(string.Format(Lang.T("Saved preset \"{0}\". Switch with the list on the Voice tab or Ctrl+Shift+1 … 9."), name));
        }

        void DeletePreset()
        {
            var p = cboPreset.SelectedItem as VoicePreset;
            if (p == null) return;
            if (p.BuiltIn) { SetStatus(Lang.T("Built-in presets can't be deleted.")); return; }
            if (MessageBox.Show(this, string.Format(Lang.T("Delete the preset \"{0}\"?"), p.Name), "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            presets.Remove(p);
            SavePresets();
            FillPresetBox("");
        }

        void ApplyPresetNumber(int i)
        {
            if (i < 0 || i >= presets.Count) return;
            applyingPreset = true; cboPreset.SelectedIndex = i; applyingPreset = false;
            ApplyPreset(presets[i]);
        }
    }
}
