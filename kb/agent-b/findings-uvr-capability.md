# UVR 复现可行性评估（Agent B）

> 评估对象：`folder_viewer/` WebGL 查看器（Three.js + dicom-parser 单页应用）的功能面
> 评估基准：Easy Volume Renderer v1.8.0（`UnityVolumeRendering/`，com.mlavik1.easyvolumerenderer）+ `VolumeRendering_2022/` 工程现状
> 日期：2026-10-08

**功能面来源说明**：初版由 Agent B 依据 `folder_viewer/使用说明.md` 归纳为 16 项。**2026-10-08 已与 Agent A 的 `kb/agent-a/findings-folder-viewer.md`（52 项、6 组）完成交叉核对**（详见文末"交叉核对"章节）：A 的 52 项中 30 项映射到初版 16 行矩阵（含合并映射），其余 22 项仅由补充矩阵覆盖（新增 21 行），并修正 2 处结论（OU 校正、3D 相机）。

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
| 12 | 快速预览分辨率切换（256×256×160 预览档，最长边 256 vs 原始 512） | 包的 `VolumeDataset.DownScaleData()` 是 public（`VolumeDataset.cs:203`），但只在超 2048 时自动触发（`FixDimensions()`，第 195 行） | 🛠️ | 加载后先对降采样副本建一版 VolumeObject 预览，切"原始分辨率"时用全量数据重建；或加载时缓存两份数据 | 0.5 天 |
| 13 | 体渲染裁切（三正交 + 自由切面裁切体渲染） | 包现成：`Runtime/VolumeObject/SlicingPlane.cs`、`CrossSectionPlane.prefab`、`CutoutBox.cs`/`CutoutSphere.cs`/`CrossSectionManager.cs`；工程已有 `SlicePlaneBinder.cs` 投屏/剖切 | ✅ 直接可用 | "反转保留方向"= 平面法线取反，SlicingPlane 可自由旋转/平移满足自由切面 | 0 |
| 14 | 基本光照（diffuse/ambient/specular）、梯度不透明度、opacity unit distance 步长校正 | 部分现成：`LIGHTING_ON` + `calculateLighting()`（`VolumeRendering.hlsl:311-313`，ambient 硬编码 0.2，`DirectVolumeRenderingShader.shader:52`）；API 有 `SetLightingEnabled/LightSource/SetGradientLightingThreshold`（`VolumeRenderedObject.cs:333-416`）；梯度通道有 TF2D（`getTF2DColour`，`VolumeRendering.hlsl:139`）；**包 DVR 已有 alpha 步长校正**：`src.a = 1-pow(1-src.a, 1/_SamplingRateMultiplier)`（`VolumeRendering.hlsl:321-323`，与 web 的 `alpha=1−pow(1−a, dt·1000/unitDistance)` 同构），仅指数语义不同；但 vp.json 的 per-preset 光照参数与 gradientOpacity 线性函数无直接入口 | 🛠️ | OU 校正：把指数从 `1/multiplier` 换成 `dt·1000/unitDistance`（shader 一处公式 + 传参）；光照参数映射到 LightSource/shader 属性或自定义 shader 常量；gradientOpacity 可忽略（31 个预设中 29 个恒为 1，例外 US-Fetal、uCT-Skull；22 个 CT-* 均为常数 1，对本工程 CT 预设无实际效果——web 侧实现了梯度不透明度功能，但当前预设数据下基本不生效） | 1 天（可选优化） |
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
6. **不映射项与 web 一致**：midpoint/sharpness（包 1D TF 只有线性控制点；web 也明确提示"非默认 midpoint/sharpness 不支持"）、gradientOpacity（31 个预设中 29 个恒为 1，例外 US-Fetal、uCT-Skull；22 个 CT-* 均为常数 1）、lighting 参数（可选映射，#14）、scatteringAnisotropy（web 未实现散射）。
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

---

## 与 Agent A findings 的交叉核对（progress 第 8 步，2026-10-08）

核对对象：`kb/agent-a/findings-folder-viewer.md`（52 项功能、6 组）及其解包产物 `unpacked-viewer-app.js` 等（A 已留档供 grep）。

