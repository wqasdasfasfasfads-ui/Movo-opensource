using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MovoLang
{
    public static class MovoGui
    {
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("user32.dll")] static extern int ToUnicode(uint vk, uint scan, byte[] state, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int cch, uint flags);
        [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        public static bool ConsoleHidden;
        public static Interp It;
        public static Window Root; public static Widget LastWidget; public static EntryW LastEntry;
        public static Font DefFont;
        static bool rootHanded;
        class PressBind { public string Key; public Action Act; }
        static List<PressBind> presses = new List<PressBind>();
        static Dictionary<string, Timer> timers = new Dictionary<string, Timer>(); static int timerN;
        static List<Window> allWindows = new List<Window>();

        public static void Safe(Action a)
        {
            try { a(); }
            catch (MovoExit) { Application.Exit(); }
            catch (MovoError ex) { ShowError("Error" + (ex.Line > 0 ? " (line " + ex.Line + ")" : "") + ": " + ex.Val.Type + (ex.Val.Message.Length > 0 ? ": " + ex.Val.Message : "")); }
            catch (Exception ex) { ShowError("Error: " + ex.Message); }
        }
        static void ShowError(string m)
        {
            if (ConsoleHidden) MessageBox.Show(m, "Movo", MessageBoxButtons.OK, MessageBoxIcon.Error); else Console.Error.WriteLine(m);
        }
        public static void ReportFatal(string msg)
        {
            if (ConsoleHidden) MessageBox.Show(msg, "Movo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else { try { Console.ForegroundColor = ConsoleColor.Red; } catch (Exception) { } Console.Error.WriteLine(msg); try { Console.ResetColor(); } catch (Exception) { } }
        }
        static void HideConsole()
        {
            if (ConsoleHidden || !Program.Pause) return;
            try { FreeConsole(); ConsoleHidden = true; Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); } catch (Exception) { }
        }

        // ---------- helpers ----------
        public static Size Measure(string text, Font f, int wrapW)
        {
            try
            {
                if (text == null || text.Length == 0) text = " ";
                TextFormatFlags fl = TextFormatFlags.NoPadding | (wrapW > 0 ? TextFormatFlags.WordBreak : 0);
                return TextRenderer.MeasureText(text, f, new Size(wrapW > 0 ? wrapW : 100000, 100000), fl);
            }
            catch (Exception) { string[] ls = text.Split('\n'); int mx = 0; foreach (string l in ls) mx = Math.Max(mx, l.Length); return new Size(mx * 7, ls.Length * 15); }
        }
        public static int CharW(Font f) { return Math.Max(6, Measure("0", f, 0).Width); }
        public static ContentAlignment Align(string a)
        {
            switch (a) { case "n": return ContentAlignment.TopCenter; case "ne": return ContentAlignment.TopRight; case "e": return ContentAlignment.MiddleRight; case "se": return ContentAlignment.BottomRight; case "s": return ContentAlignment.BottomCenter; case "sw": return ContentAlignment.BottomLeft; case "w": return ContentAlignment.MiddleLeft; case "nw": return ContentAlignment.TopLeft; }
            return ContentAlignment.MiddleCenter;
        }
        public static Color Color(object v)
        {
            string s = Ops.Str(v).Trim().ToLowerInvariant().Replace(" ", "");
            if (s.StartsWith("#"))
            {
                string h = s.Substring(1);
                if (h.Length == 3) return System.Drawing.Color.FromArgb(Convert.ToInt32(h.Substring(0, 1) + h.Substring(0, 1), 16), Convert.ToInt32(h.Substring(1, 1) + h.Substring(1, 1), 16), Convert.ToInt32(h.Substring(2, 1) + h.Substring(2, 1), 16));
                if (h.Length == 6) return System.Drawing.Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
                throw new MovoError("ValueError", "invalid color: " + s);
            }
            if (s == "grey") s = "gray"; if (s.StartsWith("grey")) s = "gray" + s.Substring(4);
            if (s == "systembuttonface" || s == "") return SystemColors.Control;
            System.Drawing.Color c = System.Drawing.Color.FromName(s);
            if (c.ToArgb() == 0 && s != "transparent") throw new MovoError("ValueError", "unknown color: " + s);
            return c;
        }
        public static Font MakeFont(object v)
        {
            string fam = DefFont.Name; float size = 9; FontStyle st = FontStyle.Regular; GraphicsUnit gu = GraphicsUnit.Point;
            List<object> parts = new List<object>();
            if (v is MTuple) parts.AddRange(((MTuple)v).Items); else if (v is List<object>) parts.AddRange((List<object>)v);
            else { foreach (string p in Ops.Str(v).Split(' ')) { double d; if (double.TryParse(p, System.Globalization.NumberStyles.Float, Ops.IC, out d)) parts.Add((long)d); else parts.Add(p); } }
            List<string> famParts = new List<string>();
            foreach (object p in parts)
            {
                if (p is long) { long n = (long)p; if (n < 0) { gu = GraphicsUnit.Pixel; n = -n; } size = n; }
                else if (p is double) size = (float)(double)p;
                else { string s = Ops.Str(p); string sl = s.ToLowerInvariant(); if (sl == "bold") st |= FontStyle.Bold; else if (sl == "italic") st |= FontStyle.Italic; else if (sl == "underline") st |= FontStyle.Underline; else if (sl == "normal" || sl == "roman") { } else famParts.Add(s); }
            }
            if (famParts.Count > 0) fam = string.Join(" ", famParts.ToArray());
            try { return new Font(fam, size, st, gu); } catch (Exception) { return new Font(DefFont.Name, size, st, gu); }
        }
        public static int EntryIdx(string text, object o, int ins)
        {
            string s = Ops.Str(o); if (s == "end") return text.Length; if (s == "insert") return ins;
            long n; if (long.TryParse(s, out n)) return (int)Math.Max(0, Math.Min(n, text.Length));
            throw new MovoError("ValueError", "invalid index: " + s);
        }

        static string KeySym(Keys k, bool shift, bool caps)
        {
            if (k >= Keys.A && k <= Keys.Z) { string l = k.ToString().ToLowerInvariant(); return (shift ^ caps) ? l.ToUpperInvariant() : l; }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)k - (int)Keys.D0).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "KP_" + ((int)k - (int)Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F12) return k.ToString();
            switch (k)
            {
                case Keys.Return: return "Return"; case Keys.Escape: return "Escape"; case Keys.Space: return "space"; case Keys.Back: return "BackSpace"; case Keys.Tab: return "Tab";
                case Keys.Left: return "Left"; case Keys.Right: return "Right"; case Keys.Up: return "Up"; case Keys.Down: return "Down"; case Keys.Delete: return "Delete";
                case Keys.Home: return "Home"; case Keys.End: return "End"; case Keys.PageUp: return "Prior"; case Keys.PageDown: return "Next"; case Keys.Insert: return "Insert";
                case Keys.OemMinus: return "minus"; case Keys.Oemplus: return "equal"; case Keys.Oemcomma: return "comma"; case Keys.OemPeriod: return "period";
                case Keys.ShiftKey: return "Shift_L"; case Keys.ControlKey: return "Control_L"; case Keys.Menu: return "Alt_L";
            }
            return k.ToString();
        }
        static string KeyChar(KeyEventArgs e, bool shift, bool caps)
        {
            try
            {
                byte[] st = new byte[256]; if (GetKeyboardState(st))
                {
                    StringBuilder sb = new StringBuilder(8); int r = ToUnicode((uint)e.KeyCode, MapVirtualKey((uint)e.KeyCode, 0), st, sb, 8, 4);
                    if (r >= 1) return sb.ToString(0, 1);
                    return "";
                }
            }
            catch (Exception) { }
            Keys k = e.KeyCode;
            if (k >= Keys.A && k <= Keys.Z) { string l = k.ToString().ToLowerInvariant(); return (shift ^ caps) ? l.ToUpperInvariant() : l; }
            if (k >= Keys.D0 && k <= Keys.D9) { if (shift) return ")!@#$%^&*(".Substring((int)k - (int)Keys.D0, 1); return ((int)k - (int)Keys.D0).ToString(); }
            if (k == Keys.Space) return " "; if (k == Keys.Return) return "\r"; if (k == Keys.Tab) return "\t"; if (k == Keys.Back) return "\b"; if (k == Keys.Escape) return "\x1b";
            return "";
        }
        public static MEvent KeyEvent(KeyEventArgs e, object w)
        {
            MEvent ev = new MEvent(); bool shift = e.Shift, caps = Control.IsKeyLocked(Keys.CapsLock);
            ev.KeySym = KeySym(e.KeyCode, shift, caps); ev.KeyCode = (int)e.KeyCode; ev.Char = KeyChar(e, shift, caps); ev.Widget = w;
            ev.State = (e.Control ? 4 : 0) | (e.Shift ? 1 : 0) | (e.Alt ? 8 : 0); return ev;
        }
        public static MEvent MouseEvent(MouseEventArgs e, object w) { MEvent ev = new MEvent(); ev.X = e.X; ev.Y = e.Y; ev.Num = e.Button == MouseButtons.Left ? 1 : e.Button == MouseButtons.Middle ? 2 : 3; ev.Widget = w; return ev; }

        // ---------- press ----------
        public static void Press(object key, Action a) { PressBind p = new PressBind(); p.Key = Ops.Str(key); p.Act = a; presses.Add(p); }
        public static void PressKey(MEvent ev, Window w)
        {
            foreach (PressBind p in new List<PressBind>(presses))
            {
                string k = p.Key; bool ok;
                if (k.Length == 1)
                {
                    if ((ev.State & 4) != 0) continue;
                    Control ac = w.Form.ActiveControl; if (ac is TextBoxBase || ac is ComboBox) continue;
                    ok = ev.Char.Length == 1 && string.Equals(ev.Char, k, StringComparison.OrdinalIgnoreCase);
                    if (!ok && char.IsLetterOrDigit(k[0]) && k[0] < 128) ok = ev.KeyCode == char.ToUpperInvariant(k[0]);
                }
                else { Binding b = Binding.Parse("<" + k + ">"); ok = b.KeyMatches(ev); }
                if (ok) Safe(p.Act);
            }
        }

        // ---------- timers ----------
        public static object After(List<object> a)
        {
            int ms = (int)Ops.ToD(a[0]);
            if (a.Count < 2) { System.Threading.Thread.Sleep(ms); return null; }
            object fn = a[1]; List<object> extra = a.GetRange(2, a.Count - 2);
            Timer t = new Timer(); t.Interval = Math.Max(1, ms); string id = "after#" + (++timerN); timers[id] = t;
            t.Tick += delegate(object s, EventArgs e) { t.Stop(); t.Dispose(); timers.Remove(id); Safe(delegate() { It.Call(fn, extra, null); }); };
            t.Start(); return id;
        }
        public static void AfterCancel(object id) { Timer t; string s = Ops.Str(id); if (timers.TryGetValue(s, out t)) { t.Stop(); t.Dispose(); timers.Remove(s); } }

        public static void MainLoop(Window w)
        {
            foreach (Window o in allWindows) if (o != w && !o.Withdrawn && !o.Destroyed) o.Form.Show();
            w.DoLayout(); Application.Run(w.Form);
            foreach (Timer t in new List<Timer>(timers.Values)) { t.Stop(); t.Dispose(); } timers.Clear();
        }

        // ---------- so.move ----------
        static void Move(object where, object widget, object step)
        {
            Widget w = widget as Widget ?? LastWidget; if (w == null || w.Parent == null) return;
            int st = step == null ? 10 : (int)Ops.ToD(step); Window top = w.TopWindow(); if (top != null) top.DoLayout();
            Container p = w.Parent; Rectangle inner = p.Inner(); Rectangle b = w.C.Bounds;
            if (w.Mgr != "place")
            {
                Window pw = p as Window; if (pw != null) pw.AutoSize = false; else { p.Propagate = false; p.Opts["width"] = (long)p.C.Width; p.Opts["height"] = (long)p.C.Height; }
                p.PackOrder.Remove(w); w.Mgr = "place"; w.Place.Clear();
                w.Place["x"] = (long)(b.X - inner.X); w.Place["y"] = (long)(b.Y - inner.Y); w.Place["width"] = (long)b.Width; w.Place["height"] = (long)b.Height; w.Place["anchor"] = "nw";
            }
            int x = (int)Ops.ToD(w.Place["x"]), y = (int)Ops.ToD(w.Place["y"]); string d = Ops.Str(where).ToLowerInvariant();
            if (d == "left") x -= st; else if (d == "right") x += st; else if (d == "up") y -= st; else if (d == "down") y += st;
            x = Math.Max(0, Math.Min(x, inner.Width - b.Width)); y = Math.Max(0, Math.Min(y, inner.Height - b.Height));
            w.Place["x"] = (long)x; w.Place["y"] = (long)y; w.Dirty(); if (top != null) top.DoLayout();
        }

        // ---------- gui module ----------
        static Container ParentOf(List<object> a, Dictionary<string, object> kw)
        {
            object p = a.Count > 0 ? a[0] : (kw.ContainsKey("master") ? kw["master"] : null); kw.Remove("master");
            Container c = p as Container; if (c != null) return c; return Root;
        }
        static Window NewWindow() { Window w = new Window(); allWindows.Add(w); return w; }

        public static void Build(Interp it, MModule g, Dictionary<string, object> extras)
        {
            It = it; HideConsole();
            try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); } catch (Exception) { }
            DefFont = new Font("Segoe UI", 9f);
            Root = NewWindow(); extras["root"] = Root;
            extras["get"] = new NativeFn("get", delegate(List<object> a) { return LastEntry == null ? "" : LastEntry.T.Text; });
            extras["press"] = new NativeFn("press", delegate(List<object> a) { Press(a[0], delegate() { It.Call0(a[1]); }); return null; });
            MModule so = new MModule("so"); so.Fn("move", delegate(List<object> a) { Move(a[0], a.Count > 1 ? a[1] : null, a.Count > 2 ? a[2] : null); return null; }); extras["so"] = so;

            g.Fn("Tk", delegate(List<object> a) { if (!rootHanded && !Root.Touched) { rootHanded = true; return Root; } rootHanded = true; return NewWindow(); });
            g.Fn("Toplevel", delegate(List<object> a) { Window w = NewWindow(); w.Touched = true; return w; });
            g.Fn("Frame", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); FrameW f = new FrameW(p, kw); LastWidget = f; return f; });
            g.Fn("LabelFrame", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); LabelFrameW f = new LabelFrameW(p, kw); LastWidget = f; return f; });
            Func<List<object>, Dictionary<string, object>, object> label = delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); LabelW l = new LabelW(p, kw); LastWidget = l; return l; };
            g.Fn("Label", label); g.Fn("Message", label);
            g.Fn("Button", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); ButtonW b = new ButtonW(p, kw); LastWidget = b; return b; });
            Func<List<object>, Dictionary<string, object>, object> entry = delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new EntryW(p, kw); };
            g.Fn("Entry", entry); g.Fn("Spinbox", entry);
            g.Fn("Text", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new TextW(p, kw); });
            g.Fn("Canvas", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new CanvasW(p, kw); });
            g.Fn("Listbox", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new ListboxW(p, kw); });
            g.Fn("Checkbutton", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new CheckW(p, kw, false); });
            g.Fn("Radiobutton", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new CheckW(p, kw, true); });
            g.Fn("Scale", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new ScaleW(p, kw); });
            g.Fn("Combobox", delegate(List<object> a, Dictionary<string, object> kw) { Container p = ParentOf(a, kw); return new ComboW(p, kw); });
            foreach (string vk in new string[] { "StringVar", "IntVar", "DoubleVar", "BooleanVar" })
            {
                string kind = vk; g.Fn(vk, delegate(List<object> a, Dictionary<string, object> kw)
                {
                    object init = kind == "StringVar" ? "" : kind == "IntVar" ? (object)0L : kind == "DoubleVar" ? (object)0.0 : (object)false;
                    object v = kw.ContainsKey("value") ? kw["value"] : (a.Count > 1 ? a[1] : (a.Count == 1 && !(a[0] is Widget) ? a[0] : init));
                    MVar mv = new MVar(kind, init); mv.Set(v); return mv;
                });
            }
            foreach (string c in new string[] { "END", "LEFT", "RIGHT", "TOP", "BOTTOM", "X", "Y", "BOTH", "N", "S", "E", "W", "NE", "NW", "SE", "SW", "CENTER", "NORMAL", "DISABLED", "ACTIVE", "HORIZONTAL", "VERTICAL", "WORD", "CHAR", "NONE", "INSERT", "RAISED", "SUNKEN", "FLAT", "GROOVE", "RIDGE", "SOLID", "SINGLE", "MULTIPLE", "EXTENDED", "BROWSE", "NSEW", "NS", "EW" })
                g.Val(c, c == "INSERT" ? "insert" : c == "END" ? "end" : c.ToLowerInvariant());
            g.Val("YES", 1L).Val("NO", 0L).Val("TRUE", true).Val("FALSE", false).Val("N", "n").Val("S", "s").Val("E", "e").Val("W", "w");

            MModule mb = new MModule("messagebox");
            Func<string, MessageBoxIcon, MessageBoxButtons, Func<DialogResult, object>, NativeFn> mk = delegate(string name, MessageBoxIcon icon, MessageBoxButtons btn, Func<DialogResult, object> conv)
            {
                return new NativeFn(name, delegate(List<object> a, Dictionary<string, object> kw)
                {
                    string title = a.Count > 0 ? Ops.Str(a[0]) : (kw.ContainsKey("title") ? Ops.Str(kw["title"]) : ""); string msg = a.Count > 1 ? Ops.Str(a[1]) : (kw.ContainsKey("message") ? Ops.Str(kw["message"]) : "");
                    return conv(MessageBox.Show(msg, title, btn, icon));
                });
            };
            Func<DialogResult, object> none = delegate(DialogResult r) { return "ok"; };
            mb.Members["showinfo"] = mk("showinfo", MessageBoxIcon.Information, MessageBoxButtons.OK, none);
            mb.Members["showwarning"] = mk("showwarning", MessageBoxIcon.Warning, MessageBoxButtons.OK, none);
            mb.Members["showerror"] = mk("showerror", MessageBoxIcon.Error, MessageBoxButtons.OK, none);
            mb.Members["askyesno"] = mk("askyesno", MessageBoxIcon.Question, MessageBoxButtons.YesNo, delegate(DialogResult r) { return r == DialogResult.Yes; });
            mb.Members["askquestion"] = mk("askquestion", MessageBoxIcon.Question, MessageBoxButtons.YesNo, delegate(DialogResult r) { return r == DialogResult.Yes ? "yes" : "no"; });
            mb.Members["askokcancel"] = mk("askokcancel", MessageBoxIcon.Question, MessageBoxButtons.OKCancel, delegate(DialogResult r) { return r == DialogResult.OK; });
            mb.Members["askretrycancel"] = mk("askretrycancel", MessageBoxIcon.Warning, MessageBoxButtons.RetryCancel, delegate(DialogResult r) { return r == DialogResult.Retry; });
            g.Val("messagebox", mb);

            MModule fd = new MModule("filedialog");
            fd.Fn("askopenfilename", delegate(List<object> a, Dictionary<string, object> kw) { OpenFileDialog d = new OpenFileDialog(); return d.ShowDialog() == DialogResult.OK ? d.FileName : ""; });
            fd.Fn("asksaveasfilename", delegate(List<object> a, Dictionary<string, object> kw) { SaveFileDialog d = new SaveFileDialog(); return d.ShowDialog() == DialogResult.OK ? d.FileName : ""; });
            g.Val("filedialog", fd);
            MModule sd = new MModule("simpledialog");
            sd.Fn("askstring", delegate(List<object> a, Dictionary<string, object> kw) { return AskString(a.Count > 0 ? Ops.Str(a[0]) : "", a.Count > 1 ? Ops.Str(a[1]) : ""); });
            g.Val("simpledialog", sd);

            MModule ttk = new MModule("ttk");
            foreach (string n in new string[] { "Frame", "Label", "Button", "Entry", "Checkbutton", "Radiobutton", "Scale", "Combobox", "LabelFrame" }) ttk.Members[n] = g.Members[n];
            g.Val("ttk", ttk);
        }

        static object AskString(string title, string prompt)
        {
            Form f = new Form(); f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new Size(340, 110); f.MaximizeBox = false; f.MinimizeBox = false;
            Label l = new Label(); l.Text = prompt; l.SetBounds(10, 10, 320, 20); TextBox t = new TextBox(); t.SetBounds(10, 36, 320, 22);
            Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(170, 72, 75, 26); Button cn = new Button(); cn.Text = "Cancel"; cn.DialogResult = DialogResult.Cancel; cn.SetBounds(255, 72, 75, 26);
            f.Controls.AddRange(new Control[] { l, t, ok, cn }); f.AcceptButton = ok; f.CancelButton = cn;
            return f.ShowDialog() == DialogResult.OK ? (object)t.Text : null;
        }
    }
}
