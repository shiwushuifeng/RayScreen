using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class DispCtl
{
    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct SRC_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    struct TGT_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_INFO { public SRC_INFO sourceInfo; public TGT_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] payload; }

    [StructLayout(LayoutKind.Sequential)]
    struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr currentTopologyId);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME info);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPath, [In] PATH_INFO[] paths, uint numMode, [In] MODE_INFO[] modes, uint flags);

    const uint QDC_ALL_PATHS = 0x1;
    const uint SDC_TOPOLOGY_EXTEND = 0x1, SDC_TOPOLOGY_CLONE = 0x2, SDC_TOPOLOGY_EXTERNAL = 0x8, SDC_TOPOLOGY_INTERNAL = 0x10;
    const uint SDC_APPLY = 0x80, SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20, SDC_ALLOW_CHANGES = 0x400, SDC_SAVE_TO_DATABASE = 0x200;
    const uint PATH_ACTIVE = 0x1;

    static string logPath = @"C:\DSH NEXT\develop\RayScreen\tests\dispctl.log";
    static void L(string s) { try { File.AppendAllText(logPath, s + Environment.NewLine); } catch { } Console.WriteLine(s); }

    static string TargetName(TGT_INFO t)
    {
        TARGET_DEVICE_NAME n = new TARGET_DEVICE_NAME();
        n.header.type = 2; // TARGET_DEVICE_NAME
        n.header.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
        n.header.adapterId = t.adapterId;
        n.header.id = t.id;
        int rc = DisplayConfigGetDeviceInfo(ref n);
        if (rc != 0) return "?rc=" + rc;
        return n.monitorFriendlyDeviceName;
    }

    static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "list";
        uint np, nm;
        int rc = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out np, out nm);
        if (rc != 0) { L("GetDisplayConfigBufferSizes rc=" + rc); return 1; }
        PATH_INFO[] paths = new PATH_INFO[np];
        MODE_INFO[] modes = new MODE_INFO[nm];
        rc = QueryDisplayConfig(QDC_ALL_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
        if (rc != 0) { L("QueryDisplayConfig rc=" + rc); return 1; }
        L("paths=" + np + " modes=" + nm);

        if (cmd == "list")
        {
            for (int i = 0; i < np; i++)
            {
                if ((paths[i].flags & PATH_ACTIVE) == 0 && !paths[i].targetInfo.targetAvailable) continue;
                L(string.Format("  [{0}] active={1} targetAvail={2} src={3} tgt={4} name={5}", i,
                    (paths[i].flags & PATH_ACTIVE) != 0, paths[i].targetInfo.targetAvailable,
                    paths[i].sourceInfo.id, paths[i].targetInfo.id, TargetName(paths[i].targetInfo)));
            }
            return 0;
        }

        if (cmd == "extend")
        {
            rc = SetDisplayConfig(0, null, 0, null, SDC_TOPOLOGY_EXTEND | SDC_APPLY);
            L("extend rc=" + rc);
            return rc == 0 ? 0 : 2;
        }

        if (cmd == "external")
        {
            rc = SetDisplayConfig(0, null, 0, null, SDC_TOPOLOGY_EXTERNAL | SDC_APPLY);
            L("external rc=" + rc);
            return rc == 0 ? 0 : 2;
        }

        if (cmd == "internal")
        {
            rc = SetDisplayConfig(0, null, 0, null, SDC_TOPOLOGY_INTERNAL | SDC_APPLY);
            L("internal rc=" + rc);
            return rc == 0 ? 0 : 2;
        }

        if (cmd == "vddonly" || cmd == "intonly")
        {
            PATH_INFO[] keep = new PATH_INFO[np];
            int count = 0;
            for (int i = 0; i < (int)np; i++)
            {
                if ((paths[i].flags & PATH_ACTIVE) == 0) continue;
                string tn = TargetName(paths[i].targetInfo);
                bool isVdd = tn.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0;
                if (cmd == "vddonly" && !isVdd) continue;
                if (cmd == "intonly" && isVdd) continue;
                PATH_INFO p2 = paths[i];
                p2.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
                p2.targetInfo.modeInfoIdx = 0xFFFFFFFF;
                keep[count] = p2;
                count++;
                L("keeping path " + i + " " + tn);
            }
            L(cmd + ": supplying " + count + " active paths");
            rc = SetDisplayConfig((uint)count, keep, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY | SDC_SAVE_TO_DATABASE);
            L(cmd + " rc=" + rc);
            return rc == 0 ? 0 : 3;
        }

        if (cmd == "vddkeep")
        {
            PATH_INFO[] keep = new PATH_INFO[1];
            int k = 0;
            for (int i = 0; i < (int)np; i++)
            {
                if ((paths[i].flags & PATH_ACTIVE) == 0) continue;
                string tn2 = TargetName(paths[i].targetInfo);
                if (tn2.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                keep[0] = paths[i];
                k = 1;
                L("keep " + tn2 + " srcModeIdx=" + paths[i].sourceInfo.modeInfoIdx + " tgtModeIdx=" + paths[i].targetInfo.modeInfoIdx);
            }
            L("vddkeep: paths=" + k + " modes=" + nm);
            rc = SetDisplayConfig((uint)k, keep, nm, modes, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
            L("vddkeep rc=" + rc);
            return rc == 0 ? 0 : 3;
        }

        if (cmd == "intforce")
        {
            PATH_INFO[] keep = new PATH_INFO[1];
            int k = 0;
            for (int i = 0; i < (int)np; i++)
            {
                if (!paths[i].targetInfo.targetAvailable) continue;
                string tn3 = TargetName(paths[i].targetInfo);
                if (tn3.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                PATH_INFO p3 = paths[i];
                p3.flags |= PATH_ACTIVE;
                p3.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
                p3.targetInfo.modeInfoIdx = 0xFFFFFFFF;
                keep[0] = p3;
                k = 1;
                L("intforce target " + tn3 + " src=" + p3.sourceInfo.id + " tgt=" + p3.targetInfo.id);
                break;
            }
            L("intforce: paths=" + k);
            rc = SetDisplayConfig((uint)k, keep, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
            L("intforce rc=" + rc);
            return rc == 0 ? 0 : 3;
        }

        if (cmd == "cycle")
        {
            // save current active config
            PATH_INFO[] saved = new PATH_INFO[np];
            int savedCount = 0;
            for (int i = 0; i < (int)np; i++) if ((paths[i].flags & PATH_ACTIVE) != 0) { saved[savedCount++] = paths[i]; }
            MODE_INFO[] savedModes = new MODE_INFO[nm];
            Array.Copy(modes, savedModes, nm);
            L("saved paths=" + savedCount + " modes=" + nm);

            // switch to VDD only
            PATH_INFO[] only = new PATH_INFO[1];
            int k = 0;
            for (int i = 0; i < (int)np; i++)
            {
                if (!paths[i].targetInfo.targetAvailable) continue;
                string tn4 = TargetName(paths[i].targetInfo);
                if (tn4.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                PATH_INFO p4 = paths[i];
                p4.flags |= PATH_ACTIVE;
                p4.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
                p4.targetInfo.modeInfoIdx = 0xFFFFFFFF;
                only[0] = p4; k = 1;
                L("switch to " + tn4);
                break;
            }
            rc = SetDisplayConfig((uint)k, only, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
            L("switch rc=" + rc);
            System.Threading.Thread.Sleep(3000);
            rc = SetDisplayConfig((uint)savedCount, saved, nm, savedModes, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
            L("restore rc=" + rc);
            return 0;
        }

        if (cmd == "activate") // activate every available target (including VDD) by extending paths
        {
            int max = (int)np + 8;
            PATH_INFO[] newPaths = new PATH_INFO[max];
            int count = 0;
            for (int i = 0; i < (int)np; i++)
            {
                if ((paths[i].flags & PATH_ACTIVE) != 0)
                {
                    newPaths[count] = paths[i];
                    count++;
                    continue;
                }
                if (!paths[i].targetInfo.targetAvailable) continue;
                PATH_INFO p = paths[i];
                p.flags |= PATH_ACTIVE;
                p.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
                p.targetInfo.modeInfoIdx = 0xFFFFFFFF;
                newPaths[count] = p;
                count++;
            }
            L("activate: supplying " + count + " active paths");
            rc = SetDisplayConfig((uint)count, newPaths, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
            L("activate rc=" + rc);
            return rc == 0 ? 0 : 3;
        }

        L("unknown command " + cmd);
        return 9;
    }
}
