using System;
using System.Collections.Generic;
using System.Text;

namespace MovoLang
{
    public enum Flow { Normal, Break, Continue, Return }

    public abstract class Expr
    {
        public abstract object Eval(Interp it, Env e);
        public virtual void Assign(Interp it, Env e, object v) { throw new MovoError("SyntaxError", "cannot assign to this expression"); }
    }

    public class ConstE : Expr { public object V; public ConstE(object v) { V = v; } public override object Eval(Interp it, Env e) { return V; } }

    public class NameE : Expr
    {
        public string Name; public NameE(string n) { Name = n; }
        public override object Eval(Interp it, Env e)
        {
            object v; if (e.TryGet(Name, out v)) return v;
            throw new MovoError("NameError", "name '" + Name + "' is not defined");
        }
        public override void Assign(Interp it, Env e, object v) { e.Set(Name, v); }
    }

    public class AttrE : Expr
    {
        public Expr Obj; public string Name; public AttrE(Expr o, string n) { Obj = o; Name = n; }
        public override object Eval(Interp it, Env e) { return it.GetAttr(Obj.Eval(it, e), Name); }
        public override void Assign(Interp it, Env e, object v) { it.SetAttr(Obj.Eval(it, e), Name, v); }
    }

    public class IndexE : Expr
    {
        public Expr Obj, Idx; public IndexE(Expr o, Expr i) { Obj = o; Idx = i; }
        public override object Eval(Interp it, Env e) { return it.GetItem(Obj.Eval(it, e), Idx.Eval(it, e)); }
        public override void Assign(Interp it, Env e, object v) { it.SetItem(Obj.Eval(it, e), Idx.Eval(it, e), v); }
    }

    public class SliceE : Expr
    {
        public Expr A, B, C; public SliceE(Expr a, Expr b, Expr c) { A = a; B = b; C = c; }
        public override object Eval(Interp it, Env e)
        {
            MSlice s = new MSlice(); s.A = A == null ? null : A.Eval(it, e); s.B = B == null ? null : B.Eval(it, e); s.C = C == null ? null : C.Eval(it, e); return s;
        }
    }

    public class CallE : Expr
    {
        public Expr Fn; public List<Expr> Args = new List<Expr>(); public List<bool> Star = new List<bool>();
        public List<string> KwNames = new List<string>(); public List<Expr> KwVals = new List<Expr>();
        public Expr DStar;
        public override object Eval(Interp it, Env e)
        {
            NameE ne = Fn as NameE;
            if (ne != null && ne.Name == "super" && Args.Count == 0) return it.MakeSuper(e);
            object f = Fn.Eval(it, e);
            List<object> a = new List<object>();
            for (int i = 0; i < Args.Count; i++)
            {
                object v = Args[i].Eval(it, e);
                if (Star[i]) { foreach (object x in it.Iter(v)) a.Add(x); } else a.Add(v);
            }
            Dictionary<object, object> dummy = null;
            Dictionary<string, object> kw = null;
            if (KwNames.Count > 0 || DStar != null)
            {
                kw = new Dictionary<string, object>();
                for (int i = 0; i < KwNames.Count; i++) kw[KwNames[i]] = KwVals[i].Eval(it, e);
                if (DStar != null) { dummy = DStar.Eval(it, e) as Dictionary<object, object>; if (dummy != null) foreach (KeyValuePair<object, object> kv in dummy) kw[Ops.Str(kv.Key)] = kv.Value; }
            }
            return it.Call(f, a, kw);
        }
    }

    public class BinE : Expr
    {
        public string Op; public Expr L, R; public BinE(string op, Expr l, Expr r) { Op = op; L = l; R = r; }
        public override object Eval(Interp it, Env e)
        {
            object a = L.Eval(it, e), b = R.Eval(it, e);
            switch (Op)
            {
                case "+": return Ops.Add(a, b);
                case "-": return Ops.Sub(a, b);
                case "*": return Ops.Mul(a, b);
                case "/": return Ops.Div(a, b);
                case "//": return Ops.FloorDiv(a, b);
                case "%": return Ops.Mod(a, b);
                case "**": return Ops.Pow(a, b);
                case "&": return Ops.ToL(a) & Ops.ToL(b);
                case "|": return Ops.ToL(a) | Ops.ToL(b);
                case "^": return Ops.ToL(a) ^ Ops.ToL(b);
            }
            throw new MovoError("SyntaxError", "unknown operator " + Op);
        }
    }

