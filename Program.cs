using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private static Mutex blokadaAplikacji;

    [STAThread]
    private static void Main(string[] args)
    {
        // ten sam exe robi tez za watchdog, jak dostanie specjalny argument
        if (StraznikSprzatania.SprobujUruchomic(args))
        {
            return;
        }

        bool utworzonoNowy;
        blokadaAplikacji = new Mutex(true, @"Global\AlgHelperSingleInstanceMutex", out utworzonoNowy);

        if (!utworzonoNowy)
        {
            MessageBox.Show(
                "AlgHelper już działa w zasobniku systemowym.",
                "AlgHelper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            blokadaAplikacji.Dispose();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new KontekstTraya());
        }
        finally
        {
            blokadaAplikacji.ReleaseMutex();
            blokadaAplikacji.Dispose();
        }
    }
}

internal static class SprzatanieAlgHelper
{
    public const string NazwaFolderu = "AlgHelper";
    public const string NazwaZnacznika = ".alghelper-owned";
    public const string NazwaManifestu = ".alghelper-manifest.txt";

    public static string PlikZnacznika(string folderDocelowy)
    {
        return Path.Combine(folderDocelowy, NazwaZnacznika);
    }

    public static string PlikManifestu(string folderDocelowy)
    {
        return Path.Combine(folderDocelowy, NazwaManifestu);
    }

