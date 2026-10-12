# 外接屏守护

<img src="assets/app-icon.png" alt="External Display Guard icon" width="128" />

Windows 笔记本的原生托盘程序。外接显示器被游戏、驱动或其他软件切成双屏时，自动恢复你选择的显示模式和缩放倍率。支持“仅外接屏”和“双屏扩展、外接屏为主屏”，默认仅外接屏、200% 缩放。

使用 Windows 显示接口工作，无需管理员权限或安装额外运行时。已在 Windows 11 x64 上验证，其他系统与显示驱动的兼容性请参考 [验证范围](VALIDATION.md)。

## 下载

在 [GitHub Releases](https://github.com/sodakitten/ExternalDisplayGuard/releases/latest) 下载 `ExternalDisplayGuard.exe`，双击即可运行。需要安装、自启或卸载脚本时下载 `ExternalDisplayGuard-v1.0.0-win-x64.zip`。

## 使用

- 双击 `ExternalDisplayGuard.exe` 启动；双击托盘图标打开设置。
- 在设置中选择“仅外接屏”或“双屏扩展，外接屏为主屏”。选择后会自动保存并在约 2–5 秒内应用。
- 插拔、显示配置变化、睡眠恢复、解锁后自动检查；正常运行每 5 秒检查一次。屏幕配置已经正确时不重复切换。
- 首次只有一台物理外接屏时自动记住其设备身份。多台外接屏时需要在设置中选择目标。虚拟显示器、无线显示器和间接显示器不会被自动选为外接屏。
- 目标屏拔掉时不强制输出到失联屏幕；Windows 通常会恢复内置屏，若检测到没有可用物理屏幕且内置屏可用，程序会主动恢复内置屏。内置屏合盖不可用时会等待。
- “暂停 10 分钟”可临时手动切换；“自动保持所选显示模式”取消勾选则长期停用。
- “仅外接屏缩放”默认 **200%**。恢复单屏后会重新读取当前外接屏对应的显示源并恢复这个倍率；即使已经是单屏，倍率被其他软件改动也会纠正。可在设置里选择其他倍率，或选择“保持当前缩放”取消缩放守护。暂停/停用守护同时暂停缩放纠正，扩展模式不主动修改缩放。
- “停用并恢复笔记本屏幕”立即解除守护并切回内置屏。退出本次运行会保留登录自启；永久停用可取消登录自启和守护两个选项。
- 扩展模式保持已有屏幕位置和活动屏的分辨率/时序，并平移布局使外接屏为主屏；启用此前不活动的屏幕时由 Windows 选择可用模式。程序不锁定分辨率、HDR、刷新率或声音设备。

## 安装、卸载

双击程序可直接运行。安装到当前用户并登录自启：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

安装位置：`%LOCALAPPDATA%\ExternalDisplayGuard`。开始菜单提供设置、仅外接屏、双屏扩展、恢复笔记本屏四个入口。自启项为当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ExternalDisplayGuard`。

卸载并恢复内置屏：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\ExternalDisplayGuard\uninstall.ps1"
```

卸载移除程序、自启项和四个快捷方式，保留配置与诊断日志；传入 `-KeepDisplayMode` 可保留当前显示模式。

## 诊断与开发

配置：`config.json`；日志：`guard.log`（1 MiB 轮换）；状态：`status.json`，均位于安装目录。日志只记录显示器拓扑、程序状态和 API 错误，不记录屏幕内容。

支持命令：`--settings`、`--external`、`--extend`、`--pause`、`--enable`、`--disable`、`--internal`、`--stop`。正在运行时命令传递给同一实例。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$reportPath = Join-Path $PWD 'diagnostic.json'
Start-Process .\dist\ExternalDisplayGuard.exe -ArgumentList '--diagnose', ('"' + $reportPath + '"') -Wait
$reportPath = Join-Path $PWD 'self-test.json'
Start-Process .\dist\ExternalDisplayGuard.exe -ArgumentList '--self-test', ('"' + $reportPath + '"') -Wait
```

编译使用 Windows 自带 .NET Framework 4.x 编译器，输出 x64 原生 Windows Forms EXE。没有网络连接、更新器、驱动安装或管理员服务。真正的 Session 0 服务无法直接修改当前用户桌面的显示配置，故采用登录用户会话中的常驻程序。锁屏、睡眠和标准远程桌面时暂停；远程控制软件创建虚拟显示器可能与强制显示拓扑发生冲突，需要远程虚拟屏时可以暂停守护。

更新已安装的版本可运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\update.ps1`。它会备份现有 EXE 和配置，更新当前安装并重启，并保留自启设置。

缩放通过 `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` 的未公开 DPI 数据包（-3 / -4）实现，并验证支持范围及写入后的读回值。已在本机 Windows 11 build 26300 实测；未来 Windows 更新可能改变该接口，此时会记录错误并重试，不会静默换成其他倍率。数据包行为参考 [SetDPI 原始实现](https://github.com/imniko/SetDPI/blob/master/DpiHelper.cpp) 和 [WindowsUndocumentedLib 的发现记录](https://github.com/lihas/WindowsUndocumentedLib)。本程序独立实现 C# 调用，不依赖这些工具的二进制。

屏幕模式切换可能短暂闪黑。API 失败时尝试恢复切换前配置，若恢复失败尝试内置屏。一分钟内连续四次纠正后冷却一分钟，避免驱动或其他软件反复争抢造成闪屏。未实测的插拔、合盖、唤醒和长期运行行为需以后观察，不能保证修复线缆、接口、显示器深度休眠或显卡驱动故障。

## 网上调研

1. **Windows 显示设置 / Win+P** 能手动切换外接、复制或扩展。窗口位置记忆可帮助恢复窗口摆放，但不能代替持续保持所选拓扑。[微软多屏说明](https://support.microsoft.com/en-us/windows/hardware/display-graphics/how-to-use-multiple-monitors-in-windows)
2. **MultiMonitorTool** 支持启用/禁用屏幕、设主屏、保存/恢复配置，可用于快捷方式和脚本；自动持续守护仍需要额外调度。[官方页面](https://www.nirsoft.net/utils/multi_monitor_tool.html)
3. **直接调用 CCD API**：`QueryDisplayConfig` 枚举可用路径，`SetDisplayConfig` 指定实际目标并保存系统显示配置，适合本程序。比只指定“第二屏”的脚本更适合有虚拟显示驱动的机器。[QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)、[SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)
4. **连接/驱动问题**：微软建议检查线缆、接口、外设和显示驱动。反复掉线的真正原因需要复现和日志，不能仅凭模式自动变化认定某个驱动有问题。[微软排查指南](https://support.microsoft.com/en-us/windows/hardware/display-graphics/troubleshoot-external-monitor-connections-in-windows)
5. **服务与桌面隔离**：Windows 服务在 Session 0，与用户桌面隔离；CCD API 需要本机控制台会话访问权限。[Interactive Services](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)

项目只包含源码、图标、构建与安装脚本。运行时配置、显示器设备路径、日志和注册表备份不会进入仓库或发行包。
