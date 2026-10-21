# ScreenLossless 图标来源

按项目所有者要求，用内置 imagegen 工具重新设计。没有使用原图或其他品牌图标作为生成参考。当前 assets 下均为新图标。

- assets/ScreenLossless-source.png：生成工具最终原图，保留原始透明度。
- assets/ScreenLossless.png：512×512 RGBA PNG，供项目及说明使用。
- assets/ScreenLossless.ico：16 / 20 / 24 / 32 / 40 / 48 / 64 / 96 / 128 / 256 像素 ICO，供 EXE 与内嵌程序图标使用。
- 派生 PNG / ICO 仅使用 Pillow 缩放和格式转换，没有重新绘制或改变设计。

用户已确认应用原始源码由 ChatGPT 生成。新图标随本项目按根目录 LICENSE（MIT）提供；生成来源说明不构成对第三方权利的独立法律保证。

生成原图 SHA256：`73f63525e8c08aa5bb04f49f5d4d915b5a5024a907083e5716d2f64f25acc10f`。

## 生成提示词

Use case: logo-brand. Create one brand-new polished Windows desktop app icon for ScreenLossless, a screen recorder. Output a single centered app icon, square 1024x1024, with genuinely transparent outside corners, no text. Design: a confident rounded-square tile in deep indigo (#4D59CF) with a restrained blue-to-indigo tonal gradient; inside it a thick crisp white rounded rectangular screen/capture frame, and a single small luminous coral-red recording circle integrated at the top right corner of that white frame, separated by indigo negative space. Simple strong geometry, balanced optical margins, generous solid stroke widths, elegant flat vector-like finish with only very subtle depth. Highly legible at 16x16 and 32x32: no tiny strokes, no letters, no play triangle, no camera lens, no checkerboard pattern, no shadows outside tile, no extra decorations, no watermark, no brand imitation, no photographic styling. Icon should fill approximately 90% of canvas. This is production artwork to replace an unattractive old camera icon in an actual app, not a presentation or mockup. Show just the final icon.

## 最终边缘修整提示词

Polish this app icon for production. Preserve the exact inner white capture frame with coral recording dot, indigo tile color, and overall geometry. Repair ONLY the outer boundary and transparency: remove every stray blue fragment, cloudy fringe, grain, wisps and speckle outside the rounded square. The rounded-square silhouette must be perfectly clean and smooth, like a precisely clipped vector path, with a narrow antialiased edge and fully transparent empty space everywhere outside it. Give equal clean transparent margin of about 7% on all four sides. No outer glow, no shadow, no noise, no checkerboard painted into image, no other changes. Single square icon, no text.
