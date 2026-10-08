# UVR 复现可行性评估（Agent B）

> 评估对象：`folder_viewer/` WebGL 查看器（Three.js + dicom-parser 单页应用）的功能面
> 评估基准：Easy Volume Renderer v1.8.0（`UnityVolumeRendering/`，com.mlavik1.easyvolumerenderer）+ `VolumeRendering_2022/` 工程现状
> 日期：2026-10-08

**功能面来源说明**：初版由 Agent B 依据 `folder_viewer/使用说明.md` 归纳为 16 项。**2026-10-08 已与 Agent A 的 `kb/agent-a/findings-folder-viewer.md`（52 项、6 组）完成交叉核对**（详见文末"交叉核对"章节）：A 的 52 项中 33 项被初版矩阵覆盖，新增 19 行补充评估，并修正 2 处结论（OU 校正、3D 相机）。

---

## 能力矩阵

结论分级：✅ 直接可用（含"工程已复现"）· 🛠️ 需开发 · ❌ 包能力缺口

| # | folder_viewer 功能 | UVR 现状 | 结论 | 实现思路 | 工作量 |
|---|--------------------|----------|------|----------|--------|
| 1 | DICOM 序列扫描/加载（递归扫描、按空间位置排序、slope/intercept、方向读取） | 包内置 DICOM 导入：`Runtime/Importing/ImageSequenceImporter/OpenDICOM/DICOMImporter.cs`（第 274-280 行读 ImageOrientation）、`SimpleITKDICOMImporter.cs`；工程已实现 `VolumeRendering_2022/Assets/Scripts/RuntimeVolumeLoader.cs`（LoadLungDicomAsync） | ✅ 已实现 | 无需开发。Web 特有的"重复实例/缺层检测"属鲁棒性增强，包没有，可按需补 | 0（可选增强 0.5 天） |
| 2 | 三视图切片（轴/冠/矢 + slider + 滚轮逐层） | 工程已实现 `VolumeViewerUI.cs` + `Assets/Resources/Shaders/SliceUI.shader`（含分割色叠加）；包亦有 `Runtime/GUI/Components/EditSliceGUI.cs` + `SliceRenderingShader.shader` | ✅ 已实现 | 无需开发 | 0 |
| 3 | 点击影像联动十字线 | 包无 crosshair 组件（grep "crosshair" Runtime/Editor 零命中） | 🛠️ | 在工程自有 SliceUI 三视图上加 RectTransform 十字线，点击换算体素索引后同步另两视图 slider | 0.5~1 天 |
| 4 | 右键拖动调窗（WW/WC） | 包切片着色器无窗宽窗位 uniform（`SliceRenderingShader.shader` 仅 `_parentInverseMat`/`_planeMat`），窗宽窗位靠 TF 控制点间接实现 | 🛠️ | 在工程 `SliceUI.shader` 加 `_WindowCenter/_WindowWidth`（HU 值域 -2048~1828 已知，见 kb 1.4），右键拖动改 material 参数 | 0.5 天 |
| 5 | STL 模型加载（二进制/ASCII、LPS/RAS 选择、按结构名着色） | **包无 STL 加载器**：grep `\.stl` 于 Runtime/、Editor/ 零命中（仅 ThirdParty 内无关命中）；kb/project-knowledge.md 1.6 已验证 | ❌ 包缺口 | 自研 `StlRuntimeImporter`（二进制：84B header + uint 数量 + 每三角形 50B；ASCII：`facet normal/vertex` 关键字解析），逐文件构建 Mesh，套与 DICOM/NRRD 相同的 LPS→Unity 变换；配色沿用 `SegmentationOverlayLoader.cs` 的结构配色 | 1~2 天（含 122 文件批量加载与内存控制，源 STL 约 277 MiB） |
| 6 | STL 逐项显隐、器官/骨骼筛选、整体透明度 | 依赖 #5（包无 Mesh 管理） | ❌ 依赖缺口 | #5 完成后每结构一个 GameObject：显隐 = SetActive，透明度 = URP 标准 shader `_Surface=Transparent` + `_BaseColor.a`；筛选 = 名称分组（organ/bone 前缀表） | 并入 #5（+0.5 天） |
| 7 | 正交/自由切面裁切 STL（含反转保留方向） | 包的 CrossSection/SlicingPlane 体系只作用于体对象（`Runtime/Shaders/Include/VolumeCutout.cginc` 编译进体渲染着色器），对普通 Mesh 无效 | 🛠️ | 自定义 STL shader：片元阶段对最多 3 个正交平面 + 1 个自由平面做 `clip(dot(pos,n)-d)`，法线可翻转实现"反转保留方向" | 1 天 |
| 8 | DVR 合成体渲染 | 包核心能力：`Runtime/Shaders/BuiltIn/DirectVolumeRenderingShader.shader` + `VolumeRenderedObject.cs` | ✅ 直接可用 | 无 | 0 |
| 9 | MIP 模式 + 按预设名（`-MIP` 后缀）自动切换 | 包支持：`Runtime/VolumeObject/RenderMode.cs`（MaximumIntensityProjectipon）、`VolumeRenderedObject.cs:619`（MODE_MIP keyword） | ✅ 直接可用 | 预设名含 "-MIP" 时调 `SetRenderMode(MIP)`，否则切回 DVR——两行 UI 逻辑，随 #10 一起做 | 0（并入 #10） |
| 10 | .vp.json 预设导入（32 项：31 内置 + 1 快照）+ 阈值偏移 | **包无 vp.json 支持**：grep "vp.json"/"presets-all" 零命中；包只有自有 .asset 格式（`TransferFunctionDatabase.cs:85` SaveTransferFunction / `RuntimeTransferFunctionEditor.cs`） | ❌ 包缺口 | 自写解析器（格式分析见下节），映射到 `TransferFunction.colourControlPoints/alphaControlPoints`；**必须**在改完控制点后显式调 `GenerateTexture()`（kb 1.1 TF 缓存坑）；HU→归一化用 kb 1.4 公式对齐；阈值偏移 = 所有 alpha 控制点 x 平移后重生成纹理 | 1~2 天 |
| 11 | 采样步长调节 | 包现成：`_SamplingRateMultiplier` Range(0.2, 2.0)（`DirectVolumeRenderingShader.shader:10`），由 `VolumeRenderedObject.cs:637` 写入；MAX_NUM_STEPS 512/1024（`VolumeRendering.hlsl:239,353,392`），即最多 1024 步——web 上限 1536 步（A findings #42），略低于 web 但 UI 上不可感知 | ✅ 直接可用 | UI 加一个 slider 即可 | 0.5 小时 |
| 12 | 快速预览分辨率切换（256³ vs 原始 512） | 包的 `VolumeDataset.DownScaleData()` 是 public（`VolumeDataset.cs:203`），但只在超 2048 时自动触发（`FixDimensions()`，第 195 行） | 🛠️ | 加载后先对降采样副本建一版 VolumeObject 预览，切"原始分辨率"时用全量数据重建；或加载时缓存两份数据 | 0.5 天 |
| 13 | 体渲染裁切（三正交 + 自由切面裁切体渲染） | 包现成：`Runtime/VolumeObject/SlicingPlane.cs`、`CrossSectionPlane.prefab`、`CutoutBox.cs`/`CutoutSphere.cs`/`CrossSectionManager.cs`；工程已有 `SlicePlaneBinder.cs` 投屏/剖切 | ✅ 直接可用 | "反转保留方向"= 平面法线取反，SlicingPlane 可自由旋转/平移满足自由切面 | 0 |
| 14 | 基本光照（diffuse/ambient/specular）、梯度不透明度、opacity unit distance 步长校正 | 部分现成：`LIGHTING_ON` + `calculateLighting()`（`VolumeRendering.hlsl:311-313`，ambient 硬编码 0.2，`DirectVolumeRenderingShader.shader:52`）；API 有 `SetLightingEnabled/LightSource/SetGradientLightingThreshold`（`VolumeRenderedObject.cs:333-416`）；梯度通道有 TF2D（`getTF2DColour`，`VolumeRendering.hlsl:139`）；**包 DVR 已有 alpha 步长校正**：`src.a = 1-pow(1-src.a, 1/_SamplingRateMultiplier)`（`VolumeRendering.hlsl:321-323`，与 web 的 `alpha=1−pow(1−a, dt·1000/unitDistance)` 同构），仅指数语义不同；但 vp.json 的 per-preset 光照参数与 gradientOpacity 线性函数无直接入口 | 🛠️ | OU 校正：把指数从 `1/multiplier` 换成 `dt·1000/unitDistance`（shader 一处公式 + 传参）；光照参数映射到 LightSource/shader 属性或自定义 shader 常量；gradientOpacity 可忽略（当前文件夹预设该函数为常数 1，无实际效果——实测 CT-Bones.vp.json gradientOpacity 两点均为 y=1.0，A 的 findings 亦确认被 web 忽略） | 1 天（可选优化） |
| 15 | 体渲染开启时隐藏 STL、关闭恢复 | 依赖 #5 | 🛠️ | UI 状态联动：开 DVR → STL 根节点 SetActive(false) 并记忆各结构显隐，关闭时恢复 | 0.5 小时（并入 #5） |
| 16 | 模型与影像共用 LPS 坐标（不逐个居中） | 包 DICOM/NRRD 导入已处理方向矩阵与 spacing（`DICOMImporter.cs:274-280`；SimpleITK 后端）；NRRD 元数据含 LPS→RAS 空间信息 | ✅ 直接可用 | STL 侧在 #5 中复用同一 LPS→Unity 变换矩阵即可对齐 | 0（并入 #5） |

