using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MovoLang
{
    // module "web": HTTP with an in-memory cookie session (nothing is written to disk)
    //   r = web.get("https://site/page")                 -> {"status": 200, "body": "...", "url": final address, "headers": {...}, "ok": True}
    //   s = web.session()                                -> keeps cookies between requests; web.session(browser=True) sends browser-like headers
    //   r = s.get(url)    r = s.get(url, headers={"X-Key": "1"}, params={"q": "movo"})
    //   r = s.post(url, {"user": "x", "pass": "y"})      -> form;   s.post(url, data, True) or json=True -> JSON
    //   r = s.put(url, data)   r = s.delete(url)
    //   s.set_header("Authorization", "Bearer ...")      -> sent with every request
    //   s.cookies()   s.download(url, "file.zip")   s.close()
    public static class WebLib
    {
        const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 Edg/124.0";

        public static void Fill(MModule m)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(192 | 768 | 3072); } catch (Exception) { }
            m.Fn("session", delegate(List<object> a, Dictionary<string, object> kw)
            {
                object o; bool br = kw != null && kw.TryGetValue("browser", out o) && Ops.Truthy(o);
                return NewSession(br);
            });
            m.Fn("get", delegate(List<object> a, Dictionary<string, object> kw)
            {
                MModule s = NewSession(false); return ((NativeFn)s.Members["get"]).F(a, kw);
            });
        }

        static MModule NewSession(bool browserLike)
        {
            MModule s = new MModule("session"); CookieContainer[] jar = new CookieContainer[] { new CookieContainer() };
            Dictionary<string, string> hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (browserLike)
            {
                hdr["User-Agent"] = BrowserUA; hdr["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"; hdr["Accept-Language"] = "en-US,en;q=0.9";
            }
            s.Fn("get", delegate(List<object> a, Dictionary<string, object> kw) { return Do(jar[0], hdr, "GET", a, kw, false); });
            s.Fn("delete", delegate(List<object> a, Dictionary<string, object> kw) { return Do(jar[0], hdr, "DELETE", a, kw, false); });
            s.Fn("post", delegate(List<object> a, Dictionary<string, object> kw) { return Do(jar[0], hdr, "POST", a, kw, true); });
            s.Fn("put", delegate(List<object> a, Dictionary<string, object> kw) { return Do(jar[0], hdr, "PUT", a, kw, true); });
            s.Fn("set_header", delegate(List<object> a) { if (a.Count < 2) throw new MovoError("TypeError", "session.set_header(name, value)"); hdr[Ops.Str(a[0])] = Ops.Str(a[1]); return null; });
            s.Fn("cookies", delegate(List<object> a)
            {
                Dictionary<object, object> d = new Dictionary<object, object>(new MComparer());
                if (a.Count > 0) { Uri u; if (Uri.TryCreate(Ops.Str(a[0]), UriKind.Absolute, out u)) foreach (Cookie c in jar[0].GetCookies(u)) d[c.Name] = c.Value; }
                return d;
            });
            s.Fn("download", delegate(List<object> a, Dictionary<string, object> kw)
            {
                if (a.Count < 2) throw new MovoError("TypeError", "session.download(url, file)");
                HttpWebResponse rs = Send(jar[0], hdr, "GET", Ops.Str(a[0]), null, false, null, null);
                using (rs) using (Stream st = rs.GetResponseStream()) using (FileStream fs = File.Create(Ops.Str(a[1]))) st.CopyTo(fs);
                return Ops.Str(a[1]);
            });
            s.Fn("close", delegate(List<object> a) { jar[0] = new CookieContainer(); hdr.Clear(); return null; });
            return s;
        }

        static object Do(CookieContainer jar, Dictionary<string, string> hdr, string method, List<object> a, Dictionary<string, object> kw, bool hasBody)
        {
            if (a.Count < 1) throw new MovoError("TypeError", "session." + method.ToLowerInvariant() + "(url" + (hasBody ? ", data" : "") + ")");
            object o; bool json = false; Dictionary<object, object> data = null, headers = null, pars = null;
            if (hasBody)
            {
                if (a.Count > 1) data = a[1] as Dictionary<object, object>;
                if (a.Count > 2) json = Ops.Truthy(a[2]);
            }
            else if (a.Count > 1) headers = a[1] as Dictionary<object, object>;
            if (kw != null)
            {
                if (kw.TryGetValue("json", out o)) json = Ops.Truthy(o);
                if (kw.TryGetValue("headers", out o)) headers = o as Dictionary<object, object>;
                if (kw.TryGetValue("params", out o)) pars = o as Dictionary<object, object>;
                if (kw.TryGetValue("data", out o)) data = o as Dictionary<object, object>;
            }
            string url = Ops.Str(a[0]);
            if (pars != null && pars.Count > 0) url += (url.Contains("?") ? "&" : "?") + Form(pars);
            HttpWebResponse rs = Send(jar, hdr, method, url, hasBody ? (data ?? new Dictionary<object, object>(new MComparer())) : null, json, headers, null);
            Dictionary<object, object> res = new Dictionary<object, object>(new MComparer());
            using (rs)
            {
                Dictionary<object, object> hh = new Dictionary<object, object>(new MComparer());
                foreach (string k in rs.Headers.AllKeys) hh[k] = rs.Headers[k];
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) res["body"] = sr.ReadToEnd();
                res["status"] = (long)(int)rs.StatusCode; res["ok"] = (int)rs.StatusCode >= 200 && (int)rs.StatusCode < 400;
                res["url"] = rs.ResponseUri.AbsoluteUri; res["headers"] = hh;
            }
            return res;
        }

        static string Form(Dictionary<object, object> data)
        {
            StringBuilder sb = new StringBuilder(); bool first = true;
            foreach (KeyValuePair<object, object> kv in data) { if (!first) sb.Append('&'); first = false; sb.Append(Uri.EscapeDataString(Ops.Str(kv.Key))).Append('=').Append(Uri.EscapeDataString(Ops.Str(kv.Value))); }
            return sb.ToString();
        }

        static void SetHeader(HttpWebRequest rq, string name, string value)
        {
            switch (name.ToLowerInvariant())
            {
                case "user-agent": rq.UserAgent = value; break;
                case "accept": rq.Accept = value; break;
                case "referer": rq.Referer = value; break;
                case "content-type": rq.ContentType = value; break;
                case "host": rq.Host = value; break;
                case "connection": case "content-length": case "expect": case "transfer-encoding": break;
                default: rq.Headers[name] = value; break;
            }
        }

        static HttpWebResponse Send(CookieContainer jar, Dictionary<string, string> hdr, string method, string url, Dictionary<object, object> data, bool json, Dictionary<object, object> extra, object unused)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) throw new MovoError("ValueError", "web needs a full address, like https://example.com/login");
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(u);
            rq.Method = method; rq.CookieContainer = jar; rq.UserAgent = "Movo/1.1"; rq.Timeout = 30000; rq.AllowAutoRedirect = true;
            rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            foreach (KeyValuePair<string, string> kv in hdr) SetHeader(rq, kv.Key, kv.Value);
            if (extra != null) foreach (KeyValuePair<object, object> kv in extra) SetHeader(rq, Ops.Str(kv.Key), Ops.Str(kv.Value));
            if (data != null)
            {
                string body, type;
                if (json) { StringBuilder jb = new StringBuilder(); HostServer.Json(jb, data); body = jb.ToString(); type = "application/json; charset=utf-8"; }
                else { body = Form(data); type = "application/x-www-form-urlencoded"; }
                if (string.IsNullOrEmpty(rq.ContentType)) rq.ContentType = type;
                byte[] bytes = new UTF8Encoding(false).GetBytes(body); rq.ContentLength = bytes.Length;
                using (Stream st = rq.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
            }
            try { return (HttpWebResponse)rq.GetResponse(); }
            catch (WebException ex)
            {
                HttpWebResponse rs = ex.Response as HttpWebResponse;
                if (rs == null) throw new MovoError("OSError", "could not reach " + u.Host + " (" + ex.Message + ")");
                return rs;
            }
        }
    }

    public static class MovoLibrary
    {
        public static void Fill(MModule m) { WebLib.Fill(m); }
    }
}
