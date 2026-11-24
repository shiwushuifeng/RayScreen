using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class VddInstall
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetINFClassW(string infName, ref Guid classGuid, StringBuilder className, uint classNameSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string deviceName, ref Guid classGuid, string description, IntPtr hwndParent, uint creationFlags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property, byte[] buffer, uint size);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwnd, string hardwareId, string infPath, uint flags, out bool reboot);

    const uint DIF_REGISTERDEVICE = 0x19;
    const uint SPDRP_HARDWAREID = 0x1;
    const uint DICD_GENERATE_ID = 0x1;
    const uint INSTALLFLAG_FORCE = 0x1;

    static string logPath = @"C:\DSH NEXT\develop\RayScreen\tests\vdd-install.log";

    static void L(string s)
    {
        try { File.AppendAllText(logPath, s + Environment.NewLine); } catch { }
        Console.WriteLine(s);
    }

    static string MultiSz(string s)
    {
        return s + "\0\0";
    }

    static int Main(string[] args)
    {
        string dir = @"C:\DSH NEXT\develop\RayScreen\vdd\VirtualDisplayDriver";
        string inf = Path.Combine(dir, "MttVDD.inf");
        string hwid = @"Root\MttVDD";

        L("dir  = " + dir);
        L("inf  = " + inf + (File.Exists(inf) ? " (found)" : " (MISSING)"));

        if (!File.Exists(inf)) return 2;

        // 1) stage the driver package with pnputil (validates signature)
        try
        {
            Process p = Process.Start(new ProcessStartInfo("pnputil.exe", "/add-driver \"" + inf + "\" /install")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            L("pnputil exit=" + p.ExitCode);
            L("pnputil output: " + o.Trim().Replace("\r\n", " | "));
        }
        catch (Exception e) { L("pnputil failed: " + e.Message); }

        // 2) create a root-enumerated device node
        Guid classGuid = Guid.Empty;
        StringBuilder className = new StringBuilder(64);
        uint required;
        if (!SetupDiGetINFClassW(inf, ref classGuid, className, 64, out required))
        {
            L("SetupDiGetINFClass failed " + Marshal.GetLastWin32Error());
            return 3;
        }
        L("class = " + className + " {" + classGuid + "}");

        IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            L("SetupDiCreateDeviceInfoList failed " + Marshal.GetLastWin32Error());
            return 4;
        }

        SP_DEVINFO_DATA did = new SP_DEVINFO_DATA();
        did.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

        if (!SetupDiCreateDeviceInfoW(set, "MttVDD", ref classGuid, "Virtual Display Driver", IntPtr.Zero, DICD_GENERATE_ID, ref did))
        {
            L("SetupDiCreateDeviceInfo failed " + Marshal.GetLastWin32Error());
            SetupDiDestroyDeviceInfoList(set);
            return 5;
        }
        L("device info created");

        byte[] hwidBytes = Encoding.Unicode.GetBytes(MultiSz(hwid));
        if (!SetupDiSetDeviceRegistryPropertyW(set, ref did, SPDRP_HARDWAREID, hwidBytes, (uint)hwidBytes.Length))
        {
            L("SetupDiSetDeviceRegistryProperty failed " + Marshal.GetLastWin32Error());
            SetupDiDestroyDeviceInfoList(set);
            return 6;
        }
        L("hardware id set");

        if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref did))
        {
            L("DIF_REGISTERDEVICE failed " + Marshal.GetLastWin32Error());
            SetupDiDestroyDeviceInfoList(set);
            return 7;
        }
        L("device registered");

        SetupDiDestroyDeviceInfoList(set);

        bool reboot;
        if (!UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hwid, inf, INSTALLFLAG_FORCE, out reboot))
        {
            L("UpdateDriverForPlugAndPlayDevices failed " + Marshal.GetLastWin32Error());
            return 8;
        }
        L("driver installed, reboot required = " + reboot);

        return 0;
    }
}