**统计**：✅ 7 项（#1、2、8、9、11、13、16）· 🛠️ 6 项（#3、4、7、12、14、15）· ❌ 3 项（#5、6、10，其中 #6 依附于 #5）

---

## 缺口清单与替代方案

| 缺口 | 依据 | 替代方案 | 工作量 |
|------|------|----------|--------|
| STL 加载/展示/筛选（#5、6） | grep `\.stl` 于 `UnityVolumeRendering/Runtime`、`Editor` 零命中；kb 1.6 | 工程内自研 `StlRuntimeImporter`（不改包源码，符合 AGENTS.md 铁律 2）；二进制+ASCII 双格式；批量加载 122 个结构网格需注意单文件可达数十 MiB，建议异步 + 逐个 Instantiate | 2~3 天（含显隐/透明度/筛选） |
| .vp.json 预设导入（#10） | grep "vp.json"/"presets-all" 零命中；包仅支持自有 .asset TF 格式（`TransferFunctionDatabase.cs`） | 工程内自研 `SlicerPresetLoader`：解析 → 归一化 → 填充 TF 控制点 → **显式 GenerateTexture()**（kb 1.1）→ `SetSecondaryTransferFunction`；MIP 预设按名称切 RenderMode | 1~2 天 |
| STL 裁切着色器（#7） | 包 `VolumeCutout.cginc` 仅编入体渲染着色器 | 自定义 URP STL shader，片元 clip 平面组（3 正交 + 1 自由，法线可翻转） | 1 天 |
| Web 特有校验/细节（#1 的缺层检测、#3 十字线、#4 调窗、#12 双分辨率） | 各行已注明 | 均为小时级工程内开发，非包缺口 | 各 0.5 天内 |