    public static bool CzyMoznaUsunacFolder(string folderDocelowy)
    {
        if (string.IsNullOrWhiteSpace(folderDocelowy) || !Directory.Exists(folderDocelowy))
        {
            return false;
        }

        string pelnaSciezka = Path.GetFullPath(folderDocelowy)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.Equals(Path.GetFileName(pelnaSciezka), NazwaFolderu, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DirectoryInfo rodzic = Directory.GetParent(pelnaSciezka);

        if (rodzic == null || !string.Equals(rodzic.Name, "My Code Snippets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        FileAttributes atrybuty = File.GetAttributes(pelnaSciezka);

        if ((atrybuty & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            return false;
        }

        // bez tego pliku nie ruszamy folderu, bo nie mamy pewnosci ze jest nasz
        return File.Exists(Path.Combine(pelnaSciezka, NazwaZnacznika));
    }

    public static bool UsunFolderProgramu(string folderDocelowy)
    {
        if (!CzyMoznaUsunacFolder(folderDocelowy))
        {
            return false;
        }

        string pelnaSciezka = Path.GetFullPath(folderDocelowy)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // jak jakis plik zostal jako readonly, to normalizujemy przed usunieciem
        UstawNormalneAtrybuty(pelnaSciezka);
        Directory.Delete(pelnaSciezka, true);
        return true;
    }

    private static void UstawNormalneAtrybuty(string folder)
    {
        foreach (string plik in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(plik, FileAttributes.Normal);
        }

        foreach (string katalog in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(katalog, FileAttributes.Directory);
        }

        File.SetAttributes(folder, FileAttributes.Directory);
    }
}

internal static class StraznikSprzatania
{
    private const string ArgumentStraznika = "--cleanup-watch";

    public static bool SprobujUruchomic(string[] args)
    {
        if (args == null || args.Length < 3 || args[0] != ArgumentStraznika)
        {
            return false;
        }

        int idGlownegoProcesu;

        if (!int.TryParse(args[1], out idGlownegoProcesu))
        {
            return true;
        }

        try
        {
            using (Process procesGlowny = Process.GetProcessById(idGlownegoProcesu))
            {
                procesGlowny.WaitForExit();
            }
        }
        catch
        {
            // jak proces juz zniknal, to sprzatanie moze isc od razu
        }

        Thread.Sleep(750);
        SprzatanieAlgHelper.UsunFolderProgramu(args[2]);
        return true;
    }

    public static void Start(int idGlownegoProcesu, string folderDocelowy)
    {
        try
        {
            string sciezkaExe = Environment.ProcessPath;

            if (string.IsNullOrEmpty(sciezkaExe))
            {
                sciezkaExe = Application.ExecutablePath;
            }

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = sciezkaExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            info.ArgumentList.Add(ArgumentStraznika);
            info.ArgumentList.Add(idGlownegoProcesu.ToString());
            info.ArgumentList.Add(folderDocelowy);

            Process.Start(info);
        }
        catch
        {
            // watchdog jest tylko dodatkiem, normalne zamkniecie i tak sprzata
        }
    }
}


internal sealed class SkrotKlawiszowy : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_BACK = 0x08;
    private const int VK_RSHIFT = 0xA1;

    private readonly Action akcja;
    private readonly SynchronizationContext kontekst;
    private readonly LowLevelKeyboardProc procedura;
    private IntPtr uchwytHooka;
    private bool skrotJuzWcisniety;

    public SkrotKlawiszowy(Action akcja)
    {
        this.akcja = akcja;
        kontekst = SynchronizationContext.Current;
        procedura = ObsluzKlawiature;
        uchwytHooka = UstawHook(procedura);
    }

    private static IntPtr UstawHook(LowLevelKeyboardProc procedura)
    {
        return SetWindowsHookEx(
            WH_KEYBOARD_LL,
            procedura,
            IntPtr.Zero,
            0
        );
    }

    private IntPtr ObsluzKlawiature(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int komunikat = wParam.ToInt32();
            int klawisz = Marshal.ReadInt32(lParam);

            bool klawiszWDol = komunikat == WM_KEYDOWN || komunikat == WM_SYSKEYDOWN;
            bool klawiszWGore = komunikat == WM_KEYUP || komunikat == WM_SYSKEYUP;

            if (klawiszWGore && (klawisz == VK_BACK || klawisz == VK_RSHIFT))
            {
                skrotJuzWcisniety = false;
            }

            if (klawiszWDol && klawisz == VK_BACK && CzyPrawyShiftWcisniety())
            {
                if (!skrotJuzWcisniety)
                {
                    skrotJuzWcisniety = true;
                    WywolajAkcje();
                }

                // nie puszczamy backspace dalej, bo to nasz skrot programu
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(uchwytHooka, nCode, wParam, lParam);
    }

    private bool CzyPrawyShiftWcisniety()
    {
        return (GetAsyncKeyState(VK_RSHIFT) & unchecked((short)0x8000)) != 0;
    }

    private void WywolajAkcje()
    {
        if (kontekst != null)
        {
            kontekst.Post(_ =>
            {
                try
                {
                    akcja();
                }
                catch
                {
                    // skrót nie powinien wywalac calego programu
                }
            }, null);

            return;
        }

        try
        {
            akcja();
        }
        catch
        {
            // awaryjnie bez komunikatow
        }
    }

    public void Dispose()
    {
        if (uchwytHooka != IntPtr.Zero)
        {
            UnhookWindowsHookEx(uchwytHooka);
            uchwytHooka = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

}

internal sealed class KontekstTraya : ApplicationContext
{
    private readonly NotifyIcon ikonaTraya;
    private readonly string folderBazowySnippetow;
    private readonly string folderDocelowy;
    private readonly string githubUrl = "https://github.com/bearbine";
    private readonly string wersja = "2.0";
    private bool powiadomieniaWlaczone = false;
    private ToolStripMenuItem elementPowiadomien;
    private bool sprzatanieZrobione = false;
    private bool sprzatanieTrwa = false;
    private SkrotKlawiszowy skrotKlawiszowy;

    private static readonly Dictionary<string, string> Snippety = new Dictionary<string, string>
    {
            ["alg_31_babelkowe_podstawowe.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjEgLSBCdWJibGUgU29ydCBwb2RzdGF3b3d5PC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ18zMV9iYWJlbGtvd2VfcG9kc3Rhd293ZTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5Tb3J0b3dhbmllIGLEhWJlbGtvd2UgYmV6IG9wdHltYWxpemFjamksIHogcG9yw7N3bmFuaWFtaSBpIHphbWlhbmFtaS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgQnViYmxlU29ydChpbnRbXSBhcnIsIG91dCBpbnQgY29tcGFyaXNvbnMsIG91dCBpbnQgc3dhcHMpCiAgICB7CiAgICAgICAgY29tcGFyaXNvbnMgPSAwOwogICAgICAgIHN3YXBzID0gMDsKCiAgICAgICAgZm9yIChpbnQgaSA9IDA7IGkgPCBhcnIuTGVuZ3RoIC0gMTsgaSsrKQogICAgICAgIHsKICAgICAgICAgICAgZm9yIChpbnQgaiA9IDA7IGogPCBhcnIuTGVuZ3RoIC0gMSAtIGk7IGorKykKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgY29tcGFyaXNvbnMrKzsKCiAgICAgICAgICAgICAgICBpZiAoYXJyW2pdID4gYXJyW2ogKyAxXSkKICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICBpbnQgdG1wID0gYXJyW2pdOwogICAgICAgICAgICAgICAgICAgIGFycltqXSA9IGFycltqICsgMV07CiAgICAgICAgICAgICAgICAgICAgYXJyW2ogKyAxXSA9IHRtcDsKICAgICAgICAgICAgICAgICAgICBzd2FwcysrOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICB9CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIFRlc3Qoc3RyaW5nIG5hbWUsIGludFtdIGRhdGEpCiAgICB7CiAgICAgICAgaW50W10gYXJyID0gKGludFtdKWRhdGEuQ2xvbmUoKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUobmFtZSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlByemVkOiAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKCiAgICAgICAgQnViYmxlU29ydChhcnIsIG91dCBpbnQgY29tcGFyaXNvbnMsIG91dCBpbnQgc3dhcHMpOwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUG86ICAgICIgKyBzdHJpbmcuSm9pbigiICIsIGFycikpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJQb3Jvd25hbmlhOiAiICsgY29tcGFyaXNvbnMgKyAiLCB6YW1pYW55OiAiICsgc3dhcHMpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgVGVzdCgiT3B0eW1pc3R5Y3pueSIsIG5ldyBpbnRbXSB7IDEsIDIsIDMsIDQsIDUgfSk7CiAgICAgICAgVGVzdCgiU3JlZG5pIiwgbmV3IGludFtdIHsgMywgMSwgNCwgMiwgNSB9KTsKICAgICAgICBUZXN0KCJQZXN5bWlzdHljem55IiwgbmV3IGludFtdIHsgNSwgNCwgMywgMiwgMSB9KTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+Cg==",
            ["alg_32_babelkowe_optymalne.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjIgLSBCdWJibGUgU29ydCB6IGZsYWfEhTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfMzJfYmFiZWxrb3dlX29wdHltYWxuZTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5CdWJibGUgc29ydCB6IGZsYWfEhSBzd2FwcGVkLCBjenlsaSBrb25pZWMgZ2R5IHRhYmxpY2EganXFvCBqZXN0IG9rLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBCdWJibGVTb3J0T3B0KGludFtdIGFyciwgb3V0IGludCBjb21wYXJpc29ucywgb3V0IGludCBzd2FwcykKICAgIHsKICAgICAgICBjb21wYXJpc29ucyA9IDA7CiAgICAgICAgc3dhcHMgPSAwOwoKICAgICAgICBmb3IgKGludCBpID0gMDsgaSA8IGFyci5MZW5ndGggLSAxOyBpKyspCiAgICAgICAgewogICAgICAgICAgICBib29sIHN3YXBwZWQgPSBmYWxzZTsKCiAgICAgICAgICAgIGZvciAoaW50IGogPSAwOyBqIDwgYXJyLkxlbmd0aCAtIDEgLSBpOyBqKyspCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGNvbXBhcmlzb25zKys7CgogICAgICAgICAgICAgICAgaWYgKGFycltqXSA+IGFycltqICsgMV0pCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgaW50IHRtcCA9IGFycltqXTsKICAgICAgICAgICAgICAgICAgICBhcnJbal0gPSBhcnJbaiArIDFdOwogICAgICAgICAgICAgICAgICAgIGFycltqICsgMV0gPSB0bXA7CiAgICAgICAgICAgICAgICAgICAgc3dhcHMrKzsKICAgICAgICAgICAgICAgICAgICBzd2FwcGVkID0gdHJ1ZTsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQoKICAgICAgICAgICAgaWYgKCFzd2FwcGVkKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBicmVhazsgLy8gamFrIG5pYyBuaWUgem1pZW5pbG8sIHRvIGRhbGVqIG5pZSBtYSBzZW5zdSBsZWNpZWMKICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBUZXN0KHN0cmluZyBuYW1lLCBpbnRbXSBkYXRhKQogICAgewogICAgICAgIGludFtdIGFyciA9IChpbnRbXSlkYXRhLkNsb25lKCk7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKG5hbWUpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJQcnplZDogIiArIHN0cmluZy5Kb2luKCIgIiwgYXJyKSk7CgogICAgICAgIEJ1YmJsZVNvcnRPcHQoYXJyLCBvdXQgaW50IGNvbXBhcmlzb25zLCBvdXQgaW50IHN3YXBzKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlBvOiAgICAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUG9yb3duYW5pYTogIiArIGNvbXBhcmlzb25zICsgIiwgemFtaWFueTogIiArIHN3YXBzKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgpOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIFRlc3QoIk9wdHltaXN0eWN6bnkiLCBuZXcgaW50W10geyAxLCAyLCAzLCA0LCA1IH0pOwogICAgICAgIFRlc3QoIlNyZWRuaSIsIG5ldyBpbnRbXSB7IDMsIDEsIDQsIDIsIDUgfSk7CiAgICAgICAgVGVzdCgiUGVzeW1pc3R5Y3pueSIsIG5ldyBpbnRbXSB7IDUsIDQsIDMsIDIsIDEgfSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPgo=",
            ["alg_33_wybor.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjMgLSBTZWxlY3Rpb24gU29ydDwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfMzNfd3lib3I8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+U29ydG93YW5pZSBwcnpleiB3eWLDs3IsIHogbGljemVuaWVtIHBvcsOzd25hxYQgaSB6YW1pYW4uPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIFNlbGVjdGlvblNvcnQoaW50W10gYXJyLCBvdXQgaW50IGNvbXBhcmlzb25zLCBvdXQgaW50IHN3YXBzKQogICAgewogICAgICAgIGNvbXBhcmlzb25zID0gMDsKICAgICAgICBzd2FwcyA9IDA7CgogICAgICAgIGZvciAoaW50IGkgPSAwOyBpIDwgYXJyLkxlbmd0aCAtIDE7IGkrKykKICAgICAgICB7CiAgICAgICAgICAgIGludCBtaW5JbmRleCA9IGk7CgogICAgICAgICAgICBmb3IgKGludCBqID0gaSArIDE7IGogPCBhcnIuTGVuZ3RoOyBqKyspCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGNvbXBhcmlzb25zKys7CgogICAgICAgICAgICAgICAgaWYgKGFycltqXSA8IGFyclttaW5JbmRleF0pCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgbWluSW5kZXggPSBqOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICB9CgogICAgICAgICAgICBpZiAobWluSW5kZXggIT0gaSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgaW50IHRtcCA9IGFycltpXTsKICAgICAgICAgICAgICAgIGFycltpXSA9IGFyclttaW5JbmRleF07CiAgICAgICAgICAgICAgICBhcnJbbWluSW5kZXhdID0gdG1wOwogICAgICAgICAgICAgICAgc3dhcHMrKzsKICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXSBhcnIgPSB7IDY0LCAyNSwgMTIsIDIyLCAxMSB9OwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUHJ6ZWQ6ICIgKyBzdHJpbmcuSm9pbigiICIsIGFycikpOwogICAgICAgIFNlbGVjdGlvblNvcnQoYXJyLCBvdXQgaW50IGNvbXBhcmlzb25zLCBvdXQgaW50IHN3YXBzKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUG86ICAgICIgKyBzdHJpbmcuSm9pbigiICIsIGFycikpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJQb3Jvd25hbmlhOiAiICsgY29tcGFyaXNvbnMgKyAiLCB6YW1pYW55OiAiICsgc3dhcHMpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4K",
            ["alg_34_wstawianie.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjQgLSBJbnNlcnRpb24gU29ydDwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfMzRfd3N0YXdpYW5pZTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5Tb3J0b3dhbmllIHByemV6IHdzdGF3aWFuaWUsIHByb3N0YSB3ZXJzamEgeiBsaWN6ZW5pZW0gb3BlcmFjamkuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIEluc2VydGlvblNvcnQoaW50W10gYXJyLCBvdXQgaW50IGNvbXBhcmlzb25zLCBvdXQgaW50IHNoaWZ0cykKICAgIHsKICAgICAgICBjb21wYXJpc29ucyA9IDA7CiAgICAgICAgc2hpZnRzID0gMDsKCiAgICAgICAgZm9yIChpbnQgaSA9IDE7IGkgPCBhcnIuTGVuZ3RoOyBpKyspCiAgICAgICAgewogICAgICAgICAgICBpbnQga2V5ID0gYXJyW2ldOwogICAgICAgICAgICBpbnQgaiA9IGkgLSAxOwoKICAgICAgICAgICAgd2hpbGUgKGogPj0gMCkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgY29tcGFyaXNvbnMrKzsKCiAgICAgICAgICAgICAgICBpZiAoYXJyW2pdIDw9IGtleSkKICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICBicmVhazsKICAgICAgICAgICAgICAgIH0KCiAgICAgICAgICAgICAgICBhcnJbaiArIDFdID0gYXJyW2pdOwogICAgICAgICAgICAgICAgc2hpZnRzKys7CiAgICAgICAgICAgICAgICBqLS07CiAgICAgICAgICAgIH0KCiAgICAgICAgICAgIGFycltqICsgMV0gPSBrZXk7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIFRlc3Qoc3RyaW5nIG5hbWUsIGludFtdIGRhdGEpCiAgICB7CiAgICAgICAgaW50W10gYXJyID0gKGludFtdKWRhdGEuQ2xvbmUoKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUobmFtZSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlByemVkOiAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKCiAgICAgICAgSW5zZXJ0aW9uU29ydChhcnIsIG91dCBpbnQgY29tcGFyaXNvbnMsIG91dCBpbnQgc2hpZnRzKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlBvOiAgICAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUG9yb3duYW5pYTogIiArIGNvbXBhcmlzb25zICsgIiwgcHJ6ZXN1bmllY2lhOiAiICsgc2hpZnRzKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgpOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIFRlc3QoIkxvc293YSIsIG5ldyBpbnRbXSB7IDgsIDMsIDUsIDIsIDYgfSk7CiAgICAgICAgVGVzdCgiTWFsZWphY2EiLCBuZXcgaW50W10geyA4LCA2LCA1LCAzLCAyIH0pOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4K",
            ["alg_35_quicksort.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjUgLSBRdWlja1NvcnQ8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzM1X3F1aWNrc29ydDwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5RdWlja1NvcnQgeiBwaXZvdGVtIG5hIGtvxYRjdSB0YWJsaWN5LjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBRdWlja1NvcnQoaW50W10gYXJyLCBpbnQgc3RhcnQsIGludCBlbmQpCiAgICB7CiAgICAgICAgaWYgKHN0YXJ0IDwgZW5kKQogICAgICAgIHsKICAgICAgICAgICAgaW50IHBpdm90SW5kZXggPSBQYXJ0aXRpb24oYXJyLCBzdGFydCwgZW5kKTsKICAgICAgICAgICAgUXVpY2tTb3J0KGFyciwgc3RhcnQsIHBpdm90SW5kZXggLSAxKTsKICAgICAgICAgICAgUXVpY2tTb3J0KGFyciwgcGl2b3RJbmRleCArIDEsIGVuZCk7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyBpbnQgUGFydGl0aW9uKGludFtdIGFyciwgaW50IHN0YXJ0LCBpbnQgZW5kKQogICAgewogICAgICAgIGludCBwaXZvdCA9IGFycltlbmRdOwogICAgICAgIGludCBpID0gc3RhcnQgLSAxOwoKICAgICAgICBmb3IgKGludCBqID0gc3RhcnQ7IGogPCBlbmQ7IGorKykKICAgICAgICB7CiAgICAgICAgICAgIGlmIChhcnJbal0gPD0gcGl2b3QpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGkrKzsKICAgICAgICAgICAgICAgIFN3YXAoYXJyLCBpLCBqKTsKICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgU3dhcChhcnIsIGkgKyAxLCBlbmQpOwogICAgICAgIHJldHVybiBpICsgMTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBTd2FwKGludFtdIGFyciwgaW50IGEsIGludCBiKQogICAgewogICAgICAgIGludCB0bXAgPSBhcnJbYV07CiAgICAgICAgYXJyW2FdID0gYXJyW2JdOwogICAgICAgIGFycltiXSA9IHRtcDsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXSBhcnIgPSB7IDksIDUsIDcsIDQsIDgsIDYgfTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlByemVkOiAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgICAgICBRdWlja1NvcnQoYXJyLCAwLCBhcnIuTGVuZ3RoIC0gMSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlBvOiAgICAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+Cg==",
            ["alg_36_liniowe.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjYgLSBMaW5lYXIgU2VhcmNoPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ18zNl9saW5pb3dlPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPld5c3p1a2l3YW5pZSBsaW5pb3dlIHogbGljemVuaWVtIHBvcsOzd25hxYQuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyBpbnQgTGluZWFyU2VhcmNoKGludFtdIGFyciwgaW50IHRhcmdldCwgb3V0IGludCBjb21wYXJpc29ucykKICAgIHsKICAgICAgICBjb21wYXJpc29ucyA9IDA7CgogICAgICAgIGZvciAoaW50IGkgPSAwOyBpIDwgYXJyLkxlbmd0aDsgaSsrKQogICAgICAgIHsKICAgICAgICAgICAgY29tcGFyaXNvbnMrKzsKCiAgICAgICAgICAgIGlmIChhcnJbaV0gPT0gdGFyZ2V0KQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICByZXR1cm4gaTsKICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIC0xOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIFRlc3QoaW50W10gYXJyLCBpbnQgdGFyZ2V0KQogICAgewogICAgICAgIGludCBpbmRleCA9IExpbmVhclNlYXJjaChhcnIsIHRhcmdldCwgb3V0IGludCBjb21wYXJpc29ucyk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlN6dWthbmE6ICIgKyB0YXJnZXQgKyAiLCBpbmRleDogIiArIGluZGV4ICsgIiwgcG9yb3duYW5pYTogIiArIGNvbXBhcmlzb25zKTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXSBhcnIgPSB7IDQsIDcsIDIsIDksIDEgfTsKCiAgICAgICAgVGVzdChhcnIsIDQpOyAvLyBvcHR5bWlzdHljem55CiAgICAgICAgVGVzdChhcnIsIDIpOyAvLyBzcmVkbmkKICAgICAgICBUZXN0KGFyciwgNSk7IC8vIHBlc3ltaXN0eWN6bnkgLyBicmFrCiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPgo=",
            ["alg_37_liniowe_wartownik.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjcgLSBMaW5lYXIgU2VhcmNoIHogd2FydG93bmlraWVtPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ18zN19saW5pb3dlX3dhcnRvd25pazwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5XeXN6dWtpd2FuaWUgbGluaW93ZSB6IHdhcnRvd25pa2llbSBuYSBrb8WEY3UgdGFibGljeS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIGludCBMaW5lYXJTZWFyY2hTZW50aW5lbChpbnRbXSBhcnIsIGludCB0YXJnZXQsIG91dCBpbnQgY29tcGFyaXNvbnMpCiAgICB7CiAgICAgICAgY29tcGFyaXNvbnMgPSAwOwogICAgICAgIGludCBuID0gYXJyLkxlbmd0aDsKICAgICAgICBpbnQgbGFzdCA9IGFycltuIC0gMV07CgogICAgICAgIGFycltuIC0gMV0gPSB0YXJnZXQ7IC8vIHdhcnRvd25pawoKICAgICAgICBpbnQgaSA9IDA7CiAgICAgICAgd2hpbGUgKGFycltpXSAhPSB0YXJnZXQpCiAgICAgICAgewogICAgICAgICAgICBjb21wYXJpc29ucysrOwogICAgICAgICAgICBpKys7CiAgICAgICAgfQoKICAgICAgICBjb21wYXJpc29ucysrOwogICAgICAgIGFycltuIC0gMV0gPSBsYXN0OwoKICAgICAgICBpZiAoaSA8IG4gLSAxIHx8IGFycltuIC0gMV0gPT0gdGFyZ2V0KQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuIGk7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gLTE7CiAgICB9CgogICAgc3RhdGljIHZvaWQgVGVzdChpbnRbXSBkYXRhLCBpbnQgdGFyZ2V0KQogICAgewogICAgICAgIGludFtdIGFyciA9IChpbnRbXSlkYXRhLkNsb25lKCk7CiAgICAgICAgaW50IGluZGV4ID0gTGluZWFyU2VhcmNoU2VudGluZWwoYXJyLCB0YXJnZXQsIG91dCBpbnQgY29tcGFyaXNvbnMpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJTenVrYW5hOiAiICsgdGFyZ2V0ICsgIiwgaW5kZXg6ICIgKyBpbmRleCArICIsIHBvcm93bmFuaWE6ICIgKyBjb21wYXJpc29ucyk7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgaW50W10gYXJyID0geyA0LCA3LCAyLCA5LCA1IH07CgogICAgICAgIFRlc3QoYXJyLCA0KTsKICAgICAgICBUZXN0KGFyciwgOSk7CiAgICAgICAgVGVzdChhcnIsIDYpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4K",
            ["alg_38_binarne.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiAzLjggLSBCaW5hcnkgU2VhcmNoPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ18zOF9iaW5hcm5lPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPld5c3p1a2l3YW5pZSBiaW5hcm5lIGkga3LDs3RraWUgcG9yw7N3bmFuaWUgeiBsaW5pb3d5bS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIGludCBCaW5hcnlTZWFyY2goaW50W10gYXJyLCBpbnQgdGFyZ2V0LCBvdXQgaW50IGNvbXBhcmlzb25zKQogICAgewogICAgICAgIGludCBsZWZ0ID0gMDsKICAgICAgICBpbnQgcmlnaHQgPSBhcnIuTGVuZ3RoIC0gMTsKICAgICAgICBjb21wYXJpc29ucyA9IDA7CgogICAgICAgIHdoaWxlIChsZWZ0IDw9IHJpZ2h0KQogICAgICAgIHsKICAgICAgICAgICAgaW50IG1pZCA9IGxlZnQgKyAocmlnaHQgLSBsZWZ0KSAvIDI7CiAgICAgICAgICAgIGNvbXBhcmlzb25zKys7CgogICAgICAgICAgICBpZiAoYXJyW21pZF0gPT0gdGFyZ2V0KQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICByZXR1cm4gbWlkOwogICAgICAgICAgICB9CgogICAgICAgICAgICBpZiAoYXJyW21pZF0gPCB0YXJnZXQpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGxlZnQgPSBtaWQgKyAxOwogICAgICAgICAgICB9CiAgICAgICAgICAgIGVsc2UKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgcmlnaHQgPSBtaWQgLSAxOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gLTE7CiAgICB9CgogICAgc3RhdGljIGludCBMaW5lYXJTZWFyY2goaW50W10gYXJyLCBpbnQgdGFyZ2V0LCBvdXQgaW50IGNvbXBhcmlzb25zKQogICAgewogICAgICAgIGNvbXBhcmlzb25zID0gMDsKCiAgICAgICAgZm9yIChpbnQgaSA9IDA7IGkgPCBhcnIuTGVuZ3RoOyBpKyspCiAgICAgICAgewogICAgICAgICAgICBjb21wYXJpc29ucysrOwogICAgICAgICAgICBpZiAoYXJyW2ldID09IHRhcmdldCkgcmV0dXJuIGk7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gLTE7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgaW50W10gYXJyID0geyAyLCA0LCA2LCA4LCAxMCwgMTIsIDE0IH07CiAgICAgICAgaW50IHRhcmdldCA9IDEwOwoKICAgICAgICBpbnQgaW5kZXhCaW4gPSBCaW5hcnlTZWFyY2goYXJyLCB0YXJnZXQsIG91dCBpbnQgY29tcEJpbik7CiAgICAgICAgaW50IGluZGV4TGluID0gTGluZWFyU2VhcmNoKGFyciwgdGFyZ2V0LCBvdXQgaW50IGNvbXBMaW4pOwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiQmluYXJ5OiBpbmRleCAiICsgaW5kZXhCaW4gKyAiLCBwb3Jvd25hbmlhICIgKyBjb21wQmluKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiTGluZWFyOiBpbmRleCAiICsgaW5kZXhMaW4gKyAiLCBwb3Jvd25hbmlhICIgKyBjb21wTGluKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+Cg==",
            ["alg_41_lucas.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjEgLSBDacSFZyBMdWNhc2E8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzQxX2x1Y2FzPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPkx1Y2FzOiByZWt1cmVuY2phLCBtZW1vaXphY2phLCBib3R0b20tdXAgaSB3ZXJzamEgb2dvbm93YS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyBpbnQgTHVjYXNSZWMoaW50IG4pCiAgICB7CiAgICAgICAgaWYgKG4gPT0gMCkgcmV0dXJuIDI7CiAgICAgICAgaWYgKG4gPT0gMSkgcmV0dXJuIDE7CiAgICAgICAgcmV0dXJuIEx1Y2FzUmVjKG4gLSAxKSArIEx1Y2FzUmVjKG4gLSAyKTsKICAgIH0KCiAgICBzdGF0aWMgaW50IEx1Y2FzTWVtbyhpbnQgbiwgRGljdGlvbmFyeTxpbnQsIGludD4gbWVtbykKICAgIHsKICAgICAgICBpZiAobWVtby5Db250YWluc0tleShuKSkgcmV0dXJuIG1lbW9bbl07CgogICAgICAgIG1lbW9bbl0gPSBMdWNhc01lbW8obiAtIDEsIG1lbW8pICsgTHVjYXNNZW1vKG4gLSAyLCBtZW1vKTsKICAgICAgICByZXR1cm4gbWVtb1tuXTsKICAgIH0KCiAgICBzdGF0aWMgaW50IEx1Y2FzQm90dG9tVXAoaW50IG4pCiAgICB7CiAgICAgICAgaWYgKG4gPT0gMCkgcmV0dXJuIDI7CiAgICAgICAgaWYgKG4gPT0gMSkgcmV0dXJuIDE7CgogICAgICAgIGludCBwcmV2MiA9IDI7CiAgICAgICAgaW50IHByZXYxID0gMTsKCiAgICAgICAgZm9yIChpbnQgaSA9IDI7IGkgPD0gbjsgaSsrKQogICAgICAgIHsKICAgICAgICAgICAgaW50IGN1cnJlbnQgPSBwcmV2MSArIHByZXYyOwogICAgICAgICAgICBwcmV2MiA9IHByZXYxOwogICAgICAgICAgICBwcmV2MSA9IGN1cnJlbnQ7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gcHJldjE7CiAgICB9CgogICAgc3RhdGljIGludCBMdWNhc1RhaWwoaW50IG4sIGludCBhID0gMiwgaW50IGIgPSAxKQogICAgewogICAgICAgIGlmIChuID09IDApIHJldHVybiBhOwogICAgICAgIGlmIChuID09IDEpIHJldHVybiBiOwogICAgICAgIHJldHVybiBMdWNhc1RhaWwobiAtIDEsIGIsIGEgKyBiKTsKICAgIH0KCiAgICBzdGF0aWMgaW50IENvdW50Q2FsbHNMdWNhcyhpbnQgbikKICAgIHsKICAgICAgICBpZiAobiA9PSAwIHx8IG4gPT0gMSkgcmV0dXJuIDE7CiAgICAgICAgcmV0dXJuIDEgKyBDb3VudENhbGxzTHVjYXMobiAtIDEpICsgQ291bnRDYWxsc0x1Y2FzKG4gLSAyKTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnQgbiA9IDU7CiAgICAgICAgdmFyIG1lbW8gPSBuZXcgRGljdGlvbmFyeTxpbnQsIGludD4geyBbMF0gPSAyLCBbMV0gPSAxIH07CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJSZWM6ICIgKyBMdWNhc1JlYyhuKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIk1lbW86ICIgKyBMdWNhc01lbW8obiwgbWVtbykpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJCb3R0b20tdXA6ICIgKyBMdWNhc0JvdHRvbVVwKG4pKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiVGFpbDogIiArIEx1Y2FzVGFpbChuKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIld5d29sYW5pYSByZWMgZGxhIG49NTogIiArIENvdW50Q2FsbHNMdWNhcyg1KSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPgo=",
            ["alg_42_tetranacci.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjIgLSBDacSFZyBUZXRyYW5hY2NpZWdvPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ180Ml90ZXRyYW5hY2NpPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlRldHJhbmFjY2kgdyBraWxrdSB3ZXJzamFjaDogcmVjLCBtZW1vLCBib3R0b20tdXAgaSB0YWlsLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIGludCBUZXRyYVJlYyhpbnQgbikKICAgIHsKICAgICAgICBpZiAobiA8IDMpIHJldHVybiAwOwogICAgICAgIGlmIChuID09IDMpIHJldHVybiAxOwogICAgICAgIHJldHVybiBUZXRyYVJlYyhuIC0gMSkgKyBUZXRyYVJlYyhuIC0gMikgKyBUZXRyYVJlYyhuIC0gMykgKyBUZXRyYVJlYyhuIC0gNCk7CiAgICB9CgogICAgc3RhdGljIGludCBUZXRyYU1lbW8oaW50IG4sIERpY3Rpb25hcnk8aW50LCBpbnQ+IG1lbW8pCiAgICB7CiAgICAgICAgaWYgKG1lbW8uQ29udGFpbnNLZXkobikpIHJldHVybiBtZW1vW25dOwoKICAgICAgICBtZW1vW25dID0gVGV0cmFNZW1vKG4gLSAxLCBtZW1vKSArIFRldHJhTWVtbyhuIC0gMiwgbWVtbykgKyBUZXRyYU1lbW8obiAtIDMsIG1lbW8pICsgVGV0cmFNZW1vKG4gLSA0LCBtZW1vKTsKICAgICAgICByZXR1cm4gbWVtb1tuXTsKICAgIH0KCiAgICBzdGF0aWMgaW50IFRldHJhQm90dG9tVXAoaW50IG4pCiAgICB7CiAgICAgICAgaWYgKG4gPCAzKSByZXR1cm4gMDsKICAgICAgICBpZiAobiA9PSAzKSByZXR1cm4gMTsKCiAgICAgICAgaW50IGEgPSAwLCBiID0gMCwgYyA9IDAsIGQgPSAxOwoKICAgICAgICBmb3IgKGludCBpID0gNDsgaSA8PSBuOyBpKyspCiAgICAgICAgewogICAgICAgICAgICBpbnQgY3VycmVudCA9IGEgKyBiICsgYyArIGQ7CiAgICAgICAgICAgIGEgPSBiOwogICAgICAgICAgICBiID0gYzsKICAgICAgICAgICAgYyA9IGQ7CiAgICAgICAgICAgIGQgPSBjdXJyZW50OwogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIGQ7CiAgICB9CgogICAgc3RhdGljIGludCBUZXRyYVRhaWwoaW50IG4sIGludCBhID0gMCwgaW50IGIgPSAwLCBpbnQgYyA9IDAsIGludCBkID0gMSkKICAgIHsKICAgICAgICBpZiAobiA9PSAwKSByZXR1cm4gYTsKICAgICAgICBpZiAobiA9PSAxKSByZXR1cm4gYjsKICAgICAgICBpZiAobiA9PSAyKSByZXR1cm4gYzsKICAgICAgICBpZiAobiA9PSAzKSByZXR1cm4gZDsKICAgICAgICByZXR1cm4gVGV0cmFUYWlsKG4gLSAxLCBiLCBjLCBkLCBhICsgYiArIGMgKyBkKTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnQgbiA9IDc7CiAgICAgICAgdmFyIG1lbW8gPSBuZXcgRGljdGlvbmFyeTxpbnQsIGludD4geyBbMF0gPSAwLCBbMV0gPSAwLCBbMl0gPSAwLCBbM10gPSAxIH07CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJSZWM6ICIgKyBUZXRyYVJlYyhuKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIk1lbW86ICIgKyBUZXRyYU1lbW8obiwgbWVtbykpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJCb3R0b20tdXA6ICIgKyBUZXRyYUJvdHRvbVVwKG4pKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiVGFpbDogIiArIFRldHJhVGFpbChuKSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPgo=",
            ["alg_43_nwd.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjMgLSBOV0Q8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzQzX253ZDwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5BbGdvcnl0bSBFdWtsaWRlc2EgdyB3ZXJzamkgcmVrdXJlbmN5am5laiBpIGl0ZXJhY3lqbmVqLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgaW50IE53ZFJlYyhpbnQgYSwgaW50IGIpCiAgICB7CiAgICAgICAgaWYgKGIgPT0gMCkgcmV0dXJuIE1hdGguQWJzKGEpOwogICAgICAgIHJldHVybiBOd2RSZWMoYiwgYSAlIGIpOwogICAgfQoKICAgIHN0YXRpYyBpbnQgTndkSXRlcihpbnQgYSwgaW50IGIpCiAgICB7CiAgICAgICAgYSA9IE1hdGguQWJzKGEpOwogICAgICAgIGIgPSBNYXRoLkFicyhiKTsKCiAgICAgICAgd2hpbGUgKGIgIT0gMCkKICAgICAgICB7CiAgICAgICAgICAgIGludCB0bXAgPSBiOwogICAgICAgICAgICBiID0gYSAlIGI7CiAgICAgICAgICAgIGEgPSB0bXA7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gYTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnQgYSA9IDQ4OwogICAgICAgIGludCBiID0gMTg7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJOV0QgcmVjOiAiICsgTndkUmVjKGEsIGIpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiTldEIGl0ZXI6ICIgKyBOd2RJdGVyKGEsIGIpKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+Cg==",
            ["alg_44_pascal.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjQgLSBUcsOzamvEhXQgUGFzY2FsYTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNDRfcGFzY2FsPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlBhc2NhbCByZWt1cmVuY3lqbmllIGkgaXRlcmFjeWpuaWUuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyBpbnQgQyhpbnQgbiwgaW50IGspCiAgICB7CiAgICAgICAgaWYgKGsgPT0gMCB8fCBrID09IG4pIHJldHVybiAxOwogICAgICAgIHJldHVybiBDKG4gLSAxLCBrIC0gMSkgKyBDKG4gLSAxLCBrKTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBQYXNjYWxSZWMoaW50IHJvd3MpCiAgICB7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlJla3VyZW5jeWpuaWU6Iik7CgogICAgICAgIGZvciAoaW50IG4gPSAwOyBuIDwgcm93czsgbisrKQogICAgICAgIHsKICAgICAgICAgICAgZm9yIChpbnQgayA9IDA7IGsgPD0gbjsgaysrKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBDb25zb2xlLldyaXRlKEMobiwgaykgKyAiICIpOwogICAgICAgICAgICB9CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIFBhc2NhbEl0ZXIoaW50IHJvd3MpCiAgICB7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIkl0ZXJhY3lqbmllOiIpOwogICAgICAgIGludFssXSBhcnIgPSBuZXcgaW50W3Jvd3MsIHJvd3NdOwoKICAgICAgICBmb3IgKGludCBuID0gMDsgbiA8IHJvd3M7IG4rKykKICAgICAgICB7CiAgICAgICAgICAgIGFycltuLCAwXSA9IDE7CiAgICAgICAgICAgIGFycltuLCBuXSA9IDE7CgogICAgICAgICAgICBmb3IgKGludCBrID0gMTsgayA8IG47IGsrKykKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgYXJyW24sIGtdID0gYXJyW24gLSAxLCBrIC0gMV0gKyBhcnJbbiAtIDEsIGtdOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICBmb3IgKGludCBuID0gMDsgbiA8IHJvd3M7IG4rKykKICAgICAgICB7CiAgICAgICAgICAgIGZvciAoaW50IGsgPSAwOyBrIDw9IG47IGsrKykKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgQ29uc29sZS5Xcml0ZShhcnJbbiwga10gKyAiICIpOwogICAgICAgICAgICB9CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIGludCByb3dzID0gNjsKICAgICAgICBQYXNjYWxSZWMocm93cyk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoKTsKICAgICAgICBQYXNjYWxJdGVyKHJvd3MpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4K",
            ["alg_45_mergesort.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjUgLSBNZXJnZSBTb3J0PC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ180NV9tZXJnZXNvcnQ8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+TWVyZ2UgU29ydCBkbGEgdGFibGljeSBpbnQsIHogb3NvYm7EhSBmdW5rY2rEhSBNZXJnZS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgTWVyZ2VTb3J0KGludFtdIGFyciwgaW50IGxlZnQsIGludCByaWdodCkKICAgIHsKICAgICAgICBpZiAobGVmdCA+PSByaWdodCkKICAgICAgICB7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGludCBtaWQgPSBsZWZ0ICsgKHJpZ2h0IC0gbGVmdCkgLyAyOwoKICAgICAgICBNZXJnZVNvcnQoYXJyLCBsZWZ0LCBtaWQpOwogICAgICAgIE1lcmdlU29ydChhcnIsIG1pZCArIDEsIHJpZ2h0KTsKICAgICAgICBNZXJnZShhcnIsIGxlZnQsIG1pZCwgcmlnaHQpOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1lcmdlKGludFtdIGFyciwgaW50IGxlZnQsIGludCBtaWQsIGludCByaWdodCkKICAgIHsKICAgICAgICBpbnQgbjEgPSBtaWQgLSBsZWZ0ICsgMTsKICAgICAgICBpbnQgbjIgPSByaWdodCAtIG1pZDsKCiAgICAgICAgaW50W10gTCA9IG5ldyBpbnRbbjFdOwogICAgICAgIGludFtdIFIgPSBuZXcgaW50W24yXTsKCiAgICAgICAgZm9yIChpbnQgaSA9IDA7IGkgPCBuMTsgaSsrKSBMW2ldID0gYXJyW2xlZnQgKyBpXTsKICAgICAgICBmb3IgKGludCBqID0gMDsgaiA8IG4yOyBqKyspIFJbal0gPSBhcnJbbWlkICsgMSArIGpdOwoKICAgICAgICBpbnQgYSA9IDA7CiAgICAgICAgaW50IGIgPSAwOwogICAgICAgIGludCBrID0gbGVmdDsKCiAgICAgICAgd2hpbGUgKGEgPCBuMSAmJiBiIDwgbjIpCiAgICAgICAgewogICAgICAgICAgICBpZiAoTFthXSA8PSBSW2JdKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBhcnJbaysrXSA9IExbYSsrXTsKICAgICAgICAgICAgfQogICAgICAgICAgICBlbHNlCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGFycltrKytdID0gUltiKytdOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICB3aGlsZSAoYSA8IG4xKSBhcnJbaysrXSA9IExbYSsrXTsKICAgICAgICB3aGlsZSAoYiA8IG4yKSBhcnJbaysrXSA9IFJbYisrXTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXSBhcnIgPSB7IDgsIDcsIDYsIDMsIDIsIDEgfTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlByemVkOiAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgICAgICBNZXJnZVNvcnQoYXJyLCAwLCBhcnIuTGVuZ3RoIC0gMSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlBvOiAgICAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+Cg==",
            ["alg_46_heapsort.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjYgLSBIZWFwIFNvcnQ8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzQ2X2hlYXBzb3J0PC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPkhlYXAgU29ydCB6IGZ1bmtjasSFIEhlYXBpZnkuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIEhlYXBTb3J0KGludFtdIGFycikKICAgIHsKICAgICAgICBpbnQgbiA9IGFyci5MZW5ndGg7CgogICAgICAgIGZvciAoaW50IGkgPSBuIC8gMiAtIDE7IGkgPj0gMDsgaS0tKQogICAgICAgIHsKICAgICAgICAgICAgSGVhcGlmeShhcnIsIG4sIGkpOwogICAgICAgIH0KCiAgICAgICAgZm9yIChpbnQgaSA9IG4gLSAxOyBpID4gMDsgaS0tKQogICAgICAgIHsKICAgICAgICAgICAgU3dhcChhcnIsIDAsIGkpOwogICAgICAgICAgICBIZWFwaWZ5KGFyciwgaSwgMCk7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIEhlYXBpZnkoaW50W10gYXJyLCBpbnQgc2l6ZSwgaW50IGkpCiAgICB7CiAgICAgICAgaW50IGxhcmdlc3QgPSBpOwogICAgICAgIGludCBsZWZ0ID0gMiAqIGkgKyAxOwogICAgICAgIGludCByaWdodCA9IDIgKiBpICsgMjsKCiAgICAgICAgaWYgKGxlZnQgPCBzaXplICYmIGFycltsZWZ0XSA+IGFycltsYXJnZXN0XSkKICAgICAgICB7CiAgICAgICAgICAgIGxhcmdlc3QgPSBsZWZ0OwogICAgICAgIH0KCiAgICAgICAgaWYgKHJpZ2h0IDwgc2l6ZSAmJiBhcnJbcmlnaHRdID4gYXJyW2xhcmdlc3RdKQogICAgICAgIHsKICAgICAgICAgICAgbGFyZ2VzdCA9IHJpZ2h0OwogICAgICAgIH0KCiAgICAgICAgaWYgKGxhcmdlc3QgIT0gaSkKICAgICAgICB7CiAgICAgICAgICAgIFN3YXAoYXJyLCBpLCBsYXJnZXN0KTsKICAgICAgICAgICAgSGVhcGlmeShhcnIsIHNpemUsIGxhcmdlc3QpOwogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBTd2FwKGludFtdIGFyciwgaW50IGEsIGludCBiKQogICAgewogICAgICAgIGludCB0bXAgPSBhcnJbYV07CiAgICAgICAgYXJyW2FdID0gYXJyW2JdOwogICAgICAgIGFycltiXSA9IHRtcDsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXSBhcnIgPSB7IDEyLCAxMSwgMTMsIDUsIDYsIDcgfTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlByemVkOiAiICsgc3RyaW5nLkpvaW4oIiAiLCBhcnIpKTsKICAgICAgICBIZWFwU29ydChhcnIpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJQbzogICAgIiArIHN0cmluZy5Kb2luKCIgIiwgYXJyKSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPgo=",
            ["alg_47_hanoi.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA0LjcgLSBXaWXFvGUgSGFub2k8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzQ3X2hhbm9pPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPldpZcW8ZSBIYW5vaSByZWt1cmVuY3lqbmllLCBwbHVzIGxpY3piYSBydWNow7N3IGRsYSBraWxrdSBuLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgbG9uZyBtb3ZlczsKCiAgICBzdGF0aWMgdm9pZCBIYW5vaShpbnQgbiwgY2hhciBmcm9tLCBjaGFyIGhlbHBlciwgY2hhciB0bykKICAgIHsKICAgICAgICBpZiAobiA9PSAwKQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgSGFub2kobiAtIDEsIGZyb20sIHRvLCBoZWxwZXIpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKGZyb20gKyAiIC0+ICIgKyB0byk7CiAgICAgICAgbW92ZXMrKzsKICAgICAgICBIYW5vaShuIC0gMSwgaGVscGVyLCBmcm9tLCB0byk7CiAgICB9CgogICAgc3RhdGljIGxvbmcgQ291bnRNb3ZlcyhpbnQgbikKICAgIHsKICAgICAgICByZXR1cm4gKDFMIDw8IG4pIC0gMTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBtb3ZlcyA9IDA7CiAgICAgICAgSGFub2koMywgJ0EnLCAnQicsICdDJyk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlJ1Y2h5IGRsYSBuPTM6ICIgKyBtb3Zlcyk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoKTsKCiAgICAgICAgZm9yIChpbnQgbiA9IDM7IG4gPD0gNzsgbisrKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIm49IiArIG4gKyAiLCBydWNoeT0iICsgQ291bnRNb3ZlcyhuKSk7CiAgICAgICAgfQogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4K",
            ["alg_510_nawiasy.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjEwIC0gTmF3aWFzeTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNTEwX25hd2lhc3k8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+U3ByYXdkemVuaWUgcG9wcmF3bm/Fm2NpIG5hd2lhc8OzdyB6YSBwb21vY8SFIHN0b3N1LjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIGJvb2wgQ3p5UG9wcmF3bmVOYXdpYXN5KHN0cmluZyB3eXJhemVuaWUpCiAgICB7CiAgICAgICAgU3RhY2s8Y2hhcj4gc3RvcyA9IG5ldyBTdGFjazxjaGFyPigpOwoKICAgICAgICBmb3JlYWNoIChjaGFyIHpuYWsgaW4gd3lyYXplbmllKQogICAgICAgIHsKICAgICAgICAgICAgaWYgKHpuYWsgPT0gJygnKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBzdG9zLlB1c2goem5hayk7CiAgICAgICAgICAgIH0KICAgICAgICAgICAgZWxzZSBpZiAoem5hayA9PSAnKScpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGlmIChzdG9zLkNvdW50ID09IDApCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlOwogICAgICAgICAgICAgICAgfQoKICAgICAgICAgICAgICAgIHN0b3MuUG9wKCk7CiAgICAgICAgICAgIH0KICAgICAgICB9CgogICAgICAgIHJldHVybiBzdG9zLkNvdW50ID09IDA7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgQ29uc29sZS5Xcml0ZSgiUG9kYWogd3lyYXplbmllOiAiKTsKICAgICAgICBzdHJpbmcgd3lyYXplbmllID0gQ29uc29sZS5SZWFkTGluZSgpOwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZShDenlQb3ByYXduZU5hd2lhc3kod3lyYXplbmllKSA/ICJQb3ByYXduZSIgOiAiTmllcG9wcmF3bmUiKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+",
            ["alg_51_lista_jedno.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjEgLSBMaXN0YSBqZWRub2tpZXJ1bmtvd2E8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzUxX2xpc3RhX2plZG5vPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPkxpc3RhIGplZG5va2llcnVua293YTogZG9kYXdhbmllLCB1c3V3YW5pZSwgc3p1a2FuaWUgaSB3eXBpc2FuaWUuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwpwdWJsaWMgY2xhc3MgTGlzdGFKZWRub2tpZXJ1bmtvd2EKewogICAgcHJpdmF0ZSBjbGFzcyBOb2RlCiAgICB7CiAgICAgICAgcHVibGljIGludCBWYWx1ZTsKICAgICAgICBwdWJsaWMgTm9kZSBOZXh0OwoKICAgICAgICBwdWJsaWMgTm9kZShpbnQgdmFsdWUpCiAgICAgICAgewogICAgICAgICAgICBWYWx1ZSA9IHZhbHVlOwogICAgICAgICAgICBOZXh0ID0gbnVsbDsKICAgICAgICB9CiAgICB9CgogICAgcHJpdmF0ZSBOb2RlIGhlYWQ7CgogICAgcHVibGljIGJvb2wgQ3p5UHVzdGEoKQogICAgewogICAgICAgIHJldHVybiBoZWFkID09IG51bGw7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpOYVBvY3phdGVrKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIG5ld05vZGUgPSBuZXcgTm9kZSh2YWx1ZSk7CiAgICAgICAgbmV3Tm9kZS5OZXh0ID0gaGVhZDsKICAgICAgICBoZWFkID0gbmV3Tm9kZTsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBEb2Rhak5hS29uaWVjKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIG5ld05vZGUgPSBuZXcgTm9kZSh2YWx1ZSk7CgogICAgICAgIGlmIChoZWFkID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gbmV3Tm9kZTsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgTm9kZSBjdXJyZW50ID0gaGVhZDsKCiAgICAgICAgd2hpbGUgKGN1cnJlbnQuTmV4dCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgY3VycmVudCA9IGN1cnJlbnQuTmV4dDsKICAgICAgICB9CgogICAgICAgIGN1cnJlbnQuTmV4dCA9IG5ld05vZGU7CiAgICB9CgogICAgcHVibGljIHZvaWQgVXN1blBpZXJ3c3p5KCkKICAgIHsKICAgICAgICBpZiAoaGVhZCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgaGVhZCA9IGhlYWQuTmV4dDsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBVc3VuKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBpZiAoaGVhZCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgaWYgKGhlYWQuVmFsdWUgPT0gdmFsdWUpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gaGVhZC5OZXh0OwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICBOb2RlIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudC5OZXh0ICE9IG51bGwgJiYgY3VycmVudC5OZXh0LlZhbHVlICE9IHZhbHVlKQogICAgICAgIHsKICAgICAgICAgICAgY3VycmVudCA9IGN1cnJlbnQuTmV4dDsKICAgICAgICB9CgogICAgICAgIGlmIChjdXJyZW50Lk5leHQgIT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIGN1cnJlbnQuTmV4dCA9IGN1cnJlbnQuTmV4dC5OZXh0OwogICAgICAgIH0KICAgIH0KCiAgICBwdWJsaWMgYm9vbCBaYXdpZXJhKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaWYgKGN1cnJlbnQuVmFsdWUgPT0gdmFsdWUpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIHJldHVybiB0cnVlOwogICAgICAgICAgICB9CgogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIGZhbHNlOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFd5cGlzeigpCiAgICB7CiAgICAgICAgTm9kZSBjdXJyZW50ID0gaGVhZDsKCiAgICAgICAgd2hpbGUgKGN1cnJlbnQgIT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlKGN1cnJlbnQuVmFsdWUgKyAiICIpOwogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICB9Cn0KCnB1YmxpYyBjbGFzcyBQcm9ncmFtCnsKICAgIHB1YmxpYyBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBMaXN0YUplZG5va2llcnVua293YSBsaXN0YSA9IG5ldyBMaXN0YUplZG5va2llcnVua293YSgpOwoKICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZUxpbmUoIkN6eSBwdXN0YSBuYSBzdGFydDogIiArIGxpc3RhLkN6eVB1c3RhKCkpOwoKICAgICAgICBsaXN0YS5Eb2Rhak5hUG9jemF0ZWsoMTApOwogICAgICAgIGxpc3RhLkRvZGFqTmFQb2N6YXRlayg1KTsKICAgICAgICBsaXN0YS5Eb2Rhak5hS29uaWVjKDIwKTsKCiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCJQbyBEb2Rhak5hUG9jemF0ZWsoMTApLCBEb2Rhak5hUG9jemF0ZWsoNSksIERvZGFqTmFLb25pZWMoMjApOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwoKICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZUxpbmUoIlphd2llcmEoMTApOiAiICsgbGlzdGEuWmF3aWVyYSgxMCkpOwoKICAgICAgICBsaXN0YS5Vc3VuKDEwKTsKICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZUxpbmUoIlBvIFVzdW4oMTApOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwoKICAgICAgICBsaXN0YS5Vc3VuUGllcndzenkoKTsKICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZUxpbmUoIlBvIFVzdW5QaWVyd3N6eSgpOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_52_lista_ogon.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjIgLSBMaXN0YSBqZWRub2tpZXJ1bmtvd2EgeiBvZ29uZW08L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzUyX2xpc3RhX29nb248L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+TGlzdGEgamVkbm9raWVydW5rb3dhIHogaGVhZC90YWlsLCBzenlia2llIGRvZGF3YW5pZSBuYSBrb25pZWMuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwpwdWJsaWMgY2xhc3MgTGlzdGFKZWRub2tpZXJ1bmtvd2FaT2dvbmVtCnsKICAgIHByaXZhdGUgY2xhc3MgTm9kZQogICAgewogICAgICAgIHB1YmxpYyBpbnQgVmFsdWU7CiAgICAgICAgcHVibGljIE5vZGUgTmV4dDsKCiAgICAgICAgcHVibGljIE5vZGUoaW50IHZhbHVlKQogICAgICAgIHsKICAgICAgICAgICAgVmFsdWUgPSB2YWx1ZTsKICAgICAgICAgICAgTmV4dCA9IG51bGw7CiAgICAgICAgfQogICAgfQoKICAgIHByaXZhdGUgTm9kZSBoZWFkOwogICAgcHJpdmF0ZSBOb2RlIHRhaWw7CgogICAgcHVibGljIGJvb2wgQ3p5UHVzdGEoKQogICAgewogICAgICAgIHJldHVybiBoZWFkID09IG51bGw7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpOYVBvY3phdGVrKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIG5ld05vZGUgPSBuZXcgTm9kZSh2YWx1ZSk7CgogICAgICAgIGlmIChoZWFkID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gbmV3Tm9kZTsKICAgICAgICAgICAgdGFpbCA9IG5ld05vZGU7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIG5ld05vZGUuTmV4dCA9IGhlYWQ7CiAgICAgICAgaGVhZCA9IG5ld05vZGU7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpOYUtvbmllYyhpbnQgdmFsdWUpCiAgICB7CiAgICAgICAgTm9kZSBuZXdOb2RlID0gbmV3IE5vZGUodmFsdWUpOwoKICAgICAgICBpZiAoaGVhZCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaGVhZCA9IG5ld05vZGU7CiAgICAgICAgICAgIHRhaWwgPSBuZXdOb2RlOwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICAvLyB0YWlsIHBvendhbGEgbmllIHN6dWthxIcga2/FhGNhIGxpc3R5IHphIGthxbxkeW0gcmF6ZW0KICAgICAgICB0YWlsLk5leHQgPSBuZXdOb2RlOwogICAgICAgIHRhaWwgPSBuZXdOb2RlOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFVzdW5QaWVyd3N6eSgpCiAgICB7CiAgICAgICAgaWYgKGhlYWQgPT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGhlYWQgPSBoZWFkLk5leHQ7CgogICAgICAgIGlmIChoZWFkID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICB0YWlsID0gbnVsbDsKICAgICAgICB9CiAgICB9CgogICAgcHVibGljIHZvaWQgVXN1bihpbnQgdmFsdWUpCiAgICB7CiAgICAgICAgaWYgKGhlYWQgPT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGlmIChoZWFkLlZhbHVlID09IHZhbHVlKQogICAgICAgIHsKICAgICAgICAgICAgVXN1blBpZXJ3c3p5KCk7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIE5vZGUgY3VycmVudCA9IGhlYWQ7CgogICAgICAgIHdoaWxlIChjdXJyZW50Lk5leHQgIT0gbnVsbCAmJiBjdXJyZW50Lk5leHQuVmFsdWUgIT0gdmFsdWUpCiAgICAgICAgewogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgaWYgKGN1cnJlbnQuTmV4dCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaWYgKGN1cnJlbnQuTmV4dCA9PSB0YWlsKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICB0YWlsID0gY3VycmVudDsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgY3VycmVudC5OZXh0ID0gY3VycmVudC5OZXh0Lk5leHQ7CiAgICAgICAgfQogICAgfQoKICAgIHB1YmxpYyB2b2lkIFd5cGlzeigpCiAgICB7CiAgICAgICAgTm9kZSBjdXJyZW50ID0gaGVhZDsKCiAgICAgICAgd2hpbGUgKGN1cnJlbnQgIT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlKGN1cnJlbnQuVmFsdWUgKyAiICIpOwogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICB9Cn0KCnB1YmxpYyBjbGFzcyBQcm9ncmFtCnsKICAgIHB1YmxpYyBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBMaXN0YUplZG5va2llcnVua293YVpPZ29uZW0gbGlzdGEgPSBuZXcgTGlzdGFKZWRub2tpZXJ1bmtvd2FaT2dvbmVtKCk7CgogICAgICAgIGxpc3RhLkRvZGFqTmFLb25pZWMoMTApOwogICAgICAgIGxpc3RhLkRvZGFqTmFLb25pZWMoMjApOwogICAgICAgIGxpc3RhLkRvZGFqTmFQb2N6YXRlayg1KTsKCiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCJMaXN0YSBwbyBkb2Rhbml1IDUsIDEwLCAyMDoiKTsKICAgICAgICBsaXN0YS5XeXBpc3ooKTsKCiAgICAgICAgbGlzdGEuVXN1bigyMCk7CiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCJQbyBVc3VuKDIwKSwgY3p5bGkgdXN1bmllY2l1IG9nb25hOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwoKICAgICAgICBsaXN0YS5Vc3VuUGllcndzenkoKTsKICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZUxpbmUoIlBvIFVzdW5QaWVyd3N6eSgpOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_53_wydarzenia.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjMgLSBMaXN0YSB3eWRhcnplxYQ8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzUzX3d5ZGFyemVuaWE8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+UHJvc3RhIGxpc3RhIHd5ZGFyemXFhCwgdGEgc2FtYSBkYXRhIGRvcGlzdWplIG9waXMuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpwdWJsaWMgY2xhc3MgV3lkYXJ6ZW5pZQp7CiAgICBwcml2YXRlIGludCBkemllbjsKICAgIHByaXZhdGUgaW50IG1pZXNpYWM7CiAgICBwcml2YXRlIGludCByb2s7CiAgICBwcml2YXRlIHN0cmluZyBvcGlzOwoKICAgIHB1YmxpYyBXeWRhcnplbmllKGludCBkemllbiwgaW50IG1pZXNpYWMsIGludCByb2ssIHN0cmluZyBvcGlzKQogICAgewogICAgICAgIHRoaXMuZHppZW4gPSBkemllbjsKICAgICAgICB0aGlzLm1pZXNpYWMgPSBtaWVzaWFjOwogICAgICAgIHRoaXMucm9rID0gcm9rOwogICAgICAgIHRoaXMub3BpcyA9IG9waXM7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpPcGlzKHN0cmluZyBub3d5T3BpcykKICAgIHsKICAgICAgICBvcGlzID0gb3BpcyArICI7ICIgKyBub3d5T3BpczsKICAgIH0KCiAgICBwdWJsaWMgYm9vbCBQb3Jvd25hakRhdGUoaW50IGR6aWVuLCBpbnQgbWllc2lhYywgaW50IHJvaykKICAgIHsKICAgICAgICByZXR1cm4gdGhpcy5kemllbiA9PSBkemllbiAmJgogICAgICAgICAgICAgICB0aGlzLm1pZXNpYWMgPT0gbWllc2lhYyAmJgogICAgICAgICAgICAgICB0aGlzLnJvayA9PSByb2s7CiAgICB9CgogICAgcHVibGljIHZvaWQgV3lzd2lldGwoKQogICAgewogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKGR6aWVuICsgIi4iICsgbWllc2lhYyArICIuIiArIHJvayArICIgLSAiICsgb3Bpcyk7CiAgICB9Cn0KCnB1YmxpYyBjbGFzcyBMaXN0YVd5ZGFyemVuCnsKICAgIHByaXZhdGUgY2xhc3MgTm9kZQogICAgewogICAgICAgIHB1YmxpYyBXeWRhcnplbmllIFZhbHVlOwogICAgICAgIHB1YmxpYyBOb2RlIE5leHQ7CgogICAgICAgIHB1YmxpYyBOb2RlKFd5ZGFyemVuaWUgdmFsdWUpCiAgICAgICAgewogICAgICAgICAgICBWYWx1ZSA9IHZhbHVlOwogICAgICAgICAgICBOZXh0ID0gbnVsbDsKICAgICAgICB9CiAgICB9CgogICAgcHJpdmF0ZSBOb2RlIGhlYWQ7CgogICAgcHVibGljIHZvaWQgRG9kYWpXeWRhcnplbmllKGludCBkemllbiwgaW50IG1pZXNpYWMsIGludCByb2ssIHN0cmluZyBvcGlzKQogICAgewogICAgICAgIE5vZGUgY3VycmVudCA9IGhlYWQ7CgogICAgICAgIHdoaWxlIChjdXJyZW50ICE9IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBpZiAoY3VycmVudC5WYWx1ZS5Qb3Jvd25hakRhdGUoZHppZW4sIG1pZXNpYWMsIHJvaykpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIC8vIHRhIHNhbWEgZGF0YSwgd2nEmWMgdHlsa28gZG9waXN1amVteSBrb2xlam55IG9waXMKICAgICAgICAgICAgICAgIGN1cnJlbnQuVmFsdWUuRG9kYWpPcGlzKG9waXMpOwogICAgICAgICAgICAgICAgcmV0dXJuOwogICAgICAgICAgICB9CgogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgTm9kZSBuZXdOb2RlID0gbmV3IE5vZGUobmV3IFd5ZGFyemVuaWUoZHppZW4sIG1pZXNpYWMsIHJvaywgb3BpcykpOwoKICAgICAgICBpZiAoaGVhZCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaGVhZCA9IG5ld05vZGU7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudC5OZXh0ICE9IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgY3VycmVudC5OZXh0ID0gbmV3Tm9kZTsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBVc3VuV3lkYXJ6ZW5pZShpbnQgZHppZW4sIGludCBtaWVzaWFjLCBpbnQgcm9rKQogICAgewogICAgICAgIGlmIChoZWFkID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICBpZiAoaGVhZC5WYWx1ZS5Qb3Jvd25hakRhdGUoZHppZW4sIG1pZXNpYWMsIHJvaykpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gaGVhZC5OZXh0OwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICBOb2RlIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudC5OZXh0ICE9IG51bGwgJiYKICAgICAgICAgICAgICAgIWN1cnJlbnQuTmV4dC5WYWx1ZS5Qb3Jvd25hakRhdGUoZHppZW4sIG1pZXNpYWMsIHJvaykpCiAgICAgICAgewogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KCiAgICAgICAgaWYgKGN1cnJlbnQuTmV4dCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgY3VycmVudC5OZXh0ID0gY3VycmVudC5OZXh0Lk5leHQ7CiAgICAgICAgfQogICAgfQoKICAgIHB1YmxpYyBXeWRhcnplbmllIFpuYWpkeld5ZGFyemVuaWUoaW50IGR6aWVuLCBpbnQgbWllc2lhYywgaW50IHJvaykKICAgIHsKICAgICAgICBOb2RlIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaWYgKGN1cnJlbnQuVmFsdWUuUG9yb3duYWpEYXRlKGR6aWVuLCBtaWVzaWFjLCByb2spKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICByZXR1cm4gY3VycmVudC5WYWx1ZTsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgY3VycmVudCA9IGN1cnJlbnQuTmV4dDsKICAgICAgICB9CgogICAgICAgIHJldHVybiBudWxsOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFd5c3dpZXRsV3N6eXN0a2llKCkKICAgIHsKICAgICAgICBOb2RlIGN1cnJlbnQgPSBoZWFkOwoKICAgICAgICB3aGlsZSAoY3VycmVudCAhPSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgY3VycmVudC5WYWx1ZS5XeXN3aWV0bCgpOwogICAgICAgICAgICBjdXJyZW50ID0gY3VycmVudC5OZXh0OwogICAgICAgIH0KICAgIH0KfQoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBMaXN0YVd5ZGFyemVuIGxpc3RhID0gbmV3IExpc3RhV3lkYXJ6ZW4oKTsKCiAgICAgICAgbGlzdGEuRG9kYWpXeWRhcnplbmllKDI1LCA1LCAyMDI2LCAiS29sb2t3aXVtIHogYWxnb3J5dG3Ds3ciKTsKICAgICAgICBsaXN0YS5Eb2Rhald5ZGFyemVuaWUoMjUsIDUsIDIwMjYsICJPZGRhxIcgcHJvamVrdCIpOwogICAgICAgIGxpc3RhLkRvZGFqV3lkYXJ6ZW5pZSgxLCA2LCAyMDI2LCAiRWd6YW1pbiIpOwoKICAgICAgICBsaXN0YS5XeXN3aWV0bFdzenlzdGtpZSgpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_54_lista_dwu.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjQgLSBMaXN0YSBkd3VraWVydW5rb3dhPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ181NF9saXN0YV9kd3U8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+TGlzdGEgZHd1a2llcnVua293YSB6IHBvZHN0YXdvd3ltaSBvcGVyYWNqYW1pLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKcHVibGljIGNsYXNzIExpc3RhRHd1a2llcnVua293YQp7CiAgICBwcml2YXRlIGNsYXNzIE5vZGUKICAgIHsKICAgICAgICBwdWJsaWMgaW50IFZhbHVlOwogICAgICAgIHB1YmxpYyBOb2RlIE5leHQ7CiAgICAgICAgcHVibGljIE5vZGUgUHJldmlvdXM7CgogICAgICAgIHB1YmxpYyBOb2RlKGludCB2YWx1ZSkKICAgICAgICB7CiAgICAgICAgICAgIFZhbHVlID0gdmFsdWU7CiAgICAgICAgICAgIE5leHQgPSBudWxsOwogICAgICAgICAgICBQcmV2aW91cyA9IG51bGw7CiAgICAgICAgfQogICAgfQoKICAgIHByaXZhdGUgTm9kZSBoZWFkOwogICAgcHJpdmF0ZSBOb2RlIHRhaWw7CgogICAgcHVibGljIGJvb2wgQ3p5UHVzdGEoKQogICAgewogICAgICAgIHJldHVybiBoZWFkID09IG51bGw7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpOYVBvY3phdGVrKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIG5ld05vZGUgPSBuZXcgTm9kZSh2YWx1ZSk7CgogICAgICAgIGlmIChoZWFkID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gbmV3Tm9kZTsKICAgICAgICAgICAgdGFpbCA9IG5ld05vZGU7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIG5ld05vZGUuTmV4dCA9IGhlYWQ7CiAgICAgICAgaGVhZC5QcmV2aW91cyA9IG5ld05vZGU7CiAgICAgICAgaGVhZCA9IG5ld05vZGU7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWpOYUtvbmllYyhpbnQgdmFsdWUpCiAgICB7CiAgICAgICAgTm9kZSBuZXdOb2RlID0gbmV3IE5vZGUodmFsdWUpOwoKICAgICAgICBpZiAodGFpbCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgaGVhZCA9IG5ld05vZGU7CiAgICAgICAgICAgIHRhaWwgPSBuZXdOb2RlOwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICB0YWlsLk5leHQgPSBuZXdOb2RlOwogICAgICAgIG5ld05vZGUuUHJldmlvdXMgPSB0YWlsOwogICAgICAgIHRhaWwgPSBuZXdOb2RlOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFVzdW5QaWVyd3N6eSgpCiAgICB7CiAgICAgICAgaWYgKGhlYWQgPT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGlmIChoZWFkID09IHRhaWwpCiAgICAgICAgewogICAgICAgICAgICBoZWFkID0gbnVsbDsKICAgICAgICAgICAgdGFpbCA9IG51bGw7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIGhlYWQgPSBoZWFkLk5leHQ7CiAgICAgICAgaGVhZC5QcmV2aW91cyA9IG51bGw7CiAgICB9CgogICAgcHVibGljIHZvaWQgVXN1bk9zdGF0bmkoKQogICAgewogICAgICAgIGlmICh0YWlsID09IG51bGwpCiAgICAgICAgewogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICBpZiAoaGVhZCA9PSB0YWlsKQogICAgICAgIHsKICAgICAgICAgICAgaGVhZCA9IG51bGw7CiAgICAgICAgICAgIHRhaWwgPSBudWxsOwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICB0YWlsID0gdGFpbC5QcmV2aW91czsKICAgICAgICB0YWlsLk5leHQgPSBudWxsOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFVzdW4oaW50IHZhbHVlKQogICAgewogICAgICAgIE5vZGUgY3VycmVudCA9IGhlYWQ7CgogICAgICAgIHdoaWxlIChjdXJyZW50ICE9IG51bGwgJiYgY3VycmVudC5WYWx1ZSAhPSB2YWx1ZSkKICAgICAgICB7CiAgICAgICAgICAgIGN1cnJlbnQgPSBjdXJyZW50Lk5leHQ7CiAgICAgICAgfQoKICAgICAgICBpZiAoY3VycmVudCA9PSBudWxsKQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgaWYgKGN1cnJlbnQgPT0gaGVhZCkKICAgICAgICB7CiAgICAgICAgICAgIFVzdW5QaWVyd3N6eSgpOwogICAgICAgICAgICByZXR1cm47CiAgICAgICAgfQoKICAgICAgICBpZiAoY3VycmVudCA9PSB0YWlsKQogICAgICAgIHsKICAgICAgICAgICAgVXN1bk9zdGF0bmkoKTsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgY3VycmVudC5QcmV2aW91cy5OZXh0ID0gY3VycmVudC5OZXh0OwogICAgICAgIGN1cnJlbnQuTmV4dC5QcmV2aW91cyA9IGN1cnJlbnQuUHJldmlvdXM7CiAgICB9CgogICAgcHVibGljIGJvb2wgWmF3aWVyYShpbnQgdmFsdWUpCiAgICB7CiAgICAgICAgTm9kZSBjdXJyZW50ID0gaGVhZDsKCiAgICAgICAgd2hpbGUgKGN1cnJlbnQgIT0gbnVsbCkKICAgICAgICB7CiAgICAgICAgICAgIGlmIChjdXJyZW50LlZhbHVlID09IHZhbHVlKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICByZXR1cm4gdHJ1ZTsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgY3VycmVudCA9IGN1cnJlbnQuTmV4dDsKICAgICAgICB9CgogICAgICAgIHJldHVybiBmYWxzZTsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBXeXBpc3ooKQogICAgewogICAgICAgIE5vZGUgY3VycmVudCA9IGhlYWQ7CgogICAgICAgIHdoaWxlIChjdXJyZW50ICE9IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBTeXN0ZW0uQ29uc29sZS5Xcml0ZShjdXJyZW50LlZhbHVlICsgIiAiKTsKICAgICAgICAgICAgY3VycmVudCA9IGN1cnJlbnQuTmV4dDsKICAgICAgICB9CgogICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlTGluZSgpOwogICAgfQp9CgpwdWJsaWMgY2xhc3MgUHJvZ3JhbQp7CiAgICBwdWJsaWMgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgTGlzdGFEd3VraWVydW5rb3dhIGxpc3RhID0gbmV3IExpc3RhRHd1a2llcnVua293YSgpOwoKICAgICAgICBsaXN0YS5Eb2Rhak5hS29uaWVjKDEwKTsKICAgICAgICBsaXN0YS5Eb2Rhak5hS29uaWVjKDIwKTsKICAgICAgICBsaXN0YS5Eb2Rhak5hUG9jemF0ZWsoNSk7CgogICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlTGluZSgiTGlzdGEgcG8gZG9kYW5pdSA1LCAxMCwgMjA6Iik7CiAgICAgICAgbGlzdGEuV3lwaXN6KCk7CgogICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlTGluZSgiWmF3aWVyYSgyMCk6ICIgKyBsaXN0YS5aYXdpZXJhKDIwKSk7CgogICAgICAgIGxpc3RhLlVzdW4oMTApOwogICAgICAgIFN5c3RlbS5Db25zb2xlLldyaXRlTGluZSgiUG8gVXN1bigxMCk6Iik7CiAgICAgICAgbGlzdGEuV3lwaXN6KCk7CgogICAgICAgIGxpc3RhLlVzdW5Pc3RhdG5pKCk7CiAgICAgICAgU3lzdGVtLkNvbnNvbGUuV3JpdGVMaW5lKCJQbyBVc3VuT3N0YXRuaSgpOiIpOwogICAgICAgIGxpc3RhLld5cGlzeigpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_55_anagramy.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjUgLSBTxYJvd25payBhbmFncmFtw7N3PC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ181NV9hbmFncmFteTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5HcnVwb3dhbmllIGFuYWdyYW3Ds3cgcHJ6ZXogd3Nww7Nsbnkga2x1Y3ogdyBEaWN0aW9uYXJ5LjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHN0cmluZyBLbHVjeihzdHJpbmcgc2xvd28pCiAgICB7CiAgICAgICAgY2hhcltdIGxpdGVyeSA9IHNsb3dvLlRvQ2hhckFycmF5KCk7CiAgICAgICAgQXJyYXkuU29ydChsaXRlcnkpOwogICAgICAgIHJldHVybiBuZXcgc3RyaW5nKGxpdGVyeSk7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgTGlzdDxzdHJpbmc+IHNsb3dhID0gbmV3IExpc3Q8c3RyaW5nPigpCiAgICAgICAgewogICAgICAgICAgICAia290IiwgInRvayIsICJrdG8iLAogICAgICAgICAgICAic29rIiwgImtvcyIsCiAgICAgICAgICAgICJrdWxhIiwgImx1a2EiLAogICAgICAgICAgICAid29yIiwgInJvdyIsCiAgICAgICAgICAgICJ0YXJhIiwgInJhdGEiLAogICAgICAgICAgICAibWFzYSIsICJzYW1hIiwKICAgICAgICAgICAgImdyYWQiLCAiZHJnYSIKICAgICAgICB9OwoKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncnVweSA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PigpOwoKICAgICAgICBmb3JlYWNoIChzdHJpbmcgc2xvd28gaW4gc2xvd2EpCiAgICAgICAgewogICAgICAgICAgICAvLyBhbmFncmFteSBwbyBwb3NvcnRvd2FuaXUgbGl0ZXIgbWFqxIUgdGVuIHNhbSBrbHVjegogICAgICAgICAgICBzdHJpbmcga2x1Y3ogPSBLbHVjeihzbG93byk7CgogICAgICAgICAgICBpZiAoIWdydXB5LkNvbnRhaW5zS2V5KGtsdWN6KSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgZ3J1cHlba2x1Y3pdID0gbmV3IExpc3Q8c3RyaW5nPigpOwogICAgICAgICAgICB9CgogICAgICAgICAgICBncnVweVtrbHVjel0uQWRkKHNsb3dvKTsKICAgICAgICB9CgogICAgICAgIGZvcmVhY2ggKHZhciBncnVwYSBpbiBncnVweSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHN0cmluZy5Kb2luKCIsICIsIGdydXBhLlZhbHVlKSk7CiAgICAgICAgfQogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_56_duplikaty.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjYgLSBaYmnDs3IgaSBkdXBsaWthdHk8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzU2X2R1cGxpa2F0eTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5Vc3V3YW5pZSBkdXBsaWthdMOzdyBwbyBJRCB6IHXFvHljaWVtIEhhc2hTZXQuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKY2xhc3MgUHJhY293bmlrCnsKICAgIHB1YmxpYyBzdHJpbmcgSW1pZTsKICAgIHB1YmxpYyBzdHJpbmcgTmF6d2lza287CiAgICBwdWJsaWMgaW50IElEOwoKICAgIHB1YmxpYyBQcmFjb3duaWsoc3RyaW5nIGltaWUsIHN0cmluZyBuYXp3aXNrbywgaW50IGlkKQogICAgewogICAgICAgIEltaWUgPSBpbWllOwogICAgICAgIE5hendpc2tvID0gbmF6d2lza287CiAgICAgICAgSUQgPSBpZDsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBXeXN3aWV0bCgpCiAgICB7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoJCJ7SUR9OiB7SW1pZX0ge05hendpc2tvfSIpOwogICAgfQp9CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIExpc3Q8UHJhY293bmlrPiBwcmFjb3duaWN5ID0gbmV3IExpc3Q8UHJhY293bmlrPigpCiAgICAgICAgewogICAgICAgICAgICBuZXcgUHJhY293bmlrKCJKYW4iLCAiS293YWxza2kiLCAxKSwKICAgICAgICAgICAgbmV3IFByYWNvd25paygiQWRhbSIsICJOb3dhayIsIDIpLAogICAgICAgICAgICBuZXcgUHJhY293bmlrKCJQaW90ciIsICJaaWVsaW5za2kiLCAxKSwKICAgICAgICAgICAgbmV3IFByYWNvd25paygiQW5uYSIsICJXaXNuaWV3c2thIiwgMyksCiAgICAgICAgICAgIG5ldyBQcmFjb3duaWsoIk1hcmVrIiwgIkxpcyIsIDIpCiAgICAgICAgfTsKCiAgICAgICAgSGFzaFNldDxpbnQ+IHV6eXRlSWQgPSBuZXcgSGFzaFNldDxpbnQ+KCk7CiAgICAgICAgTGlzdDxQcmFjb3duaWs+IGJlekR1cGxpa2F0b3cgPSBuZXcgTGlzdDxQcmFjb3duaWs+KCk7CgogICAgICAgIGZvcmVhY2ggKFByYWNvd25payBwcmFjb3duaWsgaW4gcHJhY293bmljeSkKICAgICAgICB7CiAgICAgICAgICAgIC8vIEFkZCB6d3JhY2EgZmFsc2UsIGdkeSB0YWtpZSBJRCBqdcW8IGJ5xYJvCiAgICAgICAgICAgIGlmICh1enl0ZUlkLkFkZChwcmFjb3duaWsuSUQpKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBiZXpEdXBsaWthdG93LkFkZChwcmFjb3duaWspOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICBmb3JlYWNoIChQcmFjb3duaWsgcHJhY293bmlrIGluIGJlekR1cGxpa2F0b3cpCiAgICAgICAgewogICAgICAgICAgICBwcmFjb3duaWsuV3lzd2lldGwoKTsKICAgICAgICB9CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_57_stos.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjcgLSBTdG9zIHRhYmxpY293eSBpIGxpc3Rvd3k8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzU3X3N0b3M8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+RHdpZSB3ZXJzamUgc3Rvc3U6IHRhYmxpY2EgaSBsaXN0YS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCnB1YmxpYyBjbGFzcyBTdG9zVGFibGljb3d5CnsKICAgIHByaXZhdGUgaW50W10gZWxlbWVudHk7CiAgICBwcml2YXRlIGludCB0b3A7CgogICAgcHVibGljIFN0b3NUYWJsaWNvd3koaW50IHJvem1pYXIpCiAgICB7CiAgICAgICAgZWxlbWVudHkgPSBuZXcgaW50W3Jvem1pYXJdOwogICAgICAgIHRvcCA9IC0xOwogICAgfQoKICAgIHB1YmxpYyBib29sIEN6eVB1c3RhKCkKICAgIHsKICAgICAgICByZXR1cm4gdG9wID09IC0xOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIERvZGFqKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBpZiAodG9wID09IGVsZW1lbnR5Lkxlbmd0aCAtIDEpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyB0YWJsaWNvd3kgamVzdCBwZWxueS4iKTsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgdG9wKys7CiAgICAgICAgZWxlbWVudHlbdG9wXSA9IHZhbHVlOwogICAgfQoKICAgIHB1YmxpYyBpbnQgWmRlam1paigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyB0YWJsaWNvd3kgamVzdCBwdXN0eS4iKTsKICAgICAgICAgICAgcmV0dXJuIC0xOwogICAgICAgIH0KCiAgICAgICAgaW50IHZhbHVlID0gZWxlbWVudHlbdG9wXTsKICAgICAgICB0b3AtLTsKICAgICAgICByZXR1cm4gdmFsdWU7CiAgICB9CgogICAgcHVibGljIGludCBQb2RlanJ6eWooKQogICAgewogICAgICAgIGlmIChDenlQdXN0YSgpKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlN0b3MgdGFibGljb3d5IGplc3QgcHVzdHkuIik7CiAgICAgICAgICAgIHJldHVybiAtMTsKICAgICAgICB9CgogICAgICAgIHJldHVybiBlbGVtZW50eVt0b3BdOwogICAgfQp9CgpwdWJsaWMgY2xhc3MgU3Rvc0xpc3Rvd3kKewogICAgcHJpdmF0ZSBjbGFzcyBOb2RlCiAgICB7CiAgICAgICAgcHVibGljIGludCBWYWx1ZTsKICAgICAgICBwdWJsaWMgTm9kZSBOZXh0OwoKICAgICAgICBwdWJsaWMgTm9kZShpbnQgdmFsdWUpCiAgICAgICAgewogICAgICAgICAgICBWYWx1ZSA9IHZhbHVlOwogICAgICAgICAgICBOZXh0ID0gbnVsbDsKICAgICAgICB9CiAgICB9CgogICAgcHJpdmF0ZSBOb2RlIHRvcDsKCiAgICBwdWJsaWMgYm9vbCBDenlQdXN0YSgpCiAgICB7CiAgICAgICAgcmV0dXJuIHRvcCA9PSBudWxsOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIERvZGFqKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBOb2RlIG5ld05vZGUgPSBuZXcgTm9kZSh2YWx1ZSk7CiAgICAgICAgbmV3Tm9kZS5OZXh0ID0gdG9wOwogICAgICAgIHRvcCA9IG5ld05vZGU7CiAgICB9CgogICAgcHVibGljIGludCBaZGVqbWlqKCkKICAgIHsKICAgICAgICBpZiAoQ3p5UHVzdGEoKSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJTdG9zIGxpc3Rvd3kgamVzdCBwdXN0eS4iKTsKICAgICAgICAgICAgcmV0dXJuIC0xOwogICAgICAgIH0KCiAgICAgICAgaW50IHZhbHVlID0gdG9wLlZhbHVlOwogICAgICAgIHRvcCA9IHRvcC5OZXh0OwogICAgICAgIHJldHVybiB2YWx1ZTsKICAgIH0KCiAgICBwdWJsaWMgaW50IFBvZGVqcnp5aigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyBsaXN0b3d5IGplc3QgcHVzdHkuIik7CiAgICAgICAgICAgIHJldHVybiAtMTsKICAgICAgICB9CgogICAgICAgIHJldHVybiB0b3AuVmFsdWU7CiAgICB9Cn0KCnB1YmxpYyBjbGFzcyBQcm9ncmFtCnsKICAgIHB1YmxpYyBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBTdG9zVGFibGljb3d5IHN0b3NUYWIgPSBuZXcgU3Rvc1RhYmxpY293eSg1KTsKICAgICAgICBzdG9zVGFiLkRvZGFqKDEpOwogICAgICAgIHN0b3NUYWIuRG9kYWooMik7CiAgICAgICAgc3Rvc1RhYi5Eb2RhaigzKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlNUT1MgTkEgVEFCTElDWSIpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJQb2RlanJ6eWo6ICIgKyBzdG9zVGFiLlBvZGVqcnp5aigpKTsKICAgICAgICB3aGlsZSAoIXN0b3NUYWIuQ3p5UHVzdGEoKSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJaZGVqbWlqOiAiICsgc3Rvc1RhYi5aZGVqbWlqKCkpOwogICAgICAgIH0KCiAgICAgICAgU3Rvc0xpc3Rvd3kgc3Rvc0xpc3RhID0gbmV3IFN0b3NMaXN0b3d5KCk7CiAgICAgICAgc3Rvc0xpc3RhLkRvZGFqKDEpOwogICAgICAgIHN0b3NMaXN0YS5Eb2RhaigyKTsKICAgICAgICBzdG9zTGlzdGEuRG9kYWooMyk7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCIKU1RPUyBOQSBMSVNDSUUiKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUG9kZWpyenlqOiAiICsgc3Rvc0xpc3RhLlBvZGVqcnp5aigpKTsKICAgICAgICB3aGlsZSAoIXN0b3NMaXN0YS5DenlQdXN0YSgpKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlpkZWptaWo6ICIgKyBzdG9zTGlzdGEuWmRlam1paigpKTsKICAgICAgICB9CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_57_stos_lista.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjcgLSBTdG9zIGxpc3Rvd3k8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzU3X3N0b3NfbGlzdGE8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+U3RvcyBMSUZPIG5hIGxpxZtjaWUgamVkbm9raWVydW5rb3dlai48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCnB1YmxpYyBjbGFzcyBTdG9zTGlzdG93eQp7CiAgICBwcml2YXRlIGNsYXNzIE5vZGUKICAgIHsKICAgICAgICBwdWJsaWMgaW50IFZhbHVlOwogICAgICAgIHB1YmxpYyBOb2RlIE5leHQ7CgogICAgICAgIHB1YmxpYyBOb2RlKGludCB2YWx1ZSkKICAgICAgICB7CiAgICAgICAgICAgIFZhbHVlID0gdmFsdWU7CiAgICAgICAgICAgIE5leHQgPSBudWxsOwogICAgICAgIH0KICAgIH0KCiAgICBwcml2YXRlIE5vZGUgdG9wOwoKICAgIHB1YmxpYyBib29sIEN6eVB1c3RhKCkKICAgIHsKICAgICAgICByZXR1cm4gdG9wID09IG51bGw7CiAgICB9CgogICAgcHVibGljIHZvaWQgRG9kYWooaW50IHZhbHVlKQogICAgewogICAgICAgIE5vZGUgbmV3Tm9kZSA9IG5ldyBOb2RlKHZhbHVlKTsKICAgICAgICBuZXdOb2RlLk5leHQgPSB0b3A7CiAgICAgICAgdG9wID0gbmV3Tm9kZTsKICAgIH0KCiAgICBwdWJsaWMgaW50IFpkZWptaWooKQogICAgewogICAgICAgIGlmIChDenlQdXN0YSgpKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlN0b3MgamVzdCBwdXN0eS4iKTsKICAgICAgICAgICAgcmV0dXJuIC0xOwogICAgICAgIH0KCiAgICAgICAgaW50IHZhbHVlID0gdG9wLlZhbHVlOwogICAgICAgIHRvcCA9IHRvcC5OZXh0OwogICAgICAgIHJldHVybiB2YWx1ZTsKICAgIH0KCiAgICBwdWJsaWMgaW50IFBvZGVqcnp5aigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyBqZXN0IHB1c3R5LiIpOwogICAgICAgICAgICByZXR1cm4gLTE7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gdG9wLlZhbHVlOwogICAgfQp9CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIFN0b3NMaXN0b3d5IHN0b3MgPSBuZXcgU3Rvc0xpc3Rvd3koKTsKCiAgICAgICAgc3Rvcy5Eb2RhaigxMCk7CiAgICAgICAgc3Rvcy5Eb2RhaigyMCk7CiAgICAgICAgc3Rvcy5Eb2RhaigzMCk7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHN0b3MuUG9kZWpyenlqKCkpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHN0b3MuWmRlam1paigpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZShzdG9zLlpkZWptaWooKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoc3Rvcy5aZGVqbWlqKCkpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_57_stos_tab.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjcgLSBTdG9zIHRhYmxpY293eTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNTdfc3Rvc190YWI8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+U3RvcyBMSUZPIG5hIHRhYmxpY3kuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CgpwdWJsaWMgY2xhc3MgU3Rvc1RhYmxpY293eQp7CiAgICBwcml2YXRlIGludFtdIGVsZW1lbnR5OwogICAgcHJpdmF0ZSBpbnQgdG9wOwoKICAgIHB1YmxpYyBTdG9zVGFibGljb3d5KGludCByb3ptaWFyKQogICAgewogICAgICAgIGVsZW1lbnR5ID0gbmV3IGludFtyb3ptaWFyXTsKICAgICAgICB0b3AgPSAtMTsKICAgIH0KCiAgICBwdWJsaWMgYm9vbCBDenlQdXN0YSgpCiAgICB7CiAgICAgICAgcmV0dXJuIHRvcCA9PSAtMTsKICAgIH0KCiAgICBwdWJsaWMgdm9pZCBEb2RhaihpbnQgdmFsdWUpCiAgICB7CiAgICAgICAgaWYgKHRvcCA9PSBlbGVtZW50eS5MZW5ndGggLSAxKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIlN0b3MgamVzdCBwZWxueS4iKTsKICAgICAgICAgICAgcmV0dXJuOwogICAgICAgIH0KCiAgICAgICAgdG9wKys7CiAgICAgICAgZWxlbWVudHlbdG9wXSA9IHZhbHVlOwogICAgfQoKICAgIHB1YmxpYyBpbnQgWmRlam1paigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyBqZXN0IHB1c3R5LiIpOwogICAgICAgICAgICByZXR1cm4gLTE7CiAgICAgICAgfQoKICAgICAgICBpbnQgdmFsdWUgPSBlbGVtZW50eVt0b3BdOwogICAgICAgIHRvcC0tOwogICAgICAgIHJldHVybiB2YWx1ZTsKICAgIH0KCiAgICBwdWJsaWMgaW50IFBvZGVqcnp5aigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiU3RvcyBqZXN0IHB1c3R5LiIpOwogICAgICAgICAgICByZXR1cm4gLTE7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gZWxlbWVudHlbdG9wXTsKICAgIH0KfQoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBTdG9zVGFibGljb3d5IHN0b3MgPSBuZXcgU3Rvc1RhYmxpY293eSg1KTsKCiAgICAgICAgc3Rvcy5Eb2RhaigxMCk7CiAgICAgICAgc3Rvcy5Eb2RhaigyMCk7CiAgICAgICAgc3Rvcy5Eb2RhaigzMCk7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHN0b3MuUG9kZWpyenlqKCkpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHN0b3MuWmRlam1paigpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZShzdG9zLlpkZWptaWooKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoc3Rvcy5aZGVqbWlqKCkpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_58_kolejka_tab.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjggLSBLb2xlamthIHRhYmxpY293YTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNThfa29sZWprYV90YWI8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+S29sZWprYSBGSUZPIG5hIHRhYmxpY3kgY3lrbGljem5lai48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKCnB1YmxpYyBjbGFzcyBLb2xlamthVGFibGljb3dhCnsKICAgIHByaXZhdGUgaW50W10gZWxlbWVudHk7CiAgICBwcml2YXRlIGludCBwb2N6YXRlazsKICAgIHByaXZhdGUgaW50IGtvbmllYzsKICAgIHByaXZhdGUgaW50IGxpY3puaWs7CgogICAgcHVibGljIEtvbGVqa2FUYWJsaWNvd2EoaW50IHJvem1pYXIpCiAgICB7CiAgICAgICAgZWxlbWVudHkgPSBuZXcgaW50W3Jvem1pYXJdOwogICAgICAgIHBvY3phdGVrID0gMDsKICAgICAgICBrb25pZWMgPSAtMTsKICAgICAgICBsaWN6bmlrID0gMDsKICAgIH0KCiAgICBwdWJsaWMgYm9vbCBDenlQdXN0YSgpCiAgICB7CiAgICAgICAgcmV0dXJuIGxpY3puaWsgPT0gMDsKICAgIH0KCiAgICBwdWJsaWMgYm9vbCBDenlQZWxuYSgpCiAgICB7CiAgICAgICAgcmV0dXJuIGxpY3puaWsgPT0gZWxlbWVudHkuTGVuZ3RoOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIERvZGFqKGludCB2YWx1ZSkKICAgIHsKICAgICAgICBpZiAoQ3p5UGVsbmEoKSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJLb2xlamthIGplc3QgcGVsbmEuIik7CiAgICAgICAgICAgIHJldHVybjsKICAgICAgICB9CgogICAgICAgIC8vIG1vZHVsbyByb2JpIHphd2lqYW5pZSBpbmRla3N1IG5hIHBvY3rEhXRlayB0YWJsaWN5CiAgICAgICAga29uaWVjID0gKGtvbmllYyArIDEpICUgZWxlbWVudHkuTGVuZ3RoOwogICAgICAgIGVsZW1lbnR5W2tvbmllY10gPSB2YWx1ZTsKICAgICAgICBsaWN6bmlrKys7CiAgICB9CgogICAgcHVibGljIGludCBVc3VuKCkKICAgIHsKICAgICAgICBpZiAoQ3p5UHVzdGEoKSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCJLb2xlamthIGplc3QgcHVzdGEuIik7CiAgICAgICAgICAgIHJldHVybiAtMTsKICAgICAgICB9CgogICAgICAgIGludCB2YWx1ZSA9IGVsZW1lbnR5W3BvY3phdGVrXTsKICAgICAgICAvLyBwcnplc3V3YW15IHBvY3rEhXRlayBrb2xlamtpIHRlxbwgY3lrbGljem5pZQogICAgICAgIHBvY3phdGVrID0gKHBvY3phdGVrICsgMSkgJSBlbGVtZW50eS5MZW5ndGg7CiAgICAgICAgbGljem5pay0tOwogICAgICAgIHJldHVybiB2YWx1ZTsKICAgIH0KCiAgICBwdWJsaWMgaW50IFBvZGVqcnp5aigpCiAgICB7CiAgICAgICAgaWYgKEN6eVB1c3RhKCkpCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiS29sZWprYSBqZXN0IHB1c3RhLiIpOwogICAgICAgICAgICByZXR1cm4gLTE7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gZWxlbWVudHlbcG9jemF0ZWtdOwogICAgfQp9CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIEtvbGVqa2FUYWJsaWNvd2Ega29sZWprYSA9IG5ldyBLb2xlamthVGFibGljb3dhKDUpOwoKICAgICAgICBrb2xlamthLkRvZGFqKDEwKTsKICAgICAgICBrb2xlamthLkRvZGFqKDIwKTsKICAgICAgICBrb2xlamthLkRvZGFqKDMwKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoa29sZWprYS5Qb2RlanJ6eWooKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoa29sZWprYS5Vc3VuKCkpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKGtvbGVqa2EuVXN1bigpKTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZShrb2xlamthLlVzdW4oKSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_59_palindrom.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA1LjkgLSBQYWxpbmRyb208L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzU5X3BhbGluZHJvbTwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5TcHJhd2R6ZW5pZSBwYWxpbmRyb211IHByemV6IHN0b3MgaSBrb2xlamvEmS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyBib29sIEN6eVBhbGluZHJvbShzdHJpbmcgdGVrc3QpCiAgICB7CiAgICAgICAgU3RhY2s8Y2hhcj4gc3RvcyA9IG5ldyBTdGFjazxjaGFyPigpOwogICAgICAgIFF1ZXVlPGNoYXI+IGtvbGVqa2EgPSBuZXcgUXVldWU8Y2hhcj4oKTsKCiAgICAgICAgZm9yZWFjaCAoY2hhciB6bmFrIGluIHRla3N0KQogICAgICAgIHsKICAgICAgICAgICAgc3Rvcy5QdXNoKHpuYWspOwogICAgICAgICAgICBrb2xlamthLkVucXVldWUoem5hayk7CiAgICAgICAgfQoKICAgICAgICB3aGlsZSAoc3Rvcy5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBpZiAoc3Rvcy5Qb3AoKSAhPSBrb2xlamthLkRlcXVldWUoKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gdHJ1ZTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBDb25zb2xlLldyaXRlKCJQb2RhaiB3eXJhejogIik7CiAgICAgICAgc3RyaW5nIHRla3N0ID0gQ29uc29sZS5SZWFkTGluZSgpOwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZShDenlQYWxpbmRyb20odGVrc3QpID8gIlBhbGluZHJvbSIgOiAiTmllIHBhbGluZHJvbSIpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_61_kolejka_prio.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjEgLSBLb2xlamthIHByaW9yeXRldG93YTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNjFfa29sZWprYV9wcmlvPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlByb3N0eSBwcnp5a8WCYWQgd2J1ZG93YW5laiBQcmlvcml0eVF1ZXVlLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgLy8gdyB0ZWoga29sZWpjZSBtbmllanN6YSBsaWN6YmEgPSB3acSZa3N6eSBwcmlvcnl0ZXQKICAgICAgICBQcmlvcml0eVF1ZXVlPHN0cmluZywgaW50PiBrb2xlamthUHJpb3J5dGV0b3dhID0gbmV3IFByaW9yaXR5UXVldWU8c3RyaW5nLCBpbnQ+KCk7CgogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSgiTmFwaXNhxIcgc3ByYXdvemRhbmllIiwgMyk7CiAgICAgICAga29sZWprYVByaW9yeXRldG93YS5FbnF1ZXVlKCJQcnp5Z290b3dhxIcgc2nEmSBkbyBrb2xva3dpdW0iLCAxKTsKICAgICAgICBrb2xlamthUHJpb3J5dGV0b3dhLkVucXVldWUoIk9kZGHEhyBwcm9qZWt0IiwgMik7CiAgICAgICAga29sZWprYVByaW9yeXRldG93YS5FbnF1ZXVlKCJQaWxuZSB6Z8WCb3N6ZW5pZSIsIDApOwoKICAgICAgICB3aGlsZSAoa29sZWprYVByaW9yeXRldG93YS5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZShrb2xlamthUHJpb3J5dGV0b3dhLkRlcXVldWUoKSk7CiAgICAgICAgfQogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_62_kolejka_max.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjIgLSBLb2xlamthIHByaW9yeXRldG93YSBtYXg8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzYyX2tvbGVqa2FfbWF4PC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPktvbGVqa2EgbWF4IHpyb2Jpb25hIG5hIFNvcnRlZERpY3Rpb25hcnkuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKcHVibGljIGNsYXNzIEtvbGVqa2FQcmlvcnl0ZXRvd2FNYXgKewogICAgcHJpdmF0ZSBTb3J0ZWREaWN0aW9uYXJ5PGludCwgUXVldWU8c3RyaW5nPj4gZWxlbWVudHkgPQogICAgICAgIG5ldyBTb3J0ZWREaWN0aW9uYXJ5PGludCwgUXVldWU8c3RyaW5nPj4oCiAgICAgICAgICAgIC8vIG9kd3LDs2NvbmUgc29ydG93YW5pZSwgxbxlYnkgbmFqd2nEmWtzenkgcHJpb3J5dGV0IGJ5xYIgcGllcndzenkKICAgICAgICAgICAgQ29tcGFyZXI8aW50Pi5DcmVhdGUoKGEsIGIpID0+IGIuQ29tcGFyZVRvKGEpKQogICAgICAgICk7CgogICAgcHVibGljIGJvb2wgQ3p5UHVzdGEoKQogICAgewogICAgICAgIHJldHVybiBlbGVtZW50eS5Db3VudCA9PSAwOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIERvZGFqKHN0cmluZyB2YWx1ZSwgaW50IHByaW9yeXRldCkKICAgIHsKICAgICAgICBpZiAoIWVsZW1lbnR5LkNvbnRhaW5zS2V5KHByaW9yeXRldCkpCiAgICAgICAgewogICAgICAgICAgICBlbGVtZW50eVtwcmlvcnl0ZXRdID0gbmV3IFF1ZXVlPHN0cmluZz4oKTsKICAgICAgICB9CgogICAgICAgIGVsZW1lbnR5W3ByaW9yeXRldF0uRW5xdWV1ZSh2YWx1ZSk7CiAgICB9CgogICAgcHVibGljIHN0cmluZyBQb2RlanJ6eWooKQogICAgewogICAgICAgIGlmIChDenlQdXN0YSgpKQogICAgICAgIHsKICAgICAgICAgICAgdGhyb3cgbmV3IEludmFsaWRPcGVyYXRpb25FeGNlcHRpb24oIktvbGVqa2EgamVzdCBwdXN0YS4iKTsKICAgICAgICB9CgogICAgICAgIGZvcmVhY2ggKHZhciBwYXJhIGluIGVsZW1lbnR5KQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuIHBhcmEuVmFsdWUuUGVlaygpOwogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIG51bGw7CiAgICB9CgogICAgcHVibGljIHN0cmluZyBVc3VuKCkKICAgIHsKICAgICAgICBpZiAoQ3p5UHVzdGEoKSkKICAgICAgICB7CiAgICAgICAgICAgIHRocm93IG5ldyBJbnZhbGlkT3BlcmF0aW9uRXhjZXB0aW9uKCJLb2xlamthIGplc3QgcHVzdGEuIik7CiAgICAgICAgfQoKICAgICAgICBmb3JlYWNoICh2YXIgcGFyYSBpbiBlbGVtZW50eSkKICAgICAgICB7CiAgICAgICAgICAgIGludCBwcmlvcnl0ZXQgPSBwYXJhLktleTsKICAgICAgICAgICAgUXVldWU8c3RyaW5nPiBrb2xlamthID0gcGFyYS5WYWx1ZTsKICAgICAgICAgICAgc3RyaW5nIHZhbHVlID0ga29sZWprYS5EZXF1ZXVlKCk7CgogICAgICAgICAgICBpZiAoa29sZWprYS5Db3VudCA9PSAwKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBlbGVtZW50eS5SZW1vdmUocHJpb3J5dGV0KTsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgcmV0dXJuIHZhbHVlOwogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIG51bGw7CiAgICB9Cn0KCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgS29sZWprYVByaW9yeXRldG93YU1heCBwcSA9IG5ldyBLb2xlamthUHJpb3J5dGV0b3dhTWF4KCk7CgogICAgICAgIHBxLkRvZGFqKCJBIiwgMik7CiAgICAgICAgcHEuRG9kYWooIkIiLCA1KTsKICAgICAgICBwcS5Eb2RhaigiQyIsIDEpOwogICAgICAgIHBxLkRvZGFqKCJEIiwgNSk7CgogICAgICAgIHdoaWxlICghcHEuQ3p5UHVzdGEoKSkKICAgICAgICB7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKHBxLlVzdW4oKSk7CiAgICAgICAgfQogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_63_kolejka_czas.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjMgLSBLb2xlamthIHByaW9yeXRldG93YSB6IGN6YXNlbTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNjNfa29sZWprYV9jemFzPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlByaW9yeXRldCBpIGN6YXMgZG9kYW5pYSwgZ2R5IGR3YSB6YWRhbmlhIG1hasSFIHRlbiBzYW0gcHJpb3J5dGV0LjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCnB1YmxpYyBjbGFzcyBaYWRhbmllCnsKICAgIHB1YmxpYyBzdHJpbmcgT3BpcyB7IGdldDsgc2V0OyB9CiAgICBwdWJsaWMgaW50IFByaW9yeXRldCB7IGdldDsgc2V0OyB9CiAgICBwdWJsaWMgRGF0ZVRpbWUgQ3phc0RvZGFuaWEgeyBnZXQ7IHNldDsgfQoKICAgIHB1YmxpYyBaYWRhbmllKHN0cmluZyBvcGlzLCBpbnQgcHJpb3J5dGV0LCBEYXRlVGltZSBjemFzRG9kYW5pYSkKICAgIHsKICAgICAgICBPcGlzID0gb3BpczsKICAgICAgICBQcmlvcnl0ZXQgPSBwcmlvcnl0ZXQ7CiAgICAgICAgQ3phc0RvZGFuaWEgPSBjemFzRG9kYW5pYTsKICAgIH0KfQoKcHVibGljIGNsYXNzIFBvcm93bnl3YWN6WmFkYW4gOiBJQ29tcGFyZXI8WmFkYW5pZT4KewogICAgcHVibGljIGludCBDb21wYXJlKFphZGFuaWUgeCwgWmFkYW5pZSB5KQogICAgewogICAgICAgIC8vIG5hanBpZXJ3IHByaW9yeXRldCwgYSBwcnp5IHJlbWlzaWUgd2N6ZcWbbmllanN6eSBjemFzCiAgICAgICAgaWYgKHguUHJpb3J5dGV0ICE9IHkuUHJpb3J5dGV0KQogICAgICAgIHsKICAgICAgICAgICAgcmV0dXJuIHkuUHJpb3J5dGV0LkNvbXBhcmVUbyh4LlByaW9yeXRldCk7CiAgICAgICAgfQoKICAgICAgICByZXR1cm4geC5DemFzRG9kYW5pYS5Db21wYXJlVG8oeS5DemFzRG9kYW5pYSk7CiAgICB9Cn0KCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgUHJpb3JpdHlRdWV1ZTxaYWRhbmllLCBaYWRhbmllPiBrb2xlamthUHJpb3J5dGV0b3dhID0KICAgICAgICAgICAgbmV3IFByaW9yaXR5UXVldWU8WmFkYW5pZSwgWmFkYW5pZT4obmV3IFBvcm93bnl3YWN6WmFkYW4oKSk7CgogICAgICAgIFphZGFuaWUgdDEgPSBuZXcgWmFkYW5pZSgiU3ByYXdvemRhbmllIiwgMiwgRGF0ZVRpbWUuTm93KTsKICAgICAgICBaYWRhbmllIHQyID0gbmV3IFphZGFuaWUoIktvbG9rd2l1bSIsIDUsIERhdGVUaW1lLk5vdy5BZGRTZWNvbmRzKDEpKTsKICAgICAgICBaYWRhbmllIHQzID0gbmV3IFphZGFuaWUoIlByb2pla3QiLCAzLCBEYXRlVGltZS5Ob3cuQWRkU2Vjb25kcygyKSk7CiAgICAgICAgWmFkYW5pZSB0NCA9IG5ldyBaYWRhbmllKCJQaWxuZSB6Z8WCb3N6ZW5pZSIsIDUsIERhdGVUaW1lLk5vdy5BZGRTZWNvbmRzKDMpKTsKICAgICAgICBaYWRhbmllIHQ1ID0gbmV3IFphZGFuaWUoIk5vdGF0a2kiLCAxLCBEYXRlVGltZS5Ob3cuQWRkU2Vjb25kcyg0KSk7CgogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0MSwgdDEpOwogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0MiwgdDIpOwogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0MywgdDMpOwogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0NCwgdDQpOwogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0NSwgdDUpOwoKICAgICAgICB3aGlsZSAoa29sZWprYVByaW9yeXRldG93YS5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBaYWRhbmllIHphZGFuaWUgPSBrb2xlamthUHJpb3J5dGV0b3dhLkRlcXVldWUoKTsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoJCJ7emFkYW5pZS5PcGlzfSB8IHByaW9yeXRldDoge3phZGFuaWUuUHJpb3J5dGV0fSB8IGN6YXM6IHt6YWRhbmllLkN6YXNEb2RhbmlhfSIpOwogICAgICAgIH0KICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+",
            ["alg_64_zgloszenia.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjQgLSBLb2xlamthIHpnxYJvc3plxYQ8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzY0X3pnbG9zemVuaWE8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+T2JzxYJ1Z2EgemfFgm9zemXFhCBwcnpleiBrb2xlamvEmSBwcmlvcnl0ZXRvd8SFLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCnB1YmxpYyBjbGFzcyBaZ2xvc3plbmllCnsKICAgIHB1YmxpYyBpbnQgSWQgeyBnZXQ7IHNldDsgfQogICAgcHVibGljIHN0cmluZyBPcGlzIHsgZ2V0OyBzZXQ7IH0KICAgIHB1YmxpYyBpbnQgUHJpb3J5dGV0IHsgZ2V0OyBzZXQ7IH0KCiAgICBwdWJsaWMgWmdsb3N6ZW5pZShpbnQgaWQsIHN0cmluZyBvcGlzLCBpbnQgcHJpb3J5dGV0KQogICAgewogICAgICAgIElkID0gaWQ7CiAgICAgICAgT3BpcyA9IG9waXM7CiAgICAgICAgUHJpb3J5dGV0ID0gcHJpb3J5dGV0OwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFd5c3dpZXRsKCkKICAgIHsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgkIklEOiB7SWR9LCBQcmlvcnl0ZXQ6IHtQcmlvcnl0ZXR9LCBPcGlzOiB7T3Bpc30iKTsKICAgIH0KfQoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBQcmlvcml0eVF1ZXVlPFpnbG9zemVuaWUsIGludD4ga29sZWprYVByaW9yeXRldG93YSA9IG5ldyBQcmlvcml0eVF1ZXVlPFpnbG9zemVuaWUsIGludD4oKTsKCiAgICAgICAgWmdsb3N6ZW5pZSB0MSA9IG5ldyBaZ2xvc3plbmllKDEsICJQcm9ibGVtIHogbG9nb3dhbmllbSIsIDIpOwogICAgICAgIFpnbG9zemVuaWUgdDIgPSBuZXcgWmdsb3N6ZW5pZSgyLCAiU2Vyd2VyIG5pZSBkemlhxYJhIiwgNSk7CiAgICAgICAgWmdsb3N6ZW5pZSB0MyA9IG5ldyBaZ2xvc3plbmllKDMsICJCxYLEhWQgdyBmb3JtdWxhcnp1IiwgMSk7CiAgICAgICAgWmdsb3N6ZW5pZSB0NCA9IG5ldyBaZ2xvc3plbmllKDQsICJBd2FyaWEgYmF6eSBkYW55Y2giLCA0KTsKCiAgICAgICAga29sZWprYVByaW9yeXRldG93YS5FbnF1ZXVlKHQxLCAtdDEuUHJpb3J5dGV0KTsKICAgICAgICBrb2xlamthUHJpb3J5dGV0b3dhLkVucXVldWUodDIsIC10Mi5Qcmlvcnl0ZXQpOwogICAgICAgIGtvbGVqa2FQcmlvcnl0ZXRvd2EuRW5xdWV1ZSh0MywgLXQzLlByaW9yeXRldCk7CiAgICAgICAga29sZWprYVByaW9yeXRldG93YS5FbnF1ZXVlKHQ0LCAtdDQuUHJpb3J5dGV0KTsKCiAgICAgICAgd2hpbGUgKGtvbGVqa2FQcmlvcnl0ZXRvd2EuQ291bnQgPiAwKQogICAgICAgIHsKICAgICAgICAgICAga29sZWprYVByaW9yeXRldG93YS5EZXF1ZXVlKCkuV3lzd2lldGwoKTsKICAgICAgICB9CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_65_scal_tablice.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjUgLSBTY2FsYW5pZSBrIHRhYmxpYzwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNjVfc2NhbF90YWJsaWNlPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlNjYWxhbmllIGtpbGt1IHBvc29ydG93YW55Y2ggdGFibGljIHByemV6IFByaW9yaXR5UXVldWUuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKcHVibGljIGNsYXNzIEluZm9FbGVtZW50dQp7CiAgICBwdWJsaWMgaW50IFZhbHVlOwogICAgcHVibGljIGludCBJbmRla3NUYWJsaWN5OwogICAgcHVibGljIGludCBJbmRla3NFbGVtZW50dTsKCiAgICBwdWJsaWMgSW5mb0VsZW1lbnR1KGludCB2YWx1ZSwgaW50IGluZGVrc1RhYmxpY3ksIGludCBpbmRla3NFbGVtZW50dSkKICAgIHsKICAgICAgICBWYWx1ZSA9IHZhbHVlOwogICAgICAgIEluZGVrc1RhYmxpY3kgPSBpbmRla3NUYWJsaWN5OwogICAgICAgIEluZGVrc0VsZW1lbnR1ID0gaW5kZWtzRWxlbWVudHU7CiAgICB9Cn0KCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIExpc3Q8aW50PiBTY2FsUG9zb3J0b3dhbmVUYWJsaWNlKGludFtdW10gdGFibGljZSkKICAgIHsKICAgICAgICBQcmlvcml0eVF1ZXVlPEluZm9FbGVtZW50dSwgaW50PiBwcSA9IG5ldyBQcmlvcml0eVF1ZXVlPEluZm9FbGVtZW50dSwgaW50PigpOwogICAgICAgIExpc3Q8aW50PiB3eW5payA9IG5ldyBMaXN0PGludD4oKTsKCiAgICAgICAgZm9yIChpbnQgaSA9IDA7IGkgPCB0YWJsaWNlLkxlbmd0aDsgaSsrKQogICAgICAgIHsKICAgICAgICAgICAgaWYgKHRhYmxpY2VbaV0uTGVuZ3RoID4gMCkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgSW5mb0VsZW1lbnR1IGVsZW1lbnQgPSBuZXcgSW5mb0VsZW1lbnR1KHRhYmxpY2VbaV1bMF0sIGksIDApOwogICAgICAgICAgICAgICAgcHEuRW5xdWV1ZShlbGVtZW50LCBlbGVtZW50LlZhbHVlKTsKICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgd2hpbGUgKHBxLkNvdW50ID4gMCkKICAgICAgICB7CiAgICAgICAgICAgIEluZm9FbGVtZW50dSBjdXJyZW50ID0gcHEuRGVxdWV1ZSgpOwogICAgICAgICAgICAvLyBiaWVyemVteSBuYWptbmllanN6eSBha3R1YWxueSBlbGVtZW50IHoga3TDs3JlasWbIHRhYmxpY3kKICAgICAgICAgICAgd3luaWsuQWRkKGN1cnJlbnQuVmFsdWUpOwoKICAgICAgICAgICAgaW50IG5hc3RlcG55SW5kZWtzID0gY3VycmVudC5JbmRla3NFbGVtZW50dSArIDE7CgogICAgICAgICAgICBpZiAobmFzdGVwbnlJbmRla3MgPCB0YWJsaWNlW2N1cnJlbnQuSW5kZWtzVGFibGljeV0uTGVuZ3RoKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBpbnQgbmFzdGVwbmFXYXJ0b3NjID0gdGFibGljZVtjdXJyZW50LkluZGVrc1RhYmxpY3ldW25hc3RlcG55SW5kZWtzXTsKICAgICAgICAgICAgICAgIEluZm9FbGVtZW50dSBuYXN0ZXBueUVsZW1lbnQgPSBuZXcgSW5mb0VsZW1lbnR1KG5hc3RlcG5hV2FydG9zYywgY3VycmVudC5JbmRla3NUYWJsaWN5LCBuYXN0ZXBueUluZGVrcyk7CiAgICAgICAgICAgICAgICBwcS5FbnF1ZXVlKG5hc3RlcG55RWxlbWVudCwgbmFzdGVwbnlFbGVtZW50LlZhbHVlKTsKICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIHd5bmlrOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIGludFtdW10gdGFibGljZSA9CiAgICAgICAgewogICAgICAgICAgICBuZXcgaW50W10geyAxLCA0LCA3IH0sCiAgICAgICAgICAgIG5ldyBpbnRbXSB7IDIsIDUsIDggfSwKICAgICAgICAgICAgbmV3IGludFtdIHsgMCwgMywgOSB9CiAgICAgICAgfTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoc3RyaW5nLkpvaW4oIiAiLCBTY2FsUG9zb3J0b3dhbmVUYWJsaWNlKHRhYmxpY2UpKSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_66_zakres_tablice.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA2LjYgLSBOYWptbmllanN6eSB6YWtyZXMgeiBrIHRhYmxpYzwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNjZfemFrcmVzX3RhYmxpY2U8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+TmFqbW5pZWpzenkgemFrcmVzLCBrdMOzcnkgYmllcnplIHBvIGplZG55bSBlbGVtZW5jaWUgeiBrYcW8ZGVqIHRhYmxpY3kuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKcHVibGljIGNsYXNzIEluZm9FbGVtZW50dQp7CiAgICBwdWJsaWMgaW50IFZhbHVlOwogICAgcHVibGljIGludCBJbmRla3NUYWJsaWN5OwogICAgcHVibGljIGludCBJbmRla3NFbGVtZW50dTsKCiAgICBwdWJsaWMgSW5mb0VsZW1lbnR1KGludCB2YWx1ZSwgaW50IGluZGVrc1RhYmxpY3ksIGludCBpbmRla3NFbGVtZW50dSkKICAgIHsKICAgICAgICBWYWx1ZSA9IHZhbHVlOwogICAgICAgIEluZGVrc1RhYmxpY3kgPSBpbmRla3NUYWJsaWN5OwogICAgICAgIEluZGVrc0VsZW1lbnR1ID0gaW5kZWtzRWxlbWVudHU7CiAgICB9Cn0KCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIChpbnQgTCwgaW50IFIpIFpuYWpkek5ham1uaWVqc3p5WmFrcmVzKGludFtdW10gdGFibGljZSkKICAgIHsKICAgICAgICBQcmlvcml0eVF1ZXVlPEluZm9FbGVtZW50dSwgaW50PiBwcSA9IG5ldyBQcmlvcml0eVF1ZXVlPEluZm9FbGVtZW50dSwgaW50PigpOwogICAgICAgIGludCBha3R1YWxuZU1ha3NpbXVtID0gaW50Lk1pblZhbHVlOwoKICAgICAgICBmb3IgKGludCBpID0gMDsgaSA8IHRhYmxpY2UuTGVuZ3RoOyBpKyspCiAgICAgICAgewogICAgICAgICAgICBJbmZvRWxlbWVudHUgZWxlbWVudCA9IG5ldyBJbmZvRWxlbWVudHUodGFibGljZVtpXVswXSwgaSwgMCk7CiAgICAgICAgICAgIHBxLkVucXVldWUoZWxlbWVudCwgZWxlbWVudC5WYWx1ZSk7CgogICAgICAgICAgICBpZiAoZWxlbWVudC5WYWx1ZSA+IGFrdHVhbG5lTWFrc2ltdW0pCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGFrdHVhbG5lTWFrc2ltdW0gPSBlbGVtZW50LlZhbHVlOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICBpbnQgbmFqbGVwc3p5TGV3eSA9IDA7CiAgICAgICAgaW50IG5hamxlcHN6eVByYXd5ID0gaW50Lk1heFZhbHVlOwoKICAgICAgICB3aGlsZSAocHEuQ291bnQgPT0gdGFibGljZS5MZW5ndGgpCiAgICAgICAgewogICAgICAgICAgICBJbmZvRWxlbWVudHUgYWt0dWFsbmVNaW5pbXVtID0gcHEuRGVxdWV1ZSgpOwoKICAgICAgICAgICAgLy8gemFrcmVzIGplc3Qgb2QgYWt0dWFsbmVnbyBtaW5pbXVtIGRvIGFrdHVhbG5lZ28gbWFrc2ltdW0KICAgICAgICAgICAgaWYgKGFrdHVhbG5lTWFrc2ltdW0gLSBha3R1YWxuZU1pbmltdW0uVmFsdWUgPCBuYWpsZXBzenlQcmF3eSAtIG5hamxlcHN6eUxld3kpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIG5hamxlcHN6eUxld3kgPSBha3R1YWxuZU1pbmltdW0uVmFsdWU7CiAgICAgICAgICAgICAgICBuYWpsZXBzenlQcmF3eSA9IGFrdHVhbG5lTWFrc2ltdW07CiAgICAgICAgICAgIH0KCiAgICAgICAgICAgIGludCBuYXN0ZXBueUluZGVrcyA9IGFrdHVhbG5lTWluaW11bS5JbmRla3NFbGVtZW50dSArIDE7CgogICAgICAgICAgICBpZiAobmFzdGVwbnlJbmRla3MgPj0gdGFibGljZVtha3R1YWxuZU1pbmltdW0uSW5kZWtzVGFibGljeV0uTGVuZ3RoKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBicmVhazsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgaW50IG5hc3RlcG5hV2FydG9zYyA9IHRhYmxpY2VbYWt0dWFsbmVNaW5pbXVtLkluZGVrc1RhYmxpY3ldW25hc3RlcG55SW5kZWtzXTsKICAgICAgICAgICAgSW5mb0VsZW1lbnR1IG5hc3RlcG55RWxlbWVudCA9IG5ldyBJbmZvRWxlbWVudHUobmFzdGVwbmFXYXJ0b3NjLCBha3R1YWxuZU1pbmltdW0uSW5kZWtzVGFibGljeSwgbmFzdGVwbnlJbmRla3MpOwogICAgICAgICAgICBwcS5FbnF1ZXVlKG5hc3RlcG55RWxlbWVudCwgbmFzdGVwbnlFbGVtZW50LlZhbHVlKTsKCiAgICAgICAgICAgIGlmIChuYXN0ZXBuYVdhcnRvc2MgPiBha3R1YWxuZU1ha3NpbXVtKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBha3R1YWxuZU1ha3NpbXVtID0gbmFzdGVwbmFXYXJ0b3NjOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gKG5hamxlcHN6eUxld3ksIG5hamxlcHN6eVByYXd5KTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBpbnRbXVtdIHRhYmxpY2UgPQogICAgICAgIHsKICAgICAgICAgICAgbmV3IGludFtdIHsgNCwgMTAsIDE1LCAyNCwgMjYgfSwKICAgICAgICAgICAgbmV3IGludFtdIHsgMCwgOSwgMTIsIDIwIH0sCiAgICAgICAgICAgIG5ldyBpbnRbXSB7IDUsIDE4LCAyMiwgMzAgfQogICAgICAgIH07CgogICAgICAgIHZhciB3eW5payA9IFpuYWpkek5ham1uaWVqc3p5WmFrcmVzKHRhYmxpY2UpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCQiW3t3eW5pay5MfSwge3d5bmlrLlJ9XSIpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_71_graf_wszerz.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjEgLSBQcnplc3p1a2l3YW5pZSB3c3plcno8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzcxX2dyYWZfd3N6ZXJ6PC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPkJGUywgY3p5bGkgcHJ6ZWrFm2NpZSBncmFmdSB3c3plcnouPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBQcnplamR6V3N6ZXJ6KERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+IGdyYWYsIHN0cmluZyBzdGFydCkKICAgIHsKICAgICAgICBRdWV1ZTxzdHJpbmc+IGtvbGVqa2EgPSBuZXcgUXVldWU8c3RyaW5nPigpOwogICAgICAgIEhhc2hTZXQ8c3RyaW5nPiBvZHdpZWR6b25lID0gbmV3IEhhc2hTZXQ8c3RyaW5nPigpOwoKICAgICAgICAvLyBCRlMgaWR6aWUgcG96aW9tYW1pLCBkbGF0ZWdvIHXFvHl3YW15IGtvbGVqa2kKICAgICAgICBvZHdpZWR6b25lLkFkZChzdGFydCk7CiAgICAgICAga29sZWprYS5FbnF1ZXVlKHN0YXJ0KTsKCiAgICAgICAgd2hpbGUgKGtvbGVqa2EuQ291bnQgPiAwKQogICAgICAgIHsKICAgICAgICAgICAgc3RyaW5nIHdpZXJ6Y2hvbGVrID0ga29sZWprYS5EZXF1ZXVlKCk7CiAgICAgICAgICAgIENvbnNvbGUuV3JpdGUod2llcnpjaG9sZWsgKyAiICIpOwoKICAgICAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHNhc2lhZCBpbiBncmFmW3dpZXJ6Y2hvbGVrXSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKHNhc2lhZCkpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgb2R3aWVkem9uZS5BZGQoc2FzaWFkKTsKICAgICAgICAgICAgICAgICAgICBrb2xlamthLkVucXVldWUoc2FzaWFkKTsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+KCk7CgogICAgICAgIGdyYWZbIkEiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJCIiwgIkUiLCAiRCIgfTsKICAgICAgICBncmFmWyJCIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQSIsICJDIiwgIkUiIH07CiAgICAgICAgZ3JhZlsiRSJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiQiIsICJEIiwgIkYiIH07CiAgICAgICAgZ3JhZlsiRCJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiRSIgfTsKICAgICAgICBncmFmWyJDIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQiIgfTsKICAgICAgICBncmFmWyJGIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiRSIgfTsKCiAgICAgICAgUHJ6ZWpkeldzemVyeihncmFmLCAiQSIpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_72_graf_wglab.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjIgLSBQcnplc3p1a2l3YW5pZSB3IGfFgsSFYiwgcmVrdXJlbmNqYSBpIHN0b3M8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzcyX2dyYWZfd2dsYWI8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+REZTIHcgZHfDs2NoIHdlcnNqYWNoOiByZWt1cmVuY2phIGkgc3Rvcy48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpwdWJsaWMgY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgdm9pZCBQcnplamR6V2dsYWJSZWsoRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8c3RyaW5nPj4gZ3JhZiwgc3RyaW5nIHdpZXJ6Y2hvbGVrLCBIYXNoU2V0PHN0cmluZz4gb2R3aWVkem9uZSkKICAgIHsKICAgICAgICBvZHdpZWR6b25lLkFkZCh3aWVyemNob2xlayk7CiAgICAgICAgQ29uc29sZS5Xcml0ZSh3aWVyemNob2xlayArICIgIik7CgogICAgICAgIGZvcmVhY2ggKHN0cmluZyBzYXNpYWQgaW4gZ3JhZlt3aWVyemNob2xla10pCiAgICAgICAgewogICAgICAgICAgICBpZiAoIW9kd2llZHpvbmUuQ29udGFpbnMoc2FzaWFkKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgUHJ6ZWpkeldnbGFiUmVrKGdyYWYsIHNhc2lhZCwgb2R3aWVkem9uZSk7CiAgICAgICAgICAgIH0KICAgICAgICB9CiAgICB9CgogICAgc3RhdGljIHZvaWQgUHJ6ZWpkeldnbGFiU3RvcyhEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmLCBzdHJpbmcgc3RhcnQpCiAgICB7CiAgICAgICAgU3RhY2s8c3RyaW5nPiBzdG9zID0gbmV3IFN0YWNrPHN0cmluZz4oKTsKICAgICAgICBIYXNoU2V0PHN0cmluZz4gb2R3aWVkem9uZSA9IG5ldyBIYXNoU2V0PHN0cmluZz4oKTsKCiAgICAgICAgc3Rvcy5QdXNoKHN0YXJ0KTsKCiAgICAgICAgd2hpbGUgKHN0b3MuQ291bnQgPiAwKQogICAgICAgIHsKICAgICAgICAgICAgc3RyaW5nIHdpZXJ6Y2hvbGVrID0gc3Rvcy5Qb3AoKTsKCiAgICAgICAgICAgIGlmICghb2R3aWVkem9uZS5Db250YWlucyh3aWVyemNob2xlaykpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIG9kd2llZHpvbmUuQWRkKHdpZXJ6Y2hvbGVrKTsKICAgICAgICAgICAgICAgIENvbnNvbGUuV3JpdGUod2llcnpjaG9sZWsgKyAiICIpOwoKICAgICAgICAgICAgICAgIGZvciAoaW50IGkgPSBncmFmW3dpZXJ6Y2hvbGVrXS5Db3VudCAtIDE7IGkgPj0gMDsgaS0tKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIHN0cmluZyBzYXNpYWQgPSBncmFmW3dpZXJ6Y2hvbGVrXVtpXTsKCiAgICAgICAgICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKHNhc2lhZCkpCiAgICAgICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgICAgICBzdG9zLlB1c2goc2FzaWFkKTsKICAgICAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgIH0KICAgICAgICB9CiAgICB9CgogICAgcHVibGljIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+IGdyYWYgPSBuZXcgRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8c3RyaW5nPj4oKTsKCiAgICAgICAgZ3JhZlsiQSJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkIiLCAiRSIsICJEIiB9OwogICAgICAgIGdyYWZbIkIiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJBIiwgIkMiLCAiRSIgfTsKICAgICAgICBncmFmWyJFIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQSIsICJCIiwgIkQiLCAiRiIgfTsKICAgICAgICBncmFmWyJEIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQSIsICJFIiB9OwogICAgICAgIGdyYWZbIkMiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJCIiB9OwogICAgICAgIGdyYWZbIkYiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJFIiB9OwoKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiUHJ6ZWpkeldnbGFiIHJla3VyZW5jeWpuaWU6Iik7CiAgICAgICAgUHJ6ZWpkeldnbGFiUmVrKGdyYWYsICJBIiwgbmV3IEhhc2hTZXQ8c3RyaW5nPigpKTsKCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIgpERlMgaXRlcmFjeWpuaWUgemUgc3Rvc2VtOiIpOwogICAgICAgIFByemVqZHpXZ2xhYlN0b3MoZ3JhZiwgIkEiKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+",
            ["alg_72_graf_wglab_rek.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjIgLSBQcnplc3p1a2l3YW5pZSB3IGfFgsSFYiByZWt1cmVuY3lqbmllPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ183Ml9ncmFmX3dnbGFiX3JlazwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5ERlMgcmVrdXJlbmN5am5pZS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIFByemVqZHpXZ2xhYihEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmLCBzdHJpbmcgd2llcnpjaG9sZWssIEhhc2hTZXQ8c3RyaW5nPiBvZHdpZWR6b25lKQogICAgewogICAgICAgIG9kd2llZHpvbmUuQWRkKHdpZXJ6Y2hvbGVrKTsKICAgICAgICBDb25zb2xlLldyaXRlKHdpZXJ6Y2hvbGVrICsgIiAiKTsKCiAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHNhc2lhZCBpbiBncmFmW3dpZXJ6Y2hvbGVrXSkKICAgICAgICB7CiAgICAgICAgICAgIGlmICghb2R3aWVkem9uZS5Db250YWlucyhzYXNpYWQpKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBQcnplamR6V2dsYWIoZ3JhZiwgc2FzaWFkLCBvZHdpZWR6b25lKTsKICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+KCk7CgogICAgICAgIGdyYWZbIkEiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJCIiwgIkUiLCAiRCIgfTsKICAgICAgICBncmFmWyJCIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQSIsICJDIiwgIkUiIH07CiAgICAgICAgZ3JhZlsiRSJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiQiIsICJEIiwgIkYiIH07CiAgICAgICAgZ3JhZlsiRCJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiRSIgfTsKICAgICAgICBncmFmWyJDIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQiIgfTsKICAgICAgICBncmFmWyJGIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiRSIgfTsKCiAgICAgICAgUHJ6ZWpkeldnbGFiKGdyYWYsICJBIiwgbmV3IEhhc2hTZXQ8c3RyaW5nPigpKTsKICAgIH0KfQpdXT48L0NvZGU+CiAgICA8L1NuaXBwZXQ+CiAgPC9Db2RlU25pcHBldD4KPC9Db2RlU25pcHBldHM+",
            ["alg_72_graf_wglab_stos.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjIgLSBQcnplc3p1a2l3YW5pZSB3IGfFgsSFYiB6ZSBzdG9zZW08L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzcyX2dyYWZfd2dsYWJfc3RvczwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5ERlMgaXRlcmFjeWpuaWUgbmEgc3Rvc2llLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHZvaWQgUHJ6ZWpkeldnbGFiKERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+IGdyYWYsIHN0cmluZyBzdGFydCkKICAgIHsKICAgICAgICBTdGFjazxzdHJpbmc+IHN0b3MgPSBuZXcgU3RhY2s8c3RyaW5nPigpOwogICAgICAgIEhhc2hTZXQ8c3RyaW5nPiBvZHdpZWR6b25lID0gbmV3IEhhc2hTZXQ8c3RyaW5nPigpOwoKICAgICAgICAvLyB3IERGUyBzdG9zIHphc3TEmXB1amUgcmVrdXJlbmNqxJkKICAgICAgICBzdG9zLlB1c2goc3RhcnQpOwoKICAgICAgICB3aGlsZSAoc3Rvcy5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBzdHJpbmcgd2llcnpjaG9sZWsgPSBzdG9zLlBvcCgpOwoKICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKHdpZXJ6Y2hvbGVrKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgb2R3aWVkem9uZS5BZGQod2llcnpjaG9sZWspOwogICAgICAgICAgICAgICAgQ29uc29sZS5Xcml0ZSh3aWVyemNob2xlayArICIgIik7CgogICAgICAgICAgICAgICAgZm9yIChpbnQgaSA9IGdyYWZbd2llcnpjaG9sZWtdLkNvdW50IC0gMTsgaSA+PSAwOyBpLS0pCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgc3RyaW5nIHNhc2lhZCA9IGdyYWZbd2llcnpjaG9sZWtdW2ldOwoKICAgICAgICAgICAgICAgICAgICBpZiAoIW9kd2llZHpvbmUuQ29udGFpbnMoc2FzaWFkKSkKICAgICAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgICAgIHN0b3MuUHVzaChzYXNpYWQpOwogICAgICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+KCk7CgogICAgICAgIGdyYWZbIkEiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJCIiwgIkUiLCAiRCIgfTsKICAgICAgICBncmFmWyJCIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQSIsICJDIiwgIkUiIH07CiAgICAgICAgZ3JhZlsiRSJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiQiIsICJEIiwgIkYiIH07CiAgICAgICAgZ3JhZlsiRCJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiRSIgfTsKICAgICAgICBncmFmWyJDIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQiIgfTsKICAgICAgICBncmFmWyJGIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiRSIgfTsKCiAgICAgICAgUHJ6ZWpkeldnbGFiKGdyYWYsICJBIik7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_73_graf_skier.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjMgLSBHcmFmIHNraWVyb3dhbnk8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzczX2dyYWZfc2tpZXI8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+R3JhZiBza2llcm93YW55LCBwcnplasWbY2llIEJGUyBpIERGUy48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIFByemVqZHpXc3plcnooRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8c3RyaW5nPj4gZ3JhZiwgc3RyaW5nIHN0YXJ0KQogICAgewogICAgICAgIFF1ZXVlPHN0cmluZz4ga29sZWprYSA9IG5ldyBRdWV1ZTxzdHJpbmc+KCk7CiAgICAgICAgSGFzaFNldDxzdHJpbmc+IG9kd2llZHpvbmUgPSBuZXcgSGFzaFNldDxzdHJpbmc+KCk7CgogICAgICAgIG9kd2llZHpvbmUuQWRkKHN0YXJ0KTsKICAgICAgICBrb2xlamthLkVucXVldWUoc3RhcnQpOwoKICAgICAgICB3aGlsZSAoa29sZWprYS5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBzdHJpbmcgd2llcnpjaG9sZWsgPSBrb2xlamthLkRlcXVldWUoKTsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZSh3aWVyemNob2xlayArICIgIik7CgogICAgICAgICAgICBmb3JlYWNoIChzdHJpbmcgc2FzaWFkIGluIGdyYWZbd2llcnpjaG9sZWtdKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBpZiAoIW9kd2llZHpvbmUuQ29udGFpbnMoc2FzaWFkKSkKICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICBvZHdpZWR6b25lLkFkZChzYXNpYWQpOwogICAgICAgICAgICAgICAgICAgIGtvbGVqa2EuRW5xdWV1ZShzYXNpYWQpOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICB9CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyB2b2lkIFByemVqZHpXZ2xhYihEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PiBncmFmLCBzdHJpbmcgc3RhcnQpCiAgICB7CiAgICAgICAgU3RhY2s8c3RyaW5nPiBzdG9zID0gbmV3IFN0YWNrPHN0cmluZz4oKTsKICAgICAgICBIYXNoU2V0PHN0cmluZz4gb2R3aWVkem9uZSA9IG5ldyBIYXNoU2V0PHN0cmluZz4oKTsKCiAgICAgICAgc3Rvcy5QdXNoKHN0YXJ0KTsKCiAgICAgICAgd2hpbGUgKHN0b3MuQ291bnQgPiAwKQogICAgICAgIHsKICAgICAgICAgICAgc3RyaW5nIHdpZXJ6Y2hvbGVrID0gc3Rvcy5Qb3AoKTsKCiAgICAgICAgICAgIGlmICghb2R3aWVkem9uZS5Db250YWlucyh3aWVyemNob2xlaykpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIG9kd2llZHpvbmUuQWRkKHdpZXJ6Y2hvbGVrKTsKICAgICAgICAgICAgICAgIENvbnNvbGUuV3JpdGUod2llcnpjaG9sZWsgKyAiICIpOwoKICAgICAgICAgICAgICAgIGZvciAoaW50IGkgPSBncmFmW3dpZXJ6Y2hvbGVrXS5Db3VudCAtIDE7IGkgPj0gMDsgaS0tKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIHN0cmluZyBzYXNpYWQgPSBncmFmW3dpZXJ6Y2hvbGVrXVtpXTsKCiAgICAgICAgICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKHNhc2lhZCkpCiAgICAgICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgICAgICBzdG9zLlB1c2goc2FzaWFkKTsKICAgICAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgIH0KICAgICAgICB9CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8c3RyaW5nPj4gZ3JhZiA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PigpOwoKICAgICAgICBncmFmWyJBIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQiIsICJEIiB9OwogICAgICAgIGdyYWZbIkIiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJDIiB9OwogICAgICAgIGdyYWZbIkMiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJFIiB9OwogICAgICAgIGdyYWZbIkQiXSA9IG5ldyBMaXN0PHN0cmluZz4oKTsKICAgICAgICBncmFmWyJFIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiRCIsICJGIiB9OwogICAgICAgIGdyYWZbIkYiXSA9IG5ldyBMaXN0PHN0cmluZz4oKTsKCiAgICAgICAgUHJ6ZWpkeldzemVyeihncmFmLCAiQSIpOwogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCk7CiAgICAgICAgUHJ6ZWpkeldnbGFiKGdyYWYsICJBIik7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_74_graf_spojny.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjQgLSBTcMOzam5vxZvEhyBncmFmdTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNzRfZ3JhZl9zcG9qbnk8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+U3ByYXdkemVuaWUgc3DDs2pub8WbY2kgZ3JhZnUgcHJ6ZXogQkZTLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIGJvb2wgQ3p5U3Bvam55KERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PHN0cmluZz4+IGdyYWYpCiAgICB7CiAgICAgICAgaWYgKGdyYWYuQ291bnQgPT0gMCkKICAgICAgICB7CiAgICAgICAgICAgIHJldHVybiB0cnVlOwogICAgICAgIH0KCiAgICAgICAgSGFzaFNldDxzdHJpbmc+IG9kd2llZHpvbmUgPSBuZXcgSGFzaFNldDxzdHJpbmc+KCk7CiAgICAgICAgUXVldWU8c3RyaW5nPiBrb2xlamthID0gbmV3IFF1ZXVlPHN0cmluZz4oKTsKCiAgICAgICAgc3RyaW5nIHN0YXJ0ID0gbnVsbDsKCiAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHdpZXJ6Y2hvbGVrIGluIGdyYWYuS2V5cykKICAgICAgICB7CiAgICAgICAgICAgIHN0YXJ0ID0gd2llcnpjaG9sZWs7CiAgICAgICAgICAgIGJyZWFrOwogICAgICAgIH0KCiAgICAgICAgb2R3aWVkem9uZS5BZGQoc3RhcnQpOwogICAgICAgIGtvbGVqa2EuRW5xdWV1ZShzdGFydCk7CgogICAgICAgIHdoaWxlIChrb2xlamthLkNvdW50ID4gMCkKICAgICAgICB7CiAgICAgICAgICAgIHN0cmluZyB3aWVyemNob2xlayA9IGtvbGVqa2EuRGVxdWV1ZSgpOwoKICAgICAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHNhc2lhZCBpbiBncmFmW3dpZXJ6Y2hvbGVrXSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKHNhc2lhZCkpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgb2R3aWVkem9uZS5BZGQoc2FzaWFkKTsKICAgICAgICAgICAgICAgICAgICBrb2xlamthLkVucXVldWUoc2FzaWFkKTsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgLy8gamXFm2xpIEJGUyBvZHdpZWR6acWCIHdzenlzdGtvLCBncmFmIGplc3Qgc3DDs2pueQogICAgICAgIHJldHVybiBvZHdpZWR6b25lLkNvdW50ID09IGdyYWYuQ291bnQ7CiAgICB9CgogICAgc3RhdGljIHZvaWQgTWFpbigpCiAgICB7CiAgICAgICAgRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8c3RyaW5nPj4gZ3JhZiA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxzdHJpbmc+PigpOwoKICAgICAgICBncmFmWyJBIl0gPSBuZXcgTGlzdDxzdHJpbmc+KCkgeyAiQiIsICJDIiB9OwogICAgICAgIGdyYWZbIkIiXSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJBIiwgIkQiIH07CiAgICAgICAgZ3JhZlsiQyJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiIH07CiAgICAgICAgZ3JhZlsiRCJdID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkIiIH07CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKEN6eVNwb2pueShncmFmKSA/ICJHcmFmIGplc3Qgc3Bvam55IiA6ICJHcmFmIG5pZSBqZXN0IHNwb2pueSIpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_75_dijkstra.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjUgLSBOYWprcsOzdHN6ZSDFm2NpZcW8a2kgRGlqa3N0cnk8L1RpdGxlPgogICAgICA8U2hvcnRjdXQ+YWxnXzc1X2RpamtzdHJhPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPkRpamtzdHJhLCBuYWprcsOzdHN6ZSBkcm9naSBvZCBzdGFydHUuPC9EZXNjcmlwdGlvbj4KICAgICAgPEF1dGhvcj5iZWFyYmluZTwvQXV0aG9yPgogICAgICA8U25pcHBldFR5cGVzPgogICAgICAgIDxTbmlwcGV0VHlwZT5FeHBhbnNpb248L1NuaXBwZXRUeXBlPgogICAgICA8L1NuaXBwZXRUeXBlcz4KICAgIDwvSGVhZGVyPgogICAgPFNuaXBwZXQ+CiAgICAgIDxDb2RlIExhbmd1YWdlPSJjc2hhcnAiPjwhW0NEQVRBWwp1c2luZyBTeXN0ZW07CnVzaW5nIFN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmljOwoKcHVibGljIGNsYXNzIEtyYXdlZHoKewogICAgcHVibGljIHN0cmluZyBDZWw7CiAgICBwdWJsaWMgaW50IFdhZ2E7CgogICAgcHVibGljIEtyYXdlZHooc3RyaW5nIGNlbCwgaW50IHdhZ2EpCiAgICB7CiAgICAgICAgQ2VsID0gY2VsOwogICAgICAgIFdhZ2EgPSB3YWdhOwogICAgfQp9CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyBEaWN0aW9uYXJ5PHN0cmluZywgaW50PiBOYWprcm90c3plRHJvZ2koRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8S3Jhd2Vkej4+IGdyYWYsIHN0cmluZyBzdGFydCkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgaW50PiBvZGxlZ2xvc2NpID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBpbnQ+KCk7CgogICAgICAgIGZvcmVhY2ggKHN0cmluZyB3aWVyemNob2xlayBpbiBncmFmLktleXMpCiAgICAgICAgewogICAgICAgICAgICBvZGxlZ2xvc2NpW3dpZXJ6Y2hvbGVrXSA9IGludC5NYXhWYWx1ZTsKICAgICAgICB9CgogICAgICAgIG9kbGVnbG9zY2lbc3RhcnRdID0gMDsKCiAgICAgICAgUHJpb3JpdHlRdWV1ZTxzdHJpbmcsIGludD4gcHEgPSBuZXcgUHJpb3JpdHlRdWV1ZTxzdHJpbmcsIGludD4oKTsKICAgICAgICBwcS5FbnF1ZXVlKHN0YXJ0LCAwKTsKCiAgICAgICAgd2hpbGUgKHBxLkNvdW50ID4gMCkKICAgICAgICB7CiAgICAgICAgICAgIHBxLlRyeURlcXVldWUob3V0IHN0cmluZyBjdXJyZW50LCBvdXQgaW50IGFrdHVhbG5hT2RsZWdsb3NjKTsKCiAgICAgICAgICAgIC8vIHN0YXJzenkgd3BpcyB3IGtvbGVqY2UsIG1vxbxuYSBnbyBwb21pbsSFxIcKICAgICAgICAgICAgaWYgKGFrdHVhbG5hT2RsZWdsb3NjID4gb2RsZWdsb3NjaVtjdXJyZW50XSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgY29udGludWU7CiAgICAgICAgICAgIH0KCiAgICAgICAgICAgIGZvcmVhY2ggKEtyYXdlZHoga3Jhd2VkeiBpbiBncmFmW2N1cnJlbnRdKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBpbnQgbm93YU9kbGVnbG9zYyA9IG9kbGVnbG9zY2lbY3VycmVudF0gKyBrcmF3ZWR6LldhZ2E7CgogICAgICAgICAgICAgICAgLy8gcmVsYWtzYWNqYSBrcmF3xJlkemkKICAgICAgICAgICAgICAgIGlmIChub3dhT2RsZWdsb3NjIDwgb2RsZWdsb3NjaVtrcmF3ZWR6LkNlbF0pCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgb2RsZWdsb3NjaVtrcmF3ZWR6LkNlbF0gPSBub3dhT2RsZWdsb3NjOwogICAgICAgICAgICAgICAgICAgIHBxLkVucXVldWUoa3Jhd2Vkei5DZWwsIG5vd2FPZGxlZ2xvc2MpOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gb2RsZWdsb3NjaTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxLcmF3ZWR6Pj4gZ3JhZiA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxLcmF3ZWR6Pj4oKTsKCiAgICAgICAgZ3JhZlsiQSJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJCIiwgNCksIG5ldyBLcmF3ZWR6KCJDIiwgMikgfTsKICAgICAgICBncmFmWyJCIl0gPSBuZXcgTGlzdDxLcmF3ZWR6PigpIHsgbmV3IEtyYXdlZHooIkMiLCAxKSwgbmV3IEtyYXdlZHooIkQiLCA1KSB9OwogICAgICAgIGdyYWZbIkMiXSA9IG5ldyBMaXN0PEtyYXdlZHo+KCkgeyBuZXcgS3Jhd2VkeigiRCIsIDgpLCBuZXcgS3Jhd2VkeigiRSIsIDEwKSB9OwogICAgICAgIGdyYWZbIkQiXSA9IG5ldyBMaXN0PEtyYXdlZHo+KCkgeyBuZXcgS3Jhd2VkeigiRSIsIDIpIH07CiAgICAgICAgZ3JhZlsiRSJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKTsKCiAgICAgICAgRGljdGlvbmFyeTxzdHJpbmcsIGludD4gb2RsZWdsb3NjaSA9IE5hamtyb3RzemVEcm9naShncmFmLCAiQSIpOwoKICAgICAgICBmb3JlYWNoICh2YXIgd3BpcyBpbiBvZGxlZ2xvc2NpKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUod3Bpcy5LZXkgKyAiOiAiICsgd3Bpcy5WYWx1ZSk7CiAgICAgICAgfQogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_76_drogi_miasta.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjYgLSBOYWprcsOzdHN6ZSBkcm9naSBtacSZZHp5IG1pYXN0YW1pPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ183Nl9kcm9naV9taWFzdGE8L1Nob3J0Y3V0PgogICAgICA8RGVzY3JpcHRpb24+RGlqa3N0cmEgZGxhIG1pYXN0LCByYXplbSB6IG9kdHdvcnplbmllbSB0cmFzeS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbCnVzaW5nIFN5c3RlbTsKdXNpbmcgU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWM7CgpwdWJsaWMgY2xhc3MgS3Jhd2Vkegp7CiAgICBwdWJsaWMgc3RyaW5nIENlbDsKICAgIHB1YmxpYyBpbnQgV2FnYTsKCiAgICBwdWJsaWMgS3Jhd2VkeihzdHJpbmcgY2VsLCBpbnQgd2FnYSkKICAgIHsKICAgICAgICBDZWwgPSBjZWw7CiAgICAgICAgV2FnYSA9IHdhZ2E7CiAgICB9Cn0KCnB1YmxpYyBjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIE5hamtyb3RzemVEcm9naSgKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxLcmF3ZWR6Pj4gZ3JhZiwKICAgICAgICBzdHJpbmcgc3RhcnQsCiAgICAgICAgb3V0IERpY3Rpb25hcnk8c3RyaW5nLCBpbnQ+IG9kbGVnbG9zY2ksCiAgICAgICAgb3V0IERpY3Rpb25hcnk8c3RyaW5nLCBzdHJpbmc+IHBvcHJ6ZWRuaWspCiAgICB7CiAgICAgICAgb2RsZWdsb3NjaSA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgaW50PigpOwogICAgICAgIHBvcHJ6ZWRuaWsgPSBuZXcgRGljdGlvbmFyeTxzdHJpbmcsIHN0cmluZz4oKTsKCiAgICAgICAgZm9yZWFjaCAoc3RyaW5nIG1pYXN0byBpbiBncmFmLktleXMpCiAgICAgICAgewogICAgICAgICAgICBvZGxlZ2xvc2NpW21pYXN0b10gPSBpbnQuTWF4VmFsdWU7CiAgICAgICAgICAgIHBvcHJ6ZWRuaWtbbWlhc3RvXSA9IG51bGw7CiAgICAgICAgfQoKICAgICAgICBvZGxlZ2xvc2NpW3N0YXJ0XSA9IDA7CgogICAgICAgIFByaW9yaXR5UXVldWU8c3RyaW5nLCBpbnQ+IHBxID0gbmV3IFByaW9yaXR5UXVldWU8c3RyaW5nLCBpbnQ+KCk7CiAgICAgICAgcHEuRW5xdWV1ZShzdGFydCwgMCk7CgogICAgICAgIHdoaWxlIChwcS5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBwcS5UcnlEZXF1ZXVlKG91dCBzdHJpbmcgY3VycmVudCwgb3V0IGludCBha3R1YWxuYU9kbGVnbG9zYyk7CgogICAgICAgICAgICBpZiAoYWt0dWFsbmFPZGxlZ2xvc2MgPiBvZGxlZ2xvc2NpW2N1cnJlbnRdKQogICAgICAgICAgICB7CiAgICAgICAgICAgICAgICBjb250aW51ZTsKICAgICAgICAgICAgfQoKICAgICAgICAgICAgZm9yZWFjaCAoS3Jhd2VkeiBrcmF3ZWR6IGluIGdyYWZbY3VycmVudF0pCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGludCBub3dhT2RsZWdsb3NjID0gb2RsZWdsb3NjaVtjdXJyZW50XSArIGtyYXdlZHouV2FnYTsKCiAgICAgICAgICAgICAgICBpZiAobm93YU9kbGVnbG9zYyA8IG9kbGVnbG9zY2lba3Jhd2Vkei5DZWxdKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIG9kbGVnbG9zY2lba3Jhd2Vkei5DZWxdID0gbm93YU9kbGVnbG9zYzsKICAgICAgICAgICAgICAgICAgICBwb3ByemVkbmlrW2tyYXdlZHouQ2VsXSA9IGN1cnJlbnQ7IC8vIGRvIG9kdHdvcnplbmlhIHRyYXN5CiAgICAgICAgICAgICAgICAgICAgcHEuRW5xdWV1ZShrcmF3ZWR6LkNlbCwgbm93YU9kbGVnbG9zYyk7CiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgIH0KICAgICAgICB9CiAgICB9CgogICAgc3RhdGljIHZvaWQgV3lwaXN6U2NpZXprZShEaWN0aW9uYXJ5PHN0cmluZywgc3RyaW5nPiBwb3ByemVkbmlrLCBzdHJpbmcgdGFyZ2V0KQogICAgewogICAgICAgIFN0YWNrPHN0cmluZz4gc2NpZXprYSA9IG5ldyBTdGFjazxzdHJpbmc+KCk7CiAgICAgICAgc3RyaW5nIGN1cnJlbnQgPSB0YXJnZXQ7CgogICAgICAgIHdoaWxlIChjdXJyZW50ICE9IG51bGwpCiAgICAgICAgewogICAgICAgICAgICBzY2llemthLlB1c2goY3VycmVudCk7CiAgICAgICAgICAgIGN1cnJlbnQgPSBwb3ByemVkbmlrW2N1cnJlbnRdOwogICAgICAgIH0KCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoc3RyaW5nLkpvaW4oIiAtPiAiLCBzY2llemthKSk7CiAgICB9CgogICAgcHVibGljIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PiBncmFmID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PigpOwoKICAgICAgICBncmFmWyJMdWJsaW4iXSA9IG5ldyBMaXN0PEtyYXdlZHo+KCkgeyBuZXcgS3Jhd2VkeigiV2Fyc3phd2EiLCAxNzApLCBuZXcgS3Jhd2VkeigiUnplc3pvdyIsIDE2MCksIG5ldyBLcmF3ZWR6KCJLaWVsY2UiLCAxOTApIH07CiAgICAgICAgZ3JhZlsiV2Fyc3phd2EiXSA9IG5ldyBMaXN0PEtyYXdlZHo+KCkgeyBuZXcgS3Jhd2VkeigiTHVibGluIiwgMTcwKSwgbmV3IEtyYXdlZHooIkxvZHoiLCAxMzApIH07CiAgICAgICAgZ3JhZlsiUnplc3pvdyJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJMdWJsaW4iLCAxNjApLCBuZXcgS3Jhd2VkeigiS3Jha293IiwgMTcwKSB9OwogICAgICAgIGdyYWZbIktpZWxjZSJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJMdWJsaW4iLCAxOTApLCBuZXcgS3Jhd2VkeigiS3Jha293IiwgMTIwKSwgbmV3IEtyYXdlZHooIkxvZHoiLCAxNjApIH07CiAgICAgICAgZ3JhZlsiTG9keiJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJXYXJzemF3YSIsIDEzMCksIG5ldyBLcmF3ZWR6KCJLaWVsY2UiLCAxNjApLCBuZXcgS3Jhd2VkeigiS3Jha293IiwgMjEwKSB9OwogICAgICAgIGdyYWZbIktyYWtvdyJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJSemVzem93IiwgMTcwKSwgbmV3IEtyYXdlZHooIktpZWxjZSIsIDEyMCksIG5ldyBLcmF3ZWR6KCJMb2R6IiwgMjEwKSB9OwoKICAgICAgICBOYWprcm90c3plRHJvZ2koZ3JhZiwgIkx1YmxpbiIsIG91dCBEaWN0aW9uYXJ5PHN0cmluZywgaW50PiBvZGxlZ2xvc2NpLCBvdXQgRGljdGlvbmFyeTxzdHJpbmcsIHN0cmluZz4gcG9wcnplZG5payk7CgogICAgICAgIGZvcmVhY2ggKHN0cmluZyBtaWFzdG8gaW4gZ3JhZi5LZXlzKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIk1pYXN0bzogIiArIG1pYXN0byArICIsIGR5c3RhbnM6ICIgKyBvZGxlZ2xvc2NpW21pYXN0b10pOwogICAgICAgICAgICBDb25zb2xlLldyaXRlKCJUcmFzYTogIik7CiAgICAgICAgICAgIFd5cGlzelNjaWV6a2UocG9wcnplZG5paywgbWlhc3RvKTsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoKTsKICAgICAgICB9CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_76_kruskal.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjYgLSBLcnVza2FsPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ183Nl9rcnVza2FsPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPktydXNrYWwgaSB6Ymlvcnkgcm96xYLEhWN6bmUgZG8gTVNULjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCnB1YmxpYyBjbGFzcyBLcmF3ZWR6CnsKICAgIHB1YmxpYyBzdHJpbmcgWnJvZGxvOwogICAgcHVibGljIHN0cmluZyBDZWw7CiAgICBwdWJsaWMgaW50IFdhZ2E7CgogICAgcHVibGljIEtyYXdlZHooc3RyaW5nIHpyb2Rsbywgc3RyaW5nIGNlbCwgaW50IHdhZ2EpCiAgICB7CiAgICAgICAgWnJvZGxvID0genJvZGxvOwogICAgICAgIENlbCA9IGNlbDsKICAgICAgICBXYWdhID0gd2FnYTsKICAgIH0KfQoKcHVibGljIGNsYXNzIFpiaW9yeVJvemxhY3puZQp7CiAgICBwcml2YXRlIERpY3Rpb25hcnk8c3RyaW5nLCBzdHJpbmc+IHJvZHppYyA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgc3RyaW5nPigpOwoKICAgIHB1YmxpYyB2b2lkIFV0d29yelpiaW9yKHN0cmluZyB3aWVyemNob2xlaykKICAgIHsKICAgICAgICByb2R6aWNbd2llcnpjaG9sZWtdID0gd2llcnpjaG9sZWs7CiAgICB9CgogICAgcHVibGljIHN0cmluZyBabmFqZHooc3RyaW5nIHdpZXJ6Y2hvbGVrKQogICAgewogICAgICAgIGlmIChyb2R6aWNbd2llcnpjaG9sZWtdICE9IHdpZXJ6Y2hvbGVrKQogICAgICAgIHsKICAgICAgICAgICAgcm9kemljW3dpZXJ6Y2hvbGVrXSA9IFpuYWpkeihyb2R6aWNbd2llcnpjaG9sZWtdKTsKICAgICAgICB9CgogICAgICAgIHJldHVybiByb2R6aWNbd2llcnpjaG9sZWtdOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFBvbGFjeihzdHJpbmcgYSwgc3RyaW5nIGIpCiAgICB7CiAgICAgICAgc3RyaW5nIGtvcnplbkEgPSBabmFqZHooYSk7CiAgICAgICAgc3RyaW5nIGtvcnplbkIgPSBabmFqZHooYik7CgogICAgICAgIGlmIChrb3J6ZW5BICE9IGtvcnplbkIpCiAgICAgICAgewogICAgICAgICAgICByb2R6aWNba29yemVuQl0gPSBrb3J6ZW5BOwogICAgICAgIH0KICAgIH0KfQoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgTGlzdDxLcmF3ZWR6PiBLcnVza2FsKExpc3Q8c3RyaW5nPiB3aWVyemNob2xraSwgTGlzdDxLcmF3ZWR6PiBrcmF3ZWR6aWUpCiAgICB7CiAgICAgICAgTGlzdDxLcmF3ZWR6PiBkcnpld28gPSBuZXcgTGlzdDxLcmF3ZWR6PigpOwogICAgICAgIFpiaW9yeVJvemxhY3puZSB1ZiA9IG5ldyBaYmlvcnlSb3psYWN6bmUoKTsKCiAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHdpZXJ6Y2hvbGVrIGluIHdpZXJ6Y2hvbGtpKQogICAgICAgIHsKICAgICAgICAgICAgdWYuVXR3b3J6WmJpb3Iod2llcnpjaG9sZWspOwogICAgICAgIH0KCiAgICAgICAgLy8gbmFqcGllcncgbmFqbMW8ZWpzemUga3Jhd8SZZHppZQogICAgICAgIGtyYXdlZHppZS5Tb3J0KChhLCBiKSA9PiBhLldhZ2EuQ29tcGFyZVRvKGIuV2FnYSkpOwoKICAgICAgICBmb3JlYWNoIChLcmF3ZWR6IGtyYXdlZHogaW4ga3Jhd2VkemllKQogICAgICAgIHsKICAgICAgICAgICAgLy8gZG9kYWplbXkgdHlsa28sIGdkeSBuaWUgenJvYmkgY3lrbHUKICAgICAgICAgICAgaWYgKHVmLlpuYWpkeihrcmF3ZWR6Llpyb2RsbykgIT0gdWYuWm5hamR6KGtyYXdlZHouQ2VsKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgZHJ6ZXdvLkFkZChrcmF3ZWR6KTsKICAgICAgICAgICAgICAgIHVmLlBvbGFjeihrcmF3ZWR6Llpyb2Rsbywga3Jhd2Vkei5DZWwpOwogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gZHJ6ZXdvOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIExpc3Q8c3RyaW5nPiB3aWVyemNob2xraSA9IG5ldyBMaXN0PHN0cmluZz4oKSB7ICJBIiwgIkIiLCAiQyIsICJEIiwgIkUiIH07CgogICAgICAgIExpc3Q8S3Jhd2Vkej4ga3Jhd2VkemllID0gbmV3IExpc3Q8S3Jhd2Vkej4oKQogICAgICAgIHsKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkIiLCAiQyIsIDEpLAogICAgICAgICAgICBuZXcgS3Jhd2VkeigiQSIsICJDIiwgMiksCiAgICAgICAgICAgIG5ldyBLcmF3ZWR6KCJEIiwgIkUiLCAyKSwKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkEiLCAiQiIsIDQpLAogICAgICAgICAgICBuZXcgS3Jhd2VkeigiQiIsICJEIiwgNSksCiAgICAgICAgICAgIG5ldyBLcmF3ZWR6KCJDIiwgIkQiLCA4KSwKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkMiLCAiRSIsIDEwKQogICAgICAgIH07CgogICAgICAgIExpc3Q8S3Jhd2Vkej4gZHJ6ZXdvID0gS3J1c2thbCh3aWVyemNob2xraSwga3Jhd2VkemllKTsKICAgICAgICBpbnQga29zenRDYWxrb3dpdHkgPSAwOwoKICAgICAgICBmb3JlYWNoIChLcmF3ZWR6IGtyYXdlZHogaW4gZHJ6ZXdvKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoJCJ7a3Jhd2Vkei5acm9kbG99IC0ge2tyYXdlZHouQ2VsfSwgd2FnYToge2tyYXdlZHouV2FnYX0iKTsKICAgICAgICAgICAga29zenRDYWxrb3dpdHkgKz0ga3Jhd2Vkei5XYWdhOwogICAgICAgIH0KCiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIktvc3p0OiAiICsga29zenRDYWxrb3dpdHkpOwogICAgfQp9Cl1dPjwvQ29kZT4KICAgIDwvU25pcHBldD4KICA8L0NvZGVTbmlwcGV0Pgo8L0NvZGVTbmlwcGV0cz4=",
            ["alg_77_drzewo_min.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjcgLSBNaW5pbWFsbmUgZHJ6ZXdvIHJvenBpbmFqxIVjZTwvVGl0bGU+CiAgICAgIDxTaG9ydGN1dD5hbGdfNzdfZHJ6ZXdvX21pbjwvU2hvcnRjdXQ+CiAgICAgIDxEZXNjcmlwdGlvbj5NU1QgdyBqZWRueW0gcHJ6eWvFgmFkemllOiBLcnVza2FsIGkgUHJpbS48L0Rlc2NyaXB0aW9uPgogICAgICA8QXV0aG9yPmJlYXJiaW5lPC9BdXRob3I+CiAgICAgIDxTbmlwcGV0VHlwZXM+CiAgICAgICAgPFNuaXBwZXRUeXBlPkV4cGFuc2lvbjwvU25pcHBldFR5cGU+CiAgICAgIDwvU25pcHBldFR5cGVzPgogICAgPC9IZWFkZXI+CiAgICA8U25pcHBldD4KICAgICAgPENvZGUgTGFuZ3VhZ2U9ImNzaGFycCI+PCFbQ0RBVEFbdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCnB1YmxpYyBjbGFzcyBLcmF3ZWR6CnsKICAgIHB1YmxpYyBzdHJpbmcgWnJvZGxvOwogICAgcHVibGljIHN0cmluZyBDZWw7CiAgICBwdWJsaWMgaW50IFdhZ2E7CgogICAgcHVibGljIEtyYXdlZHooc3RyaW5nIHpyb2Rsbywgc3RyaW5nIGNlbCwgaW50IHdhZ2EpCiAgICB7CiAgICAgICAgWnJvZGxvID0genJvZGxvOwogICAgICAgIENlbCA9IGNlbDsKICAgICAgICBXYWdhID0gd2FnYTsKICAgIH0KfQoKcHVibGljIGNsYXNzIFpiaW9yeVJvemxhY3puZQp7CiAgICBwcml2YXRlIERpY3Rpb25hcnk8c3RyaW5nLCBzdHJpbmc+IHJvZHppYyA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgc3RyaW5nPigpOwoKICAgIHB1YmxpYyB2b2lkIFV0d29yelpiaW9yKHN0cmluZyB3aWVyemNob2xlaykKICAgIHsKICAgICAgICByb2R6aWNbd2llcnpjaG9sZWtdID0gd2llcnpjaG9sZWs7CiAgICB9CgogICAgcHVibGljIHN0cmluZyBabmFqZHooc3RyaW5nIHdpZXJ6Y2hvbGVrKQogICAgewogICAgICAgIGlmIChyb2R6aWNbd2llcnpjaG9sZWtdICE9IHdpZXJ6Y2hvbGVrKQogICAgICAgIHsKICAgICAgICAgICAgcm9kemljW3dpZXJ6Y2hvbGVrXSA9IFpuYWpkeihyb2R6aWNbd2llcnpjaG9sZWtdKTsKICAgICAgICB9CgogICAgICAgIHJldHVybiByb2R6aWNbd2llcnpjaG9sZWtdOwogICAgfQoKICAgIHB1YmxpYyB2b2lkIFBvbGFjeihzdHJpbmcgYSwgc3RyaW5nIGIpCiAgICB7CiAgICAgICAgc3RyaW5nIGtvcnplbkEgPSBabmFqZHooYSk7CiAgICAgICAgc3RyaW5nIGtvcnplbkIgPSBabmFqZHooYik7CgogICAgICAgIGlmIChrb3J6ZW5BICE9IGtvcnplbkIpCiAgICAgICAgewogICAgICAgICAgICByb2R6aWNba29yemVuQl0gPSBrb3J6ZW5BOwogICAgICAgIH0KICAgIH0KfQoKcHVibGljIGNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIExpc3Q8S3Jhd2Vkej4gS3J1c2thbChMaXN0PHN0cmluZz4gd2llcnpjaG9sa2ksIExpc3Q8S3Jhd2Vkej4ga3Jhd2VkemllKQogICAgewogICAgICAgIExpc3Q8S3Jhd2Vkej4gZHJ6ZXdvID0gbmV3IExpc3Q8S3Jhd2Vkej4oKTsKICAgICAgICBaYmlvcnlSb3psYWN6bmUgemJpb3J5ID0gbmV3IFpiaW9yeVJvemxhY3puZSgpOwoKICAgICAgICBmb3JlYWNoIChzdHJpbmcgd2llcnpjaG9sZWsgaW4gd2llcnpjaG9sa2kpCiAgICAgICAgewogICAgICAgICAgICB6YmlvcnkuVXR3b3J6WmJpb3Iod2llcnpjaG9sZWspOwogICAgICAgIH0KCiAgICAgICAgLy8gS3J1c2thbCB6YWN6eW5hIG9kIG5hanRhxYRzenljaCBrcmF3xJlkemkKICAgICAgICBrcmF3ZWR6aWUuU29ydCgoYSwgYikgPT4gYS5XYWdhLkNvbXBhcmVUbyhiLldhZ2EpKTsKCiAgICAgICAgZm9yZWFjaCAoS3Jhd2VkeiBrcmF3ZWR6IGluIGtyYXdlZHppZSkKICAgICAgICB7CiAgICAgICAgICAgIGlmICh6YmlvcnkuWm5hamR6KGtyYXdlZHouWnJvZGxvKSAhPSB6YmlvcnkuWm5hamR6KGtyYXdlZHouQ2VsKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgZHJ6ZXdvLkFkZChrcmF3ZWR6KTsKICAgICAgICAgICAgICAgIHpiaW9yeS5Qb2xhY3ooa3Jhd2Vkei5acm9kbG8sIGtyYXdlZHouQ2VsKTsKICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIGRyemV3bzsKICAgIH0KCiAgICBzdGF0aWMgTGlzdDxLcmF3ZWR6PiBQcmltKERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PiBncmFmLCBzdHJpbmcgc3RhcnQpCiAgICB7CiAgICAgICAgTGlzdDxLcmF3ZWR6PiBkcnpld28gPSBuZXcgTGlzdDxLcmF3ZWR6PigpOwogICAgICAgIEhhc2hTZXQ8c3RyaW5nPiBvZHdpZWR6b25lID0gbmV3IEhhc2hTZXQ8c3RyaW5nPigpOwogICAgICAgIFByaW9yaXR5UXVldWU8S3Jhd2VkeiwgaW50PiBrb2xlamthID0gbmV3IFByaW9yaXR5UXVldWU8S3Jhd2VkeiwgaW50PigpOwoKICAgICAgICAvLyBQcmltIHN0YXJ0dWplIHogamVkbmVnbyB3aWVyemNob8WCa2EKICAgICAgICBvZHdpZWR6b25lLkFkZChzdGFydCk7CgogICAgICAgIGZvcmVhY2ggKEtyYXdlZHoga3Jhd2VkeiBpbiBncmFmW3N0YXJ0XSkKICAgICAgICB7CiAgICAgICAgICAgIGtvbGVqa2EuRW5xdWV1ZShrcmF3ZWR6LCBrcmF3ZWR6LldhZ2EpOwogICAgICAgIH0KCiAgICAgICAgd2hpbGUgKGtvbGVqa2EuQ291bnQgPiAwKQogICAgICAgIHsKICAgICAgICAgICAgS3Jhd2VkeiBha3R1YWxuYSA9IGtvbGVqa2EuRGVxdWV1ZSgpOwoKICAgICAgICAgICAgaWYgKG9kd2llZHpvbmUuQ29udGFpbnMoYWt0dWFsbmEuQ2VsKSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgY29udGludWU7CiAgICAgICAgICAgIH0KCiAgICAgICAgICAgIG9kd2llZHpvbmUuQWRkKGFrdHVhbG5hLkNlbCk7CiAgICAgICAgICAgIGRyemV3by5BZGQoYWt0dWFsbmEpOwoKICAgICAgICAgICAgZm9yZWFjaCAoS3Jhd2VkeiBrcmF3ZWR6IGluIGdyYWZbYWt0dWFsbmEuQ2VsXSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgaWYgKCFvZHdpZWR6b25lLkNvbnRhaW5zKGtyYXdlZHouQ2VsKSkKICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICBrb2xlamthLkVucXVldWUoa3Jhd2Vkeiwga3Jhd2Vkei5XYWdhKTsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQogICAgICAgIH0KCiAgICAgICAgcmV0dXJuIGRyemV3bzsKICAgIH0KCiAgICBzdGF0aWMgRGljdGlvbmFyeTxzdHJpbmcsIExpc3Q8S3Jhd2Vkej4+IFpidWR1akdyYWYoTGlzdDxzdHJpbmc+IHdpZXJ6Y2hvbGtpLCBMaXN0PEtyYXdlZHo+IGtyYXdlZHppZSkKICAgIHsKICAgICAgICBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxLcmF3ZWR6Pj4gZ3JhZiA9IG5ldyBEaWN0aW9uYXJ5PHN0cmluZywgTGlzdDxLcmF3ZWR6Pj4oKTsKCiAgICAgICAgZm9yZWFjaCAoc3RyaW5nIHdpZXJ6Y2hvbGVrIGluIHdpZXJ6Y2hvbGtpKQogICAgICAgIHsKICAgICAgICAgICAgZ3JhZlt3aWVyemNob2xla10gPSBuZXcgTGlzdDxLcmF3ZWR6PigpOwogICAgICAgIH0KCiAgICAgICAgZm9yZWFjaCAoS3Jhd2VkeiBrcmF3ZWR6IGluIGtyYXdlZHppZSkKICAgICAgICB7CiAgICAgICAgICAgIGdyYWZba3Jhd2Vkei5acm9kbG9dLkFkZChuZXcgS3Jhd2VkeihrcmF3ZWR6Llpyb2Rsbywga3Jhd2Vkei5DZWwsIGtyYXdlZHouV2FnYSkpOwogICAgICAgICAgICBncmFmW2tyYXdlZHouQ2VsXS5BZGQobmV3IEtyYXdlZHooa3Jhd2Vkei5DZWwsIGtyYXdlZHouWnJvZGxvLCBrcmF3ZWR6LldhZ2EpKTsKICAgICAgICB9CgogICAgICAgIHJldHVybiBncmFmOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIFd5cGlzekRyemV3byhzdHJpbmcgbmF6d2EsIExpc3Q8S3Jhd2Vkej4gZHJ6ZXdvKQogICAgewogICAgICAgIGludCBrb3N6dENhbGtvd2l0eSA9IDA7CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKG5hendhICsgIjoiKTsKICAgICAgICBmb3JlYWNoIChLcmF3ZWR6IGtyYXdlZHogaW4gZHJ6ZXdvKQogICAgICAgIHsKICAgICAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoa3Jhd2Vkei5acm9kbG8gKyAiIC0gIiArIGtyYXdlZHouQ2VsICsgIiwgd2FnYTogIiArIGtyYXdlZHouV2FnYSk7CiAgICAgICAgICAgIGtvc3p0Q2Fsa293aXR5ICs9IGtyYXdlZHouV2FnYTsKICAgICAgICB9CgogICAgICAgIENvbnNvbGUuV3JpdGVMaW5lKCLFgcSFY3pueSBrb3N6dDogIiArIGtvc3p0Q2Fsa293aXR5KTsKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgpOwogICAgfQoKICAgIHB1YmxpYyBzdGF0aWMgdm9pZCBNYWluKCkKICAgIHsKICAgICAgICBMaXN0PHN0cmluZz4gbWlhc3RhID0gbmV3IExpc3Q8c3RyaW5nPigpIHsgIkEiLCAiQiIsICJDIiwgIkQiLCAiRSIgfTsKCiAgICAgICAgTGlzdDxLcmF3ZWR6PiBwb2xhY3plbmlhID0gbmV3IExpc3Q8S3Jhd2Vkej4oKQogICAgICAgIHsKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkIiLCAiQyIsIDEpLAogICAgICAgICAgICBuZXcgS3Jhd2VkeigiQSIsICJDIiwgMiksCiAgICAgICAgICAgIG5ldyBLcmF3ZWR6KCJEIiwgIkUiLCAyKSwKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkEiLCAiQiIsIDQpLAogICAgICAgICAgICBuZXcgS3Jhd2VkeigiQiIsICJEIiwgNSksCiAgICAgICAgICAgIG5ldyBLcmF3ZWR6KCJDIiwgIkQiLCA4KSwKICAgICAgICAgICAgbmV3IEtyYXdlZHooIkMiLCAiRSIsIDEwKQogICAgICAgIH07CgogICAgICAgIERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PiBncmFmID0gWmJ1ZHVqR3JhZihtaWFzdGEsIHBvbGFjemVuaWEpOwoKICAgICAgICBXeXBpc3pEcnpld28oIktydXNrYWwiLCBLcnVza2FsKG1pYXN0YSwgcG9sYWN6ZW5pYSkpOwogICAgICAgIFd5cGlzekRyemV3bygiUHJpbSIsIFByaW0oZ3JhZiwgIkEiKSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
            ["alg_77_prim.snippet"] = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPENvZGVTbmlwcGV0cyB4bWxucz0iaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS9WaXN1YWxTdHVkaW8vMjAwNS9Db2RlU25pcHBldCI+CiAgPENvZGVTbmlwcGV0IEZvcm1hdD0iMS4wLjAiPgogICAgPEhlYWRlcj4KICAgICAgPFRpdGxlPkxhYiA3LjcgLSBQcmltPC9UaXRsZT4KICAgICAgPFNob3J0Y3V0PmFsZ183N19wcmltPC9TaG9ydGN1dD4KICAgICAgPERlc2NyaXB0aW9uPlByaW0sIG1pbmltYWxuZSBkcnpld28gcm96cGluYWrEhWNlLjwvRGVzY3JpcHRpb24+CiAgICAgIDxBdXRob3I+YmVhcmJpbmU8L0F1dGhvcj4KICAgICAgPFNuaXBwZXRUeXBlcz4KICAgICAgICA8U25pcHBldFR5cGU+RXhwYW5zaW9uPC9TbmlwcGV0VHlwZT4KICAgICAgPC9TbmlwcGV0VHlwZXM+CiAgICA8L0hlYWRlcj4KICAgIDxTbmlwcGV0PgogICAgICA8Q29kZSBMYW5ndWFnZT0iY3NoYXJwIj48IVtDREFUQVsKdXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKCnB1YmxpYyBjbGFzcyBLcmF3ZWR6CnsKICAgIHB1YmxpYyBzdHJpbmcgWnJvZGxvOwogICAgcHVibGljIHN0cmluZyBDZWw7CiAgICBwdWJsaWMgaW50IFdhZ2E7CgogICAgcHVibGljIEtyYXdlZHooc3RyaW5nIHpyb2Rsbywgc3RyaW5nIGNlbCwgaW50IHdhZ2EpCiAgICB7CiAgICAgICAgWnJvZGxvID0genJvZGxvOwogICAgICAgIENlbCA9IGNlbDsKICAgICAgICBXYWdhID0gd2FnYTsKICAgIH0KfQoKY2xhc3MgUHJvZ3JhbQp7CiAgICBzdGF0aWMgTGlzdDxLcmF3ZWR6PiBQcmltKERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PiBncmFmLCBzdHJpbmcgc3RhcnQpCiAgICB7CiAgICAgICAgTGlzdDxLcmF3ZWR6PiBkcnpld28gPSBuZXcgTGlzdDxLcmF3ZWR6PigpOwogICAgICAgIEhhc2hTZXQ8c3RyaW5nPiBvZHdpZWR6b25lID0gbmV3IEhhc2hTZXQ8c3RyaW5nPigpOwogICAgICAgIFByaW9yaXR5UXVldWU8S3Jhd2VkeiwgaW50PiBwcSA9IG5ldyBQcmlvcml0eVF1ZXVlPEtyYXdlZHosIGludD4oKTsKCiAgICAgICAgLy8gc3RhcnR1amVteSBvZCBqZWRuZWdvIHdpZXJ6Y2hvxYJrYSBpIGRvYmllcmFteSBuYWp0YcWEc3plIGtyYXfEmWR6aWUKICAgICAgICBvZHdpZWR6b25lLkFkZChzdGFydCk7CgogICAgICAgIGZvcmVhY2ggKEtyYXdlZHoga3Jhd2VkeiBpbiBncmFmW3N0YXJ0XSkKICAgICAgICB7CiAgICAgICAgICAgIHBxLkVucXVldWUoa3Jhd2Vkeiwga3Jhd2Vkei5XYWdhKTsKICAgICAgICB9CgogICAgICAgIHdoaWxlIChwcS5Db3VudCA+IDApCiAgICAgICAgewogICAgICAgICAgICBLcmF3ZWR6IGN1cnJlbnQgPSBwcS5EZXF1ZXVlKCk7CgogICAgICAgICAgICBpZiAob2R3aWVkem9uZS5Db250YWlucyhjdXJyZW50LkNlbCkpCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGNvbnRpbnVlOwogICAgICAgICAgICB9CgogICAgICAgICAgICBvZHdpZWR6b25lLkFkZChjdXJyZW50LkNlbCk7CiAgICAgICAgICAgIGRyemV3by5BZGQoY3VycmVudCk7CgogICAgICAgICAgICBmb3JlYWNoIChLcmF3ZWR6IGtyYXdlZHogaW4gZ3JhZltjdXJyZW50LkNlbF0pCiAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgIGlmICghb2R3aWVkem9uZS5Db250YWlucyhrcmF3ZWR6LkNlbCkpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgcHEuRW5xdWV1ZShrcmF3ZWR6LCBrcmF3ZWR6LldhZ2EpOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICByZXR1cm4gZHJ6ZXdvOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIE1haW4oKQogICAgewogICAgICAgIERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PiBncmFmID0gbmV3IERpY3Rpb25hcnk8c3RyaW5nLCBMaXN0PEtyYXdlZHo+PigpOwoKICAgICAgICBncmFmWyJBIl0gPSBuZXcgTGlzdDxLcmF3ZWR6PigpIHsgbmV3IEtyYXdlZHooIkEiLCAiQiIsIDQpLCBuZXcgS3Jhd2VkeigiQSIsICJDIiwgMikgfTsKICAgICAgICBncmFmWyJCIl0gPSBuZXcgTGlzdDxLcmF3ZWR6PigpIHsgbmV3IEtyYXdlZHooIkIiLCAiQSIsIDQpLCBuZXcgS3Jhd2VkeigiQiIsICJDIiwgMSksIG5ldyBLcmF3ZWR6KCJCIiwgIkQiLCA1KSB9OwogICAgICAgIGdyYWZbIkMiXSA9IG5ldyBMaXN0PEtyYXdlZHo+KCkgeyBuZXcgS3Jhd2VkeigiQyIsICJBIiwgMiksIG5ldyBLcmF3ZWR6KCJDIiwgIkIiLCAxKSwgbmV3IEtyYXdlZHooIkMiLCAiRCIsIDgpLCBuZXcgS3Jhd2VkeigiQyIsICJFIiwgMTApIH07CiAgICAgICAgZ3JhZlsiRCJdID0gbmV3IExpc3Q8S3Jhd2Vkej4oKSB7IG5ldyBLcmF3ZWR6KCJEIiwgIkIiLCA1KSwgbmV3IEtyYXdlZHooIkQiLCAiQyIsIDgpLCBuZXcgS3Jhd2VkeigiRCIsICJFIiwgMikgfTsKICAgICAgICBncmFmWyJFIl0gPSBuZXcgTGlzdDxLcmF3ZWR6PigpIHsgbmV3IEtyYXdlZHooIkUiLCAiQyIsIDEwKSwgbmV3IEtyYXdlZHooIkUiLCAiRCIsIDIpIH07CgogICAgICAgIExpc3Q8S3Jhd2Vkej4gZHJ6ZXdvID0gUHJpbShncmFmLCAiQSIpOwogICAgICAgIGludCBrb3N6dENhbGtvd2l0eSA9IDA7CgogICAgICAgIGZvcmVhY2ggKEtyYXdlZHoga3Jhd2VkeiBpbiBkcnpld28pCiAgICAgICAgewogICAgICAgICAgICBDb25zb2xlLldyaXRlTGluZSgkIntrcmF3ZWR6Llpyb2Rsb30gLSB7a3Jhd2Vkei5DZWx9LCB3YWdhOiB7a3Jhd2Vkei5XYWdhfSIpOwogICAgICAgICAgICBrb3N6dENhbGtvd2l0eSArPSBrcmF3ZWR6LldhZ2E7CiAgICAgICAgfQoKICAgICAgICBDb25zb2xlLldyaXRlTGluZSgiS29zenQ6ICIgKyBrb3N6dENhbGtvd2l0eSk7CiAgICB9Cn0KXV0+PC9Db2RlPgogICAgPC9TbmlwcGV0PgogIDwvQ29kZVNuaXBwZXQ+CjwvQ29kZVNuaXBwZXRzPg==",
    };




    private string SciezkaZnacznika
    {
        get { return SprzatanieAlgHelper.PlikZnacznika(folderDocelowy); }
    }

    private string SciezkaManifestu
    {
        get { return SprzatanieAlgHelper.PlikManifestu(folderDocelowy); }
    }

    public KontekstTraya()
    {
        string dokumenty = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        folderBazowySnippetow = Path.Combine(
            dokumenty,
            "Visual Studio 2022",
            "Code Snippets",
            "Visual C#",
            "My Code Snippets"
        );

        folderDocelowy = Path.Combine(folderBazowySnippetow, SprzatanieAlgHelper.NazwaFolderu);

        PosprzatajPoPoprzednimUruchomieniu();
        StraznikSprzatania.Start(Environment.ProcessId, folderDocelowy);

        ContextMenuStrip menu = new ContextMenuStrip
        {
            Renderer = new AlgHelperMenuRenderer(),
            BackColor = Color.FromArgb(250, 250, 252),
            ForeColor = Color.FromArgb(32, 36, 44),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            Padding = new Padding(6, 6, 6, 6),
            ShowImageMargin = true
        };

        ToolStripLabel tytulMenu = new ToolStripLabel("AlgHelper 2.0")
        {
            ForeColor = Color.FromArgb(0, 92, 184),
            Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold),
            Padding = new Padding(9, 5, 12, 5),
            TextAlign = ContentAlignment.MiddleLeft
        };

        ToolStripMenuItem instalacja = new ToolStripMenuItem("Zainstaluj / odśwież  (RShift + Backspace)");
        instalacja.Image = WczytajObrazek("AlgHelper.Resources.install.png");
        instalacja.Click += (_, __) => ZainstalujSnippety();

        ToolStripMenuItem folder = new ToolStripMenuItem("Folder snippetów");
        folder.Image = WczytajObrazek("AlgHelper.Resources.folder.png");
        folder.Click += (_, __) => OtworzFolderSnippetow();

        ToolStripMenuItem licznik = new ToolStripMenuItem("Licznik aktywnych");
        licznik.Image = WczytajObrazek("AlgHelper.Resources.count.png");
        licznik.Click += (_, __) => PokazLicznikSnippetow();

        elementPowiadomien = new ToolStripMenuItem("Powiadomienia: wyłączone");
        elementPowiadomien.Image = WczytajObrazek("AlgHelper.Resources.bell.png");
        elementPowiadomien.Click += (_, __) => PrzelaczPowiadomienia();

        ToolStripMenuItem informacje = new ToolStripMenuItem("O AlgHelper");
        informacje.Image = WczytajObrazek("AlgHelper.Resources.about.png");
        informacje.Click += (_, __) => PokazInformacje();

        ToolStripMenuItem wyjscie = new ToolStripMenuItem("Zamknij i posprzątaj");
        wyjscie.Image = WczytajObrazek("AlgHelper.Resources.exit.png");
        wyjscie.Click += (_, __) => ZamknijProgram();

        UstawStylElementu(instalacja);
        UstawStylElementu(folder);
        UstawStylElementu(licznik);
        UstawStylElementu(elementPowiadomien);
        UstawStylElementu(informacje);
        UstawStylElementu(wyjscie);

        menu.Items.Add(tytulMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(instalacja);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(folder);
        menu.Items.Add(licznik);
        menu.Items.Add(elementPowiadomien);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(informacje);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(wyjscie);

        ikonaTraya = new NotifyIcon
        {
            Text = "AlgHelper",
            Icon = WczytajIkone(),
            ContextMenuStrip = menu,
            Visible = true
        };

        ikonaTraya.DoubleClick += (_, __) => PokazLicznikSnippetow();

        // RShift + Backspace: najpierw instaluje snippety, drugi raz zamyka program
        skrotKlawiszowy = new SkrotKlawiszowy(ObsluzSkrotInstalacjaWyjscie);

        Application.ApplicationExit += (_, __) => PosprzatajPrzyWyjsciu(pokazBledy: false);
        AppDomain.CurrentDomain.ProcessExit += (_, __) => PosprzatajPrzyWyjsciu(pokazBledy: false);
    }

    private static void UstawStylElementu(ToolStripMenuItem element)
    {
        element.ForeColor = Color.FromArgb(32, 36, 44);
        element.Margin = new Padding(1, 1, 1, 1);
        element.Padding = new Padding(8, 5, 14, 5);
        element.ImageScaling = ToolStripItemImageScaling.SizeToFit;
    }

    private Icon WczytajIkone()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using (Stream stream = assembly.GetManifestResourceStream("AlgHelper.alghelper.ico"))
        {
            if (stream == null)
            {
                return SystemIcons.Application;
            }

            return (Icon)new Icon(stream).Clone();
        }
    }

    private Bitmap WczytajObrazek(string nazwaZasobu)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using (Stream stream = assembly.GetManifestResourceStream(nazwaZasobu))
        {
            if (stream == null)
            {
                Bitmap zastepczy = new Bitmap(24, 24);

                using (Graphics g = Graphics.FromImage(zastepczy))
                {
                    g.Clear(Color.Transparent);
                    g.FillEllipse(Brushes.DimGray, 3, 3, 18, 18);
                }

                return zastepczy;
            }

            using (Bitmap obrazek = new Bitmap(stream))
            {
                return new Bitmap(obrazek);
            }
        }
    }

    private void Powiadom(string tytul, string tekst, ToolTipIcon ikona)
    {
        if (!powiadomieniaWlaczone)
        {
            return;
        }

        ikonaTraya.ShowBalloonTip(2500, tytul, tekst, ikona);
    }

    private void PrzelaczPowiadomienia()
    {
        powiadomieniaWlaczone = !powiadomieniaWlaczone;
        elementPowiadomien.Text = powiadomieniaWlaczone
            ? "Powiadomienia: włączone"
            : "Powiadomienia: wyłączone";

        if (powiadomieniaWlaczone)
        {
            ikonaTraya.ShowBalloonTip(
                1800,
                "AlgHelper",
                "Powiadomienia zostały włączone.",
                ToolTipIcon.Info
            );
        }
    }

    private void PosprzatajPoPoprzednimUruchomieniu()
    {
        try
        {
            SprzatanieAlgHelper.UsunFolderProgramu(folderDocelowy);
        }
        catch
        {
            // bez dramatu, przy nastepnym zamknieciu program sprobuje jeszcze raz
        }
    }

    private void ZapiszPlikiWlasnosci()
    {
        // marker jest glownym dowodem ze folder jest od AlgHelper
        File.WriteAllText(
            SciezkaZnacznika,
            "Folder tymczasowy AlgHelper " + wersja + ". Mozna usunac przy sprzataniu." + Environment.NewLine
        );

        List<string> linieManifestu = new List<string>(Snippety.Keys);
        linieManifestu.Sort(StringComparer.OrdinalIgnoreCase);
        File.WriteAllLines(SciezkaManifestu, linieManifestu);
    }

    private void ZainstalujSnippety(bool pokazKomunikat = true)
    {
        try
        {
            // folder AlgHelper jest roboczy, po zamknieciu ma zniknac caly
            SprzatanieAlgHelper.UsunFolderProgramu(folderDocelowy);
            Directory.CreateDirectory(folderDocelowy);
            ZapiszPlikiWlasnosci();

            int zainstalowane = 0;

            foreach (var snippet in Snippety)
            {
                string sciezkaPliku = Path.Combine(folderDocelowy, snippet.Key);
                byte[] dane = Convert.FromBase64String(snippet.Value);
                File.WriteAllBytes(sciezkaPliku, dane);
                zainstalowane++;
            }

            Powiadom(
                "AlgHelper",
                "Aktywne snippety: " + zainstalowane + ".",
                ToolTipIcon.Info
            );

            if (pokazKomunikat && !powiadomieniaWlaczone)
            {
                MessageBox.Show(
                    "Zainstalowano/odświeżono snippetów: " + zainstalowane + ".",
                    "AlgHelper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Nie udało się zainstalować snippetów.\n\n" + ex.Message,
                "Błąd",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private bool CzySnippetyZainstalowane()
    {
        if (!Directory.Exists(folderDocelowy) || !File.Exists(SciezkaZnacznika))
        {
            return false;
        }

        foreach (string nazwaPliku in Snippety.Keys)
        {
            if (File.Exists(Path.Combine(folderDocelowy, nazwaPliku)))
            {
                return true;
            }
        }

        return false;
    }

    private void ObsluzSkrotInstalacjaWyjscie()
    {
        if (sprzatanieTrwa)
        {
            return;
        }

        if (CzySnippetyZainstalowane())
        {
            ZamknijProgram();
            return;
        }

        ZainstalujSnippety(pokazKomunikat: false);
    }

    private void OtworzFolderSnippetow()
    {
        try
        {
            Directory.CreateDirectory(folderBazowySnippetow);

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = folderBazowySnippetow,
                UseShellExecute = true
            };

            Process.Start(info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Nie udało się otworzyć folderu snippetów.\n\n" + ex.Message,
                "Błąd",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void PokazLicznikSnippetow()
    {
        int liczba = 0;

        if (Directory.Exists(folderDocelowy))
        {
            foreach (string nazwaPliku in Snippety.Keys)
            {
                if (File.Exists(Path.Combine(folderDocelowy, nazwaPliku)))
                {
                    liczba++;
                }
            }
        }

        string infoFolderu = Directory.Exists(folderDocelowy)
            ? folderDocelowy
            : folderDocelowy + "\n(folder AlgHelper jeszcze nie istnieje)";

        MessageBox.Show(
            "Folder:\n" + infoFolderu + "\n\nZainstalowane snippety AlgHelper: " + liczba,
            "AlgHelper",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void PokazInformacje()
    {
        using (AboutForm form = new AboutForm(
            WczytajObrazek("AlgHelper.Resources.author.png"),
            githubUrl,
            wersja
        ))
        {
            form.ShowDialog();
        }
    }

    private void ZamknijProgram()
    {
        skrotKlawiszowy?.Dispose();
        skrotKlawiszowy = null;

        PosprzatajPrzyWyjsciu();

        ikonaTraya.Visible = false;
        ikonaTraya.Dispose();
        Application.Exit();
    }

    private void PosprzatajPrzyWyjsciu(bool pokazBledy = true)
    {
        if (sprzatanieZrobione || sprzatanieTrwa)
        {
            return;
        }

        sprzatanieTrwa = true;

        try
        {
            SprzatanieAlgHelper.UsunFolderProgramu(folderDocelowy);
            sprzatanieZrobione = true;
        }
        catch (Exception ex)
        {
            if (pokazBledy)
            {
                MessageBox.Show(
                    "Nie udało się usunąć folderu AlgHelper przy zamykaniu.\n\n" + ex.Message,
                    "AlgHelper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
        finally
        {
            sprzatanieTrwa = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            skrotKlawiszowy?.Dispose();
            skrotKlawiszowy = null;
            PosprzatajPrzyWyjsciu();
            ikonaTraya.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class AlgHelperMenuRenderer : ToolStripProfessionalRenderer
{
    public AlgHelperMenuRenderer()
        : base(new AlgHelperColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);

        if (e.Item.Selected)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(229, 241, 255)))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (SolidBrush accent = new SolidBrush(Color.FromArgb(0, 120, 212)))
            {
                e.Graphics.FillRectangle(accent, 0, 0, 3, bounds.Height);
            }
        }
        else
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(250, 250, 252)))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(241, 246, 252)))
        {
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;

        using (Pen pen = new Pen(Color.FromArgb(218, 225, 233)))
        {
            e.Graphics.DrawLine(pen, 34, y, e.Item.Width - 8, y);
        }
    }
}

internal sealed class AlgHelperColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground
    {
        get { return Color.FromArgb(250, 250, 252); }
    }

    public override Color ImageMarginGradientBegin
    {
        get { return Color.FromArgb(241, 246, 252); }
    }

    public override Color ImageMarginGradientMiddle
    {
        get { return Color.FromArgb(241, 246, 252); }
    }

    public override Color ImageMarginGradientEnd
    {
        get { return Color.FromArgb(241, 246, 252); }
    }

    public override Color MenuItemSelected
    {
        get { return Color.FromArgb(229, 241, 255); }
    }

    public override Color MenuItemBorder
    {
        get { return Color.FromArgb(0, 120, 212); }
    }

    public override Color SeparatorDark
    {
        get { return Color.FromArgb(218, 225, 233); }
    }
}

internal sealed class AboutForm : Form
{
    private readonly string githubUrl;

    public AboutForm(Image authorImage, string githubUrl, string version)
    {
        this.githubUrl = githubUrl;

        Text = "O autorze";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 185);
        BackColor = Color.FromArgb(245, 245, 247);
        ForeColor = Color.FromArgb(30, 30, 34);

        PictureBox picture = new PictureBox
        {
            Image = authorImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(22, 30),
            Size = new Size(96, 96)
        };

        Label title = new Label
        {
            Text = "AlgHelper",
            ForeColor = Color.FromArgb(20, 20, 24),
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(140, 24)
        };

        Label versionLabel = new Label
        {
            Text = "Wersja: " + version,
            ForeColor = Color.FromArgb(70, 70, 78),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(143, 58)
        };

        Label author = new Label
        {
            Text = "Autor: bearbine",
            ForeColor = Color.FromArgb(45, 45, 52),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(143, 84)
        };

        LinkLabel link = new LinkLabel
        {
            Text = githubUrl,
            LinkColor = Color.FromArgb(0, 102, 204),
            ActiveLinkColor = Color.FromArgb(0, 70, 160),
            VisitedLinkColor = Color.FromArgb(0, 102, 204),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(143, 112)
        };

        link.Click += (_, __) =>
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = githubUrl,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się otworzyć linku.\n\n" + ex.Message,
                    "Błąd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        };

        Button okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(333, 145),
            Size = new Size(78, 28),
            UseVisualStyleBackColor = true
        };

        Controls.Add(picture);
        Controls.Add(title);
        Controls.Add(versionLabel);
        Controls.Add(author);
        Controls.Add(link);
        Controls.Add(okButton);

        AcceptButton = okButton;
    }
}