    public class UnE : Expr
    {
        public string Op; public Expr X; public UnE(string op, Expr x) { Op = op; X = x; }
        public override object Eval(Interp it, Env e)
        {
            object v = X.Eval(it, e);
            if (Op == "-") return Ops.Neg(v);
            if (Op == "+") { if (!Ops.IsNum(v)) throw new MovoError("TypeError", "bad operand type for unary +"); return v is bool ? (object)Ops.ToL(v) : v; }
            if (Op == "~") return ~Ops.ToL(v);
            return !Ops.Truthy(v);
        }
    }

    public class BoolE : Expr
    {
        public bool IsAnd; public Expr L, R; public BoolE(bool and, Expr l, Expr r) { IsAnd = and; L = l; R = r; }
        public override object Eval(Interp it, Env e)
        {
            object a = L.Eval(it, e);
            if (IsAnd) return Ops.Truthy(a) ? R.Eval(it, e) : a;
            return Ops.Truthy(a) ? a : R.Eval(it, e);
        }
    }

    public class CmpE : Expr
    {
        public List<string> Ops_ = new List<string>(); public List<Expr> Items = new List<Expr>();
        public override object Eval(Interp it, Env e)
        {
            object a = Items[0].Eval(it, e);
            for (int i = 0; i < Ops_.Count; i++)
            {
                object b = Items[i + 1].Eval(it, e); bool r;
                switch (Ops_[i])
                {
                    case "==": r = Ops.Eq(a, b); break;
                    case "!=": r = !Ops.Eq(a, b); break;
                    case "<": r = Ops.Cmp(a, b) < 0; break;
                    case ">": r = Ops.Cmp(a, b) > 0; break;
                    case "<=": r = Ops.Cmp(a, b) <= 0; break;
                    case ">=": r = Ops.Cmp(a, b) >= 0; break;
                    case "in": r = Ops.Contains(b, a); break;
                    case "not in": r = !Ops.Contains(b, a); break;
                    case "is": r = (a == null && b == null) || object.ReferenceEquals(a, b) || (a != null && b != null && !(a is List<object>) && !(a is Dictionary<object, object>) && Ops.Eq(a, b)); break;
                    default: r = !((a == null && b == null) || object.ReferenceEquals(a, b) || (a != null && b != null && !(a is List<object>) && !(a is Dictionary<object, object>) && Ops.Eq(a, b))); break;
                }
                if (!r) return false;
                a = b;
            }
            return true;
        }
    }

    public class TernE : Expr
    {
        public Expr C, A, B; public TernE(Expr c, Expr a, Expr b) { C = c; A = a; B = b; }
        public override object Eval(Interp it, Env e) { return Ops.Truthy(C.Eval(it, e)) ? A.Eval(it, e) : B.Eval(it, e); }
    }

    public class ListE : Expr
    {
        public List<Expr> Items = new List<Expr>(); public bool IsTuple;
        public override object Eval(Interp it, Env e)
        {
            List<object> r = new List<object>(); foreach (Expr x in Items) r.Add(x.Eval(it, e));
            if (IsTuple) return new MTuple(r); return r;
        }
        public override void Assign(Interp it, Env e, object v)
        {
            List<object> vals = new List<object>(it.Iter(v));
            if (vals.Count != Items.Count) throw new MovoError("ValueError", "need to unpack " + Items.Count + " values, but got " + vals.Count);
            for (int i = 0; i < Items.Count; i++) Items[i].Assign(it, e, vals[i]);
        }
    }

    public class DictE : Expr
    {
        public List<Expr> Keys = new List<Expr>(), Vals = new List<Expr>();
        public override object Eval(Interp it, Env e)
        {
            Dictionary<object, object> d = new Dictionary<object, object>(new MComparer());
            for (int i = 0; i < Keys.Count; i++) d[Keys[i].Eval(it, e)] = Vals[i].Eval(it, e);
            return d;
        }
    }

