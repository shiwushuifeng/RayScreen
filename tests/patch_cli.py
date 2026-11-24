import io

p = r"C:\DSH NEXT\develop\RayScreen\src\RayScreen.cs"
s = io.open(p, encoding="utf-8").read()

# 1) add RecoverInternal to VScreen
anchor = """    /// <summary>恢复切换前的显示配置（内屏重新点亮）。</summary>"""
recover = """    /// <summary>兜底恢复：在没有保存配置时，直接重新点亮内屏（用于程序异常退出后的抢救）。</summary>
    public static bool RecoverInternal()
    {
        PATH_INFO[] pa; MODE_INFO[] ma; uint np, nm;
        if (!Query(out pa, out ma, out np, out nm)) return false;
        PATH_INFO only = new PATH_INFO();
        bool found = false;
        for (int i = 0; i < (int)np; i++)
        {
            if (!pa[i].targetInfo.targetAvailable) continue;
            string nm2 = NameOf(pa[i].targetInfo);
            if (nm2.Length == 0) continue;
            if (nm2.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            only = pa[i];
            only.flags |= PATH_ACTIVE;
            only.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
            only.targetInfo.modeInfoIdx = 0xFFFFFFFF;
            found = true;
            break;
        }
        if (!found) return false;
        PATH_INFO[] arr = new PATH_INFO[] { only };
        int rc = SetDisplayConfig(1, arr, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
        if (rc == 0) { Switched = false; return true; }
        return false;
    }

"""
assert anchor in s
s = s.replace(anchor, recover + anchor, 1)

# 2) CLI: patch the switch block
old = """        bool realOff = screenOff && Power.ConnectedStandbyDisabled();
        if (screenOff)
        {
            if (realOff) Power.MonitorOff();
            else Blanker.Blank();
        }"""
new = """        bool realOff = false;
        bool vScreen = false;
        if (screenOff)
        {
            string verr = null;
            if (VScreen.Available && VScreen.SwitchOff(out verr))
            {
                vScreen = true;
                Line("  屏幕：已切到虚拟屏（内屏断电），按任意键或用 --screen-on 恢复");
            }
            else
            {
                realOff = Power.ConnectedStandbyDisabled();
                if (realOff) Power.MonitorOff();
                else Blanker.Blank();
            }
        }"""
assert old in s
s = s.replace(old, new, 1)

old2 = """            if (screenOff)
            {
                if (realOff) Power.MonitorOn();
                else Blanker.Unblank();
            }"""
new2 = """            if (screenOff)
            {
                if (vScreen) VScreen.Restore();
                else if (realOff) Power.MonitorOn();
                else Blanker.Unblank();
            }"""
assert old2 in s
s = s.replace(old2, new2, 1)

# 3) parse --screen-on
old3 = """            if (a == "--screen-off" || a == "-s") screenOff = true;"""
new3 = """            if (a == "--screen-off" || a == "-s") screenOff = true;
            else if (a == "--screen-on" || a == "-o") screenOn = true;"""
assert old3 in s
s = s.replace(old3, new3, 1)

# 4) declare screenOn
old4 = "        bool screenOff = false;"
new4 = "        bool screenOff = false;\n        bool screenOn = false;"
assert old4 in s
s = s.replace(old4, new4, 1)

# 5) handle --screen-on early
old5 = """        if (selfTest)
            return RunSelfTest();"""
new5 = """        if (screenOn)
        {
            VScreen.Probe();
            bool ok = VScreen.RecoverInternal();
            Line(ok ? "  已重新点亮内屏。" : "  [!] 恢复内屏失败（需要管理员权限或已无可用内屏）。");
            return ok ? 0 : 5;
        }

        if (selfTest)
            return RunSelfTest();"""
assert old5 in s
s = s.replace(old5, new5, 1)

io.open(p, "w", encoding="utf-8").write(s)
print("patched CLI ok")
