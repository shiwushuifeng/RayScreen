using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

static class DispOffProbe2
{
    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint numPath, IntPtr paths, uint numMode, IntPtr modes, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr param);

    const uint SDC_APPLY = 0x00000080;
    const uint SDC_ALLOW_CHANGES = 0x00000400;
    const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    const uint SDC_TOPOLOGY_INTERNAL = 0x00000010;

    static void Restore()
    {
        int r = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_TOPOLOGY_INTERNAL | SDC_APPLY);
        Console.WriteLine("restore rc=" + r);
        if (r != 0)
        {
            Process.Start(new ProcessStartInfo("DisplaySwitch.exe", "/internal") { UseShellExecute = false, CreateNoWindow = true });
            Console.WriteLine("fallback DisplaySwitch /internal");
        }
    }

    static void Main()
    {
        Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 30 >nul & DisplaySwitch.exe /internal")
        { UseShellExecute = false, CreateNoWindow = true });

        int a1 = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | SDC_ALLOW_CHANGES);
        Console.WriteLine("apply(ALLOW_CHANGES) rc=" + a1);
        if (a1 == 0)
        {
            Thread.Sleep(5000);
            Restore();
            Console.WriteLine("DONE");
            return;
        }

        int a2 = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        Console.WriteLine("apply(+SAVE_TO_DB) rc=" + a2);
        if (a2 == 0)
        {
            Thread.Sleep(5000);
            Restore();
            Console.WriteLine("DONE");
            return;
        }

        int d = ChangeDisplaySettingsEx(@"\\.\DISPLAY1", IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        Console.WriteLine("ChangeDisplaySettingsEx(detach) rc=" + d);
        if (d == 0)
        {
            Thread.Sleep(5000);
            int back = ChangeDisplaySettingsEx(@"\\.\DISPLAY1", IntPtr.Zero, IntPtr.Zero, 0x00000001 /*CDS_UPDATEREGISTRY*/, IntPtr.Zero);
            Console.WriteLine("ChangeDisplaySettingsEx(reattach) rc=" + back);
            Restore();
        }
        Console.WriteLine("DONE");
    }
}