    public class FStrE : Expr
    {
        public List<string> Lits = new List<string>(); public List<Expr> Exprs = new List<Expr>(); public List<string> Specs = new List<string>(); public List<char> Convs = new List<char>();
        public override object Eval(Interp it, Env e)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Exprs.Count; i++)
            {
                sb.Append(Lits[i]);
                object v = Exprs[i].Eval(it, e);
                if (Convs[i] == 'r') v = Ops.Repr(v); else if (Convs[i] == 's') v = Ops.Str(v);
                string spec = Specs[i];
                if (spec != null && spec.Contains("{")) spec = ((string)ParserUtil.ParseFString(spec).Eval(it, e));
                sb.Append(spec == null ? Ops.Str(v) : Ops.FormatValue(v, spec));
            }
            sb.Append(Lits[Lits.Count - 1]);
            return sb.ToString();
        }
    }

    public class LambdaE : Expr
    {
        public List<Param> Params; public Expr Body;
        public override object Eval(Interp it, Env e)
        {
            MFunc f = new MFunc(); f.Name = "<lambda>"; f.Params = new List<Param>();
            foreach (Param p in Params) { Param q = new Param(); q.Name = p.Name; q.Star = p.Star; q.DStar = p.DStar; if (p.Default != null) { q.HasDefault = true; q.DefaultVal = p.Default.Eval(it, e); } f.Params.Add(q); }
            ReturnS r = new ReturnS(); r.Value = Body; f.Body = new List<Stmt> { r }; f.Closure = e; return f;
        }
    }

    public class CompE : Expr
    {
        public Expr Elt; public Expr Target; public Expr Iter; public Expr Cond;
        public override object Eval(Interp it, Env e)
        {
            Env ce = new Env(e); List<object> r = new List<object>();
            foreach (object x in it.Iter(Iter.Eval(it, e)))
            {
                Target.Assign(it, ce, x);
                if (Cond != null && !Ops.Truthy(Cond.Eval(it, ce))) continue;
                r.Add(Elt.Eval(it, ce));
            }
            return r;
        }
    }

    public class Param { public string Name; public Expr Default; public bool HasDefault; public object DefaultVal; public bool Star, DStar; }

    public class MFunc : IMCallable
    {
        public string Name; public List<Param> Params; public List<Stmt> Body; public Env Closure; public MClass Owner;
        public object Call(Interp it, List<object> a, Dictionary<string, object> kw)
        {
            Env env = new Env(Closure); env.Fn = this;
            int ai = 0; HashSet<string> used = new HashSet<string>();
            string dstarName = null;
            foreach (Param p in Params)
            {
                if (p.Star) { List<object> rest = new List<object>(); while (ai < a.Count) rest.Add(a[ai++]); env.Vars[p.Name] = new MTuple(rest); continue; }
                if (p.DStar) { dstarName = p.Name; continue; }
                object v;
                if (ai < a.Count) { env.Vars[p.Name] = a[ai++]; used.Add(p.Name); }
                else if (kw != null && kw.TryGetValue(p.Name, out v)) { env.Vars[p.Name] = v; used.Add(p.Name); }
                else if (p.HasDefault) env.Vars[p.Name] = p.DefaultVal;
                else throw new MovoError("TypeError", Name + "() missing argument '" + p.Name + "'");
            }
            if (ai < a.Count) throw new MovoError("TypeError", Name + "() got too many arguments");
            Dictionary<object, object> extra = new Dictionary<object, object>(new MComparer());
            if (kw != null) foreach (KeyValuePair<string, object> kv in kw)
                {
                    if (used.Contains(kv.Key)) continue;
                    bool known = false; foreach (Param p in Params) if (p.Name == kv.Key && !p.Star && !p.DStar) known = true;
                    if (known) continue;
                    if (dstarName == null) throw new MovoError("TypeError", Name + "() has no argument '" + kv.Key + "'");
                    extra[kv.Key] = kv.Value;
                }
            if (dstarName != null) env.Vars[dstarName] = extra;
            it.Depth++;
            if (it.Depth > 900) { it.Depth = 0; throw new MovoError("RecursionError", "maximum recursion depth exceeded"); }
            try
            {
                Flow f = it.ExecBlock(Body, env);
                if (f == Flow.Return) { object r = it.RetVal; it.RetVal = null; return r; }
                return null;
            }
            finally { if (it.Depth > 0) it.Depth--; }
        }
    }

    // ===================== Statements =====================
    public abstract class Stmt { public int Line; public abstract Flow Exec(Interp it, Env e); }

    public class ExprS : Stmt { public Expr X; public override Flow Exec(Interp it, Env e) { X.Eval(it, e); return Flow.Normal; } }

    public class AssignS : Stmt
    {
        public Expr Target; public Expr Value; public string Op;
        public override Flow Exec(Interp it, Env e)
        {
            if (Op == null) { Target.Assign(it, e, Value.Eval(it, e)); return Flow.Normal; }
            object cur = Target.Eval(it, e); object v = Value.Eval(it, e); object r;
            switch (Op)
            {
                case "+": r = Ops.Add(cur, v); break;
                case "-": r = Ops.Sub(cur, v); break;
                case "*": r = Ops.Mul(cur, v); break;
                case "/": r = Ops.Div(cur, v); break;
                case "//": r = Ops.FloorDiv(cur, v); break;
                case "%": r = Ops.Mod(cur, v); break;
                default: r = Ops.Pow(cur, v); break;
            }
            Target.Assign(it, e, r); return Flow.Normal;
        }
    }

    public class IfS : Stmt
    {
        public List<Expr> Conds = new List<Expr>(); public List<List<Stmt>> Bodies = new List<List<Stmt>>(); public List<Stmt> Else;
        public override Flow Exec(Interp it, Env e)
        {
            for (int i = 0; i < Conds.Count; i++) if (Ops.Truthy(Conds[i].Eval(it, e))) return it.ExecBlock(Bodies[i], e);
            if (Else != null) return it.ExecBlock(Else, e);
            return Flow.Normal;
        }
    }

    public class WhileS : Stmt
    {
        public Expr Cond; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            while (Ops.Truthy(Cond.Eval(it, e)))
            {
                Flow f = it.ExecBlock(Body, e);
                if (f == Flow.Break) break; if (f == Flow.Return) return f;
            }
            return Flow.Normal;
        }
    }

    // while.so{ ... }so.N.times  -> runs the body N times
    public class RepeatS : Stmt
    {
        public Expr Count; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            long n = Ops.ToL(Count.Eval(it, e));
            for (long i = 0; i < n; i++)
            {
                Flow f = it.ExecBlock(Body, e);
                if (f == Flow.Break) break; if (f == Flow.Return) return f;
            }
            return Flow.Normal;
        }
    }

    public class ForS : Stmt
    {
        public Expr Target, Iter; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            object src = Iter.Eval(it, e);
            List<object> l = src as List<object>;
            if (l != null)
            {
                for (int i = 0; i < l.Count; i++)
                {
                    Target.Assign(it, e, l[i]);
                    Flow f = it.ExecBlock(Body, e); if (f == Flow.Break) break; if (f == Flow.Return) return f;
                }
                return Flow.Normal;
            }
            foreach (object x in it.Iter(src))
            {
                Target.Assign(it, e, x);
                Flow f = it.ExecBlock(Body, e); if (f == Flow.Break) break; if (f == Flow.Return) return f;
            }
            return Flow.Normal;
        }
    }

    public class FuncDefS : Stmt
    {
        public string Name; public List<Param> Params; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            MFunc f = new MFunc(); f.Name = Name; f.Body = Body; f.Params = new List<Param>();
            foreach (Param p in Params) { Param q = new Param(); q.Name = p.Name; q.Star = p.Star; q.DStar = p.DStar; if (p.Default != null) { q.HasDefault = true; q.DefaultVal = p.Default.Eval(it, e); } f.Params.Add(q); }
            f.Closure = e.IsClass ? e.Parent : e;
            e.Set(Name, f); return Flow.Normal;
        }
    }

    public class ClassDefS : Stmt
    {
        public string Name; public Expr Base; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            MClass c = new MClass(); c.Name = Name;
            if (Base != null) c.Base = Base.Eval(it, e);
            Env ce = new Env(e); ce.IsClass = true;
            it.ExecBlock(Body, ce);
            c.Members = ce.Vars;
            foreach (object v in c.Members.Values) { MFunc f = v as MFunc; if (f != null) f.Owner = c; }
            e.Set(Name, c); return Flow.Normal;
        }
    }

    public class CatchClause { public Expr Type; public string Var; public List<Stmt> Body; }

    public class TryS : Stmt
    {
        public List<Stmt> Body; public List<CatchClause> Catches = new List<CatchClause>(); public List<Stmt> Final;
        public override Flow Exec(Interp it, Env e)
        {
            Flow fl = Flow.Normal; MovoError pending = null;
            try { fl = it.ExecBlock(Body, e); }
            catch (Exception ex)
            {
                MovoError me = it.Wrap(ex); if (me == null) throw;
                bool handled = false;
                foreach (CatchClause c in Catches)
                {
                    if (c.Type == null || it.ExcMatches(me.Val, c.Type.Eval(it, e)))
                    {
                        if (c.Var != null) e.Set(c.Var, me.Val.Inst != null ? (object)me.Val : (object)me.Val);
                        it.ExcStack.Add(me);
                        try { fl = it.ExecBlock(c.Body, e); }
                        catch (Exception ex2) { pending = it.Wrap(ex2); if (pending == null) throw; }
                        finally { it.ExcStack.RemoveAt(it.ExcStack.Count - 1); }
                        handled = true; break;
                    }
                }
                if (!handled) pending = me;
            }
            if (Final != null) { Flow f2 = it.ExecBlock(Final, e); if (f2 != Flow.Normal) return f2; }
            if (pending != null) throw pending;
            return fl;
        }
    }

    public class ReturnS : Stmt { public Expr Value; public override Flow Exec(Interp it, Env e) { it.RetVal = Value == null ? null : Value.Eval(it, e); return Flow.Return; } }
    public class BreakS : Stmt { public override Flow Exec(Interp it, Env e) { return Flow.Break; } }
    public class ContinueS : Stmt { public override Flow Exec(Interp it, Env e) { return Flow.Continue; } }
    public class PassS : Stmt { public override Flow Exec(Interp it, Env e) { return Flow.Normal; } }

    public class RaiseS : Stmt
    {
        public Expr Value;
        public override Flow Exec(Interp it, Env e)
        {
            if (Value == null)
            {
                if (it.ExcStack.Count > 0) throw it.ExcStack[it.ExcStack.Count - 1];
                throw new MovoError("RuntimeError", "no active error to re-raise");
            }
            object v = Value.Eval(it, e);
            MExcType t = v as MExcType; if (t != null) v = t.Call(it, new List<object>(), null);
            MClass c = v as MClass; if (c != null) v = c.Call(it, new List<object>(), null);
            MException ex = v as MException; if (ex != null) throw new MovoError(ex);
            MInstance inst = v as MInstance;
            if (inst != null)
            {
                string msg = ""; object av; MTuple at;
                if (inst.Fields.TryGetValue("args", out av) && (at = av as MTuple) != null && at.Items.Length > 0) msg = Ops.Str(at.Items[0]);
                MException me = new MException(inst.Cls.Name, msg); me.Inst = inst;
                if (inst.Cls.Has("__str__")) me.Message = Ops.Str(inst);
                throw new MovoError(me);
            }
            throw new MovoError("TypeError", "raise can only be used with errors");
        }
    }

    public class GlobalS : Stmt
    {
        public List<string> Names; public bool NonLocal;
        public override Flow Exec(Interp it, Env e)
        {
            if (NonLocal) { if (e.Nonlocals == null) e.Nonlocals = new HashSet<string>(); foreach (string n in Names) e.Nonlocals.Add(n); }
            else { if (e.Globals == null) e.Globals = new HashSet<string>(); foreach (string n in Names) e.Globals.Add(n); }
            return Flow.Normal;
        }
    }

    public class DelS : Stmt
    {
        public Expr Target;
        public override Flow Exec(Interp it, Env e)
        {
            NameE n = Target as NameE; if (n != null) { e.Vars.Remove(n.Name); return Flow.Normal; }
            IndexE ix = Target as IndexE;
            if (ix != null)
            {
                object o = ix.Obj.Eval(it, e), k = ix.Idx.Eval(it, e);
                List<object> l = o as List<object>; if (l != null) { long i = Ops.ToL(k); if (i < 0) i += l.Count; if (i < 0 || i >= l.Count) throw new MovoError("IndexError", "index out of range"); l.RemoveAt((int)i); return Flow.Normal; }
                Dictionary<object, object> d = o as Dictionary<object, object>; if (d != null) { if (!d.Remove(k)) throw new MovoError("KeyError", Ops.Repr(k)); return Flow.Normal; }
            }
            throw new MovoError("TypeError", "cannot delete");
        }
    }

    public class AssertS : Stmt
    {
        public Expr Cond, Msg;
        public override Flow Exec(Interp it, Env e)
        {
            if (!Ops.Truthy(Cond.Eval(it, e))) throw new MovoError("AssertionError", Msg == null ? "" : Ops.Str(Msg.Eval(it, e)));
            return Flow.Normal;
        }
    }

    public class PutS : Stmt
    {
        public string Name, Alias; public bool IsPut;
        public override Flow Exec(Interp it, Env e)
        {
            object mod = it.ImportModule(Name, IsPut, e);
            string bind = Alias != null ? Alias : (IsPut ? Name.Substring(Name.LastIndexOf('.') + 1) : Name.Split('.')[0]);
            if (!IsPut && Alias == null && Name.Contains(".")) mod = it.ImportModule(Name.Split('.')[0], false, e);
            e.Set(bind, mod);
            return Flow.Normal;
        }
    }

    public class FromS : Stmt
    {
        public string Name; public List<string> Names = new List<string>(); public List<string> Aliases = new List<string>(); public bool Star;
        public override Flow Exec(Interp it, Env e)
        {
            object mod = it.ImportModule(Name, false, e);
            if (Star)
            {
                MModule m = mod as MModule; if (m != null) foreach (KeyValuePair<string, object> kv in m.Members) if (!kv.Key.StartsWith("__")) e.Set(kv.Key, kv.Value);
                return Flow.Normal;
            }
            for (int i = 0; i < Names.Count; i++) e.Set(Aliases[i] ?? Names[i], it.GetAttr(mod, Names[i]));
            return Flow.Normal;
        }
    }

    public class WithS : Stmt
    {
        public Expr X; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            object o = X.Eval(it, e);
            IMContext ctx = o as IMContext; MInstance inst = o as MInstance;
            if (ctx != null) ctx.Enter();
            else if (inst != null && inst.Cls.Has("__enter__")) it.Call(new MBound(inst, inst.Cls.Find("__enter__")), new List<object>(), null);
            try { return it.ExecBlock(Body, e); }
            finally
            {
                if (ctx != null) ctx.Exit();
                else if (inst != null && inst.Cls.Has("__exit__")) it.Call(new MBound(inst, inst.Cls.Find("__exit__")), new List<object> { null, null, null }, null);
            }
        }
    }

    public class MainS : Stmt
    {
        public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e) { it.MainBody = Body; it.MainEnv = e; return Flow.Normal; }
    }

    public class PressS : Stmt
    {
        public Expr Key; public List<Expr> Actions = new List<Expr>();
        public override Flow Exec(Interp it, Env e)
        {
            object k = Key.Eval(it, e);
            List<Expr> acts = Actions;
            it.Gui_Press(k, delegate() { foreach (Expr a in acts) a.Eval(it, e); });
            return Flow.Normal;
        }
    }

    public class DefIfS : Stmt { public override Flow Exec(Interp it, Env e) { return Flow.Normal; } }

    // name == {  statements  }   -> the statements run in their own scope; the names they create become the fields (name.field)
    //   price == {  egg == "200 coins" }   -> record, read with price.egg
    //   age == {  age = 2026 - year }      -> a field with the block's own name IS the block's value
    public class RecordS : Stmt
    {
        public string Name; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            Env inner = new Env(e);
            it.ExecBlock(Body, inner);
            MRecord r = new MRecord();
            foreach (KeyValuePair<string, object> kv in inner.Vars) r.SetAttr(kv.Key, kv.Value);
            object self;
            if (r.Vals.TryGetValue(Name, out self)) e.Set(Name, self); else e.Set(Name, r);
            return Flow.Normal;
        }
    }

    // time.do=(3.10s) { ... }  -> runs the body 3 times, waiting 10 seconds between runs
    public class TimerS : Stmt
    {
        public Expr Count, Interval; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            long n = Ops.ToL(Count.Eval(it, e)); double sec = Ops.ToD(Interval.Eval(it, e));
            for (long i = 0; i < n; i++)
            {
                if (i > 0 && sec > 0) System.Threading.Thread.Sleep((int)Math.Min(sec * 1000.0, 2000000000.0));
                Flow f = it.ExecBlock(Body, e);
                if (f == Flow.Break) break; if (f == Flow.Return) return f;
            }
            return Flow.Normal;
        }
    }
}
