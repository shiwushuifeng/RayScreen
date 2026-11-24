# AGENTS.md — 给接手的 AI / 开发者

这是一份**交接文档**：读完这一页就能安全地改代码、跑测试、重新打包发布。人类向的介绍看 [README.md](README.md)。

---

## 1. 这个项目在解决什么

目标：Windows 上跑长任务时**只关屏幕、不休眠**。

难点在于这台机器（联想/机械革命笔记本，NVIDIA RTX 5070 Ti Laptop + AMD 610M，Windows 11 `10.0.26300`）是 **S0 现代待机（Modern Standby）** 机型：

> **任何「让 Windows 认为屏幕关了」的操作，都会立刻触发连接待机**（风扇停、任务冻结、内核事件 `Kernel-Power 506 原因: SC_MONITORPOWER` / `Input Keyboard` 唤醒 507）。

已经**实测无效**的手段（不要重复尝试，浪费时间）：

| 手段 | 结果 |
| --- | --- |
| `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` | 只防睡眠，关屏仍进待机 |
| 加 `ES_DISPLAY_REQUIRED` | 仍进待机（实测进程被冻结 >120s） |
| Away Mode（`ES_AWAYMODE_REQUIRED`） | 无效 |
| `PowerCreateRequest` + System/ExecutionRequired | 无效 |
| 隐藏电源项 `UNATTENDSLEEP` 设 0 | 无效（默认 120s，改了仍睡） |
| 注册表 `PlatformAoAcOverride=0` | 有效但**需重启**才生效，当前机器不能随便重启 |
| 合盖（LIDACTION=0） | 硬件/EC 仍走 S0，用户实测会睡 |
| DDC/CI（`SetVCPFeature` 0xD6） | 显示器不支持，`GetVCPFeatureAndVCPFeatureReply` 无响应 |
| WMI 亮度 `WmiSetBrightness` 设 0 →  | 值能改，但面板背光不变（假值） |
| `SetDisplayConfig(0,NULL,0,NULL,SDC_APPLY)` | `rc=87`，系统不允许没有任何活动显示输出 |
| 复用 UU 远程的 `GameViewer Virtual Display Adapter` | 建屏由 UU 用户态软件私有接口触发，`SetupDiCreateDeviceInfo` 一律 `0xE0000205` |

**最终方案**：装一块虚拟显示器（VDD），把桌面切到它上面，物理内屏失去信号 → 面板自己断电（背光真灭）；Windows 认为屏幕一切正常，因此不进待机。

---

## 2. 代码结构

单文件 `src/RayScreen.cs`（约 1330 行），用 `#if CLI` 分出图形版/命令行版两个入口，`build.cmd` 编译两次。

| 类型 | 作用 |
| --- | --- |
| `Native` | P/Invoke 汇总（`SetThreadExecutionState`、`SendMessage`、`CommandLineToArgvW`、钩子等） |
| `Power` | 保活：`KeepAwake()` / `Release()` / `Reassert()`；记录 `DisplayRequiredActive` 等状态 |
| `Brightness` | WMI 亮度（`root\WMI` `WmiMonitorBrightness`），保留作旧方案回退 |
| `OverlayForm` / `Blanker` | 旧方案：全黑置顶窗口盖屏（没有 VDD 时回退用） |
| `VScreen` | **核心**：`SwitchOff(out err)` 只保留名字含 `VDD` 的活动显示路径；`Restore()` 按保存的配置还原；`RecoverInternal()` 点亮第一块非 VDD 屏 |
| `WakeHook` | `WH_KEYBOARD_LL` + `WH_MOUSE_LL` 全局钩子，任意键/鼠标键触发 `OnWake` |
| `MainForm` | 界面（自绘圆角按钮、状态卡、计时） |
| `Program` / `RunMain` / `TaskHost` | 入口分派：`--task-host` 走计划任务代理 |

关键行为：

- `MainForm.Start()`：先试 `VScreen.SwitchOff()`，成功则 `vScreenMode = true` 并挂 `WakeHook`；失败回退 `Blanker`。
- `Stop()` / `WakeScreen()` / `FormClosing`：`VScreen.Restore()` + `WakeHook.Stop()`。
- 切到虚拟屏后界面不可见，所以**点亮只能靠按键**（钩子），同时 `FormClosing` 兜底恢复。

命令行：`--screen-off`（包裹命令）、`--screen-on` / `-o`（强制恢复内屏）、`--selftest`、`--`（分隔被包裹命令）、无参数进入守护。`--task-host` 是计划任务代理，读 `%LOCALAPPDATA%\RayScreen\args.txt`，输出写 `out.txt`，返回码写 `done.txt`。

---

## 3. 构建

```bat
build.cmd
```

- 只用系统自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（C# 5 语法：**不能**用字符串插值、`?.`、`nameof`、局部函数等）。
- GUI：`/target:winexe /win32icon:assets\app.ico /win32manifest:src\app.manifest`；CLI：`/define:CLI /target:exe`。
- `src\app.manifest` 声明 `requireAdministrator` + `dpiAware`，**exe 必须管理员运行**（切显示拓扑的硬限制）。
- 编译产物被正在运行的实例占用会报 `CS0016`：先 `Get-Process RayScreen | Stop-Process -Force`。
- CLI 构建会出现 `warning CS0649: WakeHook.OnWake` 未赋值，正常（CLI 不挂钩子）。

---

## 4. 测试（不改代码也能验证）