**核对结论**：A 的 52 项中，映射到初版 16 行矩阵的不同 A 项为 30 个（含合并映射，A28 并入 B#6、A38 并入 B#10），其余 22 项仅由下方补充矩阵覆盖（21 行，其中 A38 与 B#10 重复计入，B-d/B-e 各合并 2 项）；原矩阵 2 处结论因 A 的发现而修正，**3 个 ❌ 缺口结论与建议实现顺序不变**。本节已经 A 反向核对确认（A findings 第五节）。

### 覆盖映射（A 项 → B 矩阵行）

| A 的项 | 映射到 |
|--------|--------|
| A1、4（DICOM 选择/解码→HU） | B#1（✅ 已实现） |
| A5、6、7（STL 选择/解析/法线） | B#5（❌ 包缺口） |
| A8（按名配色） | B#6 附注——工程 `SegmentationOverlayLoader.cs` 已实现代码配色（kb 4 节），✅ |
| A10、11、38、41（预设导入/校验/切换/阈值偏移） | B#10（❌ 包缺口→自研） |
| A14、15（三视图/滑块） | B#2（✅ 已实现） |
| A17（十字线联动） | B#3（🛠️） |
| A19、20（调窗拖动/数字输入） | B#4（🛠️） |
| A26、27、29（模型显隐/骨骼器官筛选/整体不透明度；A28 搜索并入） | B#6（❌ 依赖 #5） |
| A32、33、35（跟随切片裁切 STL/反转/自由切面裁切） | B#7（🛠️） |
| A36（裁切同步体渲染 clips[4]） | B#13（✅ 包 SlicingPlane/CutoutBox/CrossSectionManager） |
| A37（体渲染开关与 STL 恢复，modelRestore） | B#15（🛠️） |
| A39（体数据纹理构建 256/512 档） | B#12（🛠️ 双分辨率） |
| A40（合成/MIP + `-MIP` 自动切换） | B#9（✅） |
| A42（采样步长 0.5–2mm，上限 1536 步） | B#11（✅，已补充包上限 1024 步的差异说明） |
| A43、44、45（OU 校正/梯度不透明度/光照） | B#14（🛠️，**已修正**：包 shader 已有同构 alpha 校正公式） |
| A46（插值方式） | 新增 B-o 行（🛠️） |
| A47（早终止） | 新增 B-q 行（✅） |
| A16、18、21、22、23、24、25、30、31、34、48、49、50、51、52、2、3、9、12、13 | 补充矩阵（下表） |

### 补充矩阵（新增 21 行：初版矩阵未单列的 22 项 + A38 的 LUT 备注独立成行）

