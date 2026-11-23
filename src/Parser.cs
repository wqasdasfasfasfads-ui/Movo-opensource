using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovoLang
{
    public class Tok { public int T; public string S; public object V; public bool F; }  // T: 0 end,1 num,2 str,3 name,4 op

    public static class ParserUtil
    {
        static Regex StrRe = new Regex("(f?\"(?:\\\\.|[^\"\\\\])*\"|f?'(?:\\\\.|[^'\\\\])*')");

        public static string OutsideStrings(string s, Func<string, string> fn)
        {
            string[] parts = StrRe.Split(s); StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++) sb.Append(i % 2 == 1 ? parts[i] : fn(parts[i]));
            return sb.ToString();
        }

        static string Dur(Match m)
        {
            double v = double.Parse(m.Groups[1].Value, Ops.IC); string u = m.Groups[2].Value;
            double mul = u == "ms" ? 0.001 : u == "s" ? 1 : u == "m" ? 60 : 3600; v *= mul;
            if (v == Math.Floor(v)) return ((long)v).ToString(Ops.IC);
            return v.ToString("R", Ops.IC);
        }

        public static string ConvertExpr(string e)
        {
            e = e.Trim();
            bool hasDat = e.Contains(".dat(");
            return OutsideStrings(e, delegate(string p)
            {
                p = Regex.Replace(p, @"\bprinton!\s*\(", "print(");
                p = Regex.Replace(p, @"\b(?:inputso|iputso)\s*\(", "input(");
                p = Regex.Replace(p, @"(?<=[\w\)\]])\s+times\s+(?=[\w\(\-\.])", " * ");
                p = Regex.Replace(p, @"(?<=[\w\)\]])\s+undo\s+(?=[\w\(\-\.])", " / ");
                p = Regex.Replace(p, @"\b(\d+(?:\.\d+)?)(ms|s|m|h)\b", new MatchEvaluator(Dur));
                p = Regex.Replace(p, @"\bso\s*\(", "(");
                if (hasDat) { p = p.Replace(".dat(", ".config("); p = Regex.Replace(p, @"(\w+)!=(?!=)", "$1="); }
                p = Regex.Replace(p, @"\b(\w+)!\(", "$1(");
                return p;
            });
        }

        // splits text on a word (and/or) outside quotes and brackets
        public static List<string> SplitWord(string s, string word)
        {
            List<string> parts = new List<string>(); int depth = 0; char q = '\0'; int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; continue; }
                if (c == '"' || c == '\'') { q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++; else if (c == ')' || c == ']' || c == '}') depth--;
                else if (depth == 0 && char.IsWhiteSpace(c))
                {
                    int j = i + 1;
                    if (j + word.Length < s.Length && string.CompareOrdinal(s, j, word, 0, word.Length) == 0 && char.IsWhiteSpace(s[j + word.Length]))
                    {
                        parts.Add(s.Substring(start, i - start)); start = j + word.Length + 1; i = start - 1;
                    }
                }
            }
            parts.Add(s.Substring(start)); return parts;
        }

        static bool Simple(string v)
        {
            if (Regex.IsMatch(v, @"==|!=|<=|>=|<|>")) return false;
            if (SplitWord(v, "and").Count > 1 || SplitWord(v, "or").Count > 1 || SplitWord(v, "in").Count > 1 || v.StartsWith("not ")) return false;
            return true;
        }

        public static string ConvertCond(string c)
        {
            c = ConvertExpr(c);
            Match m = Regex.Match(c, @"^(\w+)\s*==\s*not\s+(.+)$");
            if (m.Success && Simple(m.Groups[2].Value)) return m.Groups[1].Value + " != " + m.Groups[2].Value;
            m = Regex.Match(c, @"^(\w+)\s*==\s*(.+)$");
            if (m.Success)
            {
                string rhs = m.Groups[2].Value.Trim();
                List<string> ors = SplitWord(rhs, "or");
                if (ors.Count > 1 && ors.TrueForAll(Simple)) return m.Groups[1].Value + " in (" + string.Join(", ", ors.ConvertAll(x => x.Trim()).ToArray()) + ",)";
                List<string> ands = SplitWord(rhs, "and");
                if (ands.Count > 1 && ands.TrueForAll(Simple))
                {
                    List<string> r = new List<string>(); foreach (string v in ands) r.Add(m.Groups[1].Value + " == " + v.Trim());
                    return string.Join(" and ", r.ToArray());
                }
            }
            return c;
        }

        public static List<string> SplitActions(string a)
        {
            List<string> parts = new List<string>(); StringBuilder cur = new StringBuilder(); int depth = 0; char q = '\0';
            foreach (char ch in a)
            {
                if (q != '\0') { cur.Append(ch); if (ch == q) q = '\0'; }
                else if (ch == '"' || ch == '\'') { q = ch; cur.Append(ch); }
                else if (ch == '(' || ch == '[' || ch == '{') { depth++; cur.Append(ch); }
                else if (ch == ')' || ch == ']' || ch == '}') { depth--; cur.Append(ch); }
                else if (ch == ';' && depth == 0) { parts.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(ch);
            }
            parts.Add(cur.ToString());
            List<string> r = new List<string>(); foreach (string p in parts) if (p.Trim().Length > 0) r.Add(p.Trim()); return r;
        }

        public static List<string> SplitTopComma(string s)
        {
            List<string> r = new List<string>(); int depth = 0; char q = '\0'; int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; continue; }
                if (c == '"' || c == '\'') q = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0) { r.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            string last = s.Substring(start); if (last.Trim().Length > 0 || r.Count > 0) r.Add(last);
            return r;
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i == s.Length - 1) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break; case '\\': sb.Append('\\'); break; case '\'': sb.Append('\''); break; case '"': sb.Append('"'); break;
                    case 'u': if (i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; } break;
                    default: sb.Append('\\'); sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        public static List<Tok> Lex(string s)
        {
            List<Tok> r = new List<Tok>(); int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int st = i;
                    if (c == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X')) { i += 2; while (i < s.Length && Uri.IsHexDigit(s[i])) i++; r.Add(new Tok { T = 1, V = Convert.ToInt64(s.Substring(st + 2, i - st - 2), 16) }); continue; }
                    bool isF = false;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                    if (i < s.Length && s[i] == '.' && !(i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))) { isF = true; i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
                    if (i < s.Length && (s[i] == 'e' || s[i] == 'E') && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || ((s[i + 1] == '-' || s[i + 1] == '+') && i + 2 < s.Length && char.IsDigit(s[i + 2])))) { isF = true; i += 2; while (i < s.Length && char.IsDigit(s[i])) i++; }
                    string txt = s.Substring(st, i - st).Replace("_", "");
                    Tok t = new Tok { T = 1 };
                    if (isF) t.V = double.Parse(txt, Ops.IC); else { long lv; if (long.TryParse(txt, NumberStyles.None, Ops.IC, out lv)) t.V = lv; else t.V = double.Parse(txt, Ops.IC); }
                    r.Add(t); continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i; while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    string name = s.Substring(st, i - st);
                    if (i < s.Length && (s[i] == '"' || s[i] == '\'') && (name == "f" || name == "r" || name == "b" || name == "u" || name == "fr" || name == "rf"))
                    {
                        bool isFs = name.Contains("f"), raw = name.Contains("r");
                        c = s[i]; string body = ReadStr(s, ref i);
                        r.Add(new Tok { T = 2, S = body, F = isFs, V = raw ? "raw" : null }); continue;
                    }
                    r.Add(new Tok { T = 3, S = name }); continue;
                }
                if (c == '"' || c == '\'')
                {
                    string body = ReadStr(s, ref i);
                    r.Add(new Tok { T = 2, S = body, F = false }); continue;
                }
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two == "**" || two == "//" || two == "==" || two == "!=" || two == "<=" || two == ">=" || two == "->") { r.Add(new Tok { T = 4, S = two }); i += 2; continue; }
                if ("+-*/%()[]{},.:<>=&|^~@".IndexOf(c) >= 0) { r.Add(new Tok { T = 4, S = c.ToString() }); i++; continue; }
                throw new MovoError("SyntaxError", "unexpected character '" + c + "'");
            }
            r.Add(new Tok { T = 0, S = "" });
            return r;
        }

        static string ReadStr(string s, ref int i)
        {
            char q = s[i]; int st = i + 1; i++;
            while (i < s.Length && s[i] != q) { if (s[i] == '\\') i++; i++; }
            if (i >= s.Length) throw new MovoError("SyntaxError", "unterminated string");
            string body = s.Substring(st, i - st); i++; return body;
        }

        public static Expr ParseExpr(string text) { ExprParser p = new ExprParser(Lex(text)); Expr e = p.ParseTop(); p.ExpectEnd(); return e; }

        public static FStrE ParseFString(string raw)
        {
            FStrE f = new FStrE(); StringBuilder lit = new StringBuilder(); int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '{' && i + 1 < raw.Length && raw[i + 1] == '{') { lit.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < raw.Length && raw[i + 1] == '}') { lit.Append('}'); i += 2; continue; }
                if (c == '{')
                {
                    int depth = 0, j = i + 1; char q = '\0'; int colon = -1, bang = -1;
                    for (; j < raw.Length; j++)
                    {
                        char d = raw[j];
                        if (q != '\0') { if (d == q) q = '\0'; continue; }
                        if (d == '"' || d == '\'') { q = d; continue; }
                        if (d == '(' || d == '[' || d == '{') depth++;
                        else if (d == ')' || d == ']') depth--;
                        else if (d == '}') { if (depth == 0) break; depth--; }
                        else if (d == ':' && depth == 0 && colon < 0) colon = j;
                        else if (d == '!' && depth == 0 && colon < 0 && bang < 0 && j + 1 < raw.Length && raw[j + 1] != '=') bang = j;
                    }
                    if (j >= raw.Length) throw new MovoError("SyntaxError", "f-string: missing closing }");
                    int exprEnd = bang >= 0 ? bang : (colon >= 0 ? colon : j);
                    string ex = raw.Substring(i + 1, exprEnd - i - 1);
                    char conv = '\0'; if (bang >= 0) conv = raw[bang + 1];
                    string spec = colon >= 0 ? raw.Substring(colon + 1, j - colon - 1) : null;
                    f.Lits.Add(Unescape(lit.ToString())); lit.Length = 0;
                    f.Exprs.Add(ParseExpr(ex.Trim())); f.Specs.Add(spec); f.Convs.Add(conv);
                    i = j + 1; continue;
                }
                lit.Append(c); i++;
            }
            f.Lits.Add(Unescape(lit.ToString()));
            return f;
        }
    }

    public class ExprParser
    {
        List<Tok> t; int p;
        public ExprParser(List<Tok> toks) { t = toks; }
        Tok Cur { get { return t[p]; } }
        bool IsOp(string s) { return t[p].T == 4 && t[p].S == s; }
        bool IsKw(string s) { return t[p].T == 3 && t[p].S == s; }
        Exception Err(string m) { return new MovoError("SyntaxError", m); }
        void ExpectOp(string s) { if (!IsOp(s)) throw Err("expected '" + s + "'"); p++; }
        public void ExpectEnd() { if (Cur.T != 0) throw Err("unexpected token '" + (Cur.S ?? Ops.Str(Cur.V)) + "'"); }

        public Expr ParseTop()
        {
            Expr first = ParseTernary();
            if (IsOp(","))
            {
                ListE tup = new ListE(); tup.IsTuple = true; tup.Items.Add(first);
                while (IsOp(",")) { p++; if (Cur.T == 0 || IsOp(")") || IsOp("=")) break; tup.Items.Add(ParseTernary()); }
                return tup;
            }
            return first;
        }

        Expr ParseTernary()
        {
            if (IsKw("lambda")) return ParseLambda();
            Expr c = ParseOr();
            if (IsKw("if"))
            {
                p++; Expr cond = ParseOr(); if (!IsKw("else")) throw Err("expected else"); p++;
                Expr b = ParseTernary(); return new TernE(cond, c, b);
            }
            return c;
        }

        Expr ParseLambda()
        {
            p++; LambdaE l = new LambdaE(); l.Params = new List<Param>();
            while (!IsOp(":"))
            {
                Param pr = new Param();
                if (IsOp("*")) { p++; pr.Star = true; } else if (IsOp("**")) { p++; pr.DStar = true; }
                pr.Name = Cur.S; p++;
                if (IsOp("=")) { p++; pr.Default = ParseTernary(); }
                l.Params.Add(pr); if (IsOp(",")) p++;
            }
            p++; l.Body = ParseTernary(); return l;
        }

        Expr ParseOr() { Expr l = ParseAnd(); while (IsKw("or")) { p++; l = new BoolE(false, l, ParseAnd()); } return l; }
        Expr ParseAnd() { Expr l = ParseNot(); while (IsKw("and")) { p++; l = new BoolE(true, l, ParseNot()); } return l; }
        Expr ParseNot() { if (IsKw("not")) { p++; return new UnE("not", ParseNot()); } return ParseCmp(); }

        Expr ParseCmp()
        {
            Expr l = ParseBit(); CmpE c = null;
            while (true)
            {
                string op = null;
                if (Cur.T == 4 && (Cur.S == "==" || Cur.S == "!=" || Cur.S == "<" || Cur.S == ">" || Cur.S == "<=" || Cur.S == ">=")) { op = Cur.S; p++; }
                else if (IsKw("in")) { op = "in"; p++; }
                else if (IsKw("not") && t[p + 1].T == 3 && t[p + 1].S == "in") { op = "not in"; p += 2; }
                else if (IsKw("is")) { p++; if (IsKw("not")) { p++; op = "is not"; } else op = "is"; }
                if (op == null) break;
                if (c == null) { c = new CmpE(); c.Items.Add(l); }
                c.Ops_.Add(op); c.Items.Add(ParseBit());
            }
            return c ?? l;
        }

        Expr ParseBit()
        {
            Expr l = ParseArith();
            while (Cur.T == 4 && (Cur.S == "&" || Cur.S == "|" || Cur.S == "^")) { string op = Cur.S; p++; l = new BinE(op, l, ParseArith()); }
            return l;
        }
        Expr ParseArith()
        {
            Expr l = ParseTerm();
            while (Cur.T == 4 && (Cur.S == "+" || Cur.S == "-")) { string op = Cur.S; p++; l = new BinE(op, l, ParseTerm()); }
            return l;
        }
        Expr ParseTerm()
        {
            Expr l = ParseFactor();
            while (Cur.T == 4 && (Cur.S == "*" || Cur.S == "/" || Cur.S == "//" || Cur.S == "%")) { string op = Cur.S; p++; l = new BinE(op, l, ParseFactor()); }
            return l;
        }
        Expr ParseFactor()
        {
            if (Cur.T == 4 && (Cur.S == "-" || Cur.S == "+" || Cur.S == "~")) { string op = Cur.S; p++; return new UnE(op, ParseFactor()); }
            Expr b = ParsePostfix();
            if (IsOp("**")) { p++; return new BinE("**", b, ParseFactor()); }
            return b;
        }

        Expr ParsePostfix()
        {
            Expr e = ParseAtom();
            while (true)
            {
                if (IsOp(".")) { p++; if (Cur.T != 3) throw Err("expected a name after '.'"); e = new AttrE(e, Cur.S); p++; }
                else if (IsOp("(")) { p++; e = ParseCall(e); }
                else if (IsOp("[")) { p++; e = ParseIndex(e); }
                else break;
            }
            return e;
        }

        Expr ParseCall(Expr fn)
        {
            CallE c = new CallE(); c.Fn = fn;
            while (!IsOp(")"))
            {
                if (IsOp("*")) { p++; c.Args.Add(ParseTernary()); c.Star.Add(true); }
                else if (IsOp("**")) { p++; c.DStar = ParseTernary(); }
                else if (Cur.T == 3 && t[p + 1].T == 4 && t[p + 1].S == "=" ) { string n = Cur.S; p += 2; c.KwNames.Add(n); c.KwVals.Add(ParseTernary()); }
                else
                {
                    Expr a = ParseTernary();
                    if (IsKw("for")) a = ParseComp(a);
                    c.Args.Add(a); c.Star.Add(false);
                }
                if (IsOp(",")) p++; else if (!IsOp(")")) throw Err("expected ',' or ')'");
            }
            p++; return c;
        }

        Expr ParseComp(Expr elt)
        {
            p++; CompE c = new CompE(); c.Elt = elt;
            ListE tg = new ListE(); tg.IsTuple = true;
            tg.Items.Add(ParseBit()); while (IsOp(",")) { p++; tg.Items.Add(ParseBit()); }
            c.Target = tg.Items.Count == 1 ? tg.Items[0] : tg;
            if (!IsKw("in")) throw Err("expected in"); p++;
            c.Iter = ParseOr();
            if (IsKw("if")) { p++; c.Cond = ParseOr(); }
            return c;
        }

        Expr ParseIndex(Expr obj)
        {
            Expr a = null, b = null, c = null; bool slice = false;
            if (!IsOp(":")) a = ParseTernary();
            if (IsOp(":")) { slice = true; p++; if (!IsOp(":") && !IsOp("]")) b = ParseTernary(); if (IsOp(":")) { p++; if (!IsOp("]")) c = ParseTernary(); } }
            ExpectOp("]");
            return new IndexE(obj, slice ? (Expr)new SliceE(a, b, c) : a);
        }

        Expr ParseAtom()
        {
            Tok k = Cur;
            if (k.T == 1) { p++; return new ConstE(k.V); }
            if (k.T == 2)
            {
                StringBuilder sb = new StringBuilder(); List<Expr> parts = new List<Expr>(); bool anyF = false;
                while (Cur.T == 2)
                {
                    Tok s = Cur; p++;
                    if (s.F) { anyF = true; parts.Add(ParserUtil.ParseFString(s.S)); }
                    else parts.Add(new ConstE(s.V != null ? s.S : ParserUtil.Unescape(s.S)));
                }
                if (!anyF) { foreach (Expr x in parts) sb.Append((string)((ConstE)x).V); return new ConstE(sb.ToString()); }
                if (parts.Count == 1) return parts[0];
                FStrE all = new FStrE(); all.Lits.Add("");
                foreach (Expr x in parts) { all.Exprs.Add(x); all.Specs.Add(null); all.Convs.Add('\0'); all.Lits.Add(""); }
                return all;
            }
            if (k.T == 3)
            {
                p++;
                if (k.S == "True") return new ConstE(true);
                if (k.S == "False") return new ConstE(false);
                if (k.S == "None") return new ConstE(null);
                return new NameE(k.S);
            }
            if (IsOp("("))
            {
                p++;
                if (IsOp(")")) { p++; ListE et = new ListE(); et.IsTuple = true; return et; }
                Expr first = ParseTernary();
                if (IsKw("for")) { first = ParseComp(first); ExpectOp(")"); return first; }
                if (IsOp(","))
                {
                    ListE tup = new ListE(); tup.IsTuple = true; tup.Items.Add(first);
                    while (IsOp(",")) { p++; if (IsOp(")")) break; tup.Items.Add(ParseTernary()); }
                    ExpectOp(")"); return tup;
                }
                ExpectOp(")"); return first;
            }
            if (IsOp("["))
            {
                p++; ListE l = new ListE();
                if (IsOp("]")) { p++; return l; }
                Expr first = ParseTernary();
                if (IsKw("for")) { Expr c = ParseComp(first); ExpectOp("]"); return c; }
                l.Items.Add(first);
                while (IsOp(",")) { p++; if (IsOp("]")) break; l.Items.Add(ParseTernary()); }
                ExpectOp("]"); return l;
            }
            if (IsOp("{"))
            {
                p++; DictE d = new DictE();
                while (!IsOp("}"))
                {
                    Expr key = ParseTernary(); ExpectOp(":"); Expr val = ParseTernary(); d.Keys.Add(key); d.Vals.Add(val);
                    if (IsOp(",")) p++; else if (!IsOp("}")) throw Err("expected ',' or '}'");
                }
                p++; return d;
            }
            throw Err("unexpected token '" + (k.S ?? Ops.Str(k.V)) + "'" + (k.T == 0 ? " (end of line)" : ""));
        }
    }

    // ================= Program =================
    public class Line { public string S; public int Ind; public int No; }

    public class ProgParser
    {
        List<Line> L = new List<Line>(); int p;

        public static List<Stmt> Parse(string src)
        {
            ProgParser pp = new ProgParser(); pp.Prep(src);
            List<Stmt> r = new List<Stmt>();
            while (pp.p < pp.L.Count)
            {
                if (pp.L[pp.p].S.StartsWith("}")) { throw new MovoError("SyntaxError", "extra } at line " + pp.L[pp.p].No); }
                r.Add(pp.ParseStmt());
            }
            return r;
        }

        static string StripComment(string s)
        {
            char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; continue; }
                if (c == '"' || c == '\'') q = c; else if (c == '#') return s.Substring(0, i);
            }
            return s;
        }

        void Prep(string src)
        {
            string[] lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = StripComment(lines[i]).TrimEnd();
                if (raw.Trim().Length == 0) continue;
                int ind = 0; foreach (char c in raw) { if (c == ' ') ind++; else if (c == '\t') ind += 4; else break; }
                L.Add(new Line { S = raw.Trim().TrimStart('\uFEFF'), Ind = ind, No = i + 1 });
            }
        }

        MovoError Err(Line ln, string m) { MovoError e = new MovoError("SyntaxError", m); e.Line = ln.No; return e; }

        List<Stmt> BlockBrace()
        {
            List<Stmt> r = new List<Stmt>();
            while (p < L.Count && !L[p].S.StartsWith("}")) r.Add(ParseStmt());
            return r;
        }
        List<Stmt> BlockIndent(int ind)
        {
            List<Stmt> r = new List<Stmt>();
            // body without deeper indent: take one next line (unless else/elif/except/catch/finally/})
            if (p < L.Count && L[p].Ind <= ind && !L[p].S.StartsWith("}") && !Regex.IsMatch(L[p].S, @"^(else|elif|except|catch|finally)\b"))
            { r.Add(ParseStmt()); return r; }
            while (p < L.Count && L[p].Ind > ind) r.Add(ParseStmt());
            return r;
        }
        void CloseBrace() { if (p < L.Count && L[p].S == "}") p++; }

        static Expr Ex(string s, Line ln)
        {
            try { return ParserUtil.ParseExpr(ParserUtil.ConvertExpr(s)); }
            catch (MovoError e) { e.Line = ln.No; throw; }
        }
        static Expr Cond(string s, Line ln)
        {
            try { return ParserUtil.ParseExpr(ParserUtil.ConvertCond(s)); }
            catch (MovoError e) { e.Line = ln.No; throw; }
        }

        List<Param> Params(string s, Line ln)
        {
            List<Param> r = new List<Param>();
            foreach (string part in ParserUtil.SplitTopComma(s))
            {
                string x = part.Trim(); if (x.Length == 0) continue;
                Param pr = new Param();
                if (x.StartsWith("**")) { pr.DStar = true; x = x.Substring(2).Trim(); }
                else if (x.StartsWith("*")) { pr.Star = true; x = x.Substring(1).Trim(); }
                int eq = x.IndexOf('=');
                if (eq > 0 && !pr.Star && !pr.DStar) { pr.Default = Ex(x.Substring(eq + 1), ln); x = x.Substring(0, eq).Trim(); }
                int col = x.IndexOf(':'); if (col > 0) x = x.Substring(0, col).Trim();
                pr.Name = x; r.Add(pr);
            }
            return r;
        }

        Stmt ParseStmt()
        {
            Line ln = L[p]; string s = ln.S; p++;
            Stmt st = ParseStmt2(ln, s); st.Line = ln.No; return st;
        }

        Stmt ParseStmt2(Line ln, string s)
        {
            Match m;
            m = Regex.Match(s, @"^if\.press!?\s*\(\s*(.+?)\s*\)\s*\.so\.\((.*)\)$");
            if (m.Success)
            {
                PressS ps = new PressS(); ps.Key = Ex(m.Groups[1].Value, ln);
                foreach (string a in ParserUtil.SplitActions(m.Groups[2].Value))
                {
                    string c = ParserUtil.ConvertExpr(a);
                    if (Regex.IsMatch(c, @"^[\w\.]+$")) c += "()";
                    ps.Actions.Add(ParserUtil.ParseExpr(c));
                }
                return ps;
            }
            // time.do=(3.10s) {  ...  }   -> 3 times, every 10 seconds      (also: time.do=(COUNT, SECONDS) {)
            m = Regex.Match(s, @"^time\.do\s*=\s*\((.*)\)\s*(\{|:)$");
            if (m.Success)
            {
                TimerS ts = new TimerS(); string spec = m.Groups[1].Value.Trim();
                Match dm = Regex.Match(spec, @"^(\d+)\s*\.\s*(\d+(?:\.\d+)?)\s*(ms|s|m|h)?$");
                if (dm.Success)
                {
                    string un = dm.Groups[3].Value; double mul = un == "ms" ? 0.001 : un == "m" ? 60.0 : un == "h" ? 3600.0 : 1.0;
                    ts.Count = new ConstE(long.Parse(dm.Groups[1].Value, Ops.IC));
                    ts.Interval = new ConstE(double.Parse(dm.Groups[2].Value, Ops.IC) * mul);
                }
                else
                {
                    List<string> tp = ParserUtil.SplitTopComma(spec);
                    if (tp.Count != 2) throw Err(ln, "time.do=(COUNT.INTERVAL) { ... }  for example  time.do=(3.10s) {  runs 3 times, every 10 seconds");
                    ts.Count = Ex(tp[0], ln); ts.Interval = Ex(tp[1], ln);
                }
                if (m.Groups[2].Value == "{") { ts.Body = BlockBrace(); CloseBrace(); } else ts.Body = BlockIndent(ln.Ind);
                return ts;
            }
            // price == {  egg == "200 coins"  bread == "180 coins"  }   -> a record, read with price.egg
            // age == {  age = 2026 - year  }                             -> ordinary code inside; a field named like the block is its value
            m = Regex.Match(s, @"^(\w+)\s*==\s*\{$");
            if (m.Success) { RecordS rs = new RecordS(); rs.Name = m.Groups[1].Value; rs.Body = BlockRecord(ln); return rs; }
            if (recDepth > 0)   // inside such a block   key == value   means   key = value
            {
                m = Regex.Match(s, @"^(\w+)\s*==\s*(.+)$");
                if (m.Success)
                {
                    AssignS ra = new AssignS(); ra.Target = new NameE(m.Groups[1].Value);
                    ra.Value = Ex(m.Groups[2].Value.TrimEnd(',', ';').TrimEnd(), ln); return ra;
                }
            }
            m = Regex.Match(s, @"^class\s+(\w+)\s*(?:\((.*)\))?\s*(\{|:)$");
            if (m.Success)
            {
                ClassDefS c = new ClassDefS(); c.Name = m.Groups[1].Value;
                if (m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0) c.Base = Ex(m.Groups[2].Value.Split(',')[0], ln);
                if (m.Groups[3].Value == "{") { c.Body = BlockBrace(); CloseBrace(); } else c.Body = BlockIndent(ln.Ind);
                return c;
            }
            if (Regex.IsMatch(s, @"^try\s*\{$")) { return ParseTry(ln, true, null); }
            m = Regex.Match(s, @"^try\s*:$");
            if (m.Success) return ParseTry(ln, false, null);
            if (Regex.IsMatch(s, @"^while\.so\s*\{$"))
            {
                RepeatS rp = new RepeatS(); rp.Body = BlockBrace();
                if (p >= L.Count) throw Err(ln, "while.so{ is not closed, it needs }so.N.times at the end");
                Line cl = L[p];
                Match cm = Regex.Match(cl.S, @"^\}\s*so\.(.+?)\.times$");
                if (!cm.Success) throw Err(cl, "while.so{ must end with }so.N.times (for example }so.10.times)");
                p++; rp.Count = Ex(cm.Groups[1].Value, cl);
                return rp;
            }
            m = Regex.Match(s, @"^host\.local\s*(?:\((.*)\))?\s*\{$");
            if (m.Success)
            {
                HostS hs = new HostS(); hs.Port = null;
                List<string> hargs = ParserUtil.SplitTopComma(m.Groups[1].Success ? m.Groups[1].Value : "");
                foreach (string ha in hargs)
                {
                    string hx = ha.Trim(); if (hx.Length == 0) continue;
                    if (hx == "open") hs.Open = true; else if (hs.Port == null) hs.Port = Ex(hx, ln); else throw Err(ln, "host.local(PORT) or host.local(PORT, open)");
                }
                hs.Body = BlockBrace(); CloseBrace(); return hs;
            }
            m = Regex.Match(s, @"^(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')\s*->\s*(.+)$");
            if (m.Success)
            {
                RouteS rt = new RouteS(); rt.Path = Ex(m.Groups[1].Value, ln); string rhs0 = m.Groups[2].Value.Trim();
                Match rm = Regex.Match(rhs0, @"^(file|folder)\s+(.+)$");
                if (rm.Success) { rt.Kind = rm.Groups[1].Value == "file" ? 1 : 2; rt.Val = Ex(rm.Groups[2].Value, ln); }
                else { rt.Kind = 0; rt.Val = Ex(rhs0, ln); }
                return rt;
            }
            m = Regex.Match(s, @"^([A-Za-z_][\w\.]*)\s*\((.*)\)\s*\{$");
            if (m.Success && p < L.Count && Regex.IsMatch(L[p].S, @"^(\w+|""[^""]*""|'[^']*')\s*->"))
            {
                // name(args){ field -> value ... }   ==   name(args, {"field": value, ...})
                List<string> pairs = new List<string>();
                while (p < L.Count && !L[p].S.StartsWith("}"))
                {
                    Line fl = L[p]; p++;
                    Match fm = Regex.Match(fl.S, @"^(\w+|""[^""]*""|'[^']*')\s*->\s*(.+)$");
                    if (!fm.Success) throw Err(fl, "inside  name(...){ }  write lines like:  field -> value   (value can be: ask  or  ask \"Prompt: \"  for a hidden prompt)");
                    string fname = fm.Groups[1].Value.Trim('"', '\'').Replace("\\", "\\\\").Replace("\"", "\\\""); string fval = fm.Groups[2].Value.Trim();
                    Match am = Regex.Match(fval, @"^ask(?:\s+(.+))?$");
                    pairs.Add("\"" + fname + "\": " + (am.Success ? "hidden_input(" + (am.Groups[1].Success ? am.Groups[1].Value : "\"\"") + ")" : "(" + fval + ")"));
                }
                CloseBrace();
                string cargs = m.Groups[2].Value.Trim(); string dict = "{" + string.Join(", ", pairs.ToArray()) + "}";
                ExprS ces = new ExprS(); ces.X = Ex(m.Groups[1].Value + "(" + (cargs.Length > 0 ? cargs + ", " : "") + dict + ")", ln); return ces;
            }
            if (Regex.IsMatch(s, @"^start\s+in\s*\{$", RegexOptions.IgnoreCase)) { MainS ms = new MainS(); ms.Body = BlockBrace(); CloseBrace(); return ms; }
            m = Regex.Match(s, @"^(?:fn|def)\s+(\w+)\s*\((.*)\)\s*(?:->\s*[\w\.\[\], ]+)?\s*\{$");
            if (m.Success)
            {
                FuncDefS f = new FuncDefS(); f.Name = m.Groups[1].Value; f.Params = Params(m.Groups[2].Value, ln); f.Body = BlockBrace(); CloseBrace(); return f;
            }
            m = Regex.Match(s, @"^([\w\.]+!?\(.*\))\s*\{$");
            if (m.Success && !Regex.IsMatch(s, @"^(if|while|for|elif)\b"))
            {
                WithS w = new WithS(); w.X = Ex(m.Groups[1].Value, ln); w.Body = BlockBrace(); CloseBrace(); return w;
            }
            m = Regex.Match(s, @"^(if|elif|while|for)\s+(.+?)\s*(?:so)?\s*:$");
            if (m.Success)
            {
                string kw = m.Groups[1].Value, cond = m.Groups[2].Value;
                if (kw == "if") return ParseIfIndent(ln, cond);
                if (kw == "while") { WhileS w = new WhileS(); w.Cond = Cond(cond, ln); w.Body = BlockIndent(ln.Ind); return w; }
                if (kw == "for") return ParseFor(ln, cond, false);
                throw Err(ln, "elif without if");
            }
            m = Regex.Match(s, @"^(if|while)\s+(.+?)\s*\{$");
            if (m.Success)
            {
                if (m.Groups[1].Value == "if") return ParseIfBrace(ln, m.Groups[2].Value);
                WhileS w = new WhileS(); w.Cond = Cond(m.Groups[2].Value, ln); w.Body = BlockBrace(); CloseBrace(); return w;
            }
            m = Regex.Match(s, @"^for\s+(.+?)\s*\{$");
            if (m.Success) return ParseFor(ln, m.Groups[1].Value, true);
            if (Regex.IsMatch(s, @"^else\s*:$")) throw Err(ln, "else without if");
            m = Regex.Match(s, @"^def\s+if\s+(\w+)\s*\((.*)\)\s*:$");
            if (m.Success) { FuncDefS f = new FuncDefS(); f.Name = "on_" + m.Groups[1].Value; f.Params = Params(m.Groups[2].Value, ln); f.Body = BlockIndent(ln.Ind); return f; }
            m = Regex.Match(s, @"^(?:fn|def)\s+(\w+)\s*\((.*)\)\s*(?:->\s*[\w\.\[\], ]+)?\s*:$");
            if (m.Success) { FuncDefS f = new FuncDefS(); f.Name = m.Groups[1].Value; f.Params = Params(m.Groups[2].Value, ln); f.Body = BlockIndent(ln.Ind); return f; }
            m = Regex.Match(s, @"^with\s+(.+?)\s*:$");
            if (m.Success) { WithS w = new WithS(); w.X = Ex(m.Groups[1].Value, ln); w.Body = BlockIndent(ln.Ind); return w; }
            m = Regex.Match(s, @"^put\.here\s+([\w\.]+)(?:\s+as\s+(\w+))?$");
            if (m.Success) { PutS ps = new PutS(); ps.IsPut = true; ps.Name = m.Groups[1].Value; ps.Alias = m.Groups[2].Success ? m.Groups[2].Value : null; return ps; }
            if (Regex.IsMatch(s, @"^import\b") || Regex.IsMatch(s, @"^from\s+[\w\.]+\s+import\b")) throw Err(ln, "'import' does not work in Movo. Write:  put.here NAME   (example: put.here web)");
            m = Regex.Match(s, @"^use\s+(?:python\s+)?([\w\.]+)(?:\s+as\s+(\w+))?$");
            if (m.Success) { PutS ps = new PutS(); ps.IsPut = false; ps.Name = m.Groups[1].Value; ps.Alias = m.Groups[2].Success ? m.Groups[2].Value : null; return ps; }
            m = Regex.Match(s, @"^from\s+([\w\.]+)\s+use\s+(.+)$");
            if (m.Success)
            {
                FromS f = new FromS(); f.Name = m.Groups[1].Value; string what = m.Groups[2].Value.Trim();
                if (what == "*") f.Star = true;
                else foreach (string part in what.Trim('(', ')').Split(','))
                    {
                        Match mm = Regex.Match(part.Trim(), @"^(\w+)(?:\s+as\s+(\w+))?$");
                        if (!mm.Success) throw Err(ln, "invalid import");
                        f.Names.Add(mm.Groups[1].Value); f.Aliases.Add(mm.Groups[2].Success ? mm.Groups[2].Value : null);
                    }
                return f;
            }
            if (Regex.IsMatch(s, @"^use\s+rust\b")) throw Err(ln, "Rust imports are not supported");
            return ParseSimple(ln, s);
        }

        int recDepth;
        // reads the statements of a  name == {  block up to its closing }
        List<Stmt> BlockRecord(Line open)
        {
            List<Stmt> r = new List<Stmt>(); recDepth++;
            try
            {
                while (true)
                {
                    if (p >= L.Count) throw Err(open, "{ is not closed, the block needs a } at the end");
                    if (Regex.IsMatch(L[p].S, @"^\},?$")) { p++; break; }
                    r.Add(ParseStmt());
                }
            }
            finally { recDepth--; }
            return r;
        }

        Stmt ParseIfIndent(Line ln, string cond)
        {
            IfS n = new IfS(); n.Conds.Add(Cond(cond, ln)); n.Bodies.Add(BlockIndent(ln.Ind));
            while (p < L.Count && L[p].Ind == ln.Ind)
            {
                Line l2 = L[p]; Match m = Regex.Match(l2.S, @"^elif\s+(.+?)\s*(?:so)?\s*:$");
                if (m.Success) { p++; n.Conds.Add(Cond(m.Groups[1].Value, l2)); n.Bodies.Add(BlockIndent(ln.Ind)); continue; }
                if (Regex.IsMatch(l2.S, @"^else\s*:$")) { p++; n.Else = BlockIndent(ln.Ind); }
                break;
            }
            return n;
        }

        Stmt ParseIfBrace(Line ln, string cond)
        {
            IfS n = new IfS(); n.Conds.Add(Cond(cond, ln)); n.Bodies.Add(BlockBrace());
            while (p < L.Count)
            {
                Line l2 = L[p];
                if (l2.S == "}") { p++; break; }
                Match m = Regex.Match(l2.S, @"^\}\s*(?:else\s+if|elif)\s+(.+?)\s*\{$");
                if (m.Success) { p++; n.Conds.Add(Cond(m.Groups[1].Value, l2)); n.Bodies.Add(BlockBrace()); continue; }
                if (Regex.IsMatch(l2.S, @"^\}\s*else\s*\{$")) { p++; n.Else = BlockBrace(); CloseBrace(); break; }
                break;
            }
            return n;
        }

        Stmt ParseFor(Line ln, string head, bool brace)
        {
            List<string> parts = ParserUtil.SplitWord(head, "in");
            if (parts.Count < 2) throw Err(ln, "expected: for x in ...");
            ForS f = new ForS(); string target = parts[0].Trim();
            string iter = head.Substring(parts[0].Length).Trim(); iter = iter.Substring(2).Trim();
            f.Target = ParserUtil.ParseExpr(target); f.Iter = Ex(iter, ln);
            if (brace) { f.Body = BlockBrace(); CloseBrace(); } else f.Body = BlockIndent(ln.Ind);
            return f;
        }

        Stmt ParseTry(Line ln, bool brace, object unused)
        {
            TryS t = new TryS();
            if (brace) t.Body = BlockBrace(); else t.Body = BlockIndent(ln.Ind);
            while (p < L.Count)
            {
                Line l2 = L[p]; Match m;
                if (brace)
                {
                    if (l2.S == "}") { p++; break; }
                    m = Regex.Match(l2.S, @"^\}\s*(catch|except|finally)\b\s*(.*?)\s*\{$");
                    if (!m.Success) break;
                    p++;
                    if (m.Groups[1].Value == "finally") { t.Final = BlockBrace(); }
                    else { t.Catches.Add(MakeCatch(m.Groups[2].Value, BlockBrace(), l2)); }
                }
                else
                {
                    if (l2.Ind != ln.Ind) break;
                    m = Regex.Match(l2.S, @"^(catch|except|finally)\b\s*(.*?)\s*:$");
                    if (!m.Success) break;
                    p++;
                    if (m.Groups[1].Value == "finally") t.Final = BlockIndent(ln.Ind);
                    else t.Catches.Add(MakeCatch(m.Groups[2].Value, BlockIndent(ln.Ind), l2));
                }
            }
            return t;
        }

        CatchClause MakeCatch(string rest, List<Stmt> body, Line ln)
        {
            CatchClause c = new CatchClause(); c.Body = body; rest = rest.Trim();
            Match m = Regex.Match(rest, @"^(.*?)\s*\bas\s+(\w+)$");
            if (m.Success) { c.Var = m.Groups[2].Value; rest = m.Groups[1].Value.Trim(); }
            if (rest.Length > 0) c.Type = Ex(rest, ln);
            return c;
        }

        Stmt ParseSimple(Line ln, string s)
        {
            Match m;
            if (s == "pass") return new PassS();
            if (s == "break") return new BreakS();
            if (s == "continue") return new ContinueS();
            m = Regex.Match(s, @"^return\b\s*(.*)$");
            if (m.Success) { ReturnS r = new ReturnS(); if (m.Groups[1].Value.Trim().Length > 0) r.Value = Ex(m.Groups[1].Value, ln); return r; }
            m = Regex.Match(s, @"^raise\b\s*(.*)$");
            if (m.Success) { RaiseS r = new RaiseS(); string v = m.Groups[1].Value.Trim(); int fr = v.IndexOf(" from "); if (fr > 0) v = v.Substring(0, fr); if (v.Length > 0) r.Value = Ex(v, ln); return r; }
            m = Regex.Match(s, @"^(global|nonlocal)\s+(.+)$");
            if (m.Success) { GlobalS g = new GlobalS(); g.NonLocal = m.Groups[1].Value == "nonlocal"; g.Names = new List<string>(); foreach (string n in m.Groups[2].Value.Split(',')) g.Names.Add(n.Trim()); return g; }
            m = Regex.Match(s, @"^del\s+(.+)$");
            if (m.Success) { DelS d = new DelS(); d.Target = Ex(m.Groups[1].Value, ln); return d; }
            m = Regex.Match(s, @"^assert\s+(.+)$");
            if (m.Success)
            {
                AssertS a = new AssertS(); List<string> parts = ParserUtil.SplitTopComma(m.Groups[1].Value);
                a.Cond = Ex(parts[0], ln); if (parts.Count > 1) a.Msg = Ex(parts[1], ln); return a;
            }
            // x == so(...)  ->  assignment
            m = Regex.Match(s, @"^(\w+)\s*==\s*((?:so|inputso|iputso)\s*\(.*)$");
            if (m.Success) { AssignS a = new AssignS(); a.Target = new NameE(m.Groups[1].Value); a.Value = Ex(m.Groups[2].Value, ln); return a; }

            int eq = FindAssign(s);
            if (eq > 0)
            {
                string op = null; int opStart = eq;
                if (eq >= 2 && (s.Substring(eq - 2, 2) == "//" || s.Substring(eq - 2, 2) == "**")) { op = s.Substring(eq - 2, 2); opStart = eq - 2; }
                else if (eq >= 1 && "+-*/%".IndexOf(s[eq - 1]) >= 0) { op = s[eq - 1].ToString(); opStart = eq - 1; }
                string lhs = s.Substring(0, opStart).Trim(), rhs = s.Substring(eq + 1).Trim();
                AssignS a = new AssignS(); a.Op = op;
                try { a.Target = ParserUtil.ParseExpr(ParserUtil.ConvertExpr(lhs)); } catch (MovoError e) { e.Line = ln.No; throw; }
                a.Value = Ex(rhs, ln);
                // chained a = b = 1 is not supported
                if (FindAssign(rhs) > 0) throw Err(ln, "chained assignment a = b = ... is not supported");
                return a;
            }
            ExprS ex = new ExprS(); ex.X = Ex(s, ln); return ex;
        }

        static int FindAssign(string s)
        {
            int depth = 0; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; continue; }
                if (c == '"' || c == '\'') { q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++; else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == '=' && depth == 0)
                {
                    if (i + 1 < s.Length && s[i + 1] == '=') { i++; continue; }
                    if (i > 0 && (s[i - 1] == '!' || s[i - 1] == '<' || s[i - 1] == '>' || s[i - 1] == '=')) continue;
                    return i;
                }
            }
            return -1;
        }
    }
}
