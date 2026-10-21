# 开发与目录维护

## 源码模块

项目文件为 `src/ScreenLossless.csproj`，入口为 `src/Program.cs`。各目录使用同一 `ScreenLossless` 命名空间，移动文件不改变类型名称。

| 目录 | 职责 |
| --- | --- |
| `src/UI/` | 主窗口、对话框、主题、窗口装饰与录制浮窗 |
| `src/Capture/` | 抓屏、区域选择、虚线框与窗口选择 |
| `src/Recording/` | 录制、分段、合并、恢复与存储保护 |
| `src/Audio/` | 音频端点、热插拔、采集、音量与时钟校正 |
| `src/Encoding/` | 编码方案、设备探测、FFmpeg 参数与媒体进程 |
| `src/Configuration/` | 用户设置与持久化 |
| `src/Platform/` | Windows API、单实例通信与内置组件缓存 |
| `tests/Cases/` | 回归测试入口与各功能测试源码 |

## 构建和运行

需要 .NET 8 SDK，脚本优先使用已有的 `.tools/dotnet/`，并统一使用 `.tools/packages/` 缓存依赖。首次安装依赖需要访问 NuGet。

```powershell
# 从源码运行；自动将本机 .tools/ffmpeg 加入子进程搜索路径
powershell -ExecutionPolicy Bypass -File scripts/run-source.ps1

# 测试并生成当前版本的三个运行版本、对应源码和 SHA256
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
# 只生成其中一种版本
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Edition Standalone
# 独立构建单 EXE，含组件缓存专项回归
powershell -ExecutionPolicy Bypass -File scripts/build-single-exe.ps1

# 回归测试，以及需要可交互桌面的浮窗测试
powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1
powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1 -MainContext

# 检查 FFmpeg 对应源码与二进制哈希
powershell -ExecutionPolicy Bypass -File tests/Test-FFmpegSourceBundle.ps1
```

`Directory.Build.props` 将两项目的编译输出统一放到 `.tools/build/<项目名>/`；单 EXE 构建使用 `<项目名>.Standalone/`，避免增量编译将内嵌工具资源带入其他版本。回归录像、截图等输出在 `tests/artifacts/`。这些目录不会提交到 GitHub。

正式运行文件在 `releases/<版本>/Standalone/`、`Portable/` 与 `Compact/`，发行 EXE / ZIP 在 `releases/packages/`。根目录启动批处理用于本机已构建的版本；仅克隆源码后应先构建，或使用源码启动脚本。

## 保留与清理

源码、测试源码、图标、许可证、FFmpeg 对应源码与构建脚本均为有效内容。尤其不要将 FFmpeg 源码、锁文件、构建记录及第三方许可证当作缓存清除。

可以清理 `.tools/build/`、`tests/artifacts/` ；清理前先关闭录制软件与测试进程。`.tools/dotnet/` 是本机 SDK，`.tools/packages/` 是 NuGet 缓存，保留可以继续离线构建；删除后需要重新准备工具和依赖。

硬件验证范围见 [HARDWARE-VALIDATION.md](HARDWARE-VALIDATION.md)，不要将跳过分支或模拟长时测试写成实机验收通过。

发布脚本默认生成三种版本和一个独立 FFmpeg 对应源码 ZIP。NoZip 仅用于本机构建，不是完整的发行步骤。正式下载页必须同时提供对应源码 ZIP，详见 FFmpeg-SOURCE.md。

单 EXE 使用 SDK 的原生库自动释放，FFmpeg / ffprobe 及声明文档为内嵌 ZIP，按清单哈希缓存至 `%LOCALAPPDATA%/ScreenLossless/components/<hash>/`。缓存命中只校验并复用，缺失或损坏时修复；归档路径使用 `/`，兼容 Windows PowerShell 5.1 创建与 .NET 8 读取。自定义 FFmpeg 路径优先，旧组件缓存路径不覆盖当前版本内置工具。

`tests/Run-Regression.ps1 -Bundle` 检查校验、修复、并发准备、非法路径和当前实际工具归档；`tests/Test-StandaloneRelease.ps1` 需可交互桌面，会备份并恢复当前用户设置，在空目录只放一个 EXE、清空外部工具与运行环境路径，实际验证启动、录制、浮窗暂停继续、逐帧解码、缓存复用及没有相邻依赖文件。运行前关闭软件。

FFmpeg 精简构建验证可用 tests/Run-Regression.ps1 -MediaOnly；发行 ZIP 的完整对应源码、二进制哈希、许可证与体积检查由 tests/Test-ReleasePackages.ps1 执行，构建脚本打包后自动调用。

程序集压缩仅用于 self-contained Portable 和 Standalone；.NET 8 不支持在 framework-dependent Compact 上启用 EnableCompressionInSingleFile，发布脚本按版本类型设置。

GitHub Actions 在托管 Windows 环境运行媒体与音频时间线检查（-MediaOnly）以及构建和分发校验；区域拖拽、屏幕像素、主窗口和浮窗测试仍需本机交互桌面，不能将云端媒体检查等同于完整桌面验收。