---

## .vp.json 预设格式分析

样例：`folder_viewer/volume_rendering_presets/CT-Bones.vp.json`、`CT-MIP.vp.json`；另有汇总 `presets-all.json`（31 个内置预设合集）与 `current-volume-property.vp.json`（当前参数快照）。

结构（3D Slicer volume-property schema v1.0.0）：

```
@schema: .../volume-property-schema-v1.0.0.json
volumeProperties[0]:
  effectiveRange: [152.19, 952.0]        // 预设设计值域（HU），x 轴归一化基准
  isoSurfaceValues: [0.0]                // 包 Isosurface 模式不读 TF，可忽略
  interpolationType: "linear"            // 包支持 linear/nearest
  useClippedVoxelIntensity/clippedVoxelIntensity/scatteringAnisotropy  // 包无对应，忽略
  components[0]:
    componentWeight: 1.0
    shade: true
    lighting: { diffuse, ambient, specular, specularPower }   // 包无直接入口（#14）
    disableGradientOpacity: false
    scalarOpacityUnitDistance: 1.0                            // 步长校正系数（#14）
    rgbTransferFunction.points[]: { x(HU), color[3], midpoint, sharpness }
    scalarOpacity.points[]:        { x(HU), y(0..1), midpoint, sharpness }
    gradientOpacity.points[]:      { x, y, ... }              // 实测常数 1，可忽略
```

映射要点：

