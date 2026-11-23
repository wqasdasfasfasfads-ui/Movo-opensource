using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MovoLang
{
    public class MVar : IMObj
    {
        public object Value; public List<Action> Listeners = new List<Action>(); public string Kind;
        public MVar(string kind, object v) { Kind = kind; Value = v; }
        public void Set(object v)
        {
            if (Kind == "IntVar") v = Ops.ToL(v is string ? (object)long.Parse((string)v) : v);
            else if (Kind == "DoubleVar") v = Ops.ToD(v is string ? (object)double.Parse((string)v, Ops.IC) : v);
            else if (Kind == "BooleanVar") v = Ops.Truthy(v);
            else if (Kind == "StringVar") v = Ops.Str(v);
            Value = v; foreach (Action a in new List<Action>(Listeners)) a();
        }
        public object GetAttr(string n)
        {
            if (n == "get") return new NativeFn(n, delegate(List<object> a) { return Value; });
            if (n == "set") return new NativeFn(n, delegate(List<object> a) { Set(a[0]); return null; });
            if (n == "trace_add" || n == "trace") return new NativeFn(n, delegate(List<object> a) { object f = a[a.Count - 1]; Listeners.Add(delegate() { MovoGui.Safe(delegate() { MovoGui.It.Call(f, new List<object>(), null); }); }); return null; });
            throw new MovoError("AttributeError", "variable has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { }
    }

    public class MEvent : IMObj
    {
        public string Char = "", KeySym = ""; public long KeyCode, X, Y, Delta, Num, State; public object Widget;
        public object GetAttr(string n)
        {
            switch (n)
            {
                case "char": return Char; case "keysym": return KeySym; case "keycode": return KeyCode; case "x": return X; case "y": return Y;
                case "x_root": return X; case "y_root": return Y; case "delta": return Delta; case "num": return Num; case "widget": return Widget; case "state": return State;
                case "width": return 0L; case "height": return 0L;
            }
            throw new MovoError("AttributeError", "event has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { }
    }

    public class Binding
    {
        public string Kind; // Key, KeyRelease, Press, Release, Motion, Enter, Leave, Wheel, Configure, FocusIn, FocusOut
        public string Detail; public bool Ctrl, Shift, Alt, Double, AnyKey; public int ButtonMask; public object Fn; public Action Act;
        public static Binding Parse(string seq)
        {
            string s = seq.Trim(); if (s.StartsWith("<")) s = s.Substring(1); if (s.EndsWith(">")) s = s.Substring(0, s.Length - 1);
            Binding b = new Binding(); string[] parts = s.Split('-'); List<string> p = new List<string>(parts);
            // key "minus": <Control-minus>; a bare "-" is not supported
            while (p.Count > 1)
            {
                string m = p[0].ToLowerInvariant();
                if (m == "control" || m == "ctrl") b.Ctrl = true; else if (m == "shift") b.Shift = true; else if (m == "alt") b.Alt = true;
                else if (m == "double") b.Double = true; else if (m == "any") { } else break;
                p.RemoveAt(0);
            }
            string t = p[0]; string rest = p.Count > 1 ? p[1] : null; string tl = t.ToLowerInvariant();
            if (tl == "key" || tl == "keypress") { b.Kind = "Key"; b.Detail = rest; if (rest == null) b.AnyKey = true; }
            else if (tl == "keyrelease") { b.Kind = "KeyRelease"; b.Detail = rest; if (rest == null) b.AnyKey = true; }
            else if (tl == "button" || tl == "buttonpress") { b.Kind = "Press"; b.Detail = rest ?? "any"; }
            else if (tl == "buttonrelease") { b.Kind = "Release"; b.Detail = rest ?? "any"; }
            else if (tl == "motion") b.Kind = "Motion";
            else if (tl.Length == 2 && tl[0] == 'b' && char.IsDigit(tl[1]) && rest != null && rest.ToLowerInvariant() == "motion") { b.Kind = "Motion"; b.ButtonMask = tl[1] - '0'; }
            else if (tl == "enter") b.Kind = "Enter"; else if (tl == "leave") b.Kind = "Leave";
            else if (tl == "mousewheel") b.Kind = "Wheel"; else if (tl == "configure") b.Kind = "Configure";
            else if (tl == "focusin") b.Kind = "FocusIn"; else if (tl == "focusout") b.Kind = "FocusOut";
            else { b.Kind = "Key"; b.Detail = p.Count > 1 ? string.Join("-", p.ToArray()) : t; }
            return b;
        }
        public bool KeyMatches(MEvent e)
        {
            if (Ctrl != ((e.State & 4) != 0)) { if (Ctrl) return false; }
            if (Shift && (e.State & 1) == 0) return false;
            if (Alt && (e.State & 8) == 0) return false;
            if (AnyKey || Detail == null) return !Ctrl || (e.State & 4) != 0;
            if (Detail.Length == 1 && e.Char.Length == 1 && string.Equals(Detail, e.Char, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(Detail, e.KeySym, StringComparison.OrdinalIgnoreCase)) return true;
            if (Detail.Length == 1 && char.IsLetterOrDigit(Detail[0]) && Detail[0] < 128 && e.KeyCode == char.ToUpperInvariant(Detail[0])) return true;
            return false;
        }
    }

    public class SafeCtl { }

    public abstract class Widget : IMObj, IMIndexable
    {
        public Control C; public Container Parent; public string Mgr; public bool Destroyed;
        public Dictionary<string, object> Opts = new Dictionary<string, object>();
        public Dictionary<string, object> Pack = new Dictionary<string, object>(), Grid = new Dictionary<string, object>(), Place = new Dictionary<string, object>();
        public List<Binding> Binds = new List<Binding>(); public HashSet<string> Hooked = new HashSet<string>();
        public MVar TextVar; public object Command;

        public virtual Size Req() { return new Size(10, 10); }
        public virtual void Configure(Dictionary<string, object> kw)
        {
            foreach (KeyValuePair<string, object> kv in kw) { Opts[kv.Key] = kv.Value; ApplyOpt(kv.Key, kv.Value); }
            Dirty();
        }
        public virtual void ApplyOpt(string k, object v)
        {
            switch (k)
            {
                case "bg": case "background": C.BackColor = MovoGui.Color(v); break;
                case "fg": case "foreground": C.ForeColor = MovoGui.Color(v); break;
                case "font": C.Font = MovoGui.MakeFont(v); break;
                case "state": C.Enabled = Ops.Str(v) != "disabled"; break;
                case "command": Command = v; break;
                case "cursor": try { string cn = Ops.Str(v); C.Cursor = (cn == "hand2" || cn == "hand1") ? Cursors.Hand : (cn == "watch" ? Cursors.WaitCursor : (cn == "xterm" ? Cursors.IBeam : (cn == "cross" ? Cursors.Cross : Cursors.Default))); } catch (Exception) { } break;
            }
        }
        public virtual object CGet(string k)
        {
            object v; if (Opts.TryGetValue(k, out v)) return v;
            if (k == "text") return C.Text; return "";
        }
        public void Dirty() { Window w = TopWindow(); if (w != null) w.ScheduleLayout(); }
        public Window TopWindow() { Widget w = this; while (w != null) { Window t = w as Window; if (t != null) return t; w = w.Parent; } return null; }

        public int OptI(string k, int d) { object v; if (Opts.TryGetValue(k, out v) && v != null) { try { return (int)Ops.ToD(v is string ? (object)double.Parse((string)v, Ops.IC) : v); } catch (Exception) { } } return d; }

        public object GetAttr(string n)
        {
            object m = Method(n); if (m != null) return m;
            throw new MovoError("AttributeError", "widget has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { Opts[n] = v; }
        public object GetItem(object k) { return CGet(Ops.Str(k)); }
        public void SetItem(object k, object v) { Dictionary<string, object> d = new Dictionary<string, object>(); d[Ops.Str(k)] = v; Configure(d); }

        protected NativeFn F(string n, Func<List<object>, Dictionary<string, object>, object> f) { return new NativeFn(n, f); }
        protected NativeFn F(string n, Func<List<object>, object> f) { return new NativeFn(n, f); }

        public virtual object Method(string n)
        {
            switch (n)
            {
                case "pack": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { DoPack(kw); return null; });
                case "grid": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { DoGrid(kw); return null; });
                case "place": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { DoPlace(kw); return null; });
                case "pack_forget": case "grid_forget": case "place_forget": case "forget": return F(n, delegate(List<object> a) { Forget(); return null; });
                case "config": case "configure": case "dat": return F(n, delegate(List<object> a, Dictionary<string, object> kw)
                    {
                        if (a.Count == 1 && a[0] is string && kw.Count == 0) return CGet((string)a[0]);
                        Configure(kw); return null;
                    });
                case "cget": return F(n, delegate(List<object> a) { return CGet(Ops.Str(a[0])); });
                case "bind": return F(n, delegate(List<object> a) { AddBind(Ops.Str(a[0]), a[1]); return null; });
                case "unbind": return F(n, delegate(List<object> a) { Binds.RemoveAll(delegate(Binding b) { return b.Kind == Binding.Parse(Ops.Str(a[0])).Kind; }); return null; });
                case "focus": case "focus_set": case "focus_force": return F(n, delegate(List<object> a) { if (C.CanFocus) C.Focus(); else C.Select(); return null; });
                case "destroy": return F(n, delegate(List<object> a) { Destroy(); return null; });
                case "update": case "update_idletasks": return F(n, delegate(List<object> a) { Window w = TopWindow(); if (w != null) w.DoLayout(); Application.DoEvents(); return null; });
                case "after": return F(n, delegate(List<object> a) { return MovoGui.After(a); });
                case "after_cancel": return F(n, delegate(List<object> a) { MovoGui.AfterCancel(a[0]); return null; });
                case "winfo_width": return F(n, delegate(List<object> a) { return (long)C.Width; });
                case "winfo_height": return F(n, delegate(List<object> a) { return (long)C.Height; });
                case "winfo_x": return F(n, delegate(List<object> a) { return (long)C.Left; });
                case "winfo_y": return F(n, delegate(List<object> a) { return (long)C.Top; });
                case "winfo_reqwidth": return F(n, delegate(List<object> a) { return (long)Req().Width; });
                case "winfo_reqheight": return F(n, delegate(List<object> a) { return (long)Req().Height; });
                case "winfo_exists": return F(n, delegate(List<object> a) { return !Destroyed; });
                case "winfo_pointerx": return F(n, delegate(List<object> a) { return (long)Cursor.Position.X; });
                case "winfo_pointery": return F(n, delegate(List<object> a) { return (long)Cursor.Position.Y; });
                case "winfo_screenwidth": return F(n, delegate(List<object> a) { return (long)Screen.PrimaryScreen.Bounds.Width; });
                case "winfo_screenheight": return F(n, delegate(List<object> a) { return (long)Screen.PrimaryScreen.Bounds.Height; });
                case "lift": case "tkraise": return F(n, delegate(List<object> a) { C.BringToFront(); return null; });
                case "lower": return F(n, delegate(List<object> a) { C.SendToBack(); return null; });
                case "winfo_children": return F(n, delegate(List<object> a) { List<object> r = new List<object>(); Container c = this as Container; if (c != null) foreach (Widget w in c.Slaves) r.Add(w); return r; });
            }
            return null;
        }

        public void AddBind(string seq, object fn)
        {
            Binding b = Binding.Parse(seq); b.Fn = fn;
            Binds.RemoveAll(delegate(Binding x) { return x.Kind == b.Kind && x.Detail == b.Detail && x.Ctrl == b.Ctrl && x.Shift == b.Shift && x.Alt == b.Alt && x.Double == b.Double; });
            Binds.Add(b); HookEvents(b.Kind);
        }
        public void AddBindAction(string seq, Action act) { Binding b = Binding.Parse(seq); b.Act = act; Binds.Add(b); HookEvents(b.Kind); }

        public void Fire(Binding b, MEvent e)
        {
            if (b.Act != null) { MovoGui.Safe(b.Act); return; }
            MovoGui.Safe(delegate() { MovoGui.It.Call(b.Fn, new List<object> { e }, null); });
        }

        public void Dispatch(string kind, MEvent e, Func<Binding, bool> test)
        {
            foreach (Binding b in new List<Binding>(Binds)) if (b.Kind == kind && test(b)) Fire(b, e);
        }

        public virtual void HookEvents(string kind)
        {
            if (Hooked.Contains(kind)) return; Hooked.Add(kind);
            Control c = C; Widget self = this;
            switch (kind)
            {
                case "Key": c.KeyDown += delegate(object s, KeyEventArgs ke) { MEvent ev = MovoGui.KeyEvent(ke, self); Dispatch("Key", ev, delegate(Binding b) { return b.KeyMatches(ev); }); }; break;
                case "KeyRelease": c.KeyUp += delegate(object s, KeyEventArgs ke) { MEvent ev = MovoGui.KeyEvent(ke, self); Dispatch("KeyRelease", ev, delegate(Binding b) { return b.KeyMatches(ev); }); }; break;
                case "Press": c.MouseDown += delegate(object s, MouseEventArgs me) { MEvent ev = MovoGui.MouseEvent(me, self); Dispatch("Press", ev, delegate(Binding b) { return BtnOk(b, me) && (!b.Double || me.Clicks >= 2); }); }; break;
                case "Release": c.MouseUp += delegate(object s, MouseEventArgs me) { MEvent ev = MovoGui.MouseEvent(me, self); Dispatch("Release", ev, delegate(Binding b) { return BtnOk(b, me); }); }; break;
                case "Motion": c.MouseMove += delegate(object s, MouseEventArgs me) { MEvent ev = MovoGui.MouseEvent(me, self); Dispatch("Motion", ev, delegate(Binding b) { return b.ButtonMask == 0 || (b.ButtonMask == 1 && (Control.MouseButtons & MouseButtons.Left) != 0) || (b.ButtonMask == 3 && (Control.MouseButtons & MouseButtons.Right) != 0); }); }; break;
                case "Enter": c.MouseEnter += delegate(object s, EventArgs ea) { Dispatch("Enter", new MEvent { Widget = self }, delegate(Binding b) { return true; }); }; break;
                case "Leave": c.MouseLeave += delegate(object s, EventArgs ea) { Dispatch("Leave", new MEvent { Widget = self }, delegate(Binding b) { return true; }); }; break;
                case "Wheel": c.MouseWheel += delegate(object s, MouseEventArgs me) { MEvent ev = MovoGui.MouseEvent(me, self); ev.Delta = me.Delta; Dispatch("Wheel", ev, delegate(Binding b) { return true; }); }; break;
                case "Configure": c.Resize += delegate(object s, EventArgs ea) { Dispatch("Configure", new MEvent { Widget = self }, delegate(Binding b) { return true; }); }; break;
                case "FocusIn": c.GotFocus += delegate(object s, EventArgs ea) { Dispatch("FocusIn", new MEvent { Widget = self }, delegate(Binding b) { return true; }); }; break;
                case "FocusOut": c.LostFocus += delegate(object s, EventArgs ea) { Dispatch("FocusOut", new MEvent { Widget = self }, delegate(Binding b) { return true; }); }; break;
            }
        }
        static bool BtnOk(Binding b, MouseEventArgs me)
        {
            if (b.Detail == "any") return true;
            int n = me.Button == MouseButtons.Left ? 1 : me.Button == MouseButtons.Middle ? 2 : 3;
            return b.Detail == n.ToString();
        }

        // ---- geometry ----
        static bool Bool(object v) { if (v is bool) return (bool)v; if (v is long) return (long)v != 0; string s = Ops.Str(v).ToLowerInvariant(); return s == "yes" || s == "true" || s == "1" || s == "on"; }
        public static bool IsTrue(object v) { return Bool(v); }

        public void ClearMgr() { if (Parent != null) { Parent.PackOrder.Remove(this); } Mgr = null; }
        public void Attach(string mgr, Dictionary<string, object> kw, Dictionary<string, object> store)
        {
            if (Parent == null) throw new MovoError("RuntimeError", "this window cannot be placed with pack/grid/place");
            if (Mgr != mgr) { Parent.PackOrder.Remove(this); }
            Mgr = mgr;
            foreach (KeyValuePair<string, object> kv in kw) store[kv.Key] = kv.Value;
            if (mgr == "pack" && !Parent.PackOrder.Contains(this)) Parent.PackOrder.Add(this);
            C.Visible = true; Parent.Touched = true; Dirty();
        }
        public void DoPack(Dictionary<string, object> kw) { Pack.Clear(); Attach("pack", kw, Pack); }
        public void DoGrid(Dictionary<string, object> kw) { Grid.Clear(); Attach("grid", kw, Grid); }
        public void DoPlace(Dictionary<string, object> kw) { Place.Clear(); Attach("place", kw, Place); }
        public void Forget() { if (Parent != null) Parent.PackOrder.Remove(this); Mgr = null; C.Visible = false; Dirty(); }
        public virtual void Destroy()
        {
            Destroyed = true; Forget();
            Container ct = this as Container; if (ct != null) foreach (Widget w in new List<Widget>(ct.Slaves)) w.Destroy();
            if (Parent != null) Parent.Slaves.Remove(this);
            C.Dispose();
        }

        public static int PadOf(Dictionary<string, object> d, string k, int def)
        {
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            MTuple t = v as MTuple; if (t != null) v = t.Items[0];
            List<object> l = v as List<object>; if (l != null) v = l[0];
            try { return (int)Ops.ToD(v is string ? (object)double.Parse((string)v, Ops.IC) : v); } catch (Exception) { return def; }
        }
        public static string StrOf(Dictionary<string, object> d, string k, string def) { object v; if (d.TryGetValue(k, out v) && v != null) return Ops.Str(v).ToLowerInvariant(); return def; }
    }

    public abstract class Container : Widget
    {
        public List<Widget> Slaves = new List<Widget>(); public List<Widget> PackOrder = new List<Widget>();
        public bool Propagate = true, Touched;
        public Dictionary<int, int> ColW = new Dictionary<int, int>(), RowW = new Dictionary<int, int>();
        public Dictionary<int, int> ColMin = new Dictionary<int, int>(), RowMin = new Dictionary<int, int>();
        public abstract Rectangle Inner();
        public virtual Size BorderExtra() { return new Size(0, 0); }

        public void Add(Widget w) { w.Parent = this; Slaves.Add(w); C.Controls.Add(w.C); w.C.Visible = false; Touched = true; }

        List<Widget> GridSlaves() { List<Widget> r = new List<Widget>(); foreach (Widget w in Slaves) if (w.Mgr == "grid") r.Add(w); return r; }

        static int GI(Dictionary<string, object> d, string k, int def) { return Widget.PadOf(d, k, def); }

        public override Size Req()
        {
            Size ex = BorderExtra();
            bool has = false; foreach (Widget w in Slaves) if (w.Mgr == "pack" || w.Mgr == "grid") has = true;
            if (!Propagate || !has) return new Size(Math.Max(0, OptI("width", 0)) + 0, Math.Max(0, OptI("height", 0)));
            Size s = PackOrder.Count > 0 ? PackReq() : GridReq();
            return new Size(s.Width + ex.Width, s.Height + ex.Height);
        }
        public Size ContentReq() { Size ex = BorderExtra(); Size s = PackOrder.Count > 0 ? PackReq() : GridReq(); return new Size(s.Width + ex.Width, s.Height + ex.Height); }

        Size SlaveSize(Widget w)
        {
            Size rq = w.Req(); Dictionary<string, object> p = w.Pack;
            return new Size(rq.Width + 2 * PadOf(p, "ipadx", 0), rq.Height + 2 * PadOf(p, "ipady", 0));
        }

        Size PackReq()
        {
            int reqW = 0, reqH = 0, curW = 0, curH = 0;
            foreach (Widget w in PackOrder)
            {
                Size sz = SlaveSize(w); int sw = sz.Width + 2 * PadOf(w.Pack, "padx", 0), sh = sz.Height + 2 * PadOf(w.Pack, "pady", 0);
                string side = StrOf(w.Pack, "side", "top");
                if (side == "top" || side == "bottom") { reqW = Math.Max(reqW, curW + sw); curH += sh; reqH = Math.Max(reqH, curH); }
                else { reqH = Math.Max(reqH, curH + sh); curW += sw; reqW = Math.Max(reqW, curW); }
            }
            return new Size(reqW, reqH);
        }

        int XExp(int idx, int cavW)
        {
            int numExpand = 0, minExpand = cavW;
            for (int i = idx; i < PackOrder.Count; i++)
            {
                Widget w = PackOrder[i]; Size sz = SlaveSize(w); int cw = sz.Width + 2 * PadOf(w.Pack, "padx", 0);
                string side = StrOf(w.Pack, "side", "top");
                if (side == "top" || side == "bottom") { if (numExpand > 0) { int cur = (cavW - cw) / numExpand; if (cur < minExpand) minExpand = cur; } }
                else { cavW -= cw; if (IsTrue(Get(w.Pack, "expand"))) numExpand++; }
            }
            if (numExpand > 0) { int cur = cavW / numExpand; if (cur < minExpand) minExpand = cur; } else return 0;
            return minExpand < 0 ? 0 : minExpand;
        }
        int YExp(int idx, int cavH)
        {
            int numExpand = 0, minExpand = cavH;
            for (int i = idx; i < PackOrder.Count; i++)
            {
                Widget w = PackOrder[i]; Size sz = SlaveSize(w); int ch = sz.Height + 2 * PadOf(w.Pack, "pady", 0);
                string side = StrOf(w.Pack, "side", "top");
                if (side == "left" || side == "right") { if (numExpand > 0) { int cur = (cavH - ch) / numExpand; if (cur < minExpand) minExpand = cur; } }
                else { cavH -= ch; if (IsTrue(Get(w.Pack, "expand"))) numExpand++; }
            }
            if (numExpand > 0) { int cur = cavH / numExpand; if (cur < minExpand) minExpand = cur; } else return 0;
            return minExpand < 0 ? 0 : minExpand;
        }
        static object Get(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }

        static Point Anchored(string anchor, Rectangle area, int w, int h)
        {
            int x = area.X + (area.Width - w) / 2, y = area.Y + (area.Height - h) / 2;
            if (anchor == "center" || anchor == "c") return new Point(x, y);
            if (anchor.Contains("w")) x = area.X; else if (anchor.Contains("e")) x = area.X + area.Width - w;
            if (anchor.Contains("n")) y = area.Y; else if (anchor.Contains("s")) y = area.Y + area.Height - h;
            return new Point(x, y);
        }

        public void LayoutChildren()
        {
            Rectangle r = Inner();
            // pack
            int cx = r.X, cy = r.Y, cw = r.Width, ch = r.Height;
            for (int i = 0; i < PackOrder.Count; i++)
            {
                Widget w = PackOrder[i]; Dictionary<string, object> p = w.Pack;
                Size sz = SlaveSize(w); int padx = PadOf(p, "padx", 0), pady = PadOf(p, "pady", 0);
                int needW = sz.Width + 2 * padx, needH = sz.Height + 2 * pady; string side = StrOf(p, "side", "top");
                bool expand = IsTrue(Get(p, "expand")); int fx, fy, fw, fh;
                if (side == "top" || side == "bottom")
                {
                    fw = cw; fh = needH; if (expand) fh += YExp(i, ch);
                    ch -= fh; if (ch < 0) { fh += ch; ch = 0; }
                    fx = cx; if (side == "top") { fy = cy; cy += fh; } else fy = cy + ch;
                }
                else
                {
                    fh = ch; fw = needW; if (expand) fw += XExp(i, cw);
                    cw -= fw; if (cw < 0) { fw += cw; cw = 0; }
                    fy = cy; if (side == "left") { fx = cx; cx += fw; } else fx = cx + cw;
                }
                string fill = StrOf(p, "fill", "none"); int wd = sz.Width, ht = sz.Height;
                if (fill == "x" || fill == "both" || wd > fw - 2 * padx) wd = Math.Max(0, fw - 2 * padx);
                if (fill == "y" || fill == "both" || ht > fh - 2 * pady) ht = Math.Max(0, fh - 2 * pady);
                Rectangle area = new Rectangle(fx + padx, fy + pady, Math.Max(0, fw - 2 * padx), Math.Max(0, fh - 2 * pady));
                Point pt = Anchored(StrOf(p, "anchor", "center"), area, wd, ht);
                w.C.SetBounds(pt.X, pt.Y, wd, ht);
            }
            // grid
            List<Widget> gs = GridSlaves();
            if (gs.Count > 0) LayoutGrid(r, gs);
            // place
            foreach (Widget w in Slaves)
            {
                if (w.Mgr != "place") continue; Dictionary<string, object> p = w.Place; Size rq = w.Req();
                double x = PadOfD(p, "x", 0) + PadOfD(p, "relx", 0) * r.Width, y = PadOfD(p, "y", 0) + PadOfD(p, "rely", 0) * r.Height;
                int wd = p.ContainsKey("width") ? (int)PadOfD(p, "width", 0) : (p.ContainsKey("relwidth") ? (int)(PadOfD(p, "relwidth", 0) * r.Width) : rq.Width);
                int ht = p.ContainsKey("height") ? (int)PadOfD(p, "height", 0) : (p.ContainsKey("relheight") ? (int)(PadOfD(p, "relheight", 0) * r.Height) : rq.Height);
                string an = StrOf(p, "anchor", "nw"); int px = (int)x, py = (int)y;
                if (an.Contains("e")) px -= wd; else if (!an.Contains("w")) px -= wd / 2;
                if (an.Contains("s")) py -= ht; else if (!an.Contains("n")) py -= ht / 2;
                w.C.SetBounds(r.X + px, r.Y + py, wd, ht);
            }
            foreach (Widget w in Slaves) { Container c = w as Container; if (c != null && w.Mgr != null) c.LayoutChildren(); }
        }
        static double PadOfD(Dictionary<string, object> d, string k, double def) { object v; if (!d.TryGetValue(k, out v) || v == null) return def; try { return Ops.ToD(v is string ? (object)double.Parse((string)v, Ops.IC) : v); } catch (Exception) { return def; } }

        // --- grid ---
        Size GridReq() { Dictionary<int, int> cols, rows; GridMetrics(GridSlaves(), out cols, out rows); int w = 0, h = 0; foreach (int v in cols.Values) w += v; foreach (int v in rows.Values) h += v; return new Size(w, h); }

        void GridMetrics(List<Widget> gs, out Dictionary<int, int> cols, out Dictionary<int, int> rows)
        {
            cols = new Dictionary<int, int>(); rows = new Dictionary<int, int>(); int maxC = -1, maxR = -1;
            foreach (Widget w in gs) { maxC = Math.Max(maxC, GI(w.Grid, "column", 0) + GI(w.Grid, "columnspan", 1) - 1); maxR = Math.Max(maxR, GI(w.Grid, "row", 0) + GI(w.Grid, "rowspan", 1) - 1); }
            for (int c = 0; c <= maxC; c++) { int m; cols[c] = ColMin.TryGetValue(c, out m) ? m : 0; }
            for (int r = 0; r <= maxR; r++) { int m; rows[r] = RowMin.TryGetValue(r, out m) ? m : 0; }
            foreach (Widget w in gs)
            {
                int c = GI(w.Grid, "column", 0), r = GI(w.Grid, "row", 0), cs = Math.Max(1, GI(w.Grid, "columnspan", 1)), rs = Math.Max(1, GI(w.Grid, "rowspan", 1));
                Size rq = w.Req(); int nw = rq.Width + 2 * GI(w.Grid, "ipadx", 0) + 2 * PadOf(w.Grid, "padx", 0), nh = rq.Height + 2 * GI(w.Grid, "ipady", 0) + 2 * PadOf(w.Grid, "pady", 0);
                if (cs == 1) cols[c] = Math.Max(cols[c], nw); else { int sum = 0; for (int i = 0; i < cs; i++) sum += cols[c + i]; if (sum < nw) cols[c + cs - 1] += nw - sum; }
                if (rs == 1) rows[r] = Math.Max(rows[r], nh); else { int sum = 0; for (int i = 0; i < rs; i++) sum += rows[r + i]; if (sum < nh) rows[r + rs - 1] += nh - sum; }
            }
        }

        void LayoutGrid(Rectangle area, List<Widget> gs)
        {
            Dictionary<int, int> cols, rows; GridMetrics(gs, out cols, out rows);
            int tw = 0, th = 0; foreach (int v in cols.Values) tw += v; foreach (int v in rows.Values) th += v;
            int totCW = 0, totRW = 0; foreach (int k in cols.Keys) { int wv; if (ColW.TryGetValue(k, out wv)) totCW += wv; } foreach (int k in rows.Keys) { int wv; if (RowW.TryGetValue(k, out wv)) totRW += wv; }
            int extraW = Math.Max(0, area.Width - tw), extraH = Math.Max(0, area.Height - th);
            Dictionary<int, int> cw = new Dictionary<int, int>(cols), rh = new Dictionary<int, int>(rows);
            if (totCW > 0) foreach (int k in new List<int>(cols.Keys)) { int wv; if (ColW.TryGetValue(k, out wv)) cw[k] = cols[k] + extraW * wv / totCW; }
            if (totRW > 0) foreach (int k in new List<int>(rows.Keys)) { int wv; if (RowW.TryGetValue(k, out wv)) rh[k] = rows[k] + extraH * wv / totRW; }
            Dictionary<int, int> cx = new Dictionary<int, int>(), ry = new Dictionary<int, int>(); int x = area.X, y = area.Y;
            for (int c = 0; c < cols.Count; c++) { cx[c] = x; x += cw[c]; } for (int r = 0; r < rows.Count; r++) { ry[r] = y; y += rh[r]; }
            foreach (Widget w in gs)
            {
                int c = GI(w.Grid, "column", 0), r = GI(w.Grid, "row", 0), cs = Math.Max(1, GI(w.Grid, "columnspan", 1)), rs = Math.Max(1, GI(w.Grid, "rowspan", 1));
                int cellW = 0, cellH = 0; for (int i = 0; i < cs; i++) cellW += cw[c + i]; for (int i = 0; i < rs; i++) cellH += rh[r + i];
                int padx = PadOf(w.Grid, "padx", 0), pady = PadOf(w.Grid, "pady", 0);
                Rectangle cell = new Rectangle(cx[c] + padx, ry[r] + pady, Math.Max(0, cellW - 2 * padx), Math.Max(0, cellH - 2 * pady));
                Size rq = w.Req(); int wd = rq.Width + 2 * GI(w.Grid, "ipadx", 0), ht = rq.Height + 2 * GI(w.Grid, "ipady", 0);
                string st = StrOf(w.Grid, "sticky", "");
                if (st.Contains("e") && st.Contains("w")) wd = cell.Width; if (st.Contains("n") && st.Contains("s")) ht = cell.Height;
                wd = Math.Min(wd, cell.Width); ht = Math.Min(ht, cell.Height);
                Point pt = Anchored(st.Replace("e", "").Length < st.Length || st.Replace("w", "").Length < st.Length || st.Contains("n") || st.Contains("s") ? st : "", cell, wd, ht);
                w.C.SetBounds(pt.X, pt.Y, wd, ht);
            }
        }

        public override object Method(string n)
        {
            switch (n)
            {
                case "pack_propagate": case "grid_propagate": return F(n, delegate(List<object> a) { if (a.Count > 0) Propagate = IsTrue(a[0]); Dirty(); return null; });
                case "columnconfigure": case "grid_columnconfigure": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { int c = (int)Ops.ToL(a[0]); if (kw.ContainsKey("weight")) ColW[c] = (int)Ops.ToL(kw["weight"]); if (kw.ContainsKey("minsize")) ColMin[c] = (int)Ops.ToL(kw["minsize"]); Dirty(); return null; });
                case "rowconfigure": case "grid_rowconfigure": return F(n, delegate(List<object> a, Dictionary<string, object> kw) { int c = (int)Ops.ToL(a[0]); if (kw.ContainsKey("weight")) RowW[c] = (int)Ops.ToL(kw["weight"]); if (kw.ContainsKey("minsize")) RowMin[c] = (int)Ops.ToL(kw["minsize"]); Dirty(); return null; });
            }
            return base.Method(n);
        }
    }
}