HEVC 10-bit 4:4:4 专项：`powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1 -HevcLossless -Capture`。包含格式、逐帧 YUV 哈希、奇数尺寸、缩放、MP4、连续分段与实际暂停/继续录制；无对应 GPU 时明确跳过硬件部分。`-MediaOnly` 也覆盖不依赖交互桌面的 HEVC 检查。

保存进度停滞、失败片段隔离和声音预览断开检查：`tests/Run-Regression.ps1 -Audit`。加 `-Capture` 会实际录制、暂停并继续，验证残留失败片段不会混入成片；这些检查也纳入完整回归，无桌面部分纳入 `-MediaOnly`。

## 本地化

语言入口在自绘标题栏。`AppSettings.Language` 保存 Automatic / Chinese / English，旧配置默认 Automatic；`L10n` 在改变线程 UI culture 前缓存 Windows 显示语言，避免「跟随系统」误用上次手动选择。中文文案是资源键，英文资源内嵌于 `src/Localization/en.json`。普通文字使用 `L10n.T`，带插值的文字使用 `L10n.F`，保留原有格式占位符；不要翻译设备名、文件路径或 FFmpeg 技术参数。动态选项数组按当前语言生成，不缓存翻译结果。

空闲时通过 `ScreenLosslessApplicationContext` 更换主窗口，先注销旧窗口的快捷键、托盘和预览资源，保持同一消息循环和单实例通信。切换保留选区、设置页、最近文件、预览、窗口位置及最大化状态；录制或处理文件时禁用语言切换。

验证：`./tests/Run-Regression.ps1 -Localization` 检查默认语言、资源、排版、英文实际录制与设置保留；`-LanguageContext` 检查真实 Application.Run(context) 消息循环下连续切换和单实例激活。

## 日夜主题

标题栏仅保留一个太阳/月亮切换按钮。`AppSettings.Theme` 保存 Light / Dark，旧配置默认 Light。启动时先配置 `Ui` 的语义配色再构造窗口；运行时 `Ui.ChangeTheme` 原位刷新控件、隐藏设置页、浮窗和菜单，不重建主窗口或录制引擎。所有颜色从 `ThemePalette` 获取；录屏原图与选区深浅对比描边保留自身色彩。切换结束同步重绘透明及双缓冲子控件，避免残留浅色像素。

专项检查：`powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1 -Themes`，覆盖单按钮、中英文、设置记忆、菜单与弹窗、原生重绘、真实录制和暂停中切换、帧哈希及编码进程连续性。

## 主题滚动条

软件内的设置页、下拉列表、窗口选择、录制恢复和设备诊断统一使用 `ThemedScrollHost`。新增可滚动控件时，用它包装原控件，保持原控件的选择、键盘和滚轮行为。轨道交互宽度为 12 个逻辑像素，圆角滑块平时 6 像素、悬停或拖动时 8 像素，跟随 DPI 缩放。系统文件选择对话框由 Windows 管理。

运行专项回归：`powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1 -Scrollbars`。检查包括拖动、翻页、滚轮、键盘、双轴滚动、过滤为空、调整窗口大小和所有界面接入位置。

## 界面布局检查

运行 `powershell -ExecutionPolicy Bypass -File tests/Run-Regression.ps1 -VisualPolish`，验证中英文语言选项、标题栏按键、分组页签、圆角菜单、取消选择和按钮状态。标题栏应保留明确的百分比行高，避免控件首选高度把整行撑出可见范围。

弹窗需在构造开始暂停布局，所有控件加入后恢复布局，让 DPI 缩放统一执行一次。`DialogLayout.Button` 的列宽是设计单位，英文文字在 96 DPI 画布测量，避免系统缩放重复作用于固定列宽。

## GitHub 源码准备

运行 `python scripts/prepare-github.py`（Git、Python 3.9+）检查并生成当前工作目录的源码 ZIP；`--verify-only` 只校验。新文件、未提交的修复和删除都会反映在上传包中，不会误用旧 HEAD。上传步骤和双语发行说明见 [GITHUB-PUBLISH.md](GITHUB-PUBLISH.md)。源码包排除本地 SDK、缓存、编译产物、测试录像和发行包，并验证每个 ZIP 文件与工作目录的 SHA256 一致。

GitHub Actions 在 main 推送、PR 和手动触发时运行源码清单检查，再构建配套 FFmpeg 和三种 Windows 发行包。主界面外观回归会在本机各显示器上检查原生 DPI 切换和最小窗口尺寸；发行包校验也覆盖 Markdown 本地链接。

## 资源占用检查

`tests/Run-Regression.ps1 -Resources` 覆盖电平轮询生命周期、稳定控件重绘、浮窗字体、帧队列预算、连续素材增量计数、进度解析分配及真实录制的暂停继续。测量口径与限制见 [运行资源与性能](PERFORMANCE.md)。
