using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MovoLang
{
    // movo install <name>  -> looks <name> up in the online library index, downloads it from MEGA into the library folder.
    public static class MovoInstall
    {
        static string IndexUrl = Environment.GetEnvironmentVariable("MOVO_INDEX_URL") ?? "https://raw.githubusercontent.com/GRANTIC-site-on/23/refs/heads/main/library";
        static string MegaApiUrl = Environment.GetEnvironmentVariable("MOVO_MEGA_API") ?? "https://g.api.mega.co.nz/cs";
        static int apiSeq = 1;

        public static int Run(List<string> args)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch (Exception) { }
            try
            {
                Dictionary<string, string> index = LoadIndex(false);
                if (args.Count < 2)
                {
                    Console.WriteLine("Usage:  movo install <library>");
                    PrintList(index);
                    return 1;
                }
                string want = args[1].Trim();
                string link = null, name = null;
                for (int pass = 0; pass < 2 && link == null; pass++)
                {
                    if (pass == 1)
                    {
                        Dictionary<string, string> fresh = LoadIndex(true);
                        if (fresh.Count == 0) break;
                        index = fresh;
                    }
                    foreach (KeyValuePair<string, string> kv in index)
                        if (string.Equals(kv.Key, want, StringComparison.OrdinalIgnoreCase)) { name = kv.Key; link = kv.Value; break; }
                }
                if (link == null)
                {
                    Console.WriteLine("Movo: library '" + want + "' was not found in the library list.");
                    PrintList(index);
                    return 1;
                }
                Console.WriteLine("Movo: installing '" + name + "'...");
                string saved = MegaDownload(link, name);
                Console.WriteLine("Movo: done -> " + saved);
                Console.WriteLine("      use it in a program with:  import " + name);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Movo: install failed: " + ex.Message);
                return 1;
            }
        }

        static void PrintList(Dictionary<string, string> index)
        {
            if (index.Count == 0) { Console.WriteLine("The library list is empty."); return; }
            Console.WriteLine("Available libraries:");
            foreach (string k in index.Keys) Console.WriteLine("  " + k);
        }

        // index file format, one library per line:   name - https://mega.nz/file/ID#KEY
        // fresh=false: normal download with a cache-buster; fresh=true: GitHub API (never behind the raw CDN cache)
        static Dictionary<string, string> LoadIndex(bool fresh)
        {
            string text = null; string err = "";
            string url = IndexUrl;
            string accept = null;
            if (fresh)
            {
                Match gm = Regex.Match(IndexUrl, "^https://raw\\.githubusercontent\\.com/([^/]+)/([^/]+)/(?:refs/heads/)?([^/]+)/(.+)$");
                if (gm.Success)
                {
                    url = "https://api.github.com/repos/" + gm.Groups[1].Value + "/" + gm.Groups[2].Value + "/contents/" + gm.Groups[4].Value + "?ref=" + gm.Groups[3].Value;
                    accept = "application/vnd.github.raw";
                }
            }
            if (accept == null) url += (url.Contains("?") ? "&" : "?") + "nocache=" + DateTime.UtcNow.Ticks;
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "movo";
                rq.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);
                rq.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                rq.Headers[HttpRequestHeader.Pragma] = "no-cache";
                if (accept != null) rq.Accept = accept;
                rq.Timeout = 20000;
                using (WebResponse rs = rq.GetResponse())
                using (StreamReader r = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    text = r.ReadToEnd();
            }
            catch (Exception ex) { err = ex.Message; }
            if (text == null)
            {
                if (fresh) return new Dictionary<string, string>();
                throw new Exception("could not download the library list (" + err + ")");
            }
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Match m = Regex.Match(line, "^([A-Za-z0-9_.\\-]+)\\s*(?:-|\u2013|\u2014|=|:)\\s*(https?://\\S+)\\s*$");
                if (!m.Success) continue;
                d[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return d;
        }

        // ---------------- MEGA ----------------
        static byte[] B64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/').Replace(",", "");
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        static string MegaError(int code)
        {
            switch (code)
            {
                case -2: return "bad request to MEGA";
                case -3: case -4: return "MEGA is temporarily busy, try again later";
                case -9: return "the file no longer exists on MEGA (broken link)";
                case -11: return "access denied by MEGA";
                case -14: case -15: return "the MEGA link key is wrong";
                case -16: return "the file was removed by MEGA (terms of service violation)";
                case -17: return "MEGA download quota exceeded - wait a while (or change IP) and try again";
                default: return "MEGA error " + code;
            }
        }

        static string MegaApi(string json)
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(MegaApiUrl + "?id=" + (apiSeq++));
            rq.Method = "POST"; rq.ContentType = "application/json"; rq.UserAgent = "movo";
            byte[] body = Encoding.UTF8.GetBytes(json);
            rq.ContentLength = body.Length;
            using (Stream s = rq.GetRequestStream()) s.Write(body, 0, body.Length);
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader r = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        sealed class Ctr
        {
            ICryptoTransform enc; byte[] counterBlock = new byte[16]; ulong counter; byte[] ks = new byte[16]; int pos = 16;
            public Ctr(byte[] key, byte[] nonce8)
            {
                Aes a = Aes.Create(); a.Mode = CipherMode.ECB; a.Padding = PaddingMode.None; a.Key = key;
                enc = a.CreateEncryptor();
                Array.Copy(nonce8, counterBlock, 8);
            }
            void Next()
            {
                for (int i = 0; i < 8; i++) counterBlock[15 - i] = (byte)(counter >> (8 * i));
                counter++;
                enc.TransformBlock(counterBlock, 0, 16, ks, 0);
                pos = 0;
            }
            public void Apply(byte[] buf, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    if (pos == 16) Next();
                    buf[i] ^= ks[pos++];
                }
            }
        }

        static string DecryptName(byte[] key16, string at)
        {
            try
            {
                byte[] data = B64Url(at);
                int len = data.Length - data.Length % 16;
                using (Aes a = Aes.Create())
                {
                    a.Mode = CipherMode.CBC; a.Padding = PaddingMode.None; a.Key = key16; a.IV = new byte[16];
                    using (ICryptoTransform d = a.CreateDecryptor())
                    {
                        byte[] o = d.TransformFinalBlock(data, 0, len);
                        string t = Encoding.UTF8.GetString(o).TrimEnd('\0');
                        if (!t.StartsWith("MEGA")) return null;
                        Match m = Regex.Match(t, "\"n\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                        return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        static string MegaDownload(string link, string libName)
        {
            if (Regex.IsMatch(link, "mega\\.nz/(folder/|#F!)", RegexOptions.IgnoreCase))
                throw new Exception("MEGA folder links are not supported - the index must point to a single file");
            Match lm = Regex.Match(link, "mega(?:\\.co)?\\.nz/(?:file/|#!)([A-Za-z0-9_-]+)[#!]([A-Za-z0-9_,-]+)", RegexOptions.IgnoreCase);
            if (!lm.Success) throw new Exception("this is not a valid MEGA file link: " + link);
            string id = lm.Groups[1].Value;
            byte[] k;
            try { k = B64Url(lm.Groups[2].Value); } catch (Exception) { throw new Exception("the MEGA link key is damaged"); }
            if (k.Length < 32) throw new Exception("the MEGA link key is incomplete");
            byte[] key = new byte[16];
            for (int i = 0; i < 16; i++) key[i] = (byte)(k[i] ^ k[i + 16]);
            byte[] nonce = new byte[8];
            Array.Copy(k, 16, nonce, 0, 8);

            string resp = null; string dl = null; long size = 0; string at = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try { resp = MegaApi("[{\"a\":\"g\",\"g\":1,\"p\":\"" + id + "\"}]"); }
                catch (Exception ex) { throw new Exception("could not reach MEGA (" + ex.Message + ")"); }
                Match em = Regex.Match(resp, "^\\s*\\[?\\s*(-\\d+)\\s*\\]?\\s*$");
                if (em.Success)
                {
                    int code = int.Parse(em.Groups[1].Value);
                    if ((code == -3 || code == -4) && attempt < 3) { Thread.Sleep(1500); continue; }
                    throw new Exception(MegaError(code));
                }
                Match gm = Regex.Match(resp, "\"g\"\\s*:\\s*\"([^\"]+)\"");
                Match sm = Regex.Match(resp, "\"s\"\\s*:\\s*(\\d+)");
                Match am = Regex.Match(resp, "\"at\"\\s*:\\s*\"([^\"]+)\"");
                Match ee = Regex.Match(resp, "\"e\"\\s*:\\s*(-\\d+)");
                if (ee.Success) throw new Exception(MegaError(int.Parse(ee.Groups[1].Value)));
                if (!gm.Success || !sm.Success) throw new Exception("unexpected answer from MEGA");
                dl = gm.Groups[1].Value.Replace("\\/", "/");
                size = long.Parse(sm.Groups[1].Value);
                at = am.Success ? am.Groups[1].Value : null;
                break;
            }
            if (dl == null) throw new Exception("MEGA did not give a download address");

            string remote = at != null ? DecryptName(key, at) : null;
            if (remote != null) Console.WriteLine("Movo: file on MEGA: " + remote + " (" + FormatSize(size) + ")");

            string libDir = Interp.LibDir;
            Directory.CreateDirectory(libDir);
            string tmp = Path.Combine(Path.GetTempPath(), "movo_dl_" + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                Ctr ctr = new Ctr(key, nonce);
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(dl);
                rq.UserAgent = "movo"; rq.ReadWriteTimeout = 60000;
                long done = 0; int lastPct = -1;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (Stream st = rs.GetResponseStream())
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[65536]; int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0)
                    {
                        ctr.Apply(buf, n); fs.Write(buf, 0, n); done += n;
                        int pct = size > 0 ? (int)(done * 100 / size) : 0;
                        if (pct != lastPct) { lastPct = pct; Console.Write("\rMovo: downloading... " + pct + "%   "); }
                    }
                }
                Console.WriteLine();
                if (done != size) throw new Exception("the download was interrupted (" + done + " of " + size + " bytes)");

                if (IsZip(tmp))
                {
                    ExtractZip(tmp, libDir);
                    return libDir + "  (archive unpacked)";
                }
                string ext = (remote != null && remote.EndsWith(".mv", StringComparison.OrdinalIgnoreCase)) ? ".mv" : ".mvl";
                string dest = Path.Combine(libDir, libName + ext);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return dest;
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
        }

        static string FormatSize(long b)
        {
            if (b >= 1048576) return (b / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            return b + " B";
        }

        static bool IsZip(string path)
        {
            using (FileStream f = File.OpenRead(path))
            {
                byte[] h = new byte[4];
                return f.Read(h, 0, 4) == 4 && h[0] == 0x50 && h[1] == 0x4B && h[2] == 3 && h[3] == 4;
            }
        }

        // .NET 4.5+ ZipFile through reflection, so movo.exe still builds on a bare .NET 4.0 compiler.
        // If that is not available, fall back to PowerShell's Expand-Archive.
        static void ExtractZip(string zip, string destDir)
        {
            string stage = Path.Combine(Path.GetTempPath(), "movo_unzip_" + Guid.NewGuid().ToString("N"));
            string zip2 = zip + ".zip";
            try
            {
                bool ok = false; string why = "";
                try
                {
                    Assembly asm = Assembly.Load("System.IO.Compression.FileSystem, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                    Type t = asm.GetType("System.IO.Compression.ZipFile");
                    t.GetMethod("ExtractToDirectory", new Type[] { typeof(string), typeof(string) }).Invoke(null, new object[] { zip, stage });
                    ok = true;
                }
                catch (TargetInvocationException ex) { throw new Exception("could not unpack the archive (" + ex.InnerException.Message + ")"); }
                catch (Exception ex) { why = ex.Message; }
                if (!ok)
                {
                    File.Copy(zip, zip2, true);
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -Command \"Expand-Archive -LiteralPath '" + zip2.Replace("'", "''") + "' -DestinationPath '" + stage.Replace("'", "''") + "' -Force\"");
                    psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true;
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                    {
                        string err = p.StandardError.ReadToEnd(); p.WaitForExit();
                        if (p.ExitCode != 0 || !Directory.Exists(stage)) throw new Exception("could not unpack the archive (" + (err.Length > 0 ? err.Trim() : why) + ")");
                    }
                }
                CopyOver(stage, destDir);
            }
            finally
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception) { }
                try { if (File.Exists(zip2)) File.Delete(zip2); } catch (Exception) { }
            }
        }

        static void CopyOver(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from)) CopyOver(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
