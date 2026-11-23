using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MovoLang
{
    public class Window : Container
    {
        public Form Form; public bool AutoSize = true, Withdrawn, pending, laying; public Size LastAuto; public object CloseFn; public bool PosSet; public Point Pos;
        public Window()
        {
            Form = new Form(); C = Form; Form.AutoScaleMode = AutoScaleMode.None; Form.Text = "tk"; Form.KeyPreview = true; Form.Font = MovoGui.DefFont;
            Form.StartPosition = FormStartPosition.CenterScreen; Form.ClientSize = new Size(200, 200);
            Form.Resize += delegate(object s, EventArgs e) { if (laying) return; if (!(AutoSize && Form.ClientSize == LastAuto)) AutoSize = false; LayoutChildren(); };
            Form.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (CloseFn != null && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; MovoGui.Safe(delegate() { MovoGui.It.Call0(CloseFn); }); }
            };
            Form.KeyDown += delegate(object s, KeyEventArgs ke) { MEvent ev = MovoGui.KeyEvent(ke, this); MovoGui.PressKey(ev, this); };
            Form.Shown += delegate(object s, EventArgs e) { DoLayout(); };
        }
        public override Rectangle Inner() { return Form.ClientRectangle; }
        public override Size Req() { return ContentReq(); }
        public void ScheduleLayout()
        {
            if (pending || !Form.IsHandleCreated) return; pending = true;
            Form.BeginInvoke(new Action(delegate() { pending = false; if (!Destroyed) DoLayout(); }));
        }
        public void DoLayout()
        {
            if (laying || Form.IsDisposed) return; laying = true;
            try
            {
                if (AutoSize) { Size rq = PackOrder.Count > 0 || HasMgr() ? ContentReq() : new Size(Math.Max(OptI("width", 200), 1), Math.Max(OptI("height", 200), 1)); if (rq.Width < 1) rq.Width = 1; if (rq.Height < 1) rq.Height = 1; LastAuto = rq; if (Form.ClientSize != rq) Form.ClientSize = rq; }
                LayoutChildren();
            }
            finally { laying = false; }
        }
        bool HasMgr() { foreach (Widget w in Slaves) if (w.Mgr == "pack" || w.Mgr == "grid") return true; return false; }
        public override void ApplyOpt(string k, object v)
        {
            if (k == "width" || k == "height") { return; }
            base.ApplyOpt(k, v);
        }
        public override void Destroy() { Destroyed = true; try { Form.Close(); } catch (Exception) { } if (MovoGui.Root == this) { } }
        public override object Method(string n)
        {
            switch (n)
            {
                case "title": case "wm_title": return F(n, delegate(List<object> a) { if (a.Count > 0) Form.Text = Ops.Str(a[0]); return Form.Text; });
                case "geometry": return F(n, delegate(List<object> a)
                {
                    if (a.Count == 0) return Form.ClientSize.Width + "x" + Form.ClientSize.Height + "+" + Form.Left + "+" + Form.Top;
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Ops.Str(a[0]), @"^(?:(\d+)x(\d+))?(?:([+-]\d+)([+-]\d+))?$");
                    if (!m.Success) throw new MovoError("ValueError", "invalid geometry: " + Ops.Str(a[0]));
                    if (m.Groups[1].Success) { AutoSize = false; laying = true; Form.ClientSize = new Size(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)); laying = false; }
                    if (m.Groups[3].Success) { Form.StartPosition = FormStartPosition.Manual; Form.Location = new Point(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)); }
                    LayoutChildren(); return null;
                });
                case "resizable": return F(n, delegate(List<object> a, Dictionary<string, object> kw)
                {
                    bool w = a.Count > 0 ? IsTrue(a[0]) : (!kw.ContainsKey("width") || IsTrue(kw["width"])), h = a.Count > 1 ? IsTrue(a[1]) : (!kw.ContainsKey("height") || IsTrue(kw["height"]));
                    if (!w && !h) { Form.FormBorderStyle = FormBorderStyle.FixedSingle; Form.MaximizeBox = false; } else { Form.FormBorderStyle = FormBorderStyle.Sizable; Form.MaximizeBox = true; }
                    return null;
                });
                case "minsize": return F(n, delegate(List<object> a) { Form.MinimumSize = new Size((int)Ops.ToL(a[0]), (int)Ops.ToL(a[1])); return null; });
                case "maxsize": return F(n, delegate(List<object> a) { Form.MaximumSize = new Size((int)Ops.ToL(a[0]), (int)Ops.ToL(a[1])); return null; });
                case "protocol": return F(n, delegate(List<object> a) { if (Ops.Str(a[0]) == "WM_DELETE_WINDOW") CloseFn = a[1]; return null; });
                case "mainloop": return F(n, delegate(List<object> a) { MovoGui.MainLoop(this); return null; });
                case "quit": return F(n, delegate(List<object> a) { Application.Exit(); return null; });
                case "withdraw": return F(n, delegate(List<object> a) { Withdrawn = true; Form.Hide(); return null; });
                case "deiconify": return F(n, delegate(List<object> a) { Withdrawn = false; Form.Show(); return null; });
                case "iconify": return F(n, delegate(List<object> a) { Form.WindowState = FormWindowState.Minimized; return null; });
                case "state": return F(n, delegate(List<object> a) { if (a.Count > 0) { string s = Ops.Str(a[0]); if (s == "zoomed") Form.WindowState = FormWindowState.Maximized; else if (s == "iconic") Form.WindowState = FormWindowState.Minimized; else Form.WindowState = FormWindowState.Normal; } return "normal"; });
                case "attributes": return F(n, delegate(List<object> a) { if (a.Count >= 2 && Ops.Str(a[0]) == "-topmost") Form.TopMost = IsTrue(a[1]); if (a.Count >= 2 && Ops.Str(a[0]) == "-fullscreen") { if (IsTrue(a[1])) { Form.FormBorderStyle = FormBorderStyle.None; Form.WindowState = FormWindowState.Maximized; } else { Form.FormBorderStyle = FormBorderStyle.Sizable; Form.WindowState = FormWindowState.Normal; } } return null; });
                case "overrideredirect": return F(n, delegate(List<object> a) { if (a.Count > 0 && IsTrue(a[0])) Form.FormBorderStyle = FormBorderStyle.None; return null; });
                case "iconbitmap": case "iconphoto": case "grab_set": case "transient": case "option_add": case "wait_window": case "focus_force": return F(n, delegate(List<object> a) { return null; });
                case "bind_all": return F(n, delegate(List<object> a) { AddBind(Ops.Str(a[0]), a[1]); return null; });
            }
            return base.Method(n);
        }
        public override void HookEvents(string kind)
        {
            if (Hooked.Contains(kind) && kind == "Key") return;
            if (kind == "Key") { Hooked.Add(kind); Form.KeyDown += delegate(object s, KeyEventArgs ke) { MEvent ev = MovoGui.KeyEvent(ke, this); Dispatch("Key", ev, delegate(Binding b) { return b.KeyMatches(ev); }); }; return; }
            base.HookEvents(kind);
        }
    }

    public class FrameW : Container
    {
        public Panel P;
        public FrameW(Container parent, Dictionary<string, object> kw) { P = new Panel(); C = P; parent.Add(this); Configure(kw); }
        public override Rectangle Inner() { return P.ClientRectangle; }
        public override void ApplyOpt(string k, object v)
        {
            if (k == "bd" || k == "borderwidth" || k == "relief") { string rel = Opts.ContainsKey("relief") ? Ops.Str(Opts["relief"]) : "flat"; P.BorderStyle = rel == "flat" ? BorderStyle.None : (rel == "solid" ? BorderStyle.FixedSingle : BorderStyle.Fixed3D); return; }
            if (k == "width" || k == "height") return;
            base.ApplyOpt(k, v);
        }
        public override Size BorderExtra() { int b = P.BorderStyle == BorderStyle.None ? 0 : 4; return new Size(b, b); }
    }

    public class LabelFrameW : Container
    {
        public GroupBox G;
        public LabelFrameW(Container parent, Dictionary<string, object> kw) { G = new GroupBox(); C = G; parent.Add(this); Configure(kw); }
        public override Rectangle Inner() { return G.DisplayRectangle; }
        public override Size BorderExtra() { Size s = G.Size - G.DisplayRectangle.Size; return G.Width > 0 ? s : new Size(10, 22); }
        public override void ApplyOpt(string k, object v) { if (k == "text") { G.Text = Ops.Str(v); return; } if (k == "width" || k == "height") return; base.ApplyOpt(k, v); }
    }

    public class LabelW : Widget
    {
        public Label L;
        public LabelW(Container parent, Dictionary<string, object> kw) { L = new Label(); C = L; L.AutoSize = false; L.UseMnemonic = false; L.TextAlign = ContentAlignment.MiddleCenter; parent.Add(this); Configure(kw); }
        public override Size Req()
        {
            int wl = OptI("wraplength", 0); Size t = MovoGui.Measure(L.Text, L.Font, wl);
            int w = Opts.ContainsKey("width") && OptI("width", 0) > 0 ? OptI("width", 0) * MovoGui.CharW(L.Font) + 4 : t.Width + 2 * OptI("padx", 1) + 2;
            int h = Opts.ContainsKey("height") && OptI("height", 0) > 0 ? OptI("height", 0) * t.Height / Math.Max(1, L.Text.Split('\n').Length) + 4 : t.Height + 2 * OptI("pady", 1) + 2;
            return new Size(w, h);
        }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "text": L.Text = Ops.Str(v); break;
                case "textvariable": TextVar = v as MVar; if (TextVar != null) { L.Text = Ops.Str(TextVar.Value); TextVar.Listeners.Add(delegate() { L.Text = Ops.Str(TextVar.Value); Dirty(); }); } break;
                case "anchor": L.TextAlign = MovoGui.Align(Ops.Str(v)); break;
                case "justify": break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        public override object CGet(string k) { if (k == "text") return L.Text; return base.CGet(k); }
    }

    public class ButtonW : Widget
    {
        public Button B;
        public ButtonW(Container parent, Dictionary<string, object> kw)
        {
            B = new Button(); C = B; B.UseMnemonic = false; B.FlatStyle = FlatStyle.Standard; parent.Add(this);
            B.Click += delegate(object s, EventArgs e) { Invoke(); };
            Configure(kw);
        }
        public void Invoke() { if (Command != null) MovoGui.Safe(delegate() { MovoGui.It.Call0(Command); }); }
        public override Size Req()
        {
            Size t = MovoGui.Measure(B.Text, B.Font, 0);
            int w = OptI("width", 0) > 0 ? OptI("width", 0) * MovoGui.CharW(B.Font) + 16 : t.Width + 2 * OptI("padx", 10) + 6;
            int h = OptI("height", 0) > 0 ? OptI("height", 0) * t.Height + 12 : t.Height + 2 * OptI("pady", 3) + 8;
            return new Size(w, h);
        }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "text": B.Text = Ops.Str(v); break;
                case "textvariable": TextVar = v as MVar; if (TextVar != null) { B.Text = Ops.Str(TextVar.Value); TextVar.Listeners.Add(delegate() { B.Text = Ops.Str(TextVar.Value); }); } break;
                case "bg": case "background": B.UseVisualStyleBackColor = false; base.ApplyOpt(k, v); break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        public override object Method(string n) { if (n == "invoke") return F(n, delegate(List<object> a) { Invoke(); return null; }); return base.Method(n); }
    }

    public class EntryW : Widget
    {
        public TextBox T;
        public EntryW(Container parent, Dictionary<string, object> kw)
        {
            T = new TextBox(); C = T; parent.Add(this); MovoGui.LastEntry = this; MovoGui.LastWidget = this;
            T.TextChanged += delegate(object s, EventArgs e) { if (TextVar != null && !settingVar) { settingVar = true; TextVar.Set(T.Text); settingVar = false; } };
            T.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Return && Command != null) { } };
            Configure(kw);
        }
        bool settingVar;
        public override Size Req() { int w = OptI("width", 20) * MovoGui.CharW(T.Font) + 8; return new Size(w, T.PreferredHeight); }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "show": T.PasswordChar = Ops.Str(v).Length > 0 ? Ops.Str(v)[0] : '\0'; break;
                case "justify": string j = Ops.Str(v); T.TextAlign = j == "center" ? HorizontalAlignment.Center : (j == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Left); break;
                case "textvariable": TextVar = v as MVar; if (TextVar != null) { T.Text = Ops.Str(TextVar.Value); TextVar.Listeners.Add(delegate() { if (!settingVar) { settingVar = true; T.Text = Ops.Str(TextVar.Value); settingVar = false; } }); } break;
                case "state": string st = Ops.Str(v); T.ReadOnly = st == "readonly" || st == "disabled"; break;
                case "width": break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        public override object CGet(string k) { if (k == "text") return T.Text; return base.CGet(k); }
        public override object Method(string n)
        {
            switch (n)
            {
                case "get": return F(n, delegate(List<object> a) { return T.Text; });
                case "insert": return F(n, delegate(List<object> a) { int i = MovoGui.EntryIdx(T.Text, a[0], T.SelectionStart); T.Text = T.Text.Insert(i, Ops.Str(a[1])); T.SelectionStart = i + Ops.Str(a[1]).Length; return null; });
                case "delete": return F(n, delegate(List<object> a)
                {
                    int s = MovoGui.EntryIdx(T.Text, a[0], T.SelectionStart); int e = a.Count > 1 ? MovoGui.EntryIdx(T.Text, a[1], T.SelectionStart) : s + 1;
                    if (e > T.Text.Length) e = T.Text.Length; if (s < 0) s = 0; if (e > s) T.Text = T.Text.Remove(s, e - s); return null;
                });
                case "icursor": return F(n, delegate(List<object> a) { T.SelectionStart = MovoGui.EntryIdx(T.Text, a[0], T.SelectionStart); T.SelectionLength = 0; return null; });
                case "selection_range": case "select_range": return F(n, delegate(List<object> a) { int s = MovoGui.EntryIdx(T.Text, a[0], 0), e = MovoGui.EntryIdx(T.Text, a[1], 0); T.Select(s, Math.Max(0, e - s)); return null; });
                case "select_clear": case "selection_clear": return F(n, delegate(List<object> a) { T.SelectionLength = 0; return null; });
            }
            return base.Method(n);
        }
    }

    public class TextW : Widget
    {
        public TextBox T;
        public TextW(Container parent, Dictionary<string, object> kw)
        {
            T = new TextBox(); T.Multiline = true; T.ScrollBars = ScrollBars.Vertical; T.AcceptsReturn = true; T.AcceptsTab = true; T.WordWrap = true; C = T; parent.Add(this); MovoGui.LastWidget = this; Configure(kw);
        }
        public override Size Req() { return new Size(OptI("width", 80) * MovoGui.CharW(T.Font) + 8, OptI("height", 24) * MovoGui.Measure("Ag", T.Font, 0).Height + 6); }
        public override void ApplyOpt(string k, object v)
        {
            if (k == "wrap") { T.WordWrap = Ops.Str(v) != "none"; return; }
            if (k == "state") { T.ReadOnly = Ops.Str(v) == "disabled"; return; }
            if (k == "width" || k == "height") return;
            base.ApplyOpt(k, v);
        }
        int Idx(object o, bool isEnd)
        {
            string s = Ops.Str(o); string t = T.Text.Replace("\r\n", "\n");
            if (s == "end") return t.Length + (isEnd ? 1 : 0); if (s == "end-1c") return t.Length; if (s == "insert") return T.SelectionStart;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\.(\d+)$");
            if (m.Success)
            {
                int line = int.Parse(m.Groups[1].Value), col = int.Parse(m.Groups[2].Value); int pos = 0;
                for (int i = 1; i < line; i++) { int nl = t.IndexOf('\n', pos); if (nl < 0) return t.Length; pos = nl + 1; }
                return Math.Min(t.Length, pos + col);
            }
            throw new MovoError("ValueError", "invalid Text index: " + s);
        }
        public override object Method(string n)
        {
            switch (n)
            {
                case "get": return F(n, delegate(List<object> a) { string t = T.Text.Replace("\r\n", "\n"); int s = Idx(a[0], false); int e = a.Count > 1 ? Idx(a[1], true) : s + 1; string r = t.Substring(Math.Min(s, t.Length), Math.Max(0, Math.Min(e, t.Length) - Math.Min(s, t.Length))); if (a.Count > 1 && Ops.Str(a[1]) == "end") r += "\n"; return r; });
                case "insert": return F(n, delegate(List<object> a) { string t = T.Text.Replace("\r\n", "\n"); int i = Math.Min(Idx(a[0], false), t.Length); t = t.Insert(i, Ops.Str(a[1])); T.Text = t.Replace("\n", "\r\n"); return null; });
                case "delete": return F(n, delegate(List<object> a) { string t = T.Text.Replace("\r\n", "\n"); int s = Math.Min(Idx(a[0], false), t.Length); int e = a.Count > 1 ? Math.Min(Idx(a[1], false), t.Length) : Math.Min(s + 1, t.Length); if (e > s) t = t.Remove(s, e - s); T.Text = t.Replace("\n", "\r\n"); return null; });
                case "see": return F(n, delegate(List<object> a) { T.SelectionStart = T.Text.Length; T.ScrollToCaret(); return null; });
            }
            return base.Method(n);
        }
    }

    public class ListboxW : Widget
    {
        public ListBox L;
        public ListboxW(Container parent, Dictionary<string, object> kw) { L = new ListBox(); C = L; parent.Add(this); MovoGui.LastWidget = this; L.SelectedIndexChanged += delegate(object s, EventArgs e) { Dispatch("ListSel", new MEvent { Widget = this }, delegate(Binding b) { return true; }); }; Configure(kw); }
        public override Size Req() { return new Size(OptI("width", 20) * MovoGui.CharW(L.Font) + 8, OptI("height", 10) * L.ItemHeight + 6); }
        public override void ApplyOpt(string k, object v) { if (k == "selectmode") { string s = Ops.Str(v); L.SelectionMode = (s == "multiple" || s == "extended") ? SelectionMode.MultiExtended : SelectionMode.One; return; } if (k == "width" || k == "height") return; base.ApplyOpt(k, v); }
        public override void HookEvents(string kind) { if (kind == "ListSel") return; base.HookEvents(kind); }
        public override object Method(string n)
        {
            switch (n)
            {
                case "insert": return F(n, delegate(List<object> a) { string i = Ops.Str(a[0]); for (int k = 1; k < a.Count; k++) { if (i == "end") L.Items.Add(Ops.Str(a[k])); else L.Items.Insert(Math.Min((int)Ops.ToL(a[0]) + k - 1, L.Items.Count), Ops.Str(a[k])); } return null; });
                case "delete": return F(n, delegate(List<object> a) { if (Ops.Str(a[0]) == "0" && a.Count > 1 && Ops.Str(a[1]) == "end") { L.Items.Clear(); return null; } int s = (int)Ops.ToL(a[0]); int e = a.Count > 1 ? (Ops.Str(a[1]) == "end" ? L.Items.Count - 1 : (int)Ops.ToL(a[1])) : s; for (int i = Math.Min(e, L.Items.Count - 1); i >= s; i--) L.Items.RemoveAt(i); return null; });
                case "get": return F(n, delegate(List<object> a) { if (a.Count > 1) { List<object> r = new List<object>(); int s = (int)Ops.ToL(a[0]); int e = Ops.Str(a[1]) == "end" ? L.Items.Count - 1 : (int)Ops.ToL(a[1]); for (int i = s; i <= e && i < L.Items.Count; i++) r.Add(L.Items[i].ToString()); return new MTuple(r); } int ix = (int)Ops.ToL(a[0]); return L.Items[ix].ToString(); });
                case "curselection": return F(n, delegate(List<object> a) { List<object> r = new List<object>(); foreach (int i in L.SelectedIndices) r.Add((long)i); return new MTuple(r); });
                case "size": return F(n, delegate(List<object> a) { return (long)L.Items.Count; });
                case "selection_set": case "select_set": return F(n, delegate(List<object> a) { L.SelectedIndex = (int)Ops.ToL(a[0]); return null; });
                case "selection_clear": case "select_clear": return F(n, delegate(List<object> a) { L.ClearSelected(); return null; });
                case "see": case "activate": return F(n, delegate(List<object> a) { return null; });
            }
            return base.Method(n);
        }
    }

    public class CheckW : Widget
    {
        public ButtonBase B; public bool IsRadio; public MVar Var; public object On = 1L, Off = 0L, RadioVal; bool busy;
        public CheckW(Container parent, Dictionary<string, object> kw, bool radio)
        {
            IsRadio = radio; if (radio) B = new RadioButton(); else B = new CheckBox(); C = B; B.UseMnemonic = false; B.AutoSize = false; parent.Add(this); MovoGui.LastWidget = this;
            B.Click += delegate(object s, EventArgs e) { if (Var != null && !busy) { busy = true; Var.Set(IsRadio ? RadioVal : (((CheckBox)B).Checked ? On : Off)); busy = false; } if (Command != null) MovoGui.Safe(delegate() { MovoGui.It.Call0(Command); }); };
            Configure(kw);
        }
        void Sync()
        {
            if (Var == null) return; busy = true;
            if (IsRadio) ((RadioButton)B).Checked = Ops.Eq(Var.Value, RadioVal); else ((CheckBox)B).Checked = Ops.Eq(Var.Value, On);
            busy = false;
        }
        public override Size Req() { Size t = MovoGui.Measure(B.Text, B.Font, 0); return new Size(t.Width + 26, Math.Max(t.Height, 16) + 4); }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "text": B.Text = Ops.Str(v); break;
                case "variable": Var = v as MVar; if (Var != null) { Var.Listeners.Add(delegate() { if (!busy) Sync(); }); Sync(); } break;
                case "onvalue": On = v; Sync(); break; case "offvalue": Off = v; break;
                case "value": RadioVal = v; Sync(); break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        public override object Method(string n)
        {
            switch (n)
            {
                case "select": return F(n, delegate(List<object> a) { if (IsRadio) { if (Var != null) Var.Set(RadioVal); } else if (Var != null) Var.Set(On); else ((CheckBox)B).Checked = true; return null; });
                case "deselect": return F(n, delegate(List<object> a) { if (Var != null) Var.Set(Off); else if (!IsRadio) ((CheckBox)B).Checked = false; return null; });
                case "toggle": return F(n, delegate(List<object> a) { if (!IsRadio) { bool c = !((CheckBox)B).Checked; if (Var != null) Var.Set(c ? On : Off); else ((CheckBox)B).Checked = c; } return null; });
                case "invoke": return F(n, delegate(List<object> a) { typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(B, new object[] { EventArgs.Empty }); return null; });
            }
            return base.Method(n);
        }
    }

    public class ScaleW : Widget
    {
        public TrackBar T; double from = 0, to = 100, res = 1; public MVar Var; bool busy;
        public ScaleW(Container parent, Dictionary<string, object> kw)
        {
            T = new TrackBar(); C = T; T.TickStyle = TickStyle.None; parent.Add(this); MovoGui.LastWidget = this;
            T.ValueChanged += delegate(object s, EventArgs e) { double v = Value(); if (Var != null && !busy) { busy = true; Var.Set(v); busy = false; } if (Command != null) MovoGui.Safe(delegate() { MovoGui.It.Call1(Command, Ops.Str(res == Math.Floor(res) ? (object)(long)v : (object)v)); }); };
            Configure(kw); Rescale();
        }
        double Value() { return from + T.Value * res; }
        void Rescale() { int max = (int)Math.Round((to - from) / res); T.Minimum = 0; T.Maximum = Math.Max(1, max); }
        public override Size Req() { int len = OptI("length", 100); return T.Orientation == Orientation.Horizontal ? new Size(len + 20, 45) : new Size(45, len + 20); }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "from_": case "from": from = Ops.ToD(v); Rescale(); break;
                case "to": to = Ops.ToD(v); Rescale(); break;
                case "resolution": res = Ops.ToD(v); Rescale(); break;
                case "orient": T.Orientation = Ops.Str(v) == "vertical" ? Orientation.Vertical : Orientation.Horizontal; break;
                case "variable": Var = v as MVar; if (Var != null) { Var.Listeners.Add(delegate() { if (!busy) SetV(Ops.ToD(Var.Value)); }); SetV(Ops.ToD(Var.Value)); } break;
                case "length": break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        void SetV(double v) { int i = (int)Math.Round((v - from) / res); T.Value = Math.Max(T.Minimum, Math.Min(T.Maximum, i)); }
        object Get() { double v = Value(); if (res == Math.Floor(res) && from == Math.Floor(from)) return (long)v; return v; }
        public override object Method(string n)
        {
            if (n == "get") return F(n, delegate(List<object> a) { return Get(); });
            if (n == "set") return F(n, delegate(List<object> a) { SetV(Ops.ToD(a[0])); return null; });
            return base.Method(n);
        }
    }

    public class ComboW : Widget
    {
        public ComboBox B;
        public ComboW(Container parent, Dictionary<string, object> kw) { B = new ComboBox(); C = B; parent.Add(this); MovoGui.LastWidget = this; B.SelectedIndexChanged += delegate(object s, EventArgs e) { if (TextVar != null) TextVar.Set(B.Text); Dispatch("ComboSel", new MEvent { Widget = this }, delegate(Binding b) { return true; }); }; Configure(kw); }
        public override void HookEvents(string kind) { if (kind == "ComboSel") return; base.HookEvents(kind); }
        public override Size Req() { return new Size(OptI("width", 20) * MovoGui.CharW(B.Font) + 24, B.PreferredHeight); }
        public override void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "values": B.Items.Clear(); foreach (object x in Interp.Cur.Iter(v)) B.Items.Add(Ops.Str(x)); break;
                case "state": B.DropDownStyle = Ops.Str(v) == "readonly" ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown; break;
                case "textvariable": TextVar = v as MVar; if (TextVar != null) B.Text = Ops.Str(TextVar.Value); break;
                case "width": break;
                default: base.ApplyOpt(k, v); break;
            }
        }
        public override object Method(string n)
        {
            switch (n)
            {
                case "get": return F(n, delegate(List<object> a) { return B.Text; });
                case "set": return F(n, delegate(List<object> a) { B.Text = Ops.Str(a[0]); return null; });
                case "current": return F(n, delegate(List<object> a) { if (a.Count > 0) B.SelectedIndex = (int)Ops.ToL(a[0]); return (long)B.SelectedIndex; });
            }
            return base.Method(n);
        }
    }

    // ================= Canvas =================
    public class CItem { public long Id; public string Kind; public double[] Co; public Color Fill = Color.Empty, Outline = Color.Black; public float Width = 1; public string Text = ""; public Font Font; public string Anchor = "center"; public List<string> Tags = new List<string>(); public bool Hidden; }

    public class CanvasCtl : Control
    {
        public List<CItem> Items = new List<CItem>();
        public CanvasCtl() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true); TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (CItem it in Items.ToArray())
            {
                if (it.Hidden) continue; double[] c = it.Co;
                using (Pen pen = new Pen(it.Outline.IsEmpty ? Color.Transparent : it.Outline, it.Width))
                {
                    if (it.Kind == "rect" || it.Kind == "oval")
                    {
                        float x = (float)Math.Min(c[0], c[2]), y = (float)Math.Min(c[1], c[3]), w = (float)Math.Abs(c[2] - c[0]), h = (float)Math.Abs(c[3] - c[1]);
                        if (!it.Fill.IsEmpty) using (Brush b = new SolidBrush(it.Fill)) { if (it.Kind == "rect") g.FillRectangle(b, x, y, w, h); else g.FillEllipse(b, x, y, w, h); }
                        if (!it.Outline.IsEmpty) { if (it.Kind == "rect") g.DrawRectangle(pen, x, y, w, h); else g.DrawEllipse(pen, x, y, w, h); }
                    }
                    else if (it.Kind == "line" && c.Length >= 4)
                    {
                        PointF[] pts = new PointF[c.Length / 2]; for (int i = 0; i < pts.Length; i++) pts[i] = new PointF((float)c[2 * i], (float)c[2 * i + 1]);
                        using (Pen lp = new Pen(it.Fill.IsEmpty ? Color.Black : it.Fill, it.Width)) g.DrawLines(lp, pts);
                    }
                    else if (it.Kind == "poly" && c.Length >= 6)
                    {
                        PointF[] pts = new PointF[c.Length / 2]; for (int i = 0; i < pts.Length; i++) pts[i] = new PointF((float)c[2 * i], (float)c[2 * i + 1]);
                        if (!it.Fill.IsEmpty) using (Brush b = new SolidBrush(it.Fill)) g.FillPolygon(b, pts);
                        if (!it.Outline.IsEmpty) g.DrawPolygon(pen, pts);
                    }
                    else if (it.Kind == "text")
                    {
                        Font f = it.Font ?? Font; SizeF sz = g.MeasureString(it.Text, f); float x = (float)c[0], y = (float)c[1];
                        if (it.Anchor.Contains("w")) { } else if (it.Anchor.Contains("e")) x -= sz.Width; else x -= sz.Width / 2;
                        if (it.Anchor.Contains("n")) { } else if (it.Anchor.Contains("s")) y -= sz.Height; else y -= sz.Height / 2;
                        using (Brush b = new SolidBrush(it.Fill.IsEmpty ? Color.Black : it.Fill)) g.DrawString(it.Text, f, b, x, y);
                    }
                }
            }
        }
    }

    public class CanvasW : Widget
    {
        public CanvasCtl K; long nextId = 1;
        public CanvasW(Container parent, Dictionary<string, object> kw) { K = new CanvasCtl(); C = K; K.BackColor = SystemColors.Control; parent.Add(this); MovoGui.LastWidget = this; K.MouseDown += delegate(object s, MouseEventArgs e) { K.Focus(); }; Configure(kw); }
        public override Size Req() { return new Size(OptI("width", 378), OptI("height", 265)); }
        public override void ApplyOpt(string k, object v) { if (k == "width" || k == "height") { return; } base.ApplyOpt(k, v); }
        List<CItem> Find(object id)
        {
            List<CItem> r = new List<CItem>();
            if (id is long) { foreach (CItem i in K.Items) if (i.Id == (long)id) r.Add(i); return r; }
            string s = Ops.Str(id); foreach (CItem i in K.Items) if (s == "all" || i.Tags.Contains(s)) r.Add(i); return r;
        }
        object Create(string kind, List<object> a, Dictionary<string, object> kw)
        {
            CItem it = new CItem(); it.Id = nextId++; it.Kind = kind; List<double> co = new List<double>();
            foreach (object o in a) { if (o is List<object> || o is MTuple) foreach (object x in Interp.Cur.Iter(o)) co.Add(Ops.ToD(x)); else co.Add(Ops.ToD(o)); }
            it.Co = co.ToArray(); if (kind == "line") it.Fill = Color.Black;
            if (kind == "text") it.Fill = Color.Black;
            ApplyItem(it, kw); K.Items.Add(it); K.Invalidate(); return it.Id;
        }
        void ApplyItem(CItem it, Dictionary<string, object> kw)
        {
            foreach (KeyValuePair<string, object> kv in kw)
            {
                switch (kv.Key)
                {
                    case "fill": it.Fill = Ops.Str(kv.Value).Length == 0 ? Color.Empty : MovoGui.Color(kv.Value); break;
                    case "outline": it.Outline = Ops.Str(kv.Value).Length == 0 ? Color.Empty : MovoGui.Color(kv.Value); break;
                    case "width": it.Width = (float)Ops.ToD(kv.Value); break;
                    case "text": it.Text = Ops.Str(kv.Value); break;
                    case "font": it.Font = MovoGui.MakeFont(kv.Value); break;
                    case "anchor": it.Anchor = Ops.Str(kv.Value); break;
                    case "state": it.Hidden = Ops.Str(kv.Value) == "hidden"; break;
                    case "tags": it.Tags.Clear(); if (kv.Value is string) it.Tags.Add((string)kv.Value); else foreach (object x in Interp.Cur.Iter(kv.Value)) it.Tags.Add(Ops.Str(x)); break;
                }
            }
            if (it.Kind == "line" && kw.ContainsKey("outline")) { }
        }
        public override object Method(string n)
        {
            switch (n)
            {
                case "create_rectangle": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { return Create("rect", a, kw); });
                case "create_oval": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { return Create("oval", a, kw); });
                case "create_line": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { return Create("line", a, kw); });
                case "create_polygon": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { return Create("poly", a, kw); });
                case "create_text": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { return Create("text", a, kw); });
                case "delete": return F(n, delegate(List<object> a) { foreach (object id in a) foreach (CItem i in Find(id)) K.Items.Remove(i); K.Invalidate(); return null; });
                case "move": return F(n, delegate(List<object> a) { double dx = Ops.ToD(a[1]), dy = Ops.ToD(a[2]); foreach (CItem i in Find(a[0])) for (int k = 0; k + 1 < i.Co.Length; k += 2) { i.Co[k] += dx; i.Co[k + 1] += dy; } K.Invalidate(); return null; });
                case "coords": return F(n, delegate(List<object> a)
                {
                    List<CItem> f = Find(a[0]); if (f.Count == 0) return new List<object>();
                    if (a.Count > 1) { List<double> co = new List<double>(); for (int k = 1; k < a.Count; k++) { if (a[k] is List<object> || a[k] is MTuple) foreach (object x in Interp.Cur.Iter(a[k])) co.Add(Ops.ToD(x)); else co.Add(Ops.ToD(a[k])); } f[0].Co = co.ToArray(); K.Invalidate(); return null; }
                    List<object> r = new List<object>(); foreach (double d in f[0].Co) r.Add(d); return r;
                });
                case "itemconfig": case "itemconfigure": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { foreach (CItem i in Find(a[0])) ApplyItem(i, kw); K.Invalidate(); return null; });
                case "find_all": return F(n, delegate(List<object> a) { List<object> r = new List<object>(); foreach (CItem i in K.Items) r.Add(i.Id); return new MTuple(r); });
                case "find_withtag": return F(n, delegate(List<object> a) { List<object> r = new List<object>(); foreach (CItem i in Find(a[0])) r.Add(i.Id); return new MTuple(r); });
                case "bbox": return F(n, delegate(List<object> a) { List<CItem> f = Find(a[0]); if (f.Count == 0) return null; double x1 = 1e18, y1 = 1e18, x2 = -1e18, y2 = -1e18; foreach (CItem i in f) for (int k = 0; k + 1 < i.Co.Length; k += 2) { x1 = Math.Min(x1, i.Co[k]); x2 = Math.Max(x2, i.Co[k]); y1 = Math.Min(y1, i.Co[k + 1]); y2 = Math.Max(y2, i.Co[k + 1]); } return new MTuple(new object[] { (long)x1, (long)y1, (long)x2, (long)y2 }); });
                case "find_overlapping": return F(n, delegate(List<object> a)
                {
                    double x1 = Ops.ToD(a[0]), y1 = Ops.ToD(a[1]), x2 = Ops.ToD(a[2]), y2 = Ops.ToD(a[3]); List<object> r = new List<object>();
                    foreach (CItem i in K.Items) { if (i.Co.Length < 2) continue; double bx1 = 1e18, by1 = 1e18, bx2 = -1e18, by2 = -1e18; for (int k = 0; k + 1 < i.Co.Length; k += 2) { bx1 = Math.Min(bx1, i.Co[k]); bx2 = Math.Max(bx2, i.Co[k]); by1 = Math.Min(by1, i.Co[k + 1]); by2 = Math.Max(by2, i.Co[k + 1]); } if (bx2 >= Math.Min(x1, x2) && bx1 <= Math.Max(x1, x2) && by2 >= Math.Min(y1, y2) && by1 <= Math.Max(y1, y2)) r.Add(i.Id); }
                    return new MTuple(r);
                });
            }
            return base.Method(n);
        }
    }
}
