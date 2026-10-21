# ScreenLossless v1.0.0 便携版

完整解压本软件 ZIP，双击 **ScreenLossless.exe**。支持 Windows 10/11 x64，无需另装 .NET Runtime。

本版压缩了内置运行环境，并使用仅保留录屏功能的 FFmpeg。编码、音频、缩放、分段、预览和浮窗功能保留。首次启动会解压内存中的程序集，启动时间可能略有增加。

| 文件或目录 | 用途 |
| --- | --- |
| ScreenLossless.exe | 录制软件及内置 .NET 运行环境 |
| ffmpeg.exe、ffprobe.exe | 视频编码与媒体检查 |
| docs/ | 使用说明、硬件验证方法与分发说明 |
| LICENSE、licenses/、ffmpeg-licenses/ | 应用及第三方许可证 |
| FFmpeg-BUILD-RECORD.json | FFmpeg 二进制与对应源码的哈希记录 |
| FFmpeg-SOURCE.md | 独立源码包的下载与再分发说明 |

使用说明见 [使用说明](UserGuide.md)。电脑性能较低时可选 H.264 CPU、MPEG-4 等轻量编码，并降低帧率或输出分辨率。

**FFmpeg 对应源码独立提供：ScreenLossless_FFmpeg_9.0.2_Source.zip，与本软件在同一个 Release / 下载页获取。** 发布者必须在运行包链接旁提供源码链接；离线再次分发时一并交付源码 ZIP。详见 [FFmpeg-SOURCE.md](FFmpeg-SOURCE.md)。

界面右上角可选择跟随系统、中文或 English。中文 Windows 显示语言默认中文，其他语言默认英文；手动选择会被记住，空闲时立即生效。

The UI defaults to Chinese on Chinese Windows systems and English otherwise. Change **System default / 中文 / English** beside the window buttons. Your choice is remembered and applies immediately while idle.
