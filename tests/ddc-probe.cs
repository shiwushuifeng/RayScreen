using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

static class DdcProbe
{
    delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("dxva2.dll")]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll")]
    static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMon, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll")]
    static extern bool DestroyPhysicalMonitor(IntPtr hMon);

    static List<IntPtr> monitors = new List<IntPtr>();

    static bool Callback(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data)
    {
        monitors.Add(hMon);
        return true;
    }

    static void Main()
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        Console.WriteLine("显示输出数量: " + monitors.Count);
        int index = 0;
        foreach (IntPtr hMon in monitors)
        {
            index++;
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count))
            {
                Console.WriteLine("  显示器 " + index + ": 取物理显示器失败");
                continue;
            }
            PHYSICAL_MONITOR[] arr = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, arr))
            {
                Console.WriteLine("  显示器 " + index + ": 取物理显示器句柄失败");
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                uint type, cur, max;
                bool ok = GetVCPFeatureAndVCPFeatureReply(arr[i].hPhysicalMonitor, 0xD6, out type, out cur, out max);
                Console.WriteLine(string.Format("  显示器 {0}[{1}] {2}  DDC电源(VCP D6): {3}",
                    index, i, arr[i].szPhysicalMonitorDescription, ok ? ("当前=" + cur) : "不支持/无响应"));
                uint b;
                bool ok2 = GetVCPFeatureAndVCPFeatureReply(arr[i].hPhysicalMonitor, 0x10, out type, out b, out max);
                Console.WriteLine(string.Format("         亮度(VCP 10): {0}", ok2 ? ("当前=" + b + " 最大=" + max) : "不支持/无响应"));
                DestroyPhysicalMonitor(arr[i].hPhysicalMonitor);
            }
        }
    }
}