```bat
:: 自检：电源请求 + 显示输出 + 虚拟屏是否可用
"%ProgramFiles%\RayScreen\ray.cmd" --selftest

:: 包裹一条任务（会切虚拟屏，结束后恢复）
"%ProgramFiles%\RayScreen\ray.cmd" --screen-off -- cmd /c "ping -n 6 127.0.0.1 >nul"

:: 直接跑 GUI（免 UAC，走计划任务）
schtasks /run /tn RayScreen

:: 抢救：把内屏点亮
"%ProgramFiles%\RayScreen\ray.cmd" --screen-on
```

验证「是否真的切过去了」**必须在另一个进程里观测**：

```powershell
[System.Windows.Forms.Screen]::AllScreens | % { $_.DeviceName + ' ' + $_.Bounds.Width + 'x' + $_.Bounds.Height }
```

> 坑：**发起切换的那个进程枚举 `Screen.AllScreens` 是滞后的**，测试进程自己看永远是 `DISPLAY1`；一定要用外部进程看，切换生效时会变成 `\\.\DISPLAY31 800x600`（或 `DISPLAY20`，编号不固定）。

验证「有没有进待机」：

```powershell
Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=506} -MaxEvents 5 |
  Select-Object TimeCreated
```

切换过程中**不应该**出现新的 506 事件。

---

## 5. 安装 / 卸载机制

- `install.cmd` → 提权 → `install.ps1`：复制到 `C:\Program Files\RayScreen`；装 VDD 驱动（`pnputil /add-driver` + 内嵌 SetupAPI 建 `ROOT\MTTVDD\0000`）；写 `C:\VirtualDisplayDriver\vdd_settings.xml`；注册**两个最高权限计划任务**（`RayScreen` 无参数、`RayScreenCli --task-host`）；桌面 + 开始菜单 `RayScreen\{主程序, 卸载 RayScreen}.lnk`；写 `HKLM\...\Uninstall\RayScreen`；最后自检。
- 快捷方式指向 `schtasks.exe /run /tn RayScreen`，所以**日常双击不弹 UAC**（免 UAC 就是靠计划任务 `RunLevel Highest` 实现的，不是靠 manifest）。
- `uninstall.cmd` → `uninstall.ps1`：结束进程、删快捷方式/计划任务/注册表/设备/驱动包（只删 `mttvdd` 对应那一个 `oemNN.inf`）/目录，最后做**残留检查**，干净才 `exit 0`。
- 驱动包名记录在 `C:\ProgramData\RayScreen\vdd-package.txt`（形如 `oem42.inf`），卸载靠它精准删除。

---

## 6. Windows 上的坑（踩过的，别再踩）

1. **`.ps1` 必须 UTF-8 with BOM + CRLF**。PS 5.1 会把无 BOM 的 UTF-8 当 ANSI 读，含中文时报「字符串缺少终止符」「意外标记 }」。
2. **`.cmd` 必须 CRLF**，LF 会把批处理切碎（报 `'elayedexpansion' is not recognized`）。
3. **MSYS bash 里给 exe 传带双引号的参数会被转义坏**（`\"`）→ 测试时把命令写进 `.cmd` 文件再执行。
4. `Start-Process -Verb RunAs -ArgumentList 'C:\path with space\...'` 会**按空格拆参数** → 改用「写一个 `.cmd` 包装，再提权运行这个 `.cmd`」。
5. cmd 重定向到带空格的路径**必须加引号**，否则目标被截成 `C:\DSH`。
6. `New-ScheduledTaskAction -Argument ''` 会参数校验失败 → 无参数时不要传 `-Argument`。
7. `ray.ps1` 不能用 `param([Parameter(ValueFromRemainingArguments)]...)`，否则 `--screen-off` 被当参数名解析 → 直接用 `$args`。
8. 读别的进程正在写的日志会共享冲突 → 用 `[IO.File]::Open($p,'Open','Read','ReadWrite')`。
9. `powercfg` 中文输出要先 `chcp 65001`；隐藏项要用 `powercfg /qh`。
10. `DSH` 环境里 `Start-Process` 会弹「打开文件 - 安全警告」（无代码签名），自动化脚本一律用 `[Diagnostics.Process]::Start` + `UseShellExecute=$false`。

---

## 7. 发布

```bash
# 推送（credential.helper 已配置在本地仓库，只存路径不存明文）
git push origin main

# 打新包并更新 Release 资产
python -c "..."   # 见 dist/RayScreen-1.0.0.zip 的打包脚本
TOKEN="$(cat /d/KEY/TOKEN/GITHUB/token.me)"
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/zip" \
  --data-binary @dist/RayScreen-1.0.0.zip \
  "https://uploads.github.com/repos/shiwushuifeng/RayScreen/releases/<release_id>/assets?name=RayScreen-1.0.0.zip"
```

仓库：https://github.com/shiwushuifeng/RayScreen （`main`，Release `v1.0.0`，release id `456623805`）

> 凭据在 `D:\KEY`，其 `docs/AI.md` 规定：**不得读取明文、不得输出 token**，只能 `$(cat TOKEN/GITHUB/token.me)` 形式喂给命令。

---

## 8. 已知限制 / 可以继续做的

- 虚拟屏默认 `800x600@30Hz`（`C:\VirtualDisplayDriver\vdd_settings.xml` 可改，支持到 4K30）。
- 没有「开机自动进入守护」；`install.cmd -Startup` 只是把 GUI 加到登录启动，不会自动开始守护。
- 守护期间按键盘会点亮屏幕（设计如此：点亮 + 守护继续，不会停止守护）。
- 卸载需要一次 UAC（删驱动/设备必须管理员）。
- 电池供电合盖仍是「睡眠」（`LIDACTION` 直流=1），与本工具无关。
- 若将来这台机器能重启一次，可以把 `PlatformAoAcOverride=0` 真正生效，那时也可以考虑回到「系统关屏 + 只灭背光」的原生方案（代码里保留了 `Power.MonitorOff()` 与看门狗分支）。
