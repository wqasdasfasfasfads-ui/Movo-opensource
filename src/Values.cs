using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MovoLang
{
    public interface IMCallable { object Call(Interp it, List<object> a, Dictionary<string, object> kw); }
    public interface IMObj { object GetAttr(string n); void SetAttr(string n, object v); }
    public interface IMIndexable { object GetItem(object k); void SetItem(object k, object v); }
    public interface IMContext { object Enter(); void Exit(); }

    public class NativeFn : IMCallable
    {
        public string Name;
        public Func<List<object>, Dictionary<string, object>, object> F;
        public NativeFn(string n, Func<List<object>, Dictionary<string, object>, object> f) { Name = n; F = f; }
        public NativeFn(string n, Func<List<object>, object> f) { Name = n; F = delegate(List<object> a, Dictionary<string, object> k) { return f(a); }; }
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw) { return F(a, kw ?? new Dictionary<string, object>()); }
    }

    public class MTuple
    {
        public object[] Items;
        public MTuple(object[] i) { Items = i; }
        public MTuple(List<object> l) { Items = l.ToArray(); }
    }

    public class MSlice { public object A, B, C; }

    // name == { key == value ... }   a small record: read fields with name.key or name["key"]
    public class MRecord : IMObj, IMIndexable
    {
        public List<string> Keys = new List<string>();
        public Dictionary<string, object> Vals = new Dictionary<string, object>();
        public object GetAttr(string n)
        {
            object v; if (Vals.TryGetValue(n, out v)) return v;
            throw new MovoError("AttributeError", "no field '" + n + "' (fields: " + string.Join(", ", Keys.ToArray()) + ")");
        }
        public void SetAttr(string n, object v) { if (!Vals.ContainsKey(n)) Keys.Add(n); Vals[n] = v; }
        public object GetItem(object k)
        {
            object v; if (Vals.TryGetValue(Ops.Str(k), out v)) return v;
            throw new MovoError("KeyError", Ops.Repr(k));
        }
        public void SetItem(object k, object v) { SetAttr(Ops.Str(k), v); }
    }

    public class Env
    {
        public Dictionary<string, object> Vars = new Dictionary<string, object>();
        public Env Parent;
        public HashSet<string> Globals, Nonlocals;
        public Env GlobalEnv;
        public MFunc Fn;
        public bool IsClass;
        public Env(Env parent) { Parent = parent; if (parent != null) GlobalEnv = parent.GlobalEnv; }
        public bool TryGet(string n, out object v)
        {
            Env e = this;
            while (e != null) { if (e.Vars.TryGetValue(n, out v)) return true; e = e.Parent; }
            v = null; return false;
        }
        public void Set(string n, object v)
        {
            if (Globals != null && Globals.Contains(n) && GlobalEnv != null) { GlobalEnv.Vars[n] = v; return; }
            if (Nonlocals != null && Nonlocals.Contains(n))
            {
                Env e = Parent;
                while (e != null) { if (e.Vars.ContainsKey(n)) { e.Vars[n] = v; return; } e = e.Parent; }
            }
            Vars[n] = v;
        }
    }

    public class MClass : IMCallable
    {
        public string Name; public object Base; public Dictionary<string, object> Members = new Dictionary<string, object>();
        public object Find(string n)
        {
            MClass c = this;
            while (c != null)
            {
                object v; if (c.Members.TryGetValue(n, out v)) return v;
                c = c.Base as MClass;
            }
            return null;
        }
        public bool Has(string n) { MClass c = this; while (c != null) { if (c.Members.ContainsKey(n)) return true; c = c.Base as MClass; } return false; }
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw)
        {
            MInstance inst = new MInstance(this);
            if (Has("__init__"))
            {
                List<object> args = new List<object>(); args.Add(inst); args.AddRange(a);
                it.Call(Find("__init__"), args, kw);
            }
            else { inst.Fields["args"] = new MTuple(a.ToArray()); }
            return inst;
        }
    }

    public class MInstance { public MClass Cls; public Dictionary<string, object> Fields = new Dictionary<string, object>(); public MInstance(MClass c) { Cls = c; } }

    public class MSuper { public object Self; public MClass Start, Owner; }

    public class MBound : IMCallable
    {
        public object Self; public object Fn;
        public MBound(object s, object f) { Self = s; Fn = f; }
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw)
        {
            List<object> args = new List<object>(); args.Add(Self); args.AddRange(a);
            return it.Call(Fn, args, kw);
        }
    }

    public class MModule : IMObj
    {
        public string Name; public Dictionary<string, object> Members = new Dictionary<string, object>();
        public MModule(string n) { Name = n; }
        public object GetAttr(string n)
        {
            object v; if (Members.TryGetValue(n, out v)) return v;
            throw new MovoError("AttributeError", "module '" + Name + "' has no '" + n + "'");
        }
        public void SetAttr(string n, object v) { Members[n] = v; }
        public MModule Fn(string n, Func<List<object>, object> f) { Members[n] = new NativeFn(n, f); return this; }
        public MModule Fn(string n, Func<List<object>, Dictionary<string, object>, object> f) { Members[n] = new NativeFn(n, f); return this; }
        public MModule Val(string n, object v) { Members[n] = v; return this; }
    }

    public class MException
    {
        public string Type; public string Message; public MTuple Args; public MInstance Inst;
        public MException(string t, string m) { Type = t; Message = m; Args = new MTuple(new object[] { m }); }
    }

    public class MExcType : IMCallable
    {
        public string Name;
        public MExcType(string n) { Name = n; }
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw)
        {
            MException e = new MException(Name, a.Count > 0 ? Ops.Str(a[0]) : "");
            e.Args = new MTuple(a.ToArray());
            return e;
        }
    }

    public class MBuiltinType : IMCallable
    {
        public string Name; public Func<List<object>, object> F;
        public MBuiltinType(string n, Func<List<object>, object> f) { Name = n; F = f; }
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw) { return F(a); }
    }

    public class MovoError : Exception
    {
        public MException Val; public int Line;
        public MovoError(string type, string msg) : base(msg) { Val = new MException(type, msg); }
        public MovoError(MException v) : base(v.Message) { Val = v; }
    }

    public class MovoExit : Exception { public int Code; public MovoExit(int c) { Code = c; } }

    public class MComparer : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) { return Ops.Eq(a, b); }
        public int GetHashCode(object o)
        {
            if (o == null) return 0;
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is long) return ((long)o).GetHashCode();
            if (o is double) { double d = (double)o; if (d == Math.Floor(d) && Math.Abs(d) < 9e18) return ((long)d).GetHashCode(); return d.GetHashCode(); }
            MTuple t = o as MTuple;
            if (t != null) { int h = 17; foreach (object x in t.Items) h = h * 31 + GetHashCode(x); return h; }
            return o.GetHashCode();
        }
    }

    public static class Ops
    {
        public static CultureInfo IC = CultureInfo.InvariantCulture;

        public static bool IsNum(object o) { return o is long || o is double || o is bool; }
        public static double ToD(object o)
        {
            if (o is double) return (double)o; if (o is long) return (long)o; if (o is bool) return ((bool)o) ? 1 : 0;
            throw new MovoError("TypeError", "expected a number, not " + TypeName(o));
        }
        public static long ToL(object o)
        {
            if (o is long) return (long)o; if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is double) return (long)(double)o;
            throw new MovoError("TypeError", "expected an integer, not " + TypeName(o));
        }
        static object NumN(object o) { if (o is bool) return ((bool)o) ? 1L : 0L; return o; }

        public static string TypeName(object o)
        {
            if (o == null) return "NoneType"; if (o is bool) return "bool"; if (o is long) return "int"; if (o is double) return "float";
            if (o is string) return "str"; if (o is List<object>) return "list"; if (o is Dictionary<object, object>) return "dict";
            if (o is MTuple) return "tuple"; if (o is MRecord) return "record"; MInstance i = o as MInstance; if (i != null) return i.Cls.Name;
            if (o is IMCallable) return "function"; return o.GetType().Name;
        }

        public static bool Truthy(object o)
        {
            if (o == null) return false; if (o is bool) return (bool)o; if (o is long) return (long)o != 0;
            if (o is double) return (double)o != 0; if (o is string) return ((string)o).Length > 0;
            List<object> l = o as List<object>; if (l != null) return l.Count > 0;
            Dictionary<object, object> d = o as Dictionary<object, object>; if (d != null) return d.Count > 0;
            MTuple t = o as MTuple; if (t != null) return t.Items.Length > 0;
            MInstance inst = o as MInstance;
            if (inst != null && inst.Cls.Has("__bool__")) return Truthy(Interp.Cur.Call(new MBound(inst, inst.Cls.Find("__bool__")), new List<object>(), null));
            if (inst != null && inst.Cls.Has("__len__")) return Truthy(Interp.Cur.Call(new MBound(inst, inst.Cls.Find("__len__")), new List<object>(), null));
            return true;
        }

        public static string FloatStr(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf"; if (double.IsNegativeInfinity(d)) return "-inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e16) { string s0 = ((long)d).ToString(IC) + ".0"; if (d == 0 && 1 / d < 0) return "-0.0"; return s0; }
            string s = null;
            for (int prec = 1; prec <= 17; prec++) { s = d.ToString("G" + prec, IC); if (double.Parse(s, IC) == d) break; }
            if (s.Contains("E")) {
                int ei = s.IndexOf('E'); string mant = s.Substring(0, ei); string ex = s.Substring(ei + 1); char sg = ex[0] == '-' ? '-' : '+';
                ex = ex.TrimStart('+', '-').TrimStart('0'); if (ex.Length < 2) ex = ex.PadLeft(2, '0'); s = mant + "e" + sg + ex;
            }
            return s;
        }

        public static string Str(object o)
        {
            if (o == null) return "None"; if (o is bool) return ((bool)o) ? "True" : "False";
            if (o is long) return ((long)o).ToString(IC); if (o is double) return FloatStr((double)o);
            if (o is string) return (string)o;
            MException ex = o as MException; if (ex != null) return ex.Inst != null ? InstStr(ex.Inst, ex.Message) : ex.Message;
            MInstance inst = o as MInstance;
            if (inst != null) return InstStr(inst, null);
            return Repr(o);
        }
        static string InstStr(MInstance inst, string dflt)
        {
            if (inst.Cls.Has("__str__")) return Str(Interp.Cur.Call(new MBound(inst, inst.Cls.Find("__str__")), new List<object>(), null));
            if (inst.Cls.Has("__repr__")) return Str(Interp.Cur.Call(new MBound(inst, inst.Cls.Find("__repr__")), new List<object>(), null));
            if (dflt != null) return dflt;
            MTuple a; object av;
            if (inst.Fields.TryGetValue("args", out av) && (a = av as MTuple) != null && inst.Cls.Base is MExcType) return a.Items.Length > 0 ? Str(a.Items[0]) : "";
            return "<" + inst.Cls.Name + " object>";
        }

        public static string Repr(object o)
        {
            if (o is string)
            {
                string s = (string)o; char q = '\'';
                if (s.Contains("'") && !s.Contains("\"")) q = '"';
                StringBuilder sb = new StringBuilder(); sb.Append(q);
                foreach (char c in s)
                {
                    if (c == '\\') sb.Append("\\\\"); else if (c == '\n') sb.Append("\\n"); else if (c == '\t') sb.Append("\\t"); else if (c == '\r') sb.Append("\\r");
                    else if (c == q) { sb.Append('\\'); sb.Append(c); } else sb.Append(c);
                }
                sb.Append(q); return sb.ToString();
            }
            List<object> l = o as List<object>;
            if (l != null) { List<string> p = new List<string>(); foreach (object x in l) p.Add(Repr(x)); return "[" + string.Join(", ", p.ToArray()) + "]"; }
            MTuple t = o as MTuple;
            if (t != null) { List<string> p = new List<string>(); foreach (object x in t.Items) p.Add(Repr(x)); return "(" + string.Join(", ", p.ToArray()) + (t.Items.Length == 1 ? ",)" : ")"); }
            Dictionary<object, object> d = o as Dictionary<object, object>;
            if (d != null) { List<string> p = new List<string>(); foreach (KeyValuePair<object, object> kv in d) p.Add(Repr(kv.Key) + ": " + Repr(kv.Value)); return "{" + string.Join(", ", p.ToArray()) + "}"; }
            MRecord rec = o as MRecord;
            if (rec != null) { List<string> p = new List<string>(); foreach (string k in rec.Keys) p.Add(k + ": " + Repr(rec.Vals[k])); return "{" + string.Join(", ", p.ToArray()) + "}"; }
            MFunc f = o as MFunc; if (f != null) return "<function " + f.Name + ">";
            MClass mc = o as MClass; if (mc != null) return "<class '" + mc.Name + "'>";
            MException ex = o as MException; if (ex != null) return ex.Type + "(" + Repr(ex.Message) + ")";
            MInstance inst = o as MInstance;
            if (inst != null && inst.Cls.Has("__repr__")) return Str(Interp.Cur.Call(new MBound(inst, inst.Cls.Find("__repr__")), new List<object>(), null));
            if (inst != null) return "<" + inst.Cls.Name + " object>";
            MModule m = o as MModule; if (m != null) return "<module '" + m.Name + "'>";
            if (o is IMCallable) return "<built-in function>";
            return Str(o);
        }

        public static bool Eq(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNum(a) && IsNum(b))
            {
                if (a is double || b is double) return ToD(a) == ToD(b);
                return ToL(a) == ToL(b);
            }
            string sa = a as string; if (sa != null) return b is string && sa == (string)b;
            List<object> la = a as List<object>;
            if (la != null) { List<object> lb = b as List<object>; if (lb == null || la.Count != lb.Count) return false; for (int i = 0; i < la.Count; i++) if (!Eq(la[i], lb[i])) return false; return true; }
            MTuple ta = a as MTuple;
            if (ta != null) { MTuple tb = b as MTuple; if (tb == null || ta.Items.Length != tb.Items.Length) return false; for (int i = 0; i < ta.Items.Length; i++) if (!Eq(ta.Items[i], tb.Items[i])) return false; return true; }
            Dictionary<object, object> da = a as Dictionary<object, object>;
            if (da != null)
            {
                Dictionary<object, object> db = b as Dictionary<object, object>; if (db == null || da.Count != db.Count) return false;
                foreach (KeyValuePair<object, object> kv in da) { object v; if (!db.TryGetValue(kv.Key, out v) || !Eq(kv.Value, v)) return false; }
                return true;
            }
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__eq__")) return Truthy(Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__eq__")), new List<object> { b }, null));
            return object.ReferenceEquals(a, b) || a.Equals(b);
        }

        public static int Cmp(object a, object b)
        {
            if (IsNum(a) && IsNum(b))
            {
                if (a is double || b is double) return ToD(a).CompareTo(ToD(b));
                return ToL(a).CompareTo(ToL(b));
            }
            if (a is string && b is string) return string.CompareOrdinal((string)a, (string)b);
            List<object> la = a as List<object>, lb = b as List<object>;
            if (la != null && lb != null) return CmpSeq(la.ToArray(), lb.ToArray());
            MTuple ta = a as MTuple, tb = b as MTuple;
            if (ta != null && tb != null) return CmpSeq(ta.Items, tb.Items);
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__lt__"))
            {
                if (Truthy(Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__lt__")), new List<object> { b }, null))) return -1;
                return Eq(a, b) ? 0 : 1;
            }
            throw new MovoError("TypeError", "cannot compare '" + TypeName(a) + "' and '" + TypeName(b) + "'");
        }
        static int CmpSeq(object[] a, object[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) { if (!Eq(a[i], b[i])) return Cmp(a[i], b[i]); }
            return a.Length.CompareTo(b.Length);
        }

        // a number typed on the keyboard arrives as text ("2000"); where arithmetic used to fail with a TypeError, treat such text as a number
        static object TextNum(object o)
        {
            string t = o as string; if (t == null) return null;
            t = t.Trim(); if (t.Length == 0) return null;
            int i = (t[0] == '-' || t[0] == '+') ? 1 : 0;
            if (i >= t.Length || !(char.IsDigit(t[i]) || t[i] == '.')) return null;
            long lv; if (long.TryParse(t, NumberStyles.AllowLeadingSign, IC, out lv)) return lv;
            double dv; if (double.TryParse(t, NumberStyles.Float, IC, out dv)) return dv;
            return null;
        }
        static void Coerce(ref object a, ref object b)
        {
            if (IsNum(a) && b is string) { object n = TextNum(b); if (n != null) b = n; }
            else if (IsNum(b) && a is string) { object n = TextNum(a); if (n != null) a = n; }
        }

        public static object Add(object a, object b)
        {
            if (IsNum(a) != IsNum(b)) Coerce(ref a, ref b);
            if (a is string && b is string) return (string)a + (string)b;
            if (IsNum(a) && IsNum(b))
            {
                if (a is double || b is double) return ToD(a) + ToD(b);
                return ToL(a) + ToL(b);
            }
            List<object> la = a as List<object>, lb = b as List<object>;
            if (la != null && lb != null) { List<object> r = new List<object>(la); r.AddRange(lb); return r; }
            MTuple ta = a as MTuple, tb = b as MTuple;
            if (ta != null && tb != null) { List<object> r = new List<object>(ta.Items); r.AddRange(tb.Items); return new MTuple(r); }
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__add__")) return Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__add__")), new List<object> { b }, null);
            throw new MovoError("TypeError", "unsupported operand types for +: '" + TypeName(a) + "' and '" + TypeName(b) + "'");
        }
        public static object Sub(object a, object b)
        {
            if (!(IsNum(a) && IsNum(b))) Coerce(ref a, ref b);
            if (IsNum(a) && IsNum(b)) { if (a is double || b is double) return ToD(a) - ToD(b); return ToL(a) - ToL(b); }
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__sub__")) return Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__sub__")), new List<object> { b }, null);
            throw new MovoError("TypeError", "unsupported operand types for -: '" + TypeName(a) + "' and '" + TypeName(b) + "'");
        }
        public static object Mul(object a, object b)
        {
            if (IsNum(a) && IsNum(b)) { if (a is double || b is double) return ToD(a) * ToD(b); return ToL(a) * ToL(b); }
            if (a is string && IsNum(b)) return Rep((string)a, ToL(b));
            if (b is string && IsNum(a)) return Rep((string)b, ToL(a));
            List<object> la = a as List<object>;
            if (la != null && IsNum(b)) { List<object> r = new List<object>(); for (long i = 0; i < ToL(b); i++) r.AddRange(la); return r; }
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__mul__")) return Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__mul__")), new List<object> { b }, null);
            throw new MovoError("TypeError", "unsupported operand types for *: '" + TypeName(a) + "' and '" + TypeName(b) + "'");
        }
        static string Rep(string s, long n) { StringBuilder sb = new StringBuilder(); for (long i = 0; i < n; i++) sb.Append(s); return sb.ToString(); }
        public static object Div(object a, object b)
        {
            if (!(IsNum(a) && IsNum(b))) Coerce(ref a, ref b);
            if (IsNum(a) && IsNum(b)) { double d = ToD(b); if (d == 0) throw new MovoError("ZeroDivisionError", "division by zero"); return ToD(a) / d; }
            MInstance ia = a as MInstance;
            if (ia != null && ia.Cls.Has("__truediv__")) return Interp.Cur.Call(new MBound(ia, ia.Cls.Find("__truediv__")), new List<object> { b }, null);
            throw new MovoError("TypeError", "unsupported operand types for /: '" + TypeName(a) + "' and '" + TypeName(b) + "'");
        }
        public static object FloorDiv(object a, object b)
        {
            if (!(IsNum(a) && IsNum(b))) Coerce(ref a, ref b);
            if (!(IsNum(a) && IsNum(b))) throw new MovoError("TypeError", "unsupported operand types for //");
            if (a is double || b is double) { double d = ToD(b); if (d == 0) throw new MovoError("ZeroDivisionError", "division by zero"); return Math.Floor(ToD(a) / d); }
            long x = ToL(a), y = ToL(b); if (y == 0) throw new MovoError("ZeroDivisionError", "division by zero");
            long q = x / y; if ((x % y != 0) && ((x < 0) != (y < 0))) q--; return q;
        }
        public static object Mod(object a, object b)
        {
            if (a is string) return PercentFormat((string)a, b);
            if (!(IsNum(a) && IsNum(b))) throw new MovoError("TypeError", "unsupported operand types for %");
            if (a is double || b is double) { double d = ToD(b); if (d == 0) throw new MovoError("ZeroDivisionError", "division by zero"); double x = ToD(a); return x - Math.Floor(x / d) * d; }
            long p = ToL(a), q = ToL(b); if (q == 0) throw new MovoError("ZeroDivisionError", "division by zero");
            long r = p % q; if (r != 0 && ((r < 0) != (q < 0))) r += q; return r;
        }
        public static object Pow(object a, object b)
        {
            if (!(IsNum(a) && IsNum(b))) Coerce(ref a, ref b);
            if (!(IsNum(a) && IsNum(b))) throw new MovoError("TypeError", "unsupported operand types for **");
            if (!(a is double) && !(b is double) && ToL(b) >= 0)
            {
                double r = Math.Pow(ToD(a), ToD(b));
                if (Math.Abs(r) < 9e18) { long res = 1, bs = ToL(a), e = ToL(b); for (long i = 0; i < e; i++) res *= bs; return res; }
                return r;
            }
            return Math.Pow(ToD(a), ToD(b));
        }
        public static object Neg(object a)
        {
            if (a is long) return -(long)a; if (a is double) return -(double)a; if (a is bool) return ((bool)a) ? -1L : 0L;
            throw new MovoError("TypeError", "bad operand type for unary -: '" + TypeName(a) + "'");
        }

        public static bool Contains(object c, object item)
        {
            string s = c as string;
            if (s != null) { if (!(item is string)) throw new MovoError("TypeError", "'in <str>' requires a string"); return s.Contains((string)item); }
            List<object> l = c as List<object>; if (l != null) { foreach (object x in l) if (Eq(x, item)) return true; return false; }
            MTuple t = c as MTuple; if (t != null) { foreach (object x in t.Items) if (Eq(x, item)) return true; return false; }
            Dictionary<object, object> d = c as Dictionary<object, object>; if (d != null) return d.ContainsKey(item);
            MRecord rc = c as MRecord; if (rc != null) return rc.Vals.ContainsKey(Str(item));
            throw new MovoError("TypeError", "'" + TypeName(c) + "' does not support 'in'");
        }

        public static string PercentFormat(string fmt, object args)
        {
            object[] arr = args is MTuple ? ((MTuple)args).Items : new object[] { args };
            int ai = 0; StringBuilder sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c != '%') { sb.Append(c); continue; }
                i++; if (i >= fmt.Length) break;
                if (fmt[i] == '%') { sb.Append('%'); continue; }
                string spec = "";
                while (i < fmt.Length && "-+ 0#.0123456789".IndexOf(fmt[i]) >= 0) { spec += fmt[i]; i++; }
                char t = i < fmt.Length ? fmt[i] : 's';
                if (ai >= arr.Length) throw new MovoError("TypeError", "not enough arguments for formatting");
                object v = arr[ai++];
                string fs = spec;
                if (t == 'd' || t == 'i') sb.Append(FormatValue(ToL(v), fs.Replace("-", "<") + "d"));
                else if (t == 'f') sb.Append(FormatValue(ToD(v), (fs.Contains(".") ? fs : fs + ".6") + "f"));
                else if (t == 's') sb.Append(FormatValue(Str(v), fs.Replace("-", "<")));
                else if (t == 'r') sb.Append(Repr(v));
                else if (t == 'x') sb.Append(Convert.ToString(ToL(v), 16));
                else sb.Append(Str(v));
            }
            return sb.ToString();
        }

        public static string FormatValue(object v, string spec)
        {
            if (spec == null || spec.Length == 0) return Str(v);
            int i = 0; char fill = ' '; char align = '\0'; char sign = '-'; bool comma = false; int width = 0; int prec = -1; char type = '\0';
            if (spec.Length >= 2 && "<>^=".IndexOf(spec[1]) >= 0) { fill = spec[0]; align = spec[1]; i = 2; }
            else if (spec.Length >= 1 && "<>^=".IndexOf(spec[0]) >= 0) { align = spec[0]; i = 1; }
            if (i < spec.Length && "+- ".IndexOf(spec[i]) >= 0) { sign = spec[i]; i++; }
            if (i < spec.Length && spec[i] == '0') { if (align == '\0') { fill = '0'; align = '='; } i++; }
            int ws = i; while (i < spec.Length && char.IsDigit(spec[i])) i++;
            if (i > ws) width = int.Parse(spec.Substring(ws, i - ws));
            if (i < spec.Length && spec[i] == ',') { comma = true; i++; }
            if (i < spec.Length && spec[i] == '.') { i++; int ps = i; while (i < spec.Length && char.IsDigit(spec[i])) i++; prec = int.Parse(spec.Substring(ps, i - ps)); }
            if (i < spec.Length) type = spec[i];
            string body;
            if (type == 'f' || type == 'F' || (type == '\0' && v is double && prec >= 0))
            {
                double d = ToD(v); body = Math.Abs(d).ToString("F" + (prec < 0 ? 6 : prec), IC);
                if (comma) body = AddCommas(body);
                if (d < 0 || (d == 0 && 1 / d < 0 && false)) body = "-" + body; else if (sign == '+') body = "+" + body; else if (sign == ' ') body = " " + body;
            }
            else if (type == 'e') { double d = ToD(v); body = d.ToString((prec < 0 ? "0.000000" : "0." + new string('0', prec)) + "e+00", IC); }
            else if (type == '%') { double d = ToD(v) * 100; body = d.ToString("F" + (prec < 0 ? 6 : prec), IC) + "%"; }
            else if (type == 'd' || (type == '\0' && (v is long) && false))
            {
                long l = ToL(v); body = Math.Abs(l).ToString(IC); if (comma) body = AddCommas(body);
                if (l < 0) body = "-" + body; else if (sign == '+') body = "+" + body; else if (sign == ' ') body = " " + body;
            }
            else if (type == 'x') body = Convert.ToString(ToL(v), 16);
            else if (type == 'X') body = Convert.ToString(ToL(v), 16).ToUpper();
            else if (type == 'b') body = Convert.ToString(ToL(v), 2);
            else if (type == 'o') body = Convert.ToString(ToL(v), 8);
            else
            {
                body = Str(v);
                if (comma && v is long) body = AddCommas(body);
                if (prec >= 0 && v is string && body.Length > prec) body = body.Substring(0, prec);
                if (v is long && sign == '+' && (long)v >= 0) body = "+" + body;
            }
            if (body.Length < width)
            {
                int pad = width - body.Length;
                char al = align; if (al == '\0') al = (v is string) ? '<' : '>';
                if (al == '<') body = body + new string(fill, pad);
                else if (al == '^') body = new string(fill, pad / 2) + body + new string(fill, pad - pad / 2);
                else if (al == '=') { int sg = (body.StartsWith("-") || body.StartsWith("+")) ? 1 : 0; body = body.Substring(0, sg) + new string(fill, pad) + body.Substring(sg); }
                else body = new string(fill, pad) + body;
            }
            return body;
        }
        static string AddCommas(string s)
        {
            int dot = s.IndexOf('.'); string ip = dot >= 0 ? s.Substring(0, dot) : s; string fp = dot >= 0 ? s.Substring(dot) : "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ip.Length; i++) { if (i > 0 && (ip.Length - i) % 3 == 0) sb.Append(','); sb.Append(ip[i]); }
            return sb.ToString() + fp;
        }
    }
}
