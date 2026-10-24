using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PiperStudio
{
    public static partial class Lang
    {
        public static bool Fa;

        public static string T(string en)
        {
            if (!Fa || string.IsNullOrEmpty(en)) return en;
            string r;
            return FaDict.TryGetValue(en, out r) ? r : en;
        }

        public static void Apply(Control root)
        {
            if (!Fa) return;
            Walk(root);
        }

        static void Walk(Control c)
        {
            if (!(c is TextBox) && !(c is ComboBox) && !(c is NumericUpDown)) c.Text = T(c.Text);
            var ms = c as ToolStrip;
            if (ms != null) foreach (ToolStripItem it in ms.Items) WalkItem(it);
            var cb = c as ComboBox;
            if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList)
                for (int i = 0; i < cb.Items.Count; i++) if (cb.Items[i] is string) cb.Items[i] = T((string)cb.Items[i]);
            foreach (Control ch in c.Controls) Walk(ch);
        }

        static void WalkItem(ToolStripItem it)
        {
            it.Text = T(it.Text);
            var dd = it as ToolStripDropDownItem;
            if (dd != null) foreach (ToolStripItem sub in dd.DropDownItems) WalkItem(sub);
        }

        // developer helper: list every visible string
        public static void Dump(Control root, string path)
        {
            var set = new List<string>();
            Action<Control> walk = null;
            Action<ToolStripItem> walkItem = null;
            walkItem = it => { if (!set.Contains(it.Text)) set.Add(it.Text); var dd = it as ToolStripDropDownItem; if (dd != null) foreach (ToolStripItem s in dd.DropDownItems) walkItem(s); };
            walk = c =>
            {
                if (!(c is TextBox) && !(c is NumericUpDown) && !string.IsNullOrEmpty(c.Text) && !set.Contains(c.Text)) set.Add(c.Text);
                var ms = c as ToolStrip; if (ms != null) foreach (ToolStripItem it in ms.Items) walkItem(it);
                var cb = c as ComboBox; if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList) foreach (var o in cb.Items) if (o is string && !set.Contains((string)o)) set.Add((string)o);
                foreach (Control ch in c.Controls) walk(ch);
            };
            walk(root);
            var sb = new StringBuilder();
            foreach (var s in set) sb.Append(s.Replace("\\", "\\\\").Replace("\n", "\\n")).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
