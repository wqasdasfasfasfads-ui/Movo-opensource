using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MovoLang
{
    public static class Program
    {
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        public static bool Pause;
        static int exitCode = 0;

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); Console.InputEncoding = new UTF8Encoding(false); } catch (Exception) { }
            List<string> rest = new List<string>();
            foreach (string a in args) { if (a == "--pause") Pause = true; else rest.Add(a); }
            if (rest.Count > 0 && rest[0] == "install") return MovoInstall.Run(rest);
            if (rest.Count == 0 || rest[0] == "--help" || rest[0] == "-h")
            {
                Console.WriteLine("Movo 1.1 - a programming language (.mv). Standalone, no Python needed.");
                Console.WriteLine("Usage:  movo file.mv");
                Console.WriteLine("               movo install <library>   (download a library into " + Interp.LibDir + ")");
                Console.WriteLine("               movo --init     (create the library folder " + Interp.LibDir + ")");
                return 0;
            }
            if (rest[0] == "--init")
            {
                try { Directory.CreateDirectory(Interp.LibDir); Console.WriteLine("Movo: library folder " + Interp.LibDir); }
                catch (Exception ex) { Console.WriteLine("Movo: could not create " + Interp.LibDir + " (" + ex.Message + ")"); return 1; }
                return 0;
            }
            string path = rest[0];
            if (!File.Exists(path)) { Console.Error.WriteLine("Movo: file not found: " + path); return Finish(1); }
            string[] sargs = rest.ToArray();
            Thread th = new Thread(delegate() { exitCode = Run(path, sargs); }, 256 * 1024 * 1024);
            th.SetApartmentState(ApartmentState.STA);
            th.Start(); th.Join();
            return Finish(exitCode);
        }

        static int Finish(int code)
        {
            if (Pause && !MovoGui.ConsoleHidden)
            {
                try { Console.WriteLine(); Console.Write("Press Enter to close..."); Console.ReadLine(); } catch (Exception) { }
            }
            return code;
        }

        static int Run(string path, string[] sargs)
        {
            Interp it = new Interp(); it.ScriptArgs = sargs;
            try
            {
                string src = File.ReadAllText(path, Encoding.UTF8);
                List<Stmt> prog = ProgParser.Parse(src);
                it.RunProgram(prog);
                return 0;
            }
            catch (MovoExit ex) { return ex.Code; }
            catch (MovoError ex)
            {
                string msg = "Error" + (ex.Line > 0 ? " (line " + ex.Line + ")" : "") + ": " + ex.Val.Type + (ex.Val.Message.Length > 0 ? ": " + ex.Val.Message : "");
                MovoGui.ReportFatal(msg); return 1;
            }
            catch (Exception ex) { MovoGui.ReportFatal("Internal Movo error: " + ex.Message); return 2; }
        }
    }
}
