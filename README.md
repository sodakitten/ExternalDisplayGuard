# 屏幕守护 · Display Guard

<img src="assets/app-icon.png" alt="Display Guard icon" width="128" />

适用于台式机和笔记本的 Windows 托盘程序。每块物理显示器都能独立勾选，决定亮哪些屏幕，再指定主屏、各屏缩放和方向。不要求组合中存在笔记本屏幕，也不把台式机的某块显示器特殊当作“内屏”。

## 版本与下载

- [稳定版 v1.0.0](https://github.com/sodakitten/ExternalDisplayGuard/releases/tag/v1.0.0)：旧版单外接屏 / 笔记本双屏功能。
- [多屏预览版 v1.1.0-beta.1](https://github.com/sodakitten/ExternalDisplayGuard/releases/tag/v1.1.0-beta.1)：本分支的新功能。只完成离线模拟和编译验证，尚未在真实三 / 四屏或真实桌面上验证。

下载预览版 `ExternalDisplayGuard.exe` 可直接运行。安装、更新和卸载脚本位于 `ExternalDisplayGuard-v1.1.0-beta.1-win-x64.zip`。EXE 未进行数字签名。

## 任意屏幕组合

连接 A / B / C / D 四块显示器时，可选任意一块、任意两块、任意三块或全部四块，共 15 种非空组合；三块屏幕共 7 种。主屏必须在已勾选的集合里。所有多屏组合使用独立扩展桌面，本版不提供复制 / 镜像模式。

1. 打开设置，勾选需要亮起的屏幕。未勾选的物理屏幕会关闭。
2. 选择主屏，为每块屏选择“保持当前”或具体缩放倍率，以及需要的方向。
3. 点击“应用选择”，在 15 秒内选择“保留画面”；不确认或点击“还原”会尝试恢复之前的显示配置。
4. 确认后自动保持该组合。游戏或驱动改变显示模式时，恢复的是当前已应用的组合。
5. 可给组合命名，再点击“保存已应用组合”。以后从托盘调用，或载入后应用。

“全选”和“仅亮主屏”只是快捷选择。它们不限制其它组合。第一次安装默认不自动关闭任何屏幕，先由用户应用选择。

## 布局和每屏缩放

用户可先在 Windows 显示设置中调整左右、上下、竖屏排列，再点击“记住当前布局”和“应用选择”。已有有效布局会保留；取消中间屏幕造成空隙或出现重叠时，会按相对次序排成连续桌面。确认后的布局保存到组合。

每屏可独立保持 100%、125%、150%、175%、200% 等倍率，或保持当前值。某块屏的倍率不被硬件支持时会独立显示错误并限速重试，不拆掉已经成功的整个扩展桌面。分辨率和刷新率默认沿用可用时序，不锁定 HDR 或声音设备。

“识别已亮屏幕”会临时在当前已启用的物理显示器上显示名称；不会为了识别而点亮关闭的屏幕。

## 屏幕连接变化

- 所选副屏离线：暂时使用剩余屏幕并压紧布局空隙，保留原组合。
- 主屏离线：从仍在线的所选屏幕中选择替补。
- 所选屏幕全部离线：如有已知的可用物理屏幕，暂用一块；没有可用屏幕时等待。
- 原屏重新连接：恢复原组合、主屏和保存的布局。
- 新接入未配置屏幕：暂停拓扑纠正，等待用户重新选择，避免直接关闭新屏幕。
- 远程会话或活动虚拟显示器：暂停物理屏幕拓扑纠正。

优先使用有效且唯一的 EDID 序列身份，保留连接路径作补充；同型号且无法唯一辨认的设备不猜测关联。虚拟、无线以及间接显示驱动（包括部分 DisplayLink 设备）不自动纳入物理屏幕选择列表。硬件必须确实支持所选屏幕数和时序。

## 免安装模式

在 EXE 同目录放置 `portable.flag`，程序将配置、日志和状态写入自身目录，整个目录可以搬迁。直接双击 EXE 使用，不运行安装或更新脚本；免安装包不附带这些脚本，也不自动添加自启。程序目录需要当前用户有写入权限。

已有配置可复制为同目录的 `config.json`；旧版配置将在使用时迁移。若主动勾选登录自启后搬迁目录，需要重新设置该自启路径。

## 暂停、停用和自启

托盘提供暂停 10 分钟、恢复守护、停用守护和退出。“停用守护”保留当前显示，不强制切到某块笔记本屏幕。关闭设置窗口后继续在托盘运行；关闭未确认的预览会尝试还原。

安装到当前用户并添加登录自启：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

更新已安装版本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\update.ps1
```

安装目录为 `%LOCALAPPDATA%\ExternalDisplayGuard`。只使用本程序自己的当前用户自启项。更新器备份原 EXE 和配置，并保留其它自启值；不需要管理员权限。

卸载会停止程序、删除本程序的自启项与快捷方式，保留配置、日志和当前显示状态：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\ExternalDisplayGuard\uninstall.ps1"
```

## 从旧版迁移

旧单屏目标和已保存的 200% 等倍率会迁移为一个屏幕组合，原停用状态保留。旧双屏配置迁移为两屏组合。之后可以选择任意屏幕，不再受旧单外接屏 / 笔记本双屏限制。

配置升级为 schema 2。回退旧版本时，应同时从 `backups` 中恢复原 EXE **和原 config.json**，不要让 v1.0.0 读取新版配置。

旧程序仍运行时，新版不会和它同时控制显示；先退出旧版或使用更新器。本文的安装、更新、真实桌面诊断命令不会由离线测试运行。

## 构建与离线验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-offline.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用 Windows 自带 .NET Framework 4.x 编译器，生成 x64 Windows Forms EXE。离线测试是单独的控制台程序，只使用 FakeDisplayBackend；不启动 GUI、不读取真实显示器、不修改缩放、不操作注册表，也不读取安装目录中的用户配置。

覆盖台式机和笔记本的全部三屏 / 四屏非空组合与所有所选主屏、独立缩放、断连重连、未知屏暂停、能力不足、部分失败恢复、预览超时及配置迁移。见 [验证范围](VALIDATION.md) 和 [设计说明](docs/multi-monitor-plan.md)。

`--self-test <报告路径>` / `--offline-tests <报告路径>` 同样只运行模拟检查。真实桌面只读诊断为 `--diagnose <报告路径>`，由使用者自行选择运行。

其它命令：`--settings`、`--single`、`--all`、`--pause`、`--enable`、`--disable`、`--stop`。`--external` 与 `--extend` 保留为单屏 / 全部屏幕的兼容别名；`--internal` 仅为旧快捷方式兼容。

## 实现与验证限制

显示拓扑使用 [QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig) 与 [SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)。每块扩展屏使用独立的适配器 / source 路径；整组配置由 Windows 先验证，再应用。

缩放使用未公开的 DPI 数据包（-3 / -4），并验证范围及读回值；参考 [SetDPI](https://github.com/imniko/SetDPI/blob/master/DpiHelper.cpp) 和 [WindowsUndocumentedLib](https://github.com/lihas/WindowsUndocumentedLib)。程序独立实现 C# 调用，不依赖它们的二进制。

预览还原会恢复之前亮起的屏幕、布局和可读取的缩放。此前熄灭的屏幕没有可靠的原倍率读数，不能保证还原它的倍率；新接入屏幕或虚拟桌面导致安全还原无法完成时，保留当前输出并暂停守护。

预览版没有运行真实桌面测试。真实显示路径、显卡 / 扩展坞兼容性、混合 DPI、UI 布局、热插拔、休眠唤醒及长期运行仍需验证；自动按游戏启动 / 退出选择组合尚未加入。

程序没有联网、更新检查、驱动安装或管理员服务。源码、发行包不包含运行时日志、显示器身份、用户配置或注册表备份。
