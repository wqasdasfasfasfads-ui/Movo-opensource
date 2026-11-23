using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MovoLang
{
    public partial class Interp
    {
        void Def(string name, Func<List<object>, object> f) { Builtins.Vars[name] = new NativeFn(name, f); }
        void DefKw(string name, Func<List<object>, Dictionary<string, object>, object> f) { Builtins.Vars[name] = new NativeFn(name, f); }
        static object Kw(Dictionary<string, object> kw, string k) { object v; return kw.TryGetValue(k, out v) ? v : null; }

        public static TextWriter Out = Console.Out;

        void SetupBuiltins()
        {
            DefKw("print", delegate(List<object> a, Dictionary<string, object> kw)
            {
                object sepo = Kw(kw, "sep"), endo = Kw(kw, "end");
                string sep = sepo == null ? " " : Ops.Str(sepo), end = endo == null ? "\n" : Ops.Str(endo);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < a.Count; i++) { if (i > 0) sb.Append(sep); sb.Append(Ops.Str(a[i])); }
                sb.Append(end); Console.Out.Write(sb.ToString()); Console.Out.Flush(); return null;
            });
            Def("input", delegate(List<object> a)
            {
                if (a.Count > 0) { Console.Out.Write(Ops.Str(a[0])); Console.Out.Flush(); }
                string l = Console.In.ReadLine(); if (l == null) throw new MovoError("EOFError", "end of input"); return l;
            });
            Def("len", delegate(List<object> a)
            {
                object o = a[0];
                if (o is string) return (long)((string)o).Length; if (o is List<object>) return (long)((List<object>)o).Count;
                if (o is Dictionary<object, object>) return (long)((Dictionary<object, object>)o).Count; if (o is MTuple) return (long)((MTuple)o).Items.Length;
                if (o is MRecord) return (long)((MRecord)o).Keys.Count;
                MInstance inst = o as MInstance; if (inst != null && inst.Cls.Has("__len__")) return Call(new MBound(inst, inst.Cls.Find("__len__")), new List<object>(), null);
                throw new MovoError("TypeError", "object of type '" + Ops.TypeName(o) + "' has no len()");
            });
            Def("range", delegate(List<object> a)
            {
                long st = 0, en, step = 1;
                if (a.Count == 1) en = Ops.ToL(a[0]); else { st = Ops.ToL(a[0]); en = Ops.ToL(a[1]); if (a.Count > 2) step = Ops.ToL(a[2]); }
                if (step == 0) throw new MovoError("ValueError", "range step cannot be 0");
                List<object> r = new List<object>();
                if (step > 0) for (long i = st; i < en; i += step) r.Add(i); else for (long i = st; i > en; i += step) r.Add(i);
                return r;
            });
            Builtins.Vars["int"] = new MBuiltinType("int", delegate(List<object> a)
            {
                if (a.Count == 0) return 0L; object o = a[0];
                if (o is long) return o; if (o is bool) return ((bool)o) ? 1L : 0L;
                if (o is double) { double d = (double)o; if (double.IsNaN(d) || double.IsInfinity(d)) throw new MovoError("ValueError", "cannot convert to int"); return (long)Math.Truncate(d); }
                string s = o as string;
                if (s != null)
                {
                    long r; int bs = a.Count > 1 ? (int)Ops.ToL(a[1]) : 10; string t = s.Trim().Replace("_", "");
                    if (bs == 10) { if (long.TryParse(t, NumberStyles.AllowLeadingSign, Ops.IC, out r)) return r; }
                    else { try { return Convert.ToInt64(t, bs); } catch (Exception) { } }
                    throw new MovoError("ValueError", "invalid integer: " + Ops.Repr(s));
                }
                throw new MovoError("TypeError", "int() does not accept '" + Ops.TypeName(o) + "'");
            });
            Builtins.Vars["float"] = new MBuiltinType("float", delegate(List<object> a)
            {
                if (a.Count == 0) return 0.0; object o = a[0];
                if (Ops.IsNum(o)) return Ops.ToD(o);
                string s = o as string;
                if (s != null)
                {
                    double r; string t = s.Trim().Replace("_", "").Replace(",", ".");
                    if (t.ToLowerInvariant() == "inf" || t.ToLowerInvariant() == "infinity") return double.PositiveInfinity;
                    if (t.ToLowerInvariant() == "nan") return double.NaN;
                    if (!t.Contains(",") && double.TryParse(t, NumberStyles.Float, Ops.IC, out r)) return r;
                    throw new MovoError("ValueError", "invalid number: " + Ops.Repr(s));
                }
                throw new MovoError("TypeError", "float() does not accept '" + Ops.TypeName(o) + "'");
            });
            Builtins.Vars["str"] = new MBuiltinType("str", delegate(List<object> a) { return a.Count == 0 ? "" : Ops.Str(a[0]); });
            Builtins.Vars["bool"] = new MBuiltinType("bool", delegate(List<object> a) { return a.Count > 0 && Ops.Truthy(a[0]); });
            Builtins.Vars["list"] = new MBuiltinType("list", delegate(List<object> a) { return a.Count == 0 ? new List<object>() : new List<object>(Iter(a[0])); });
            Builtins.Vars["tuple"] = new MBuiltinType("tuple", delegate(List<object> a) { return a.Count == 0 ? new MTuple(new object[0]) : new MTuple(new List<object>(Iter(a[0]))); });
            Builtins.Vars["dict"] = new NativeFn("dict", delegate(List<object> a, Dictionary<string, object> kw)
            {
                Dictionary<object, object> d = new Dictionary<object, object>(new MComparer());
                if (a.Count > 0)
                {
                    Dictionary<object, object> o = a[0] as Dictionary<object, object>;
                    if (o != null) foreach (KeyValuePair<object, object> kv in o) d[kv.Key] = kv.Value;
                    else foreach (object pr in Iter(a[0])) { List<object> p = new List<object>(Iter(pr)); d[p[0]] = p[1]; }
                }
                foreach (KeyValuePair<string, object> kv in kw) d[kv.Key] = kv.Value; return d;
            });
            Builtins.Vars["dict"] = new MBuiltinType("dict", delegate(List<object> a)
            {
                Dictionary<object, object> d = new Dictionary<object, object>(new MComparer());
                if (a.Count > 0)
                {
                    Dictionary<object, object> o = a[0] as Dictionary<object, object>;
                    if (o != null) foreach (KeyValuePair<object, object> kv in o) d[kv.Key] = kv.Value;
                    else foreach (object pr in Iter(a[0])) { List<object> p = new List<object>(Iter(pr)); d[p[0]] = p[1]; }
                }
                return d;
            });
            Def("repr", delegate(List<object> a) { return Ops.Repr(a[0]); });
            Def("abs", delegate(List<object> a) { object o = a[0]; if (o is long) return Math.Abs((long)o); if (o is double) return Math.Abs((double)o); return Math.Abs(Ops.ToL(o)); });
            Def("pow", delegate(List<object> a) { return Ops.Pow(a[0], a[1]); });
            Def("divmod", delegate(List<object> a) { return new MTuple(new object[] { Ops.FloorDiv(a[0], a[1]), Ops.Mod(a[0], a[1]) }); });
            Def("round", delegate(List<object> a)
            {
                if (a.Count == 1 || a[1] == null) { return (long)Math.Round(Ops.ToD(a[0]), MidpointRounding.ToEven); }
                int nd = (int)Ops.ToL(a[1]); if (a[0] is long) return a[0];
                return Math.Round(Ops.ToD(a[0]), Math.Max(0, Math.Min(15, nd)), MidpointRounding.ToEven);
            });
            Def("chr", delegate(List<object> a) { return char.ConvertFromUtf32((int)Ops.ToL(a[0])); });
            Def("ord", delegate(List<object> a) { return (long)char.ConvertToUtf32((string)a[0], 0); });
            Def("hex", delegate(List<object> a) { return "0x" + Convert.ToString(Ops.ToL(a[0]), 16); });
            Def("bin", delegate(List<object> a) { return "0b" + Convert.ToString(Ops.ToL(a[0]), 2); });
            Def("any", delegate(List<object> a) { foreach (object x in Iter(a[0])) if (Ops.Truthy(x)) return true; return false; });
            Def("all", delegate(List<object> a) { foreach (object x in Iter(a[0])) if (!Ops.Truthy(x)) return false; return true; });
            Def("callable", delegate(List<object> a) { return a[0] is IMCallable; });
            Def("format", delegate(List<object> a) { return Ops.FormatValue(a[0], a.Count > 1 ? Ops.Str(a[1]) : ""); });
            DefKw("sum", delegate(List<object> a, Dictionary<string, object> kw)
            {
                object acc = a.Count > 1 ? a[1] : 0L; foreach (object x in Iter(a[0])) acc = Ops.Add(acc, x); return acc;
            });
            DefKw("min", delegate(List<object> a, Dictionary<string, object> kw) { return MinMax(a, kw, -1); });
            DefKw("max", delegate(List<object> a, Dictionary<string, object> kw) { return MinMax(a, kw, 1); });
            DefKw("sorted", delegate(List<object> a, Dictionary<string, object> kw) { return SortList(Iter(a[0]), Kw(kw, "key"), Ops.Truthy(Kw(kw, "reverse"))); });
            Def("reversed", delegate(List<object> a) { List<object> r = new List<object>(Iter(a[0])); r.Reverse(); return r; });
            DefKw("enumerate", delegate(List<object> a, Dictionary<string, object> kw)
            {
                long st = a.Count > 1 ? Ops.ToL(a[1]) : (kw.ContainsKey("start") ? Ops.ToL(kw["start"]) : 0); List<object> r = new List<object>();
                foreach (object x in Iter(a[0])) r.Add(new MTuple(new object[] { st++, x })); return r;
            });
            Def("zip", delegate(List<object> a)
            {
                List<List<object>> ls = new List<List<object>>(); int n = int.MaxValue;
                foreach (object x in a) { List<object> l = new List<object>(Iter(x)); ls.Add(l); n = Math.Min(n, l.Count); }
                List<object> r = new List<object>(); if (ls.Count == 0) return r;
                for (int i = 0; i < n; i++) { object[] t = new object[ls.Count]; for (int j = 0; j < ls.Count; j++) t[j] = ls[j][i]; r.Add(new MTuple(t)); }
                return r;
            });
            Def("map", delegate(List<object> a) { List<object> r = new List<object>(); foreach (object x in Iter(a[1])) r.Add(Call1(a[0], x)); return r; });
            Def("filter", delegate(List<object> a) { List<object> r = new List<object>(); foreach (object x in Iter(a[1])) if (a[0] == null ? Ops.Truthy(x) : Ops.Truthy(Call1(a[0], x))) r.Add(x); return r; });
            Def("type", delegate(List<object> a)
            {
                string n = Ops.TypeName(a[0]); object b; if (a[0] is MInstance) return ((MInstance)a[0]).Cls;
                if (Builtins.Vars.TryGetValue(n, out b)) return b; return n;
            });
            Def("isinstance", delegate(List<object> a) { return IsInstance(a[0], a[1]); });
            Def("hasattr", delegate(List<object> a) { try { GetAttr(a[0], (string)a[1]); return true; } catch (MovoError) { return false; } });
            Def("getattr", delegate(List<object> a) { try { return GetAttr(a[0], (string)a[1]); } catch (MovoError) { if (a.Count > 2) return a[2]; throw; } });
            Def("setattr", delegate(List<object> a) { SetAttr(a[0], (string)a[1], a[2]); return null; });
            Def("hidden_input", delegate(List<object> a) { return AskHiddenStatic(a.Count > 0 ? Ops.Str(a[0]) : ""); });
            Def("exit", delegate(List<object> a) { throw new MovoExit(a.Count > 0 && a[0] is long ? (int)(long)a[0] : 0); });
            Builtins.Vars["quit"] = Builtins.Vars["exit"];
            DefKw("open", delegate(List<object> a, Dictionary<string, object> kw)
            {
                string path = Ops.Str(a[0]); string mode = a.Count > 1 ? Ops.Str(a[1]) : (kw.ContainsKey("mode") ? Ops.Str(kw["mode"]) : "r");
                FileObj f = new FileObj(); f.Path = path; UTF8Encoding enc = new UTF8Encoding(false);
                if (mode.StartsWith("r")) { if (!File.Exists(path)) throw new MovoError("FileNotFoundError", "file not found: " + path); f.R = new StreamReader(path, Encoding.UTF8); }
                else if (mode.StartsWith("a")) f.W = new StreamWriter(path, true, enc);
                else f.W = new StreamWriter(path, false, enc);
                return f;
            });
            Builtins.Vars["None"] = null;
            foreach (string en in new string[] { "Exception", "ValueError", "TypeError", "KeyError", "IndexError", "ZeroDivisionError", "FileNotFoundError", "NameError", "AttributeError",
                "RuntimeError", "NotImplementedError", "OSError", "PermissionError", "StopIteration", "AssertionError", "ArithmeticError", "LookupError", "KeyboardInterrupt", "RecursionError",
                "ModuleNotFoundError", "ImportError", "SyntaxError", "EOFError", "OverflowError", "FileExistsError", "UnicodeError" })
                Builtins.Vars[en] = new MExcType(en);
        }

        bool IsInstance(object o, object t)
        {
            MTuple tup = t as MTuple; if (tup != null) { foreach (object x in tup.Items) if (IsInstance(o, x)) return true; return false; }
            MBuiltinType bt = t as MBuiltinType;
            if (bt != null) { string n = Ops.TypeName(o); if (bt.Name == "int") return n == "int" || n == "bool"; return n == bt.Name; }
            MClass c = t as MClass; if (c != null) { MInstance i = o as MInstance; MClass k = i == null ? null : i.Cls; while (k != null) { if (k == c) return true; k = k.Base as MClass; } return false; }
            MExcType et = t as MExcType; MException ex = o as MException;
            if (et != null && ex != null) return ExcChain(ex).Contains(et.Name);
            return false;
        }

        object MinMax(List<object> a, Dictionary<string, object> kw, int sign)
        {
            IEnumerable<object> items = a.Count == 1 ? Iter(a[0]) : (IEnumerable<object>)a; object key = Kw(kw, "key");
            object best = null, bestK = null; bool first = true;
            foreach (object x in items)
            {
                object k = key == null ? x : Call1(key, x);
                if (first || Ops.Cmp(k, bestK) * sign > 0) { best = x; bestK = k; first = false; }
            }
            if (first) { if (kw.ContainsKey("default")) return kw["default"]; throw new MovoError("ValueError", (sign < 0 ? "min" : "max") + "() of an empty sequence"); }
            return best;
        }

        // ================= libraries =================
        static string FindLib(string name)
        {
            string rel = name.Replace('.', Path.DirectorySeparatorChar);
            foreach (string ext in new string[] { ".mvl", ".mv", ".dll", ".py" })
            {
                string p = Path.Combine(LibDir, rel + ext); if (File.Exists(p)) return p;
            }
            return null;
        }

        public object ImportModule(string name, bool isPut, Env env)
        {
            object m;
            if (name == "gui" || name == "tkinter")
            {
                if (!modCache.TryGetValue("gui", out m)) { UsesGui = true; MModule g = new MModule("gui"); Dictionary<string, object> extras = new Dictionary<string, object>(); MovoGui.Build(this, g, extras); modCache["gui"] = g; modCache["gui.extras"] = extras; m = g; }
                if (isPut) { Dictionary<string, object> ex = (Dictionary<string, object>)modCache["gui.extras"]; foreach (KeyValuePair<string, object> kv in ex) env.GlobalEnv.Vars[kv.Key] = kv.Value; }
                return m;
            }
            if (!modCache.TryGetValue(name, out m))
            {
                m = MakeNative(name);
                if (m == null)
                {
                    string path = FindLib(name);
                    if (path != null && path.EndsWith(".dll")) m = LoadNativeLib(name, path);
                    else if (path != null && !path.EndsWith(".py")) m = LoadMovoLib(name, path);
                    else if (path != null) throw new MovoError("ImportError", "Library '" + name + ".py' is written in Python. Movo runs without Python - rewrite it as .mvl");
                }
                if (m == null) throw new MovoError("ModuleNotFoundError", "module '" + name + "' not found (Movo libraries are searched in " + LibDir + ")");
                modCache[name] = m;
            }
            if (isPut)
            {
                if (name == "time") env.GlobalEnv.Vars["sleep"] = ((MModule)m).Members["sleep"];
                MModule mm = m as MModule; object ex;
                if (mm != null && mm.Members.TryGetValue("__movo_export__", out ex)) foreach (object n in Iter(ex)) env.GlobalEnv.Vars[(string)n] = mm.Members[(string)n];
            }
            return m;
        }

        // a native library = a .NET dll in the library folder with  public static class MovoLibrary { public static void Fill(MModule m) }
        object LoadNativeLib(string name, string path)
        {
            System.Reflection.Assembly asm;
            try { asm = System.Reflection.Assembly.LoadFrom(path); }
            catch (Exception ex) { throw new MovoError("ImportError", "could not load library '" + name + "' (" + ex.Message + ")"); }
            Type t = asm.GetType("MovoLang.MovoLibrary");
            System.Reflection.MethodInfo mi = t == null ? null : t.GetMethod("Fill", new Type[] { typeof(MModule) });
            if (mi == null) throw new MovoError("ImportError", "'" + Path.GetFileName(path) + "' is not a Movo library (it has no MovoLibrary.Fill)");
            MModule mod = new MModule(name);
            try { mi.Invoke(null, new object[] { mod }); }
            catch (System.Reflection.TargetInvocationException ex) { if (ex.InnerException is MovoError) throw ex.InnerException; throw new MovoError("ImportError", "library '" + name + "' failed to start (" + (ex.InnerException ?? ex).Message + ")"); }
            return mod;
        }

        // reads a secret from the keyboard without showing it
        public static string AskHiddenStatic(string prompt)
        {
            Console.Out.Write(prompt); Console.Out.Flush();
            StringBuilder sb = new StringBuilder();
            try
            {
                if (Console.IsInputRedirected) { string l = Console.In.ReadLine(); return l ?? ""; }
                while (true)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter) break;
                    if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
                    else if (k.KeyChar != '\0') sb.Append(k.KeyChar);
                }
                Console.Out.WriteLine();
            }
            catch (InvalidOperationException) { string l = Console.In.ReadLine(); return l ?? ""; }
            return sb.ToString();
        }

        object LoadMovoLib(string name, string path)
        {
            MModule mod = new MModule(name);
            Env menv = new Env(Builtins); menv.GlobalEnv = menv;
            List<Stmt> prog = ProgParser.Parse(File.ReadAllText(path, Encoding.UTF8));
            Env save = null; ExecBlock(prog, menv); if (save != null) { }
            foreach (KeyValuePair<string, object> kv in menv.Vars) mod.Members[kv.Key] = kv.Value;
            return mod;
        }

        MModule MakeNative(string name)
        {
            MModule m = new MModule(name);
            switch (name)
            {
                case "math":
                    m.Val("pi", Math.PI).Val("e", Math.E).Val("tau", Math.PI * 2).Val("inf", double.PositiveInfinity).Val("nan", double.NaN);
                    m.Fn("sqrt", delegate(List<object> a) { double d = Ops.ToD(a[0]); if (d < 0) throw new MovoError("ValueError", "math domain error"); return Math.Sqrt(d); });
                    m.Fn("sin", delegate(List<object> a) { return Math.Sin(Ops.ToD(a[0])); }); m.Fn("cos", delegate(List<object> a) { return Math.Cos(Ops.ToD(a[0])); });
                    m.Fn("tan", delegate(List<object> a) { return Math.Tan(Ops.ToD(a[0])); }); m.Fn("asin", delegate(List<object> a) { return Math.Asin(Ops.ToD(a[0])); });
                    m.Fn("acos", delegate(List<object> a) { return Math.Acos(Ops.ToD(a[0])); }); m.Fn("atan", delegate(List<object> a) { return Math.Atan(Ops.ToD(a[0])); });
                    m.Fn("atan2", delegate(List<object> a) { return Math.Atan2(Ops.ToD(a[0]), Ops.ToD(a[1])); });
                    m.Fn("floor", delegate(List<object> a) { return (long)Math.Floor(Ops.ToD(a[0])); }); m.Fn("ceil", delegate(List<object> a) { return (long)Math.Ceiling(Ops.ToD(a[0])); });
                    m.Fn("trunc", delegate(List<object> a) { return (long)Math.Truncate(Ops.ToD(a[0])); }); m.Fn("fabs", delegate(List<object> a) { return Math.Abs(Ops.ToD(a[0])); });
                    m.Fn("pow", delegate(List<object> a) { return Math.Pow(Ops.ToD(a[0]), Ops.ToD(a[1])); }); m.Fn("exp", delegate(List<object> a) { return Math.Exp(Ops.ToD(a[0])); });
                    m.Fn("log", delegate(List<object> a) { double d = Ops.ToD(a[0]); if (d <= 0) throw new MovoError("ValueError", "math domain error"); return a.Count > 1 ? Math.Log(d) / Math.Log(Ops.ToD(a[1])) : Math.Log(d); });
                    m.Fn("log10", delegate(List<object> a) { return Math.Log10(Ops.ToD(a[0])); }); m.Fn("log2", delegate(List<object> a) { return Math.Log(Ops.ToD(a[0])) / Math.Log(2); });
                    m.Fn("radians", delegate(List<object> a) { return Ops.ToD(a[0]) * Math.PI / 180; }); m.Fn("degrees", delegate(List<object> a) { return Ops.ToD(a[0]) * 180 / Math.PI; });
                    m.Fn("hypot", delegate(List<object> a) { return Math.Sqrt(Ops.ToD(a[0]) * Ops.ToD(a[0]) + Ops.ToD(a[1]) * Ops.ToD(a[1])); });
                    m.Fn("isnan", delegate(List<object> a) { return double.IsNaN(Ops.ToD(a[0])); }); m.Fn("isinf", delegate(List<object> a) { return double.IsInfinity(Ops.ToD(a[0])); });
                    m.Fn("factorial", delegate(List<object> a) { long n = Ops.ToL(a[0]); if (n < 0) throw new MovoError("ValueError", "factorial of a negative number"); long r = 1; for (long i = 2; i <= n; i++) r *= i; return r; });
                    m.Fn("gcd", delegate(List<object> a) { long x = Math.Abs(Ops.ToL(a[0])), y = Math.Abs(Ops.ToL(a[1])); while (y != 0) { long t = y; y = x % y; x = t; } return x; });
                    return m;
                case "time":
                    m.Val("sleep", new SleepObj());
                    m.Fn("time", delegate(List<object> a) { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; });
                    m.Fn("perf_counter", delegate(List<object> a) { return System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency; });
                    m.Members["monotonic"] = m.Members["perf_counter"];
                    m.Fn("strftime", delegate(List<object> a)
                    {
                        DateTime n = DateTime.Now; string f = Ops.Str(a[0]);
                        return f.Replace("%Y", n.ToString("yyyy")).Replace("%m", n.ToString("MM")).Replace("%d", n.ToString("dd")).Replace("%H", n.ToString("HH")).Replace("%M", n.ToString("mm")).Replace("%S", n.ToString("ss")).Replace("%y", n.ToString("yy"));
                    });
                    return m;
                case "random":
                    m.Fn("random", delegate(List<object> a) { return Rng.NextDouble(); });
                    m.Fn("randint", delegate(List<object> a) { long lo = Ops.ToL(a[0]), hi = Ops.ToL(a[1]); if (hi < lo) throw new MovoError("ValueError", "empty range"); return lo + (long)(Rng.NextDouble() * (hi - lo + 1)); });
                    m.Fn("randrange", delegate(List<object> a) { long lo = a.Count > 1 ? Ops.ToL(a[0]) : 0, hi = a.Count > 1 ? Ops.ToL(a[1]) : Ops.ToL(a[0]); if (hi <= lo) throw new MovoError("ValueError", "empty range"); return lo + (long)(Rng.NextDouble() * (hi - lo)); });
                    m.Fn("uniform", delegate(List<object> a) { double lo = Ops.ToD(a[0]), hi = Ops.ToD(a[1]); return lo + Rng.NextDouble() * (hi - lo); });
                    m.Fn("choice", delegate(List<object> a) { List<object> l = new List<object>(Iter(a[0])); if (l.Count == 0) throw new MovoError("IndexError", "choice from an empty sequence"); return l[Rng.Next(l.Count)]; });
                    m.Fn("shuffle", delegate(List<object> a) { List<object> l = (List<object>)a[0]; for (int i = l.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); object t = l[i]; l[i] = l[j]; l[j] = t; } return null; });
                    m.Fn("seed", delegate(List<object> a) { Rng = new Random((int)Ops.ToL(a[0])); return null; });
                    return m;
                case "sys":
                    { List<object> argv = new List<object>(); foreach (string s in ScriptArgs) argv.Add(s); m.Val("argv", argv).Val("platform", "win32").Val("version", "Movo 1.1").Val("maxsize", long.MaxValue);
                      m.Fn("exit", delegate(List<object> a) { throw new MovoExit(a.Count > 0 && a[0] is long ? (int)(long)a[0] : 0); }); return m; }
                case "os": return MakeOs(m);
            }
            return null;
        }

        MModule MakeOs(MModule m)
        {
            Func<object, string> norm = delegate(object p) { return Path.GetFullPath(Ops.Str(p)); };
            m.Fn("vanish", delegate(List<object> a)
            {
                string p = norm(a[0]);
                if (Path.GetPathRoot(p) == p) throw new MovoError("PermissionError", "cannot delete a drive root: " + Ops.Str(a[0]));
                if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p); else throw new MovoError("FileNotFoundError", "not found: " + Ops.Str(a[0]));
                return null;
            });
            m.Fn("open", delegate(List<object> a)
            {
                string p = norm(a[0]); if (!File.Exists(p) && !Directory.Exists(p)) throw new MovoError("FileNotFoundError", "not found: " + Ops.Str(a[0]));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); return null;
            });
            m.Members["startfile"] = m.Members["open"];
            m.Fn("ren", delegate(List<object> a) { RenCtx r = new RenCtx(); r.Path = norm(a[0]); return r; });
            m.Fn("rename", delegate(List<object> a)
            {
                if (a.Count == 2) { string src = Ops.Str(a[0]), dst = Ops.Str(a[1]); if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst); return null; }
                if (RenCtx.Stack.Count == 0) throw new MovoError("RuntimeError", "os.rename!(...) works only inside os.ren!(path){ ... }");
                RenCtx r = RenCtx.Stack[RenCtx.Stack.Count - 1]; string name = Ops.Str(a[0]);
                string nw = Path.GetDirectoryName(name).Length > 0 ? name : Path.Combine(Path.GetDirectoryName(r.Path), name);
                if (Directory.Exists(r.Path)) Directory.Move(r.Path, nw); else File.Move(r.Path, nw);
                r.Path = Path.GetFullPath(nw); return nw;
            });
            m.Fn("getcwd", delegate(List<object> a) { return Directory.GetCurrentDirectory(); });
            m.Fn("chdir", delegate(List<object> a) { Directory.SetCurrentDirectory(Ops.Str(a[0])); return null; });
            m.Fn("listdir", delegate(List<object> a)
            {
                string p = a.Count > 0 ? Ops.Str(a[0]) : "."; if (!Directory.Exists(p)) throw new MovoError("FileNotFoundError", "folder not found: " + p);
                List<object> r = new List<object>(); foreach (string e in Directory.GetFileSystemEntries(p)) r.Add(Path.GetFileName(e)); return r;
            });
            m.Fn("mkdir", delegate(List<object> a) { Directory.CreateDirectory(Ops.Str(a[0])); return null; });
            m.Fn("makedirs", delegate(List<object> a) { Directory.CreateDirectory(Ops.Str(a[0])); return null; });
            m.Fn("remove", delegate(List<object> a) { string p = Ops.Str(a[0]); if (!File.Exists(p)) throw new MovoError("FileNotFoundError", "file not found: " + p); File.Delete(p); return null; });
            m.Fn("rmdir", delegate(List<object> a) { Directory.Delete(Ops.Str(a[0])); return null; });
            m.Fn("getenv", delegate(List<object> a) { string v = Environment.GetEnvironmentVariable(Ops.Str(a[0])); return v == null ? (a.Count > 1 ? a[1] : null) : (object)v; });
            m.Fn("system", delegate(List<object> a)
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + Ops.Str(a[0])); psi.UseShellExecute = false;
                System.Diagnostics.Process pr = System.Diagnostics.Process.Start(psi); pr.WaitForExit(); return (long)pr.ExitCode;
            });
            m.Val("sep", Path.DirectorySeparatorChar.ToString()).Val("name", "nt").Val("linesep", "\r\n");
            Dictionary<object, object> env = new Dictionary<object, object>(new MComparer());
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables()) env[(string)de.Key] = (string)de.Value;
            m.Val("environ", env);
            MModule path = new MModule("os.path");
            path.Fn("join", delegate(List<object> a) { string r = Ops.Str(a[0]); for (int i = 1; i < a.Count; i++) r = Path.Combine(r, Ops.Str(a[i])); return r; });
            path.Fn("exists", delegate(List<object> a) { string p = Ops.Str(a[0]); return File.Exists(p) || Directory.Exists(p); });
            path.Fn("isfile", delegate(List<object> a) { return File.Exists(Ops.Str(a[0])); });
            path.Fn("isdir", delegate(List<object> a) { return Directory.Exists(Ops.Str(a[0])); });
            path.Fn("basename", delegate(List<object> a) { return Path.GetFileName(Ops.Str(a[0])); });
            path.Fn("dirname", delegate(List<object> a) { return Path.GetDirectoryName(Ops.Str(a[0])) ?? ""; });
            path.Fn("abspath", delegate(List<object> a) { return Path.GetFullPath(Ops.Str(a[0])); });
            path.Fn("splitext", delegate(List<object> a) { string p = Ops.Str(a[0]); string e = Path.GetExtension(p); return new MTuple(new object[] { p.Substring(0, p.Length - e.Length), e }); });
            path.Fn("getsize", delegate(List<object> a) { return new FileInfo(Ops.Str(a[0])).Length; });
            m.Val("path", path);
            return m;
        }
    }
}
