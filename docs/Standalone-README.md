# ScreenLossless v1.0.0 单 EXE 版 / Standalone

下载 **ScreenLossless_v1.0.0_Win64_Standalone.exe** 后双击运行。支持 Windows 10/11 x64，不需要安装 .NET，也不需要把 FFmpeg、ffprobe 或 DLL 放在 EXE 旁。功能与 Portable / Compact 相同。

Open the downloaded **ScreenLossless_v1.0.0_Win64_Standalone.exe** directly on Windows 10/11 x64. No separate .NET installation or adjacent FFmpeg, ffprobe or DLL files are required. Features match Portable / Compact.

## 首次启动 / First launch

首次运行会自动准备内置组件，可能比后续启动稍慢。录制工具按校验清单放入 `%LOCALAPPDATA%/ScreenLossless/components/<hash>/`；后续启动校验并复用已有文件，缺失或损坏时自动修复。此目录同时保留 `LICENSE`、`licenses/`、`ffmpeg-licenses/`、`FFmpeg-BUILD-RECORD.json`、`FFmpeg-SOURCE.md` 和 `docs/`。将 `%LOCALAPPDATA%/ScreenLossless/components` 粘贴到资源管理器地址栏即可查看。

The first launch prepares embedded components automatically. Recording tools are verified and cached under `%LOCALAPPDATA%/ScreenLossless/components/<hash>/`; later launches reuse intact files and repair missing or damaged files. The same folder contains the application license, third-party notices, FFmpeg build record, source directions and guides. Paste `%LOCALAPPDATA%/ScreenLossless/components` into Explorer's address bar to view them.

.NET 的原生运行组件由运行环境自动释放到当前用户的临时缓存。单 EXE 指下载和启动只需要一个文件；运行时仍使用组件缓存，需要用户缓存目录可写。录制内容保存在软件设置的保存位置。

.NET also extracts its native runtime components into the current user's temporary cache. One EXE is sufficient to download and launch; runtime component caches still require writable user storage. Recordings are saved in the location selected in the application.

关闭软件后可删除组件缓存，下一次启动会重新准备；不要在录制或保存期间清理。软件设置与录制文件不在该组件目录。

You may remove the component cache after closing the application; it is prepared again on the next launch. Do not clear it during recording or finalization. Settings and recordings are stored separately.

## 下载校验与分发 / Verification and redistribution

校验命令：

```powershell
Get-FileHash .\ScreenLossless_v1.0.0_Win64_Standalone.exe -Algorithm SHA256
```

将结果与同名 `.sha256` 文件比较。应用内嵌的 FFmpeg 与其他两个版本相同，匹配的 **ScreenLossless_FFmpeg_9.0.2_Source.zip** 和校验文件应在同一 Release / 下载页提供。离线再分发时一并交付源码 ZIP，保留内嵌声明。单文件打包不改变第三方许可要求。

Compare the result with the matching `.sha256` file. The embedded FFmpeg is identical to the other editions. Provide **ScreenLossless_FFmpeg_9.0.2_Source.zip** and its checksum on the same Release / download page. Include the source ZIP when redistributing offline and retain embedded notices; single-file packaging does not change third-party licensing requirements.

[使用说明 / User guide](UserGuide.md) · [源码分发 / Source distribution](FFmpeg-SOURCE.md) · [第三方声明 / Third-party notices](THIRD-PARTY-NOTICES.md)