1. **颜色**：`rgbTransferFunction.points` → `TransferFunction.colourControlPoints`（TFColourControlPoint，取 x 归一化 + RGB）。
2. **Alpha**：`scalarOpacity.points` 的 y → `alphaControlPoints`（TFAlphaControlPoint）。
3. **归一化**：控制点 x 用 `(x - min) / (max - min)` 换算（kb 1.4）；建议除以数据实际值域（胸部 CT -2048~1828）而非 effectiveRange，使预设落在数据可显示范围；effectiveRange 可用于 alpha 归一化的分母（Slicer 语义）。两种口径需在实现时各试一次取效果正确者。
4. **TF 缓存坑（kb 1.1）**：`CreateTransferFunction()` 已用默认控制点生成纹理缓存，填充控制点后**必须**调 `tf.GenerateTexture()`，否则拿到的是默认棕黄渐变。
5. **渲染模式**：仅 DVR 读取 TF alpha（kb 1.2）；`CT-MIP`/`MR-MIP` 按名称切换 MIP 模式（对应 web 的 `-MIP` 后缀行为）；等值面模式无视 TF alpha，预设中的 `isoSurfaceValues` 无从生效。
6. **不映射项与 web 一致**：midpoint/sharpness（包 1D TF 只有线性控制点；web 也明确提示"非默认 midpoint/sharpness 不支持"）、gradientOpacity（当前全为常数 1）、lighting 参数（可选映射，#14）、scatteringAnisotropy（web 未实现散射）。
7. `presets-all.json` 与同名原生 `.vp.json` 按名称去重、原生优先——加载器按文件遍历即可复刻该行为。

结论：**31 个预设全部为 RGB + 线性控制点，可完整映射到包的 1D TransferFunction，无需包源码改动**。

---

## 建议的实现顺序（按性价比排序）

1. **`.vp.json` 预设导入器（#10 + #9 自动 MIP）— 1~2 天**。纯 C# 解析 + 已有 TF API，不依赖任何缺口项；一次让 31 个 Slicer 预设生效，是"教学演示价值/工作量"比最高的项。注意 kb 1.1 TF 缓存坑与 kb 1.4 值域对齐。
2. **STL 加载器 + 显隐/透明度/筛选（#5、6、16、15）— 2~3 天**。补齐最大缺口，复用 `DatasetPathResolver.cs` 的路径解析与 `SegmentationOverlayLoader.cs` 的结构配色；加载后模型与现有 CT/分割天然同坐标对齐。
3. **STL 裁切着色器（#7）— 1 天**。依赖第 2 步；与现有 `SlicePlaneBinder.cs` 的切面参数联动后即可复刻"切片移动同步模型切口"。
4. **调窗 WW/WC（#4）与十字线联动（#3）— 各 0.5~1 天**。增强既有 SliceUI，独立可并行。
5. **采样步长 slider（#11，0.5 小时）随手做**；**双分辨率预览（#12）与光照参数映射（#14）为可选优化**，前者在低端机演示卡顿时再做。

第 1、2 步完成后，folder_viewer 的核心演示能力（体渲染 + 预设 + 结构模型 + 裁切）即可在 Unity 2022 工程内完整复现；剩余项均为体验增强。

---

## 源码依据索引

- STL 缺口：grep `\.stl` 于 `UnityVolumeRendering/Runtime/`、`Editor/` → 零命中
- vp.json 缺口：grep `vp.json`、`presets-all` 于包源码 → 零命中
- 包自有 TF 存取：`UnityVolumeRendering/Runtime/TransferFunction/TransferFunctionDatabase.cs`（Load/Save TF 与 TF2D）
- MIP/渲染模式：`Runtime/VolumeObject/RenderMode.cs`、`VolumeRenderedObject.cs:615-637`
- 采样率：`Runtime/Shaders/Include/VolumeRendering.hlsl:239-244`、`DirectVolumeRenderingShader.shader:10`
- 光照：`VolumeRendering.hlsl:296-313`、`VolumeRenderedObject.cs:333-416`
- 裁切体系：`Runtime/VolumeObject/SlicingPlane.cs`、`CrossSectionManager.cs`、`CutoutBox.cs`、`CutoutSphere.cs`、`Runtime/Shaders/Include/VolumeCutout.cginc`
- 降采样：`Runtime/VolumeData/VolumeDataset.cs:185-232`
- DICOM 方向：`Runtime/Importing/ImageSequenceImporter/OpenDICOM/DICOMImporter.cs:274-280`
- 十字线缺失：grep `crosshair` 于包源码 → 零命中
- vp.json 样例：`folder_viewer/volume_rendering_presets/CT-Bones.vp.json`（实测解析）、`CT-MIP.vp.json`
- 工程现状：`VolumeRendering_2022/Assets/Scripts/`（RuntimeVolumeLoader / SegmentationOverlayLoader / VolumeViewerUI / SlicePlaneBinder / DatasetPathResolver）
- STL 数据：`VolumeRendering_2022/Assets/StreamingAssets/STL/` 实测 122 个；`folder_viewer/STL/` 61 个（说明.md 称源目录 61 个、约 277 MiB）
