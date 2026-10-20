# Q 版 2D 遊戲 UI 靜態素材

- 素材：`cozy-inventory-atlas.png`（1536 × 1024，約 2.6 MB）。
- 生成方式：內建 `image_gen`；未使用 API／CLI 備援。
- 用途：木製面板框、布面標題、皮革按鈕、收納格、紙張底紋與收納箱標誌。
- 程式以 `GameSkin.cs` 的九宮格切片繪製，保留角飾比例；圖集完整嵌入 WPF Resource，無外部下載依賴。
- 依實際產圖邊界選定切片，並非假設產圖能精確服從網格座標。

| 素材 | 實際來源矩形（x、y、寬、高） |
|---|---|
| 木框 | 0、0、512、464 |
| 標題牌 | 512、84、512、304 |
| 按鈕 | 1024、0、512、464 |
| 收納格 | 0、464、512、560 |
| 紙張 | 512、464、512、560 |
| 收納箱 | 1024、464、512、560 |

## 最終生成提示詞

```text
Use case: stylized-concept. Create a production-ready 2D cozy chibi fantasy inventory UI texture atlas for a Windows desktop file organizer. This is actual game UI artwork to embed, not a screenshot, no text, no letters, no numbers, no labels, no watermark. Landscape 1536x1024 atlas. Precisely divide canvas into 3 columns and 2 rows, each cell 512x512, no gutters and no external frame. Each cell fills its entire square. Top-left cell: carved warm honey wood inventory panel frame seen straight-on, rounded beveled corners, brass corner brackets, stitches and tiny clover carvings, large clear cream parchment center covering middle 75%, dimensional painted game art, suitable for nine-slice UI. Top-middle cell: emerald green felt title plaque with chunky warm wooden rim and gold endcaps, blank clear center, front facing, fills square, suitable for nine-slice banner. Top-right cell: raised amber leather game button, rounded wooden rim, tiny rivets in corners, softly lit pale golden blank center, suitable for nine-slice. Bottom-left cell: recessed inventory slot square, dark teal felt center with stitched leather rim and brass rounded corners, no item inside, fills square. Bottom-middle cell: seamless delicate warm parchment paper texture, no objects, no border, very low contrast to keep black UI text legible. Bottom-right cell: a cute chibi closed wooden storage chest with brass lock, two little dot eyes and rosy cheeks, a few folded documents beside it, centered isolated on solid cream background, thick hand-painted outlines, object occupies central 70%. Cohesive polished hand-painted mobile RPG UI asset art, visibly textured crafted materials, chunky appealing silhouettes, gentle highlights and shadows, flat frontal view, not pixel art, no photorealism. All six assets must be distinct and exactly contained within their 512x512 grid cells.
```
