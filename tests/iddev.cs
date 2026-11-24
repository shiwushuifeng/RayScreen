using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

static class IddDev
{
    const uint DICD_GENERATE_ID = 0x01;
    const uint SPDRP_HARDWAREID = 0x01;
    const uint INSTALLFLAG_FORCE = 0x1;
    const uint INSTALLFLAG_NONINTERACTIVE = 0x4;
    const int DIF_REGISTERDEVICE = 0x19;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetINFClassW(string InfName, out Guid ClassGuid, StringBuilder ClassName, int ClassNameSize, out int RequiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCreateDeviceInfoW(IntPtr DeviceInfoSet, string DeviceName, ref Guid ClassGuid, string DeviceDescription, IntPtr hwndParent, uint CreationFlags, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, byte[] PropertyBuffer, uint PropertyBufferSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwndParent, string HardwareId, string FullInfPath, uint InstallFlags, out bool bRebootRequired);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    static StreamWriter log;
    static void L(string s) { Console.WriteLine(s); log.WriteLine(s); log.Flush(); }

    static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        log = new StreamWriter(Path.Combine(dir, "iddev.log"), false, Encoding.UTF8);

        string inf = @"C:\Windows\System32\DriverStore\FileRepository\gamevieweridddriver.inf_amd64_9586929572de2d1b\gamevieweridddriver.inf";
        string hwid = @"Root\GameViewerIddDriver";

        Guid classGuid; StringBuilder cls = new StringBuilder(256); int need;
        L("INF class ok=" + SetupDiGetINFClassW(inf, out classGuid, cls, cls.Capacity, out need) + " guid=" + classGuid + " cls=" + cls);

        string[] names = { @"ROOT\GameViewerIddDriver", @"Root\GameViewerIddDriver", @"ROOT\GameViewerIddDriver\0000", "GameViewerIddDriver" };
        bool anyCreated = false;
        foreach (string nm in names)
        {
            IntPtr devInfo = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            SP_DEVINFO_DATA did = new SP_DEVINFO_DATA();
            did.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
            bool created = SetupDiCreateDeviceInfoW(devInfo, nm, ref classGuid, "RayScreen Virtual Display", IntPtr.Zero, DICD_GENERATE_ID, ref did);
            int err = Marshal.GetLastWin32Error();
            L(string.Format("create[{0}] = {1} err=0x{2:X8}", nm, created, err));
            if (created)
            {
                byte[] ids = Encoding.Unicode.GetBytes(hwid + "\0\0");
                L("  setHwid=" + SetupDiSetDeviceRegistryPropertyW(devInfo, ref did, SPDRP_HARDWAREID, ids, (uint)ids.Length) + " err=0x" + Marshal.GetLastWin32Error().ToString("X8"));
                L("  register=" + SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfo, ref did) + " err=0x" + Marshal.GetLastWin32Error().ToString("X8"));
                anyCreated = true;
            }
            SetupDiDestroyDeviceInfoList(devInfo);
            if (anyCreated) break;
        }

        if (!anyCreated) { L("no device created"); log.Close(); return 1; }

        bool reboot;
        bool ok = UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hwid, inf, INSTALLFLAG_FORCE | INSTALLFLAG_NONINTERACTIVE, out reboot);
        L("UpdateDriver=" + ok + " err=0x" + Marshal.GetLastWin32Error().ToString("X8") + " reboot=" + reboot);
        log.Close();
        return ok ? 0 : 2;
    }
}
