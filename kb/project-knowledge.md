# 项目知识库（已验证的工程事实与踩坑记录）

> 由主 Agent 维护，所有 Agent 动代码前必读。**每条都经过源码核实或实际运行验证。**

## 1. 包行为（Easy Volume Renderer v1.8.0，`UnityVolumeRendering/`）

### 1.1 传递函数纹理缓存坑（⚠️ 最重要的坑）
`TransferFunctionDatabase.CreateTransferFunction()` 在创建时就用**默认控制点**调用了 `GenerateTexture()`（棕黄→白渐变、半透明 ramp）。之后修改 `colourControlPoints/alphaControlPoints` 再调 `GetTexture()`，拿到的是**缓存的默认纹理**——`GetTexture()` 只在 `texture == null` 时才重新生成。
**正确做法**：修改控制点后必须显式调 `tf.GenerateTexture()`。
（历史教训：教学预设/标准预设曾因此"完全不起作用"，外观一直是默认 TF 的白黄半透明圆柱。）

### 1.2 渲染模式与 TF alpha
- 只有 **DVR** 逐采样读取 TF alpha 合成；
- **等值面（Isosurface）**：命中第一个表面时 `col.a = 1.0f; break;` ——强制不透明，无视 TF alpha；
- **MIP**：输出 `float4(1,1,1,maxDensity)`——完全不采样 TF。
→ 改 TF 后"没反应"，先确认是不是不在 DVR 模式。

### 1.3 DVR alpha 累积物理
沿光线逐采样 alpha 合成，**每厘米约 15 个采样点**（512 步 / 体对角线）。外壳组织想"视觉透明"，单点 alpha 必须压到千分位（0.04 × 45 层 ≈ 84% 不透明）。

### 1.4 数据纹理归一化
`VolumeDataset.CreateTextureInternalAsync` 将数据按 `(raw - min) / (max - min)` 写入纹理（RHalf/RFloat）。TF 控制点用同样公式归一化即可对齐。胸部 CT 实际值域 **-2048 ~ 1828**（-2048 是体外 padding，空气约 -1003）。

### 1.5 分割叠加机制
`AddSegmentation(dataset, labels)`：分割体的**原始体素值 = 结构编号**（0=背景），通过第二传递函数（编号/maxId → 颜色阶梯）渲染；单结构显隐 = 改 `SegmentationLabel.colour.a` 后 `SetSegmentationLabels()` 重建。
注意：`UpdateSegmentationLabels` 里的 `segmentationLabels.OrderBy(l => l.id)` 没有回写（LINQ 陷阱），但功能不受影响。

### 1.6 格式支持边界
支持 DICOM（内置 openDICOM + SimpleITK 后端）/NRRD（**必须 SimpleITK**）/NIfTI/RAW/图像序列。**不支持 .mrb / .vti / .vtk / STL**（grep 已验证零命中）。`.mrb` = zip，解包取 NRRD（见 `VolumeRendering_2022/Assets/Scripts/Editor/MrbExtractor.cs`）。

### 1.7 SimpleITK
每工程独立启用（`Volume Rendering → Settings → Enable SimpleITK`，下载二进制到工程内 + 定义 `UVR_USE_SIMPLEITK`）。**2022 工程已启用**。

## 2. 本工程（VolumeRendering_2022）已实现的功能

| 功能 | 关键脚本 |
| --- | --- |
| 运行时自举（免改场景） | `Assets/Scripts/RuntimeVolumeLoader.cs`（`[RuntimeInitializeOnLoadMethod]`） |
| Lung DICOM / Chest NRRD 运行时加载 | 同上（`LoadLungDicomAsync` / `LoadChestCtAsync`） |
| 分割叠加（61 结构、显隐/隔离） | `Assets/Scripts/SegmentationOverlayLoader.cs` + UI `VolumeViewerUI.cs` |
| 切片查看器 UI（横/纵两视图 + slider） | `VolumeViewerUI.cs` + `Assets/Resources/Shaders/SliceUI.shader`（含分割色叠加） |
| 外挂 Plane 切片绑定（投屏/真实剖切/跟随 SlicingPlane） | `Assets/Scripts/SlicePlaneBinder.cs` + `Editor/SliceBinderMenu.cs` |
| mrb 解包 | `Assets/Scripts/Editor/MrbExtractor.cs` |
| HU 传递函数预设（教学版=只看分割色；标准版） | `RuntimeVolumeLoader.ApplyHounsfieldTransferFunction` |
| Slicer `.vp.json` 预设导入（32 个、-MIP 自动切换、阈值偏移 ±1000 HU；Editor 校验菜单 Tools/Volume Rendering/校验 Slicer 预设解析） | `Assets/Scripts/SlicerPresetLibrary.cs` + `VolumeViewerUI.DrawSlicerPresetSection` |
| 分割配色：骨骼结构统一暖白 RGB(255,251,240)（rib_/vertebrae_/humerus_/scapula_/clavicula_/costal_cartilages 前缀 + sternum；2026-10-08 用户指定并验证） | `SegmentationOverlayLoader.BoneColour` + `IsBone` |
| 数据路径解析（StreamingAssets 优先） | `Assets/Scripts/DatasetPathResolver.cs` |

## 3. Unity / 工程环境

- Unity 2022.3.13f1c1 + URP 14 + Input System 1.7.0（`activeInputHandler: 1`，旧版 `Input` API 会抛异常）。
- 包以 `file:` 引用（manifest）， Assembly-CSharp 默认引用包的 asmdef。
- **编译歧义坑**：`RenderMode`、`TransferFunction` 与 UnityEngine 同名类型冲突 → 用全限定名或 using 别名（脚本里已有示例）。
- 编辑器自动刷新不可靠：改脚本后需用户点一下编辑器窗口/Ctrl+R；验证编译看 `Library/ScriptAssemblies/Assembly-CSharp.dll` 时间戳。
- Play 模式中途重编译会恢复场景备份：**运行时动态添加的组件/加载的数据会全部丢失**；需要持久的组件用编辑器菜单挂载（参考 `SliceBinderMenu` 模式）。
- Unity 6 工程（VolumeRenderingSample）已冻结，不再同步。

## 4. 数据事实

- 胸部 CT（Chest_CT.nrrd）：512×512×320，short，gzip，LPS，spacing 0.515/0.515/0.5，值域 -2048~1828。
- 分割（Chest_Seg.nrrd）：同尺寸，unsigned char，**TotalSegmentator 标签方案**，数据中实际存在 61 个结构（CSV 定义 117 + 背景）。
- `ReconstructionLabels.csv`：`"LabelValue","Name","Color_R/G/B/A",...`（颜色全是统一灰，实际配色在代码里生成）。
- `STL/`：61 个按结构命名的 STL（目录 122 文件含 .meta；名称与 TotalSegmentator 一致）。
- `volume_rendering_presets/`：3D Slicer 风格 `CT-*.vp.json` 预设（格式契约待 Agent A/B 分析归档）。
