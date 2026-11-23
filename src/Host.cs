using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace MovoLang
{
    // one line inside host.local(...){ }   "/path" -> something
    public class HostRoute { public string Path; public int Kind; public object Val; }   // Kind: 0 value, 1 file, 2 folder

    public class RouteS : Stmt
    {
        public Expr Path, Val; public int Kind;
        public override Flow Exec(Interp it, Env e)
        {
            if (HostServer.Current == null) throw new MovoError("RuntimeError", "a route (\"/path\" -> ...) can only be used inside host.local(...){ }");
            HostRoute r = new HostRoute(); r.Path = HostServer.NormPath(Ops.Str(Path.Eval(it, e))); r.Kind = Kind; r.Val = Val.Eval(it, e);
            if (Kind != 0) r.Val = HostServer.FullPath(Ops.Str(r.Val));
            HostServer.Current.Routes.Add(r);
            return Flow.Normal;
        }
    }

    public class HostS : Stmt
    {
        public Expr Port; public bool Open; public List<Stmt> Body;
        public override Flow Exec(Interp it, Env e)
        {
            HostServer old = HostServer.Current; HostServer hs = new HostServer(); HostServer.Current = hs;
            try
            {
                long port = Port == null ? 8080 : Ops.ToL(Port.Eval(it, e));
                Flow f = it.ExecBlock(Body, e);
                if (f == Flow.Return) return f;
                hs.Serve(it, (int)port, Open);
            }
            finally { HostServer.Current = old; }
            return Flow.Normal;
        }
    }

    public class HostServer
    {
        public static HostServer Current;
        public List<HostRoute> Routes = new List<HostRoute>();

        public static string NormPath(string p)
        {
            if (p == "*") return p;
            if (!p.StartsWith("/")) p = "/" + p;
            if (p.Length > 1) p = p.TrimEnd('/');
            return p.Length == 0 ? "/" : p;
        }

        public static string FullPath(string p)
        {
            if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
            string baseDir = Directory.GetCurrentDirectory();
            string[] sa = Interp.Cur != null ? Interp.Cur.ScriptArgs : null;
            if (sa != null && sa.Length > 0) { try { baseDir = Path.GetDirectoryName(Path.GetFullPath(sa[0])); } catch (Exception) { } }
            return Path.GetFullPath(Path.Combine(baseDir, p));
        }

        static string Mime(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".txt": return "text/plain; charset=utf-8";
                case ".xml": return "application/xml; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".webp": return "image/webp";
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".mp4": return "video/mp4";
                case ".pdf": return "application/pdf";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".mv": case ".mvl": return "text/plain; charset=utf-8";
            }
            return "application/octet-stream";
        }

        public static void Json(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is long || o is double) { sb.Append(Ops.Str(o)); return; }
            string s = o as string;
            if (s != null)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\"); else if (c == '\n') sb.Append("\\n");
                    else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
                    else if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c);
                }
                sb.Append('"'); return;
            }
            IEnumerable<object> seq = o as List<object>;
            if (seq == null && o is MTuple) seq = ((MTuple)o).Items;
            if (seq != null)
            {
                sb.Append('['); bool first = true;
                foreach (object x in seq) { if (!first) sb.Append(','); first = false; Json(sb, x); }
                sb.Append(']'); return;
            }
            Dictionary<object, object> d = o as Dictionary<object, object>;
            if (d != null)
            {
                sb.Append('{'); bool first = true;
                foreach (KeyValuePair<object, object> kv in d) { if (!first) sb.Append(','); first = false; Json(sb, Ops.Str(kv.Key)); sb.Append(':'); Json(sb, kv.Value); }
                sb.Append('}'); return;
            }
            Json(sb, Ops.Str(o));
        }

        static Dictionary<object, object> ParseQuery(string q)
        {
            Dictionary<object, object> d = new Dictionary<object, object>(new MComparer());
            if (string.IsNullOrEmpty(q)) return d;
            foreach (string part in q.TrimStart('?').Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string k = eq < 0 ? part : part.Substring(0, eq), v = eq < 0 ? "" : part.Substring(eq + 1);
                try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch (Exception) { d[k] = v; }
            }
            return d;
        }

        static void Send(HttpListenerContext c, int status, string type, byte[] data)
        {
            HttpListenerResponse r = c.Response;
            r.StatusCode = status; r.ContentType = type; r.ContentLength64 = data.Length;
            r.AddHeader("Cache-Control", "no-cache");
            if (c.Request.HttpMethod != "HEAD") r.OutputStream.Write(data, 0, data.Length);
            r.OutputStream.Close();
        }
        static void SendText(HttpListenerContext c, int status, string body) { Send(c, status, "text/html; charset=utf-8", new UTF8Encoding(false).GetBytes(body)); }

        // returns status
        int Respond(Interp it, HttpListenerContext c)
        {
            string path = NormPath(Uri.UnescapeDataString(c.Request.Url.AbsolutePath));
            HostRoute hit = null, wild = null, best = null;
            foreach (HostRoute r in Routes)
            {
                if (r.Path == "*") { wild = r; continue; }
                if (r.Kind != 2) { if (r.Path == path) hit = r; }
                else if (r.Path == "/" || path == r.Path || path.StartsWith(r.Path + "/")) { if (best == null || r.Path.Length >= best.Path.Length) best = r; }
            }
            if (hit == null) hit = best;
            bool isWild = false;
            if (hit == null) { hit = wild; isWild = true; }
            if (hit == null) { SendText(c, 404, "<h1>404</h1><p>Movo host: nothing at " + WebUtility_Html(path) + "</p>"); return 404; }

            if (hit.Kind == 1) return SendFile(c, (string)hit.Val);
            if (hit.Kind == 2)
            {
                string root = (string)hit.Val;
                string rel = hit.Path == "/" ? path : path.Substring(hit.Path.Length);
                rel = rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(root, rel));
                string rootFull = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!(full + Path.DirectorySeparatorChar).StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) { SendText(c, 403, "<h1>403</h1>"); return 403; }
                if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
                return SendFile(c, full);
            }

            object v = hit.Val;
            if (v is IMCallable || v is MInstance)
            {
                List<object> args = new List<object>();
                MFunc mf = v as MFunc;
                if (mf != null && mf.Params.Count > 0)
                {
                    Dictionary<object, object> req = new Dictionary<object, object>(new MComparer());
                    req["path"] = path; req["method"] = c.Request.HttpMethod; req["query"] = ParseQuery(c.Request.Url.Query);
                    string body = ""; if (c.Request.HasEntityBody) { using (StreamReader sr = new StreamReader(c.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd(); }
                    req["body"] = body; args.Add(req);
                }
                v = it.Call(v, args, null);
            }
            string type = "text/html; charset=utf-8"; string text;
            if (v is Dictionary<object, object> || v is List<object> || v is MTuple) { StringBuilder sb = new StringBuilder(); Json(sb, v); text = sb.ToString(); type = "application/json; charset=utf-8"; }
            else text = v == null ? "" : Ops.Str(v);
            int code = isWild ? 404 : 200;
            Send(c, code, type, new UTF8Encoding(false).GetBytes(text)); return code;
        }

        static string WebUtility_Html(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }

        static int SendFile(HttpListenerContext c, string file)
        {
            if (!File.Exists(file)) { SendText(c, 404, "<h1>404</h1><p>file not found</p>"); return 404; }
            Send(c, 200, Mime(file), File.ReadAllBytes(file)); return 200;
        }

        public void Serve(Interp it, int port, bool open)
        {
            HttpListener L = new HttpListener();
            L.Prefixes.Add("http://localhost:" + port + "/"); L.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try { L.Start(); }
            catch (Exception)
            {
                try { L = new HttpListener(); L.Prefixes.Add("http://localhost:" + port + "/"); L.Start(); }
                catch (Exception ex2) { throw new MovoError("OSError", "could not start the host on port " + port + " (is it already in use?): " + ex2.Message); }
            }
            string url = "http://localhost:" + port + "/";
            Console.Out.WriteLine("Movo host is running: " + url + "   (Ctrl+C to stop)"); Console.Out.Flush();
            if (open) { try { Process.Start(url); } catch (Exception) { } }
            while (true)
            {
                HttpListenerContext c;
                try { c = L.GetContext(); } catch (Exception) { break; }
                string line = c.Request.HttpMethod + " " + c.Request.Url.PathAndQuery;
                try { int st = Respond(it, c); Console.Out.WriteLine(line + "  ->  " + st); }
                catch (MovoError ex)
                {
                    Console.Out.WriteLine(line + "  ->  500  " + ex.Val.Type + ": " + ex.Val.Message);
                    try { SendText(c, 500, "<h1>500</h1><pre>" + WebUtility_Html(ex.Val.Type + ": " + ex.Val.Message) + "</pre>"); } catch (Exception) { }
                }
                catch (MovoExit) { break; }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(line + "  ->  500  " + ex.Message);
                    try { SendText(c, 500, "<h1>500</h1>"); } catch (Exception) { }
                }
                Console.Out.Flush();
            }
            try { L.Close(); } catch (Exception) { }
        }
    }
}