| # | A 的功能 | UVR / 工程现状 | 结论 | 实现思路 | 工作量 |
|---|---------|---------------|------|----------|--------|
| B-a | A2 序列自动分组下拉（按 SeriesInstanceUID 多序列选择） | 工程当前固定加载 lung/chest 两套数据（`DatasetPathResolver.cs`），无序列选择 UI | 🛠️ | 沿用包 `DICOMImporter`（其内部即按 UID 分组），自建序列列表 UI | 1 天（当前数据固定，可不做） |
| B-b | A3 序列合法性校验（方向 [1,0,0,0,1,0]、单帧、等间距、SOP 去重、内存上限） | 包/工程均无此类校验 | 🛠️ | 在导入入口加校验函数，报错即中止 | 0.5~1 天（可选增强） |
| B-c | A9 模型-影像交叠检查（STL AABB 与体 AABB 相交） | 无 | 🛠️ | `Bounds.Intersects` 一处检查；工程数据已配准 | 0.5 小时（可选） |
| B-d | A12、13 加载联控 / 更换数据文件夹 | 工程为运行时自举（`RuntimeVolumeLoader.cs`），无"更换数据"流程 | 🛠️ | 重入式加载（先 ClearSegmentations/销毁旧对象再加载） | 0.5 天 |
| B-e | A16、22 三视图缩放（Shift+滚轮 0.3–8）与适应复位 | 工程 SliceUI 未见缩放实现 | 🛠️ | 三视图 RectTransform localScale + 滚轮事件 | 0.5 天 |
| B-f | A18 HU 实时读数 | 无 | 🛠️ | 指针位置→体素索引→`dataset.GetData` 显示 HU | 0.5 天 |
| B-g | A21 窗预设按钮（肺窗 [1500,-600] / 软组织 [400,40] / 骨窗 [1800,400]） | 无（工程 HU 预设是 TF 预设，非窗宽窗位） | 🛠️ | 并入 B#4 的 WW/WC，三个按钮设 material 参数 | 0.5 小时 |
| B-h | A23 3D 场景内 CT 切片平面 + 不透明度 | ✅ 工程已实现：`SlicePlaneBinder.cs` 投屏（外挂 Plane 切片绑定） | ✅ 已实现 | 不透明度滑块为 material 参数，随手可加 | 0（+0.5 小时） |
| B-i | A24 3D 轨道相机（拖动旋转/右键平移/滚轮缩放） | 工程 `CameraController.cs`（60 行）是**飞行式移动**（WASD+右键转向+shift 加速），不是轨道相机 | 🛠️ | 换标准 Orbit 脚本（target=体中心，theta/phi/radius） | 0.5~1 天 |
| B-j | A25 视角预设 前/后/侧/斜 | 无 | 🛠️ | 在 B-i 的轨道相机上设 theta/phi 按钮 | 0.5 小时 |
| B-k | A30 一键重置视图（层位/裁切/窗位/相机归位） | 无 | 🛠️ | 聚合上述各模块的 Reset 调用 | 0.5 小时 |
| B-l | A31 自适应布局（ResizeObserver） | Unity 引擎天然满足（Game view + Canvas Scaler） | ✅ 直接可用 | 无 | 0 |
| B-m | A34 自由角度切面斜切重采样图（320×320 三线性贴到斜面 mesh） | ✅ 工程已实现：`SlicePlaneBinder.cs` 支持任意姿态 Plane 的投屏与真实剖切 | ✅ 已实现 | 无 | 0 |
| B-n | A38 预设控制点烘焙 8192 项 1D LUT | 包同类机制：`GenerateTexture()` 烘焙 TF 纹理（256 宽） | ✅ 直接可用 | 无需自写 LUT；注意 kb 1.1 缓存坑 | 0 |
| B-o | A46 插值方式 nearest/linear（预设 `interpolationType`） | 包仅三线性（默认）+ 三立方（`SetCubicInterpolationEnabled`，`VolumeRenderedObject.cs:475`），**无 nearest 过滤**（grep nearest 于 Shaders 零命中） | 🛠️ | 当前 31 个预设全部 linear（A findings §3.1），实际无需处理；如需 nearest 再加 shader keyword | 可忽略 |
| B-p | A48 有预设时自动启动体渲染 | 无自动启动 | 🛠️ | 在 `RuntimeVolumeLoader` 自举尾部调 SetRenderMode(DVR)+加载预设 | 0.5 小时 |
| B-q | A47 早终止（accum.a>0.985 截断） | 包现成：`RAY_TERMINATE_ON` keyword + `SetRayTerminationEnabled`（`VolumeRenderedObject.cs:449-454`） | ✅ 直接可用 | 默认可开 | 0 |
| B-r | A49 状态/错误提示 | Unity `Debug.Log` + 简单 UI 文本，trivial | ✅ 直接可用 | 随各功能顺带 | 0 |
| B-s | A50 事件驱动按需渲染（无 RAF 常驻） | Unity 渲染循环由引擎管理，概念不适用 | ✅ 直接可用 | 无 | 0 |
| B-t | A51 自动化测试钩子（viewerTest/volumeTest） | 无对应物 | 🛠️ | Editor 菜单校验脚本（或 PlayMode 测试），输出与验证记录同构 JSON | 1 天（见下节） |
| B-u | A52 明确不支持的输入给出错误 | 包导入器对不支持格式报错；.mrb 已有 `MrbExtractor.cs` 解包；kb 1.6 | ✅ 直接可用 | 无 | 0 |

（行号 B-a 顺延至 B-u，实际新增 21 行、覆盖 23 个 A 项：其中 22 项为初版矩阵未单列项（B-d、B-e 各合并 2 项，占 20 行），另 1 行 B-n 系 A38 由初版 B#10 备注独立成行、与 B#10 重复计入。）

