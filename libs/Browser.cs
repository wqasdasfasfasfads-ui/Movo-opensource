using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MovoLang
{
    // module "browser": drives a REAL Edge / Chrome window (or a hidden one) through the DevTools protocol.
    //   import browser
    //   browser.open("https://example.com")              -> opens the site in your normal browser
    //   b = browser.start()                              -> new controllable browser (browser.start(headless=True) = invisible)
    //   b.goto(url)  b.url()  b.title()  b.html()  b.text()  b.eval("document.title")
    //   b.click("text=Log in")  b.click("#id")  b.fill("input[type=password]", "x")  b.type(sel, "abc")  b.press("Enter")
    //   b.wait(2)  b.wait_for(sel)  b.wait_load()  b.exists(sel)  b.get_text(sel)  b.get_attr(sel, "href")
    //   b.screenshot("page.png")  b.cookies()  b.scroll(500)  b.back()  b.wait_close()  b.close()
    public static class BrowserLib
    {
        public static void Fill(MModule m)
        {
            m.Fn("open", delegate(List<object> a)
            {
                if (a.Count < 1) throw new MovoError("TypeError", "browser.open(url)");
                string url = Ops.Str(a[0]);
                Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) throw new MovoError("ValueError", "browser.open needs a full address, like https://example.com");
                try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) { throw new MovoError("OSError", "could not open the browser (" + ex.Message + ")"); }
                return null;
            });
            m.Fn("start", delegate(List<object> a, Dictionary<string, object> kw)
            {
                bool headless = false; string profile = null; string url = null; object o;
                if (a.Count > 0 && a[0] is string) url = (string)a[0];
                if (kw != null)
                {
                    if (kw.TryGetValue("headless", out o)) headless = Ops.Truthy(o);
                    if (kw.TryGetValue("profile", out o) && o != null) profile = Ops.Str(o);
                    if (kw.TryGetValue("url", out o) && o != null) url = Ops.Str(o);
                }
                Session s = new Session(); s.Launch(headless, profile);
                MModule page = s.MakePage();
                if (url != null) s.Goto(url);
                return page;
            });
            m.Fn("find", delegate(List<object> a) { string p = Session.FindBrowser(); return p; });
        }

        static readonly List<Session> all = new List<Session>();
        static bool hooked;

        sealed class Session
        {
            Process proc; string profileDir; bool tempProfile; ClientWebSocket ws; int nextId = 1; int loadEvents; int seenLoads;
            bool closed; readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };

            public static string FindBrowser()
            {
                string pf = Environment.GetEnvironmentVariable("ProgramFiles"), pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)"), la = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                List<string> c = new List<string>();
                string forced = Environment.GetEnvironmentVariable("MOVO_BROWSER"); if (!string.IsNullOrEmpty(forced)) c.Add(forced);
                if (pf86 != null) c.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
                if (pf != null) c.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
                if (pf != null) c.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
                if (pf86 != null) c.Add(Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
                if (la != null) c.Add(Path.Combine(la, @"Google\Chrome\Application\chrome.exe"));
                if (pf != null) c.Add(Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"));
                foreach (string p in c) if (File.Exists(p)) return p;
                return null;
            }

            static int FreePort() { TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

            public void Launch(bool headless, string profile)
            {
                string exe = FindBrowser();
                if (exe == null) throw new MovoError("OSError", "no Edge / Chrome found. Install Microsoft Edge or Google Chrome.");
                if (profile != null)
                {
                    string safe = System.Text.RegularExpressions.Regex.Replace(profile, @"[^\w\-]", "_");
                    profileDir = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.GetTempPath(), @"Movo\profiles\" + safe); tempProfile = false;
                }
                else { profileDir = Path.Combine(Path.GetTempPath(), "movo_browser_" + Guid.NewGuid().ToString("N").Substring(0, 8)); tempProfile = true; }
                Directory.CreateDirectory(profileDir);
                int port = FreePort();
                string args = "--remote-debugging-port=" + port + " --remote-allow-origins=* --user-data-dir=\"" + profileDir + "\" --no-first-run --no-default-browser-check --disable-default-apps --window-size=1280,900"
                    + (headless ? " --headless=new --hide-scrollbars" : "") + " " + (Environment.GetEnvironmentVariable("MOVO_BROWSER_ARGS") ?? "") + " about:blank";
                ProcessStartInfo psi = new ProcessStartInfo(exe, args); psi.UseShellExecute = false;
                try { proc = Process.Start(psi); } catch (Exception ex) { throw new MovoError("OSError", "could not start the browser (" + ex.Message + ")"); }

                string wsUrl = null; DateTime end = DateTime.UtcNow.AddSeconds(25);
                while (wsUrl == null && DateTime.UtcNow < end)
                {
                    Thread.Sleep(250);
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            object parsed = js.DeserializeObject(wc.DownloadString("http://127.0.0.1:" + port + "/json/list"));
                            foreach (object it in (IEnumerable)parsed)
                            {
                                Dictionary<string, object> d = it as Dictionary<string, object>;
                                object t, w;
                                if (d != null && d.TryGetValue("type", out t) && "page".Equals(t) && d.TryGetValue("webSocketDebuggerUrl", out w) && w != null) { wsUrl = w.ToString(); break; }
                            }
                        }
                    }
                    catch (Exception) { }
                }
                if (wsUrl == null) { Kill(); throw new MovoError("OSError", "the browser started but did not answer (try again, or close other Edge/Chrome windows)"); }
                ws = new ClientWebSocket();
                try { ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).Wait(15000); }
                catch (Exception ex) { Kill(); throw new MovoError("OSError", "could not connect to the browser (" + ex.Message + ")"); }
                lock (all) { all.Add(this); if (!hooked) { hooked = true; AppDomain.CurrentDomain.ProcessExit += delegate { CloseAll(); }; } }
                Send("Page.enable", "{}");
            }

            static void CloseAll() { Session[] l; lock (all) l = all.ToArray(); foreach (Session s in l) { try { s.Close(); } catch (Exception) { } } }

            // ---------- protocol ----------
            Dictionary<string, object> ReadMsg(int timeoutMs)
            {
                byte[] buf = new byte[65536]; MemoryStream ms = new MemoryStream();
                DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (true)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds; if (left <= 0) return null;
                    System.Threading.Tasks.Task<WebSocketReceiveResult> t;
                    try { t = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (!t.Wait(left)) return null; }
                    catch (Exception ex) { throw new MovoError("OSError", "lost connection to the browser (" + ex.Message + ")"); }
                    WebSocketReceiveResult r = t.Result;
                    if (r.MessageType == WebSocketMessageType.Close) throw new MovoError("OSError", "the browser window was closed");
                    ms.Write(buf, 0, r.Count);
                    if (r.EndOfMessage) break;
                }
                return js.DeserializeObject(Encoding.UTF8.GetString(ms.ToArray())) as Dictionary<string, object>;
            }

            void Note(Dictionary<string, object> msg)
            {
                object mth; if (msg.TryGetValue("method", out mth) && "Page.loadEventFired".Equals(mth)) loadEvents++;
            }

            public Dictionary<string, object> Send(string method, string paramsJson, int timeoutMs = 60000)
            {
                if (closed) throw new MovoError("OSError", "the browser is already closed");
                int id = nextId++;
                byte[] data = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}");
                try { ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None).Wait(15000); }
                catch (Exception ex) { throw new MovoError("OSError", "could not talk to the browser (" + ex.Message + ")"); }
                DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (true)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) throw new MovoError("TimeoutError", "the browser did not answer to " + method);
                    Dictionary<string, object> msg = ReadMsg(left); if (msg == null) continue;
                    Note(msg);
                    object mid; if (!msg.TryGetValue("id", out mid) || Convert.ToInt32(mid) != id) continue;
                    object err;
                    if (msg.TryGetValue("error", out err) && err is Dictionary<string, object>)
                    { object em; ((Dictionary<string, object>)err).TryGetValue("message", out em); throw new MovoError("BrowserError", method + ": " + em); }
                    object res; if (msg.TryGetValue("result", out res) && res is Dictionary<string, object>) return (Dictionary<string, object>)res;
                    return new Dictionary<string, object>();
                }
            }

            static string Q(string s)
            {
                StringBuilder sb = new StringBuilder("\"");
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break; case '\\': sb.Append("\\\\"); break; case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break; case '\t': sb.Append("\\t"); break;
                        default: if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c); break;
                    }
                }
                return sb.Append('"').ToString();
            }

            static object ToMovo(object o)
            {
                if (o == null) return null;
                if (o is string || o is bool) return o;
                if (o is int) return (long)(int)o; if (o is long) return o;
                if (o is decimal) return (double)(decimal)o; if (o is double) return o; if (o is float) return (double)(float)o;
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d != null) { Dictionary<object, object> r = new Dictionary<object, object>(new MComparer()); foreach (KeyValuePair<string, object> kv in d) r[kv.Key] = ToMovo(kv.Value); return r; }
                IEnumerable e = o as IEnumerable;
                if (e != null) { List<object> l = new List<object>(); foreach (object x in e) l.Add(ToMovo(x)); return l; }
                return o.ToString();
            }

            // ---------- page actions ----------
            public object Eval(string expr)
            {
                Dictionary<string, object> r = Send("Runtime.evaluate", "{\"expression\":" + Q(expr) + ",\"returnByValue\":true,\"awaitPromise\":true,\"userGesture\":true}");
                object ex; if (r.TryGetValue("exceptionDetails", out ex) && ex is Dictionary<string, object>)
                {
                    Dictionary<string, object> ed = (Dictionary<string, object>)ex; object txt = null, exo;
                    if (ed.TryGetValue("exception", out exo) && exo is Dictionary<string, object>) ((Dictionary<string, object>)exo).TryGetValue("description", out txt);
                    if (txt == null) ed.TryGetValue("text", out txt);
                    throw new MovoError("JSError", txt == null ? "script failed" : txt.ToString());
                }
                object res; if (!r.TryGetValue("result", out res)) return null;
                Dictionary<string, object> rd = res as Dictionary<string, object>; object val;
                if (rd == null || !rd.TryGetValue("value", out val)) return null;
                return ToMovo(val);
            }

            public void Goto(string url)
            {
                if (url.IndexOf("://") < 0 && !url.StartsWith("about:") && !url.StartsWith("data:")) url = "https://" + url;
                Dictionary<string, object> r = Send("Page.navigate", "{\"url\":" + Q(url) + "}");
                object et; if (r.TryGetValue("errorText", out et) && et != null) throw new MovoError("OSError", "could not open " + url + " (" + et + ")");
                object lid; if (!r.TryGetValue("loaderId", out lid)) { seenLoads = loadEvents; return; }
                WaitLoad(30);
            }

            public bool WaitLoad(double seconds)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                while (loadEvents <= seenLoads)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds; if (left <= 0) return false;
                    Dictionary<string, object> msg = ReadMsg(Math.Min(left, 500)); if (msg != null) Note(msg);
                }
                seenLoads = loadEvents; Thread.Sleep(150); return true;
            }

            const string FIND = "var __f=function(sel){if(sel.indexOf('text=')===0){var t=sel.slice(5).trim().toLowerCase();var tiers=['a,button,input[type=submit],input[type=button],[role=button],summary','label,li,span,div,h1,h2,h3,h4,p,td,th'];for(var k=0;k<tiers.length;k++){var els=document.querySelectorAll(tiers[k]);var part=null;for(var i=0;i<els.length;i++){var e=els[i];var s=((e.innerText||e.value||'')+'').trim().toLowerCase();if(s===t)return e;if(part===null&&s.length>0&&s.length<200&&s.indexOf(t)>=0)part=e;}if(part)return part;}return null;}return document.querySelector(sel);};";

            public bool Exists(string sel) { return Ops.Truthy(Eval("(function(){" + FIND + "return !!__f(" + Q(sel) + ");})()")); }

            public bool WaitFor(string sel, double seconds)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                while (true)
                {
                    if (Exists(sel)) return true;
                    if (DateTime.UtcNow >= end) return false;
                    Thread.Sleep(200);
                }
            }

            void Need(string sel, double timeout) { if (!WaitFor(sel, timeout)) throw new MovoError("LookupError", "element not found on the page: " + sel); }

            static double Num(object o) { return o == null ? 0 : Ops.ToD(o); }

            public void Click(string sel, double timeout)
            {
                Need(sel, timeout);
                object pos = Eval("(function(){" + FIND + "var e=__f(" + Q(sel) + ");if(!e)return null;e.scrollIntoView({block:'center',inline:'center'});var r=e.getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2,r.width,r.height];})()");
                List<object> p = pos as List<object>;
                if (p == null) throw new MovoError("LookupError", "element not found on the page: " + sel);
                if (Num(p[2]) < 1 || Num(p[3]) < 1) { Eval("(function(){" + FIND + "__f(" + Q(sel) + ").click();})()"); return; }
                string xy = "\"x\":" + Num(p[0]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + Num(p[1]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                Send("Input.dispatchMouseEvent", "{\"type\":\"mouseMoved\"," + xy + "}");
                Send("Input.dispatchMouseEvent", "{\"type\":\"mousePressed\"," + xy + ",\"button\":\"left\",\"clickCount\":1}");
                Send("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\"," + xy + ",\"button\":\"left\",\"clickCount\":1}");
            }

            public void Fill(string sel, string text, bool clear, double timeout)
            {
                Need(sel, timeout);
                Eval("(function(){" + FIND + "var e=__f(" + Q(sel) + ");e.scrollIntoView({block:'center'});e.focus();" + (clear ? "if(e.select){e.select();}else{document.execCommand('selectAll');}" : "if(e.setSelectionRange&&e.value!==undefined){var n=e.value.length;try{e.setSelectionRange(n,n);}catch(x){}}") + "})()");
                if (text.Length == 0) { if (clear) Eval("(function(){" + FIND + "var e=__f(" + Q(sel) + ");if(e.value!==undefined){e.value='';e.dispatchEvent(new Event('input',{bubbles:true}));}else{e.textContent='';}})()"); return; }
                Send("Input.insertText", "{\"text\":" + Q(text) + "}");
            }

            public void Press(string key)
            {
                string k = key, code = key, text = ""; int vk = 0;
                switch (key.ToLowerInvariant())
                {
                    case "enter": k = "Enter"; code = "Enter"; vk = 13; text = "\r"; break;
                    case "tab": k = "Tab"; code = "Tab"; vk = 9; break;
                    case "escape": case "esc": k = "Escape"; code = "Escape"; vk = 27; break;
                    case "backspace": k = "Backspace"; code = "Backspace"; vk = 8; break;
                    case "delete": k = "Delete"; code = "Delete"; vk = 46; break;
                    case "space": k = " "; code = "Space"; vk = 32; text = " "; break;
                    case "arrowdown": case "down": k = "ArrowDown"; code = "ArrowDown"; vk = 40; break;
                    case "arrowup": case "up": k = "ArrowUp"; code = "ArrowUp"; vk = 38; break;
                    case "arrowleft": case "left": k = "ArrowLeft"; code = "ArrowLeft"; vk = 37; break;
                    case "arrowright": case "right": k = "ArrowRight"; code = "ArrowRight"; vk = 39; break;
                    case "pagedown": k = "PageDown"; code = "PageDown"; vk = 34; break;
                    case "pageup": k = "PageUp"; code = "PageUp"; vk = 33; break;
                    case "home": k = "Home"; code = "Home"; vk = 36; break;
                    case "end": k = "End"; code = "End"; vk = 35; break;
                    default:
                        if (key.Length == 1) { Send("Input.insertText", "{\"text\":" + Q(key) + "}"); return; }
                        throw new MovoError("ValueError", "unknown key: " + key + " (try Enter, Tab, Escape, Backspace, Delete, Space, ArrowDown ...)");
                }
                string common = "\"key\":" + Q(k) + ",\"code\":" + Q(code) + ",\"windowsVirtualKeyCode\":" + vk + ",\"nativeVirtualKeyCode\":" + vk;
                Send("Input.dispatchKeyEvent", "{\"type\":\"" + (text.Length > 0 ? "keyDown" : "rawKeyDown") + "\"," + common + (text.Length > 0 ? ",\"text\":" + Q(text) : "") + "}");
                Send("Input.dispatchKeyEvent", "{\"type\":\"keyUp\"," + common + "}");
            }

            public void Screenshot(string path)
            {
                Dictionary<string, object> r = Send("Page.captureScreenshot", "{\"format\":\"png\"}"); object d;
                if (!r.TryGetValue("data", out d)) throw new MovoError("BrowserError", "no screenshot data");
                File.WriteAllBytes(path, Convert.FromBase64String(d.ToString()));
            }

            public object Cookies()
            {
                Dictionary<string, object> r = Send("Network.getCookies", "{}"); object c; r.TryGetValue("cookies", out c); return ToMovo(c);
            }

            public void WaitClose()
            {
                while (proc != null && !proc.HasExited) Thread.Sleep(300);
                Close();
            }

            void Kill() { try { if (proc != null && !proc.HasExited) proc.Kill(); } catch (Exception) { } }

            public void Close()
            {
                if (closed) return; closed = true;
                lock (all) all.Remove(this);
                try { if (ws != null && ws.State == WebSocketState.Open) { byte[] d = Encoding.UTF8.GetBytes("{\"id\":" + (nextId++) + ",\"method\":\"Browser.close\"}"); ws.SendAsync(new ArraySegment<byte>(d), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000); } } catch (Exception) { }
                try { if (proc != null) proc.WaitForExit(3000); } catch (Exception) { }
                Kill();
                try { if (ws != null) ws.Dispose(); } catch (Exception) { }
                if (tempProfile && profileDir != null)
                    for (int i = 0; i < 8; i++) { try { Directory.Delete(profileDir, true); break; } catch (Exception) { Thread.Sleep(300); } }
            }

            // ---------- Movo object ----------
            static double Timeout(List<object> a, int idx, Dictionary<string, object> kw)
            {
                object o; if (kw != null && kw.TryGetValue("timeout", out o)) return Ops.ToD(o);
                if (a.Count > idx && a[idx] != null && !(a[idx] is string)) return Ops.ToD(a[idx]);
                return 10;
            }
            static string S(List<object> a, int i, string usage) { if (a.Count <= i) throw new MovoError("TypeError", usage); return Ops.Str(a[i]); }

            public MModule MakePage()
            {
                MModule p = new MModule("page"); Session s = this;
                p.Fn("goto", delegate(List<object> a) { s.Goto(S(a, 0, "page.goto(url)")); return null; });
                p.Fn("url", delegate(List<object> a) { return s.Eval("location.href"); });
                p.Fn("title", delegate(List<object> a) { return s.Eval("document.title"); });
                p.Fn("html", delegate(List<object> a) { return s.Eval("document.documentElement.outerHTML"); });
                p.Fn("text", delegate(List<object> a) { return s.Eval("document.body ? document.body.innerText : ''"); });
                p.Fn("eval", delegate(List<object> a) { return s.Eval(S(a, 0, "page.eval(javascript)")); });
                p.Fn("click", delegate(List<object> a, Dictionary<string, object> kw) { s.Click(S(a, 0, "page.click(selector)"), Timeout(a, 1, kw)); return null; });
                p.Fn("fill", delegate(List<object> a, Dictionary<string, object> kw) { s.Fill(S(a, 0, "page.fill(selector, text)"), S(a, 1, "page.fill(selector, text)"), true, Timeout(a, 2, kw)); return null; });
                p.Fn("type", delegate(List<object> a, Dictionary<string, object> kw) { s.Fill(S(a, 0, "page.type(selector, text)"), S(a, 1, "page.type(selector, text)"), false, Timeout(a, 2, kw)); return null; });
                p.Fn("press", delegate(List<object> a) { s.Press(S(a, 0, "page.press(key)")); return null; });
                p.Fn("exists", delegate(List<object> a) { return s.Exists(S(a, 0, "page.exists(selector)")); });
                p.Fn("wait_for", delegate(List<object> a, Dictionary<string, object> kw) { double t = 15; object o; if (kw != null && kw.TryGetValue("timeout", out o)) t = Ops.ToD(o); else if (a.Count > 1) t = Ops.ToD(a[1]); return s.WaitFor(S(a, 0, "page.wait_for(selector)"), t); });
                p.Fn("wait_load", delegate(List<object> a) { return s.WaitLoad(a.Count > 0 ? Ops.ToD(a[0]) : 15); });
                p.Fn("wait", delegate(List<object> a) { Thread.Sleep((int)((a.Count > 0 ? Ops.ToD(a[0]) : 1) * 1000)); return null; });
                p.Fn("get_text", delegate(List<object> a, Dictionary<string, object> kw) { string sel = S(a, 0, "page.get_text(selector)"); s.Need(sel, Timeout(a, 1, kw)); return s.Eval("(function(){" + FIND + "var e=__f(" + Q(sel) + ");return e.innerText!==undefined?e.innerText:e.textContent;})()"); });
                p.Fn("get_attr", delegate(List<object> a, Dictionary<string, object> kw) { string sel = S(a, 0, "page.get_attr(selector, name)"); s.Need(sel, Timeout(a, 2, kw)); return s.Eval("(function(){" + FIND + "return __f(" + Q(sel) + ").getAttribute(" + Q(S(a, 1, "page.get_attr(selector, name)")) + ");})()"); });
                p.Fn("scroll", delegate(List<object> a) { s.Eval("window.scrollBy(0," + (a.Count > 0 ? ((long)Ops.ToD(a[0])).ToString() : "600") + ")"); return null; });
                p.Fn("back", delegate(List<object> a) { s.Eval("history.back()"); s.WaitLoad(10); return null; });
                p.Fn("reload", delegate(List<object> a) { s.Send("Page.reload", "{}"); s.WaitLoad(30); return null; });
                p.Fn("screenshot", delegate(List<object> a) { s.Screenshot(a.Count > 0 ? Ops.Str(a[0]) : "screenshot.png"); return null; });
                p.Fn("cookies", delegate(List<object> a) { return s.Cookies(); });
                p.Fn("wait_close", delegate(List<object> a) { s.WaitClose(); return null; });
                p.Fn("close", delegate(List<object> a) { s.Close(); return null; });
                return p;
            }
        }
    }

    public static class MovoLibrary
    {
        public static void Fill(MModule m) { BrowserLib.Fill(m); }
    }
}
