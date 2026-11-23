using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MovoLang
{
    public class FileObj : IMObj, IMContext
    {
        public StreamReader R; public StreamWriter W; public string Path;
        public object GetAttr(string n)
        {
            switch (n)
            {
                case "read": return new NativeFn(n, delegate(List<object> a) { return R.ReadToEnd(); });
                case "readline": return new NativeFn(n, delegate(List<object> a) { string l = R.ReadLine(); return l == null ? "" : l + "\n"; });
                case "readlines": return new NativeFn(n, delegate(List<object> a) { List<object> r = new List<object>(); string l; while ((l = R.ReadLine()) != null) r.Add(l + "\n"); return r; });
                case "write": return new NativeFn(n, delegate(List<object> a) { W.Write(Ops.Str(a[0])); return (long)Ops.Str(a[0]).Length; });
                case "writelines": return new NativeFn(n, delegate(List<object> a) { foreach (object x in Interp.Cur.Iter(a[0])) W.Write(Ops.Str(x)); return null; });
                case "close": return new NativeFn(n, delegate(List<object> a) { Close(); return null; });
            }
            throw new MovoError("AttributeError", "file has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { throw new MovoError("AttributeError", "file is read-only"); }
        public void Close() { if (R != null) R.Dispose(); if (W != null) W.Dispose(); R = null; W = null; }
        public object Enter() { return this; }
        public void Exit() { Close(); }
    }

    public class RenCtx : IMContext
    {
        public string Path;
        public static List<RenCtx> Stack = new List<RenCtx>();
        public object Enter() { if (!File.Exists(Path) && !Directory.Exists(Path)) throw new MovoError("FileNotFoundError", "not found: " + Path); Stack.Add(this); return this; }
        public void Exit() { Stack.Remove(this); }
    }

    public class SleepObj : IMCallable, IMObj
    {
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw) { Do(a[0]); return null; }
        static void Do(object s) { Thread.Sleep((int)(Ops.ToD(s) * 1000)); }
        public object GetAttr(string n)
        {
            if (n == "wait") return new NativeFn("wait", delegate(List<object> a) { Do(a[0]); return null; });
            throw new MovoError("AttributeError", "sleep has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { }
    }

    class ObjComparer : IComparer<object> { public int Compare(object a, object b) { return Ops.Cmp(a, b); } }

    public partial class Interp
    {
        public static Interp Cur;
        public static string LibDir = Environment.GetEnvironmentVariable("MOVO_LIB") ?? (Environment.OSVersion.Platform == PlatformID.Win32NT ? @"C:\movo\library" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "movo", "library"));
        public Env Builtins = new Env(null); public Env Globals;
        public object RetVal; public int Depth;
        public List<MovoError> ExcStack = new List<MovoError>();
        public List<Stmt> MainBody; public Env MainEnv;
        public bool UsesGui; public string[] ScriptArgs = new string[0];
        Dictionary<string, object> modCache = new Dictionary<string, object>();
        public static Random Rng = new Random();
        static Dictionary<string, string> ExcParents = new Dictionary<string, string>();

        static Interp()
        {
            string[] pairs = { "ArithmeticError:Exception", "LookupError:Exception", "ValueError:Exception", "TypeError:Exception", "NameError:Exception", "AttributeError:Exception",
                "RuntimeError:Exception", "OSError:Exception", "ImportError:Exception", "SyntaxError:Exception", "AssertionError:Exception", "StopIteration:Exception", "EOFError:Exception",
                "KeyboardInterrupt:Exception", "ZeroDivisionError:ArithmeticError", "OverflowError:ArithmeticError", "KeyError:LookupError", "IndexError:LookupError", "FileNotFoundError:OSError",
                "PermissionError:OSError", "FileExistsError:OSError", "NotImplementedError:RuntimeError", "RecursionError:RuntimeError", "ModuleNotFoundError:ImportError", "UnicodeError:ValueError" };
            foreach (string s in pairs) { string[] kv = s.Split(':'); ExcParents[kv[0]] = kv[1]; }
        }

        public Interp()
        {
            Cur = this; Globals = new Env(Builtins); Globals.GlobalEnv = Globals;
            SetupBuiltins();
        }

        // ===== execution =====
        public Flow ExecBlock(List<Stmt> body, Env e)
        {
            for (int i = 0; i < body.Count; i++)
            {
                Stmt s = body[i];
                try
                {
                    Flow f = s.Exec(this, e);
                    if (f != Flow.Normal) return f;
                }
                catch (MovoError ex) { if (ex.Line == 0) ex.Line = s.Line; throw; }
                catch (MovoExit) { throw; }
                catch (ThreadAbortException) { throw; }
                catch (InvalidCastException ex) { MovoError me = new MovoError("TypeError", "wrong data type"); me.Line = s.Line; throw me; }
                catch (DivideByZeroException) { MovoError me = new MovoError("ZeroDivisionError", "division by zero"); me.Line = s.Line; throw me; }
                catch (OverflowException) { MovoError me = new MovoError("OverflowError", "number too large"); me.Line = s.Line; throw me; }
                catch (FileNotFoundException ex) { MovoError me = new MovoError("FileNotFoundError", "not found: " + ex.FileName); me.Line = s.Line; throw me; }
                catch (DirectoryNotFoundException ex) { MovoError me = new MovoError("FileNotFoundError", ex.Message); me.Line = s.Line; throw me; }
                catch (UnauthorizedAccessException ex) { MovoError me = new MovoError("PermissionError", ex.Message); me.Line = s.Line; throw me; }
                catch (IOException ex) { MovoError me = new MovoError("OSError", ex.Message); me.Line = s.Line; throw me; }
                catch (ArgumentOutOfRangeException) { MovoError me = new MovoError("IndexError", "index out of range"); me.Line = s.Line; throw me; }
                catch (ArgumentException ex) { MovoError me = new MovoError("TypeError", ex.Message); me.Line = s.Line; throw me; }
                catch (IndexOutOfRangeException) { MovoError me = new MovoError("IndexError", "index out of range"); me.Line = s.Line; throw me; }
                catch (FormatException ex) { MovoError me = new MovoError("ValueError", ex.Message); me.Line = s.Line; throw me; }
            }
            return Flow.Normal;
        }

        public MovoError Wrap(Exception ex)
        {
            MovoError me = ex as MovoError; if (me != null) return me;
            if (ex is MovoExit || ex is ThreadAbortException) return null;
            if (ex is InvalidCastException) return new MovoError("TypeError", "wrong data type");
            return new MovoError("Exception", ex.Message);
        }

        List<string> ExcChain(MException ex)
        {
            List<string> r = new List<string>();
            if (ex.Inst != null)
            {
                MClass c = ex.Inst.Cls;
                while (c != null) { r.Add(c.Name); MExcType bt = c.Base as MExcType; if (bt != null) { AddBuiltinChain(r, bt.Name); } c = c.Base as MClass; }
            }
            else AddBuiltinChain(r, ex.Type);
            r.Add("Exception");
            return r;
        }
        static void AddBuiltinChain(List<string> r, string n) { while (n != null) { r.Add(n); string p; if (ExcParents.TryGetValue(n, out p)) n = p; else n = null; } }

        public bool ExcMatches(MException ex, object t)
        {
            MTuple tup = t as MTuple;
            if (tup != null) { foreach (object x in tup.Items) if (ExcMatches(ex, x)) return true; return false; }
            string name = null;
            MExcType et = t as MExcType; if (et != null) name = et.Name;
            MClass c = t as MClass; if (c != null) name = c.Name;
            if (name == null) return false;
            return ExcChain(ex).Contains(name);
        }

        public void RunProgram(List<Stmt> prog)
        {
            ExecBlock(prog, Globals);
            if (MainBody != null) ExecBlock(MainBody, MainEnv);
        }

        // ===== calls =====
        public object Call(object f, List<object> a, Dictionary<string, object> kw)
        {
            IMCallable c = f as IMCallable;
            if (c != null) return c.Call(this, a, kw);
            MInstance inst = f as MInstance;
            if (inst != null && inst.Cls.Has("__call__")) return Call(new MBound(inst, inst.Cls.Find("__call__")), a, kw);
            throw new MovoError("TypeError", "'" + Ops.TypeName(f) + "' is not callable");
        }
        public object Call0(object f) { return Call(f, new List<object>(), null); }
        public object Call1(object f, object x) { return Call(f, new List<object> { x }, null); }

        public object MakeSuper(Env e)
        {
            Env c = e; while (c != null && c.Fn == null) c = c.Parent;
            if (c == null || c.Fn.Owner == null || c.Fn.Params.Count == 0) throw new MovoError("RuntimeError", "super() works only inside a method");
            MSuper s = new MSuper(); s.Self = c.Vars[c.Fn.Params[0].Name]; s.Start = c.Fn.Owner.Base as MClass; s.Owner = c.Fn.Owner; return s;
        }

        public void Gui_Press(object key, Action a) { UsesGui = true; MovoGui.Press(key, a); }

        // ===== attributes =====
        public object GetAttr(object o, string n)
        {
            IMObj io = o as IMObj; if (io != null) return io.GetAttr(n);
            MInstance inst = o as MInstance;
            if (inst != null)
            {
                object v; if (inst.Fields.TryGetValue(n, out v)) return v;
                v = inst.Cls.Find(n);
                if (v != null || inst.Cls.Has(n)) { if (v is MFunc) return new MBound(inst, v); return v; }
                if (n == "__class__") return inst.Cls;
                throw new MovoError("AttributeError", "'" + inst.Cls.Name + "' has no attribute '" + n + "'");
            }
            MClass cls = o as MClass;
            if (cls != null) { if (cls.Has(n)) return cls.Find(n); if (n == "__name__") return cls.Name; throw new MovoError("AttributeError", "class '" + cls.Name + "' has no '" + n + "'"); }
            MSuper sp = o as MSuper;
            if (sp != null)
            {
                if (sp.Start != null && sp.Start.Has(n)) { object f = sp.Start.Find(n); return f is MFunc ? (object)new MBound(sp.Self, f) : f; }
                if (n == "__init__") return new NativeFn("__init__", delegate(List<object> a) { MInstance si = sp.Self as MInstance; if (si != null) si.Fields["args"] = new MTuple(a.ToArray()); return null; });
                throw new MovoError("AttributeError", "super has no '" + n + "'");
            }
            string s = o as string; if (s != null) return StrMethod(s, n);
            List<object> l = o as List<object>; if (l != null) return ListMethod(l, n);
            Dictionary<object, object> d = o as Dictionary<object, object>; if (d != null) return DictMethod(d, n);
            MTuple t = o as MTuple;
            if (t != null)
            {
                if (n == "count") return new NativeFn(n, delegate(List<object> a) { long c = 0; foreach (object x in t.Items) if (Ops.Eq(x, a[0])) c++; return c; });
                if (n == "index") return new NativeFn(n, delegate(List<object> a) { for (int i = 0; i < t.Items.Length; i++) if (Ops.Eq(t.Items[i], a[0])) return (long)i; throw new MovoError("ValueError", "not in tuple"); });
            }
            MException ex = o as MException;
            if (ex != null) { if (n == "args") return ex.Args; if (n == "message") return ex.Message; }
            if (o is double && n == "is_integer") { double dv = (double)o; return new NativeFn(n, delegate(List<object> a) { return dv == Math.Floor(dv); }); }
            throw new MovoError("AttributeError", "'" + Ops.TypeName(o) + "' has no attribute '" + n + "'");
        }

        public void SetAttr(object o, string n, object v)
        {
            IMObj io = o as IMObj; if (io != null) { io.SetAttr(n, v); return; }
            MInstance inst = o as MInstance; if (inst != null) { inst.Fields[n] = v; return; }
            MClass c = o as MClass; if (c != null) { c.Members[n] = v; return; }
            throw new MovoError("AttributeError", "cannot set attribute '" + n + "' on " + Ops.TypeName(o));
        }

        // ===== indexing =====
        public static List<int> SliceIdx(int len, MSlice s)
        {
            long step = s.C == null ? 1 : Ops.ToL(s.C);
            if (step == 0) throw new MovoError("ValueError", "slice step cannot be 0");
            long start, stop;
            if (step > 0)
            {
                start = s.A == null ? 0 : Ops.ToL(s.A); stop = s.B == null ? len : Ops.ToL(s.B);
                if (start < 0) start += len; if (stop < 0) stop += len;
                start = Math.Max(0, Math.Min(start, len)); stop = Math.Max(0, Math.Min(stop, len));
            }
            else
            {
                start = s.A == null ? len - 1 : Ops.ToL(s.A); stop = s.B == null ? -1 : Ops.ToL(s.B);
                if (s.A != null && start < 0) start += len; if (s.B != null && stop < 0) stop += len;
                start = Math.Max(-1, Math.Min(start, len - 1)); stop = Math.Max(-1, Math.Min(stop, len - 1));
            }
            List<int> r = new List<int>();
            if (step > 0) for (long i = start; i < stop; i += step) r.Add((int)i); else for (long i = start; i > stop; i += step) r.Add((int)i);
            return r;
        }

        public object GetItem(object o, object k)
        {
            MSlice sl = k as MSlice;
            string s = o as string;
            if (s != null)
            {
                if (sl != null) { StringBuilder sb = new StringBuilder(); foreach (int i in SliceIdx(s.Length, sl)) sb.Append(s[i]); return sb.ToString(); }
                long i2 = Ops.ToL(k); if (i2 < 0) i2 += s.Length; if (i2 < 0 || i2 >= s.Length) throw new MovoError("IndexError", "string index out of range");
                return s[(int)i2].ToString();
            }
            List<object> l = o as List<object>;
            if (l != null)
            {
                if (sl != null) { List<object> r = new List<object>(); foreach (int i in SliceIdx(l.Count, sl)) r.Add(l[i]); return r; }
                long i2 = Ops.ToL(k); if (i2 < 0) i2 += l.Count; if (i2 < 0 || i2 >= l.Count) throw new MovoError("IndexError", "list index out of range");
                return l[(int)i2];
            }
            MTuple t = o as MTuple;
            if (t != null)
            {
                if (sl != null) { List<object> r = new List<object>(); foreach (int i in SliceIdx(t.Items.Length, sl)) r.Add(t.Items[i]); return new MTuple(r); }
                long i2 = Ops.ToL(k); if (i2 < 0) i2 += t.Items.Length; if (i2 < 0 || i2 >= t.Items.Length) throw new MovoError("IndexError", "tuple index out of range");
                return t.Items[(int)i2];
            }
            Dictionary<object, object> d = o as Dictionary<object, object>;
            if (d != null) { object v; if (d.TryGetValue(k, out v)) return v; throw new MovoError("KeyError", Ops.Repr(k)); }
            IMIndexable ix = o as IMIndexable; if (ix != null) return ix.GetItem(k);
            MInstance inst = o as MInstance;
            if (inst != null && inst.Cls.Has("__getitem__")) return Call(new MBound(inst, inst.Cls.Find("__getitem__")), new List<object> { k }, null);
            throw new MovoError("TypeError", "'" + Ops.TypeName(o) + "' does not support indexing");
        }

        public void SetItem(object o, object k, object v)
        {
            List<object> l = o as List<object>;
            if (l != null) { long i = Ops.ToL(k); if (i < 0) i += l.Count; if (i < 0 || i >= l.Count) throw new MovoError("IndexError", "list index out of range"); l[(int)i] = v; return; }
            Dictionary<object, object> d = o as Dictionary<object, object>; if (d != null) { d[k] = v; return; }
            IMIndexable ix = o as IMIndexable; if (ix != null) { ix.SetItem(k, v); return; }
            MInstance inst = o as MInstance;
            if (inst != null && inst.Cls.Has("__setitem__")) { Call(new MBound(inst, inst.Cls.Find("__setitem__")), new List<object> { k, v }, null); return; }
            throw new MovoError("TypeError", "'" + Ops.TypeName(o) + "' does not support item assignment");
        }

        public IEnumerable<object> Iter(object o)
        {
            List<object> l = o as List<object>; if (l != null) return l;
            string s = o as string; if (s != null) { List<object> r = new List<object>(); foreach (char c in s) r.Add(c.ToString()); return r; }
            MTuple t = o as MTuple; if (t != null) return t.Items;
            Dictionary<object, object> d = o as Dictionary<object, object>; if (d != null) return new List<object>(d.Keys);
            MRecord rc = o as MRecord; if (rc != null) { List<object> rk = new List<object>(); foreach (string k in rc.Keys) rk.Add(k); return rk; }
            FileObj f = o as FileObj; if (f != null) { List<object> r = new List<object>(); string ln; while ((ln = f.R.ReadLine()) != null) r.Add(ln + "\n"); return r; }
            MInstance inst = o as MInstance;
            if (inst != null && inst.Cls.Has("__iter__")) return Iter(Call(new MBound(inst, inst.Cls.Find("__iter__")), new List<object>(), null));
            throw new MovoError("TypeError", "'" + Ops.TypeName(o) + "' is not iterable");
        }

        // ===== built-in type methods =====
        static List<object> Parts(string[] arr) { List<object> r = new List<object>(); foreach (string x in arr) r.Add(x); return r; }

        object StrMethod(string s, string n)
        {
            switch (n)
            {
                case "upper": return new NativeFn(n, delegate(List<object> a) { return s.ToUpperInvariant(); });
                case "lower": return new NativeFn(n, delegate(List<object> a) { return s.ToLowerInvariant(); });
                case "strip": return new NativeFn(n, delegate(List<object> a) { return a.Count > 0 ? s.Trim(Ops.Str(a[0]).ToCharArray()) : s.Trim(); });
                case "lstrip": return new NativeFn(n, delegate(List<object> a) { return a.Count > 0 ? s.TrimStart(Ops.Str(a[0]).ToCharArray()) : s.TrimStart(); });
                case "rstrip": return new NativeFn(n, delegate(List<object> a) { return a.Count > 0 ? s.TrimEnd(Ops.Str(a[0]).ToCharArray()) : s.TrimEnd(); });
                case "split": return new NativeFn(n, delegate(List<object> a)
                {
                    if (a.Count == 0 || a[0] == null) return Parts(s.Split(new char[0], StringSplitOptions.RemoveEmptyEntries));
                    string sep = Ops.Str(a[0]); if (sep.Length == 0) throw new MovoError("ValueError", "empty separator");
                    if (a.Count > 1) return Parts(s.Split(new string[] { sep }, (int)Ops.ToL(a[1]) + 1, StringSplitOptions.None));
                    return Parts(s.Split(new string[] { sep }, StringSplitOptions.None));
                });
                case "splitlines": return new NativeFn(n, delegate(List<object> a) { return Parts(s.Replace("\r\n", "\n").Split('\n')); });
                case "join": return new NativeFn(n, delegate(List<object> a) { List<string> p = new List<string>(); foreach (object x in Iter(a[0])) { if (!(x is string)) throw new MovoError("TypeError", "join expects strings"); p.Add((string)x); } return string.Join(s, p.ToArray()); });
                case "replace": return new NativeFn(n, delegate(List<object> a) { string f = Ops.Str(a[0]); if (f.Length == 0) return s; return s.Replace(f, Ops.Str(a[1])); });
                case "startswith": return new NativeFn(n, delegate(List<object> a) { MTuple t = a[0] as MTuple; if (t != null) { foreach (object x in t.Items) if (s.StartsWith((string)x, StringComparison.Ordinal)) return true; return false; } return s.StartsWith(Ops.Str(a[0]), StringComparison.Ordinal); });
                case "endswith": return new NativeFn(n, delegate(List<object> a) { MTuple t = a[0] as MTuple; if (t != null) { foreach (object x in t.Items) if (s.EndsWith((string)x, StringComparison.Ordinal)) return true; return false; } return s.EndsWith(Ops.Str(a[0]), StringComparison.Ordinal); });
                case "find": return new NativeFn(n, delegate(List<object> a) { return (long)s.IndexOf(Ops.Str(a[0]), StringComparison.Ordinal); });
                case "rfind": return new NativeFn(n, delegate(List<object> a) { return (long)s.LastIndexOf(Ops.Str(a[0]), StringComparison.Ordinal); });
                case "index": return new NativeFn(n, delegate(List<object> a) { int i = s.IndexOf(Ops.Str(a[0]), StringComparison.Ordinal); if (i < 0) throw new MovoError("ValueError", "substring not found"); return (long)i; });
                case "count": return new NativeFn(n, delegate(List<object> a) { string f = Ops.Str(a[0]); if (f.Length == 0) return (long)s.Length + 1; long c = 0; int i = 0; while ((i = s.IndexOf(f, i, StringComparison.Ordinal)) >= 0) { c++; i += f.Length; } return c; });
                case "title": return new NativeFn(n, delegate(List<object> a) { return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()); });
                case "capitalize": return new NativeFn(n, delegate(List<object> a) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant(); });
                case "swapcase": return new NativeFn(n, delegate(List<object> a) { StringBuilder sb = new StringBuilder(); foreach (char c in s) sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)); return sb.ToString(); });
                case "isdigit": return new NativeFn(n, delegate(List<object> a) { if (s.Length == 0) return false; foreach (char c in s) if (!char.IsDigit(c)) return false; return true; });
                case "isalpha": return new NativeFn(n, delegate(List<object> a) { if (s.Length == 0) return false; foreach (char c in s) if (!char.IsLetter(c)) return false; return true; });
                case "isalnum": return new NativeFn(n, delegate(List<object> a) { if (s.Length == 0) return false; foreach (char c in s) if (!char.IsLetterOrDigit(c)) return false; return true; });
                case "isspace": return new NativeFn(n, delegate(List<object> a) { if (s.Length == 0) return false; foreach (char c in s) if (!char.IsWhiteSpace(c)) return false; return true; });
                case "isupper": return new NativeFn(n, delegate(List<object> a) { return s.Length > 0 && s == s.ToUpperInvariant() && s != s.ToLowerInvariant(); });
                case "islower": return new NativeFn(n, delegate(List<object> a) { return s.Length > 0 && s == s.ToLowerInvariant() && s != s.ToUpperInvariant(); });
                case "zfill": return new NativeFn(n, delegate(List<object> a) { int w = (int)Ops.ToL(a[0]); bool neg = s.StartsWith("-"); string b = neg ? s.Substring(1) : s; return (neg ? "-" : "") + b.PadLeft(w - (neg ? 1 : 0), '0'); });
                case "ljust": return new NativeFn(n, delegate(List<object> a) { return s.PadRight((int)Ops.ToL(a[0]), a.Count > 1 ? Ops.Str(a[1])[0] : ' '); });
                case "rjust": return new NativeFn(n, delegate(List<object> a) { return s.PadLeft((int)Ops.ToL(a[0]), a.Count > 1 ? Ops.Str(a[1])[0] : ' '); });
                case "center": return new NativeFn(n, delegate(List<object> a) { return Ops.FormatValue(s, (a.Count > 1 ? Ops.Str(a[1]) : " ") + "^" + Ops.ToL(a[0])); });
                case "format": return new NativeFn(n, delegate(List<object> a, Dictionary<string, object> kw) { return StrFormat(s, a, kw); });
            }
            throw new MovoError("AttributeError", "str has no method '" + n + "'");
        }

        string StrFormat(string fmt, List<object> a, Dictionary<string, object> kw)
        {
            StringBuilder sb = new StringBuilder(); int auto = 0;
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{') { sb.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}') { sb.Append('}'); i++; continue; }
                if (c != '{') { sb.Append(c); continue; }
                int j = fmt.IndexOf('}', i); if (j < 0) throw new MovoError("ValueError", "format: missing }");
                string field = fmt.Substring(i + 1, j - i - 1); string spec = null; int ci = field.IndexOf(':');
                if (ci >= 0) { spec = field.Substring(ci + 1); field = field.Substring(0, ci); }
                char conv = '\0'; int bi = field.IndexOf('!'); if (bi >= 0) { conv = field[bi + 1]; field = field.Substring(0, bi); }
                string rest = null; int di = field.IndexOfAny(new char[] { '.', '[' });
                if (di >= 0) { rest = field.Substring(di); field = field.Substring(0, di); }
                object v;
                if (field.Length == 0) { if (auto >= a.Count) throw new MovoError("IndexError", "format: not enough arguments"); v = a[auto++]; }
                else if (char.IsDigit(field[0])) { int ix = int.Parse(field); if (ix >= a.Count) throw new MovoError("IndexError", "format: not enough arguments"); v = a[ix]; }
                else { if (!kw.TryGetValue(field, out v)) throw new MovoError("KeyError", field); }
                if (rest != null && rest.StartsWith(".")) v = GetAttr(v, rest.Substring(1));
                if (conv == 'r') v = Ops.Repr(v); else if (conv == 's') v = Ops.Str(v);
                sb.Append(spec == null ? Ops.Str(v) : Ops.FormatValue(v, spec));
                i = j;
            }
            return sb.ToString();
        }

        public List<object> SortList(IEnumerable<object> src, object keyFn, bool reverse)
        {
            List<KeyValuePair<object, object>> pairs = new List<KeyValuePair<object, object>>();
            foreach (object x in src) pairs.Add(new KeyValuePair<object, object>(keyFn == null ? x : Call1(keyFn, x), x));
            IEnumerable<KeyValuePair<object, object>> sorted = reverse ? pairs.OrderByDescending(kv => kv.Key, new ObjComparer()) : pairs.OrderBy(kv => kv.Key, new ObjComparer());
            List<object> r = new List<object>(); foreach (KeyValuePair<object, object> kv in sorted) r.Add(kv.Value); return r;
        }

        object ListMethod(List<object> l, string n)
        {
            switch (n)
            {
                case "append": return new NativeFn(n, delegate(List<object> a) { l.Add(a[0]); return null; });
                case "extend": return new NativeFn(n, delegate(List<object> a) { l.AddRange(new List<object>(Iter(a[0]))); return null; });
                case "insert": return new NativeFn(n, delegate(List<object> a) { long i = Ops.ToL(a[0]); if (i < 0) i += l.Count; i = Math.Max(0, Math.Min(i, l.Count)); l.Insert((int)i, a[1]); return null; });
                case "pop": return new NativeFn(n, delegate(List<object> a) { if (l.Count == 0) throw new MovoError("IndexError", "pop from an empty list"); long i = a.Count > 0 ? Ops.ToL(a[0]) : l.Count - 1; if (i < 0) i += l.Count; if (i < 0 || i >= l.Count) throw new MovoError("IndexError", "index out of range"); object v = l[(int)i]; l.RemoveAt((int)i); return v; });
                case "remove": return new NativeFn(n, delegate(List<object> a) { for (int i = 0; i < l.Count; i++) if (Ops.Eq(l[i], a[0])) { l.RemoveAt(i); return null; } throw new MovoError("ValueError", "item is not in list"); });
                case "index": return new NativeFn(n, delegate(List<object> a) { for (int i = 0; i < l.Count; i++) if (Ops.Eq(l[i], a[0])) return (long)i; throw new MovoError("ValueError", "item is not in list"); });
                case "count": return new NativeFn(n, delegate(List<object> a) { long c = 0; foreach (object x in l) if (Ops.Eq(x, a[0])) c++; return c; });
                case "reverse": return new NativeFn(n, delegate(List<object> a) { l.Reverse(); return null; });
                case "copy": return new NativeFn(n, delegate(List<object> a) { return new List<object>(l); });
                case "clear": return new NativeFn(n, delegate(List<object> a) { l.Clear(); return null; });
                case "sort": return new NativeFn(n, delegate(List<object> a, Dictionary<string, object> kw)
                {
                    object key = null; bool rev = false; kw.TryGetValue("key", out key); object rv; if (kw.TryGetValue("reverse", out rv)) rev = Ops.Truthy(rv);
                    List<object> r = SortList(l, key, rev); l.Clear(); l.AddRange(r); return null;
                });
            }
            throw new MovoError("AttributeError", "list has no method '" + n + "'");
        }

        object DictMethod(Dictionary<object, object> d, string n)
        {
            switch (n)
            {
                case "get": return new NativeFn(n, delegate(List<object> a) { object v; return d.TryGetValue(a[0], out v) ? v : (a.Count > 1 ? a[1] : null); });
                case "keys": return new NativeFn(n, delegate(List<object> a) { return new List<object>(d.Keys); });
                case "values": return new NativeFn(n, delegate(List<object> a) { return new List<object>(d.Values); });
                case "items": return new NativeFn(n, delegate(List<object> a) { List<object> r = new List<object>(); foreach (KeyValuePair<object, object> kv in d) r.Add(new MTuple(new object[] { kv.Key, kv.Value })); return r; });
                case "pop": return new NativeFn(n, delegate(List<object> a) { object v; if (d.TryGetValue(a[0], out v)) { d.Remove(a[0]); return v; } if (a.Count > 1) return a[1]; throw new MovoError("KeyError", Ops.Repr(a[0])); });
                case "update": return new NativeFn(n, delegate(List<object> a, Dictionary<string, object> kw) { if (a.Count > 0) { Dictionary<object, object> o = a[0] as Dictionary<object, object>; if (o != null) foreach (KeyValuePair<object, object> kv in o) d[kv.Key] = kv.Value; } foreach (KeyValuePair<string, object> kv in kw) d[kv.Key] = kv.Value; return null; });
                case "setdefault": return new NativeFn(n, delegate(List<object> a) { object v; if (d.TryGetValue(a[0], out v)) return v; d[a[0]] = a.Count > 1 ? a[1] : null; return d[a[0]]; });
                case "copy": return new NativeFn(n, delegate(List<object> a) { return new Dictionary<object, object>(d, new MComparer()); });
                case "clear": return new NativeFn(n, delegate(List<object> a) { d.Clear(); return null; });
            }
            throw new MovoError("AttributeError", "dict has no method '" + n + "'");
        }
    }
}