### 修正的原结论

1. **B#14（OU 校正）**：初版写"包未必做此校正"——**错误**。包 DVR shader 已有同构的 alpha 步长校正（`VolumeRendering.hlsl:321-323`），只是指数由 `_SamplingRateMultiplier` 而非预设 `scalarOpacityUnitDistance` 决定。复现工作量从"新增 shader 逻辑"降为"改一处公式 + 传参"。
2. **B-i（3D 相机）**：初版功能面未单列；核对后确认工程 `CameraController.cs` 为飞行式而非 web 的轨道式，若要复刻 web 手感需补轨道相机（0.5~1 天）。

### 两个量化验证记录的复用性评估

来源：`folder_viewer/验证记录.json` 与 `folder_viewer/体渲染验证记录.json`（由页面内置钩子 `window.viewerTest`/`window.volumeTest` 生成，A findings #51）。

**可直接复用为 Unity 复现验收指标（强烈建议采用）**：

| 指标 | 值 | Unity 侧断言方式 |
|------|-----|-----------------|
| `models: 61` + `triangles: 5815720` | 61 模型、5,815,720 三角形 | **STL 解析器最有力的自动化断言**：自研解析器批量读入 `StreamingAssets/STL/` 后 sum 三角形数应精确等于该值（二进制 STL 三角形数可整读），不等即解析错误 |
| `dimensions: [512,512,320]` | 体数据维度 | DICOM/NRRD 加载后 dataset.dimX/Y/Z 相等 |
| 预设 `count: 32`（31 预设 + 快照） | 32 | vp.json 解析器成功解析数量 |
| `lung: [-1000, 2952]` | lung DICOM 体数据值域（slope/intercept 应用后） | 加载 lung 序列后 min/max 对比，验证 HU 换算正确（注意：这是 lung 数据集的值域；Chest NRRD 值域为 -2048~1828，kb 1.4，两套数据勿混） |
| `clipping / mip / modelRestore / clippingPassed` | 均 true | 功能冒烟项：切面裁切、MIP 切换、体渲染关闭后 STL 显隐恢复各跑一次布尔断言 |
| `unsupportedOrientationRejected: true` | 斜位序列被拒 | 若实现 B-b 校验，用倾斜 DICOM 样例断言拒绝 |
| `errors: []` | 无错误 | 全流程跑完无异常 |

**不可直接复用 / 需转化的项**：

- `texture: [256,256,160]`：web 的 CPU 三线性重采样预览档位；Unity 用 GPU 直接采样原始体数据，无对应物（若做了 B#12 双分辨率，降采样档理论可比对，性价比低）。
- `ctSamplesMatched: 100`：web 内部两次采样路径一致性检查，Unity 无对应双路径，不可比。
- `networkRequests: []`：Web 专有，无意义。
- **渲染像素级对比不可用**：web 自述不承诺与 Slicer 像素级一致（使用说明.md:23），Unity 渲染管线又不同，视觉验收只能人工对照 4 张 PNG。

**落地建议**：写一个 Editor 菜单校验脚本（对应 B-t，1 天）：依次执行 STL 批量解析→比对 61/5815720、体数据加载→比对维度与值域、预设解析→比对 count=32，输出与验证记录同构的 JSON 便于 diff。这把 A 的验收基准变成回归测试，成本极低、收益高。

### 交叉核对后的最终统计

- 覆盖口径：映射到初版 16 行的不同 A 项 30 个 + 仅由补充矩阵覆盖的 22 个 = A 的 52 项（补充矩阵 21 行覆盖 23 项，A38 与 B#10 重复计入）；矩阵总行数：16（初版）+ 21（补充）= 37 行。
- 结论分布：✅ 15 · 🛠️ 19 · ❌ 3（❌ 仍为 STL 加载/显隐筛选、vp.json 导入两项核心缺口及其依附项）。
- 建议实现顺序**不变**：① vp.json 预设导入器 → ② STL 加载器+显隐 → ③ STL 裁切 shader → ④ 调窗/十字线/相机等 UI 增强 → ⑤ Editor 校验脚本（可提前到与 ①② 并行，因验收指标已明确）。
