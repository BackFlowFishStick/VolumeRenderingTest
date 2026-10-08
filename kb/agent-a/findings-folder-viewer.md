# folder_viewer 功能分析（Agent A）

> 分析对象：`folder_viewer/文件夹影像模型查看器.html`（约 0.74 MB 单文件）及其数据目录。
> 分析日期：2026-10-08。所有结论均给出源码依据。
>
> **源码引用约定**：原 HTML 是「外层选择页 + 内嵌查看器」双层结构，内层代码全部挤在原文件第 12 行的一行字符串里，无法按行引用。本目录已生成 4 个解包产物用于交叉核对（Agent B 可直接 grep）：
> - `kb/agent-a/unpacked-viewer.html` —— 解包后的完整内层查看器 HTML
> - `kb/agent-a/unpacked-viewer-ui.html` —— 内层 UI（style + body，约 11 KB）
> - `kb/agent-a/unpacked-viewer-app.js` —— 内层主程序（86 行，含体渲染 shader）
> - `kb/agent-a/unpacked-picker.js` —— 外层选择页 JS（原文件第 13–53 行）
>
> 下文 `app.js:N` 指 `unpacked-viewer-app.js` 行号，`picker.js:N` 指 `unpacked-picker.js` 行号，`原 HTML:N` 指 `文件夹影像模型查看器.html` 行号。

---

## 一、技术栈概览

### 1.1 双层架构（原 HTML:12）

- **外层「选择页」**：文件夹选择（DICOM / STL / 预设三张卡片）、DICOM 扫描与序列分组、STL/预设预读校验，全部实现在外层 `<script>`（picker.js）。加载完成后把解析结果挂到 `window.currentData` / `window.importedPresets`，用 `iframe.srcdoc = VIEWER_HTML` 注入内层查看器（picker.js:38）。
- **内层「查看器」**：以 JS 字符串常量 `VIEWER_HTML`（原 HTML:12，约 688 KB）内嵌，通过 `<iframe id="frame" title="三视图与三维模型查看器">`（原 HTML:5）渲染。父子页面经 `window.parent.currentData`、`window.parent.viewerLoaded()`、`window.parent.viewerFailed()` 通信（app.js:2、84；picker.js:40–41）。
- 纯本地 File API（`webkitdirectory`），零网络请求（验证记录.json 中 `"networkRequests": []`）。

### 1.2 库

| 库 | 版本 | 用途 | 依据 |
| --- | --- | --- | --- |
| dicom-parser | 1.8.12（MIT, Chris Hafey） | 外层 DICOM 解析（只到像素数据 tag 为止） | 原 HTML:10 banner；picker.js:6 |
| Three.js | 内嵌 minified `build/three.min.js`，带 r150+ deprecation 警告，版权 2010–2023 | WebGL 渲染、ShaderMaterial、Data3DTexture | 原 HTML:11；THREE-LICENSE.txt |

无其他第三方库；STL 解析、窗宽窗位、光线投射 shader、相机交互均为手写。

### 1.3 渲染方式（三种并存）

1. **2D 三视图 = Canvas2D 逐像素**：不用 WebGL。`updateSlice()`（app.js:21）遍历体数据，经 `gray()`（app.js:20）做窗宽窗位线性映射后写 ImageData，`putImageData` 到各向 canvas；另建 `THREE.CanvasTexture` 把同一张图贴到 3D 场景中的平面（app.js:15）。
2. **3D STL = Three.js 网格**：`MeshStandardMaterial`（roughness 0.7，DoubleSide），半球光 + 跟随相机的平行光（app.js:5–6）。
3. **体渲染 = GLSL3 光线投射（shader raycasting）**：`ShaderMaterial`（glslVersion GLSL3，BackSide 盒子，app.js:64–75），体数据为 CPU 端三线性重采样生成的 `THREE.Data3DTexture`（R32F，线性过滤，app.js:61）；片元 shader 做 AABB 求交 + 前向合成 / MIP，上限 1536 步。要求 WebGL2 + `OES_texture_float_linear`，不支持则降级为「三视图 + STL」（app.js:63；使用说明.md）。

### 1.4 坐标系约定（app.js:7–9；picker.js:26、31）

- DICOM 保持 LPS；STL 可选 LPS/RAS（picker.js:36）。场景单位毫米→米（÷1000），以 `centerRAS` 把体中心移到原点（app.js:6 模型减 center、app.js:7 `world()`）。
- `world(i,j,k)` 体素→世界坐标，`ijk(p)` 世界→体素（app.js:7–8）；体渲染 shader 内 `uvw()` 还要做 x 取反、y/z 交换的轴重排（app.js:66）。

---

## 二、功能清单

### A. 数据加载（外层 picker.js）

| # | 功能 | 实现要点 | 数据依赖 | 备注 |
|---|------|---------|----------|------|
| 1 | DICOM 文件夹选择 | `<input id="dicom" type="file" webkitdirectory multiple>`，异步逐文件扫描，每 20 个文件让出主线程 | DICOM 目录 | 原 HTML:4；picker.js:8 |
| 2 | 序列自动分组 | 按 SeriesInstanceUID（x0020000e）分 Map，下拉框显示 `模态 · 描述 · 层数 · 尺寸 · 相对路径`，按层数降序 | 同上 | picker.js:7、10 |
| 3 | 序列合法性校验 | 方向必须 `[1,0,0,0,1,0]`、单帧（NumberOfFrames=1）、尺寸/方向/间距一致、SOP 不重复、z 等间距（容差 max(0.01, 1%·dz)）、内存上限 3.5 亿体素 | 同上 | picker.js:17–22 |
| 4 | 像素解码 → HU 体数据 | 仅未压缩 Implicit/Explicit VR LE/BE；MONOCHROME2、8/16 位、单通道；应用 slope(x00281053)/intercept(x00281052) → `Float32Array ct`；按 position[2] 排序重建 | 同上 | picker.js:22–26 `readVolume()` |
| 5 | STL 文件夹选择 | 递归收集 `*.stl`，按文件名排序，显示数量与总 MiB | STL 目录 | picker.js:12 |
| 6 | STL 解析 | `readSTL()`：`84+count*50==byteLength` 判二进制，否则 ASCII `vertex` 正则；逐三角形平移旋转到场景坐标（LPS/RAS 开关），单位 mm→m，减 centerRAS | STL 目录 | picker.js:29–34 |
| 7 | 法线重建 | 每面叉积算 flat normal（注释：STL 逐三角形独立顶点，不用索引） | — | picker.js:33 |
| 8 | 按名称自动配色 | `color()` 正则匹配 lung/rib/vein/aorta/heart/trachea/liver → 7 组颜色，兜底紫色 | STL 文件名 | picker.js:28 |
| 9 | 模型-影像交叠检查 | 计算 STL 包围盒与体半尺寸 AABB 相交，不交叠报错拒绝加载 | 两者 | picker.js:37 |
| 10 | 预设文件夹导入 | 收集 `.json`；`presets-all.json`（有 `presets` 数组）逐个 normalize；`.vp.json`（有 `volumeProperties`）整文件 normalize；名称去重，**同名时原生 .vp.json 后插入覆盖汇总项**（排序把 .vp.json 放后） | volume_rendering_presets/ | picker.js:50–52 |
| 11 | 预设校验 `normalizePreset()` | 控制点必须 6 元（颜色）/4 元（不透明度）、数值有限、x 严格递增；**拒绝 midpoint≠0.5 或 sharpness≠0（非线性控制点）与非 RGB 颜色空间**；光照参数必须有限且 unitDistance>0 | 同上 | picker.js:43–48 |
| 12 | 「加载查看」联控 | `ready()`：需扫描完成 && 选中序列 && （有 STL 或有预设）；加载中禁用全部控件 | — | picker.js:4、13、36 |
| 13 | 更换数据文件夹 | 顶栏 `#change` 重新显示选择页，重置 iframe | — | picker.js:13；原 HTML:3 |

### B. 2D 三视图与成像控制（内层 app.js / unpacked-viewer-ui.html）

| # | 功能 | 实现要点 | 数据依赖 | 备注 |
|---|------|---------|----------|------|
| 14 | 轴位/冠状/矢状三视图 | `axes{Z,Y,X}` 描述表（宽高、物理间距、体素映射、颜色），`updateSlice()` Canvas2D 逐像素渲染 + 3D 内 CanvasTexture 平面与彩色描边框 | ct 数组 | app.js:14–15、21；ui.html:11–14 |
| 15 | 层位置滑块 + −/＋ 步进 | `sliceZ/Y/X` slider、`[data-step]` 按钮 → `move()`，读数 `readX/Y/Z` | — | app.js:33、37；ui.html:11–14 |
| 16 | 滚轮逐层 / Shift+滚轮缩放 | wheel 事件区分 shift；zoom 范围 0.3–8 | — | app.js:34 |
| 17 | 点击/拖拽三平面联动十字线 | pointer 拾取体素坐标同步 `state.x/y/z` 重绘三视图；`#showCross` 开关 | — | app.js:19、34 |
| 18 | HU 实时读数 | `pointer()` 更新 `#huInfo`（CT 显示 HU，MR 显示灰度） | — | app.js:34 |
| 19 | 右键拖动调窗（WW/WL） | 水平Δ×4=窗宽，垂直Δ×−2=窗位，同步数字框 | — | app.js:35 |
| 20 | 窗宽窗位数字输入 | `#ww/#wl` → `setWindow()` | — | app.js:38、40；ui.html:5 |
| 21 | 窗预设按钮 | 肺窗 [1500,−600] / 软组织 [400,40] / 骨窗 [1800,400]（`[data-preset]`） | — | app.js:39 |
| 22 | 「适应」缩放复位 | `[data-fit]` 把该视图 zoom 归 1 | — | app.js:37 |
| 23 | 3D 中显示/隐藏 CT 切片平面 + 不透明度 | `#showPlanes`、`#planeAlpha`（10–100%） | — | app.js:30、41；ui.html:5 |

### C. 3D 与模型

| # | 功能 | 实现要点 | 数据依赖 | 备注 |
|---|------|---------|----------|------|
| 24 | 3D 相机交互 | 手写轨道：拖动旋转（theta/phi）、右键或 Shift 拖动平移、滚轮缩放（0.2–8 倍 extent） | — | app.js:49 |
| 25 | 视角预设 前/后/侧/斜 | `[data-view]` 设置 theta/phi | — | app.js:48；ui.html:12 |
| 26 | STL 逐项显隐列表 | 动态生成 checkbox + 颜色圆点 + 名称，`#modelCount` 计数 | 61 个 STL | app.js:44–45 |
| 27 | 骨骼/器官/全部/隐藏筛选 | `bone()` 正则（rib_/vertebrae_/humerus_/scapula_/clavicula_/sternum/costal_cartilages） | STL 文件名 | app.js:46 |
| 28 | 结构名搜索 | `#search` 按子串隐藏列表行 | — | app.js:46 |
| 29 | 模型整体不透明度 | `#modelAlpha`：opacity、transparent、depthWrite=（a==1） | — | app.js:47 |
| 30 | 重置视图 | `#resetAll`：层位归中、裁切全关、窗位软组织、相机归位 | — | app.js:52 |
| 31 | 自适应布局 | ResizeObserver 重设 renderer 与三张 canvas | — | app.js:50–51 |

### D. 裁切 / 切面（显示级裁切，不改数据）

| # | 功能 | 实现要点 | 数据依赖 | 备注 |
|---|------|---------|----------|------|
| 32 | 轴/冠/矢跟随切片裁切 STL | `updateClip()`：勾选 `cutZ/cutY/cutX` 后按当前层位生成 `THREE.Plane`（setFromNormalAndCoplanarPoint），赋给所有 mesh 的 `material.clippingPlanes`（renderer.localClippingEnabled=true，app.js:4） | — | app.js:30 |
| 33 | 保留方向反转 | `flipZ/Y/X` 按钮，文案随状态切换（保留下方/上方等） | — | app.js:36 |
| 34 | 自由角度切面 | `freeCut` 启用；yaw（−180~180°）/pitch（−90~90°）/offset（mm，±体对角一半）定义平面；生成 320×320 斜切重采样图（`sample()` 三线性，app.js:13）贴到橙色半透明平面 mesh + 描边 | ct 数组 | app.js:22–29 |
| 35 | 自由切面裁切 STL 与体渲染 | `freeSign` 反转（flipFree）、`resetFree` 复位、`showFree` 控制切面可见 | — | app.js:29–30、43 |
| 36 | 裁切同步体渲染 | `vrSyncClip()` 把最多 4 个裁切平面写入 shader uniform `clips[4]`，GPU 求交时收紧 t0/t1 | — | app.js:70、76 |

### E. 体渲染（Slicer 预设驱动，app.js:53–80）

| # | 功能 | 实现要点 | 数据依赖 | 备注 |
|---|------|---------|----------|------|
| 37 | 体渲染开关 | `vrEnabled`：检测 WebGL2 + OES_texture_float_linear；开启时隐藏全部 STL（记住原状态）、禁用模型控件、隐藏 CT 平面；关闭恢复 | 预设 + ct | app.js:63、77–78 |
| 38 | 预设下拉切换 | `vrPreset`，默认 CT-Bones；`vrApplyPreset()` 把控制点烘焙成 8192 项 1D LUT（RGBA8 颜色+不透明度）与梯度 LUT（R8） | .vp.json | app.js:56–58、62 |
| 39 | 体数据纹理构建 | `vrBuildTexture()`：CPU 三线性重采样（`sample()`）为 Float32 Data3DTexture；默认最长边 256 快速预览，可切 512 原始；逐 24 层让出主线程 + 进度提示 | ct 数组 | app.js:61 |
| 40 | 合成 / MIP 投射模式 | shader `mode` uniform：0=前向 alpha 合成，1=沿射线取最大 HU 后过 LUT；**预设名以 `-MIP` 结尾自动切 MIP，普通预设自动切回** | — | app.js:71、75、62 |
| 41 | 阈值偏移 | `vrShift`（−1000~1000 HU）在 `transfer()` 中平移标量查表位置 | — | app.js:68、79 |
| 42 | 采样步长 | `vrStep` 0.5–2 mm；`dt=max(stepSize,(t1−t0)/1535)`（长射线自动加密） | — | app.js:63、71、79 |
| 43 | opacity unit distance 校正 | `alpha=1−pow(1−alpha, dt*1000/unitDistance)`（mm 换算） | 预设 scalarOpacityUnitDistance | app.js:73 |
| 44 | 梯度不透明度 | 中心差分梯度（除以物理 spacing），模长/gradMax 过梯度 LUT 乘到 alpha；`disableGradientOpacity` 可关 | 预设 | app.js:72–73 |
| 45 | 基本光照 | `shade` 开启时：法线=归一化梯度（轴重排 vec3(−g.x,g.z,g.y)），`diff=abs(dot(n,−rd))`，颜色 = rgb·(ambient+diffuse·diff)+specular·pow(diff,specularPower) | 预设 lighting | app.js:74 |
| 46 | 插值方式 | `interpolationType` nearest→NearestFilter，linear→LinearFilter | 预设 | app.js:62 |
| 47 | 早终止与背景 | accum.a>0.985 截断；背景色 (3,11,27)/255 与 3D 场景底色一致 | — | app.js:71、75 |
| 48 | 自动启动体渲染 | 有预设时加载完成即 `vrEnabled.checked=true; vrToggle()` | — | app.js:83 |

### F. 其他

| # | 功能 | 实现要点 | 备注 |
|---|------|---------|------|
| 49 | 状态/错误提示 | 外层 `status()` + 内层 `#loading/#voxelInfo/#huInfo/#progress/#busy` spinner | picker.js:3；app.js:82 |
| 50 | 事件驱动按需渲染 | 无常驻 RAF 循环；`schedule()` 合并脏标记到 rAF 批量重绘（性能设计） | app.js:31–32 |
| 51 | 自动化测试钩子 | `window.viewerTest`（ct/state/uniforms 等）与 `window.volumeTest`（presets/uniforms/tableValue），供外部脚本验证（两个验证记录.json 即其产物） | app.js:80、83 |
| 52 | 明确不支持的输入 | 压缩 DICOM（JPEG/J2K/RLE）、多帧增强 DICOM、斜位序列、MRB/NRRD/OBJ/GLB/ZIP；失败时给出明确中文错误而非错误显示 | picker.js:18–24；使用说明.md |

**功能总数：52 项**（控件级穷举；若按面板归组约 12 组）。

**未实现（作者自述）**：散射、等值面模式、非 RGB 颜色空间、非线性控制点（midpoint/sharpness）、切口封盖、STL 减面、预设编辑保存。见 使用说明.md:23、44。

---

## 三、数据格式契约

### 3.1 `.vp.json`（Slicer 体属性文件，原生格式）

- Schema：`https://raw.githubusercontent.com/slicer/slicer/main/Modules/Loadable/VolumeRendering/Resources/Schema/volume-property-schema-v1.0.0.json#`（CT-Lung.vp.json:2）。
- 顶层 `volumeProperties[]`，网页只取 **`[0].components[0]`**（picker.js:44–45）。读取的字段：

| 字段 | 类型 | 网页用法 |
| --- | --- | --- |
| `interpolationType` | "linear"/"nearest" | 0/1 → 3D 纹理过滤（picker.js:45；app.js:62） |
| `shade` | bool | 光照开关 |
| `lighting.{ambient,diffuse,specular,specularPower}` | float | 光照系数（默认 .2/.7/.2/10） |
| `scalarOpacityUnitDistance` | float | alpha 步长校正（默认 1，须 >0） |
| `disableGradientOpacity` | bool | 关梯度不透明度 |
| `rgbTransferFunction.points[]` | `{x, color[3], midpoint, sharpness}` | 展开为 `[x, r, g, b, midpoint, sharpness]` |
| `scalarOpacity.points[]` / `gradientOpacity.points[]` | `{x, y, midpoint, sharpness}` | 展开为 `[x, y, midpoint, sharpness]` |

- **单位**：控制点 x = 标量值（CT 即 HU），y 与颜色 ∈ [0,1]；`scalarOpacityUnitDistance` 以毫米计（shader 中 `dt*1000/unitDistance`，app.js:73）。
- **被忽略的字段**：`effectiveRange`、`isoSurfaceValues`、`independentComponents`、`useClippedVoxelIntensity`、`clippedVoxelIntensity`、`scatteringAnisotropy`、`componentWeight`、midpoint/sharpness（后者若非 0.5/0 直接拒绝）。
- **汇总格式 `presets-all.json`**：`{source, componentNote, colorPointColumns, opacityPointColumns, presets:[{name, component, interpolationType(数值), shade, ambient, diffuse, specular, specularPower, scalarOpacityUnitDistance, disableGradientOpacity, colorSpace, color:[[hu,r,g,b,mid,sharp]...], scalarOpacity:[[hu,a,mid,sharp]...], gradientOpacity:[[g,a,mid,sharp]...]}]}`，31 个预设（CT-* 22 + MR-* 5 + DTI/US/uCT 4）。与原生文件同名去重，原生优先（picker.js:50–51 排序键 `Number(/\.vp\.json$/.test(name))`）。
- 目录共 32 个 `.vp.json`（31 预设 + `current-volume-property.vp.json` 快照）。

### 3.2 STL

- **命名规则 = TotalSegmentator 结构名**：小写下划线，如 `aorta.stl`、`lung_upper_lobe_left.stl`、`rib_left_1.stl`、`vertebrae_T10.stl`；本目录 61 个（肋骨未到每侧 12 根，rib 只到 *_9/*_10）。文件名去掉 `.stl` 即结构显示名，`color()` 与 `bone()` 筛选都依赖该命名（picker.js:28、46）。
- 解析：二进制（84 + n×50 字节）或 ASCII；**顶点直接使用，单位毫米，LPS 默认（可选 RAS），居中到 centerRAS，不索引化、不减面**（picker.js:29–34）。

### 3.3 DICOM 目录假设

- 目录任意层级递归（webkitdirectory），按 UID 自动分组，无需固定目录名；`lung/` 实测 320 张 `fu (N).dcm`，512×512。
- 隐含假设：**标准轴位序列**（ImageOrientationPatient=[1,0,0,0,1,0]）、等间距 z、单帧、MONOCHROME2 未压缩 8/16 位；层序按 IPPz 排序。像素 → HU 靠 slope/intercept。

### 3.4 页面间数据契约

`window.currentData = { ct: Float32Array(HU), modelData: [{name, positions:Float32Array(9/面), normals, color[3]}], meta: {dims:[nx,ny,nz], spacing:[sx,sy,dz]mm, originLPS:[3], centerRAS:[3], description, modality, models} }`；`window.importedPresets = normalizePreset() 输出数组`（picker.js:26、34、45）。

---

## 四、与 Unity 复现相关的注意点

1. **体渲染是纯 GPU 光线投射**，不依赖 Web 特有能力：Unity 里对应「Texture3D(R32F) + raymarch shader」。包 v1.8.0 的 DVR 即同类（kb/project-knowledge.md），差异点见下。
2. **传递函数实现为 8-bit 一维 LUT（8192 项 RGBA）+ 标量范围归一化 + shift 偏移**，而不是逐采样点解析 TF——Unity 复现建议同样烘焙 LUT 纹理，注意包里 `CreateTransferFunction()` 的 TF 纹理缓存坑（AGENTS.md §5）。
3. **alpha 的 unit distance 校正公式** `alpha=1−pow(1−a, dt·1000/unitDistance)` 是贴近 Slicer 显示的关键（app.js:73）；包的 DVR 未必做此校正，属需要新增的 shader 逻辑。
4. **梯度不透明度与光照**：中心差分梯度除以物理 spacing；光照用 `abs(dot(n,−rd))` 的简化的 Blinn-Phong 变体（无视线反射向量、无阴影/散射）。
5. **裁切最多 4 平面**（3 正交 + 1 自由），平面在 ray marching 中收紧 t0/t1 而非逐采样判断（app.js:70）——Unity 中对应 shader 的平面裁切参数；**无切口封盖**，STL 裁切同样只是显示级 clippingPlanes。
6. **MIP 与合成共用一条 shader 路径**（mode uniform），`-MIP` 后缀预设自动切模式——Unity 端用「预设名约定 + 模式枚举」即可复刻。
7. **CPU 端三线性重采样生成低分辨率预览纹理**（app.js:61）在 Unity 里非必需（GPU 直接采样原始体数据即可），但「256 预览 / 512 原始」的分辨率档位是交互体验设计，可保留为 LOD 选项。
8. **坐标转换链**（LPS→RAS→米→居中）集中在 `world()/ijk()/readSTL()`，Unity 端需保持同一套约定才能让 STL 与体数据对齐；本工程 STL 与 DICOM 已配准（AGENTS.md §4）。
9. **预设解析的宽容度是刻意的**：只支持 RGB + 线性控制点，其余报错拒绝。Unity 复现可沿用同一契约（见 §3.1），`volume_rendering_presets/` 已作为 Unity 资产导入，可直接复用 31 个预设数据。
10. **验收基准**：`体渲染验证记录.json` / `验证记录.json` 给出量化指标（32 预设可读、纹理 256×256×160、fullResolution 512×512×320、61 模型 5,815,720 三角形、ctSamplesMatched 100、clippingPassed、unsupportedOrientationRejected、errors 空），可作为 Unity 复现的验收清单。
11. **UI/交互可直接对标**：窗宽窗位（肺/软组织/骨预设 + 右键拖动调窗）、三视图联动十字线与层滑块、骨骼/器官筛选、自由切面（yaw/pitch/offset）——这些是通用图形学/UI 能力，无 Web 依赖。
12. **Two JSON 验证记录来自页面自带的 `window.viewerTest`/`window.volumeTest` 钩子**（app.js:80、83）——Unity 复现时建议同样暴露自动化验证入口。

---

## 附：信息缺口

- 4 张预览 PNG 因当前分析模型不支持图片输入未能目视核对，UI 布局改由 `unpacked-viewer-ui.html`（style + DOM 结构）推断：左栏 276px 侧栏（影像显示/跟随切片切割/自由角度切割/模型结构四组）+ 右侧 2×2 面板（轴位、3D、冠状、矢状），深色主题。此缺口不影响功能结论（全部控件已在源码中定位）。
- Three.js 精确小版本未能从 minified 代码提取（REVISION 常量名被压缩），只能确定 ≥ r150、版权至 2023。

---

## 五、与 Agent B 能力矩阵的交叉核对（第 7 步，2026-10-08）

核对对象：`kb/agent-b/findings-uvr-capability.md`（含文末「与 Agent A findings 的交叉核对」章节）。

### 5.1 核对方式

1. **逐条**：把 B 的「覆盖映射」表与本文 A1–A52 逐一比对（归宿、行号、是否重复/遗漏），并独立清点各口径下的覆盖数。
2. **抽查**：补充矩阵 B-a–B-u 中对「A 的功能」的描述与本文原文比对（A21 三个窗预设数值、A42 步长 0.5–2 mm 与 1536 步上限、A47 早终止 0.985、A34 320×320 三线性重切片、A16 缩放 0.3–8、A38 8192 项 LUT 等），另用 Python 实测复核了 `CT-Bones.vp.json` 与 `presets-all.json` 的 gradientOpacity 数值。
3. **机器复核**：`folder_viewer/验证记录.json` 与 `folder_viewer/体渲染验证记录.json` 逐字段与 B 引用的指标值比对。

### 5.2 核对为准确的项

- **覆盖完整性**：A1–A52 每一项在 B 的初版 16 行或补充 21 行中都有归宿，无遗漏项、无悬空引用；映射方向（如 A36→B#13、A40→B#9、A39→B#12、A43/44/45→B#14）无张冠李戴。
- **抽查的描述忠实度**：A21 窗预设（肺 [1500,−600]/软组织 [400,40]/骨 [1800,400]）、A42（0.5–2 mm、`dt=max(stepSize,(t1−t0)/1535)`）、A47（accum.a>0.985）、A34（320×320 三线性）、A24（theta/phi 轨道）、A38（8192 项 RGBA8 LUT）等均与本文原文一致。
- **关键事实**：预设 count=32（31+快照，与 `体渲染验证记录.json` 的 `info.count=32` 一致）、`folder_viewer/STL` 61 个（与 `验证记录.json` 的 `models:61` 一致；工程 `StreamingAssets/STL` 122 个的区分 B 亦已注明）、52 项 6 组、`.vp.json` schema v1.0.0、`-MIP` 后缀约定——B 的数字全部与本文一致。
- **验证记录.json 指标**：B 复用性表引用的 11 项值（models 61、triangles 5815720、dimensions [512,512,320]、count 32、texture [256,256,160]、fullResolution [512,512,320]、lung [−1000,2952]、clipping/mip/modelRestore/clippingPassed/unsupportedOrientationRejected 均 true、ctSamplesMatched 100、networkRequests []、errors []）**逐一与 JSON 原文相符**；「lung 值域 vs Chest NRRD 值域（−2048~1828）勿混」的提醒正确。
- **算术**：16+21=37 行、✅15+🛠️19+❌3=37，两式均验算正确（初版 ✅7/🛠️6/❌3，补充 ✅8/🛠️13）。

### 5.3 发现的问题清单（需要 B 修正）

| # | 问题 | B 原文位置 | 正确值 / 依据 |
|---|------|-----------|---------------|
| 1 | **映射行号错**：「A46（插值方式）→ 新增 B-b 行」 | 覆盖映射表倒数第 3 行 | 应为 **B-o**（B-o 才是 A46 插值方式；B-b 是 A3 序列合法性校验）。同类写法「A47 → B-q」是对的，可佐证此为笔误 |
| 2 | **A28 重复计数**：A28（结构名搜索）既在 B#6 行注明「A28 搜索并入」，又被列入末行的补充矩阵清单；但补充矩阵 B-a–B-u 中**没有任何一行对应 A28** | 覆盖映射表 B#6 行 + 末行清单 | 应从末行清单中删除 A28（清单由 21 项变 20 项）；A28 的归宿就是 B#6 |
| 3 | **统计口径不自洽**：「33 项被初版矩阵覆盖、遗漏 19 项」无法从其自身表格推出；文首还写「新增 19 行」与文末「实际新增 21 行」矛盾；「2 项原矩阵备注独立成行」只能明确识别 1 行（B-n，A38 与 B#10 重复） | 交叉核对章节结论段、补充矩阵末尾括注、最终统计 | 按映射表独立清点：映射到初版 16 行的不同 A 项为 **30** 项，仅由补充矩阵覆盖的为 **22** 项（补充矩阵 21 行覆盖 23 个不同 A 项，其中 A38 与 B#10 重复；B-d 合并 A12+13、B-e 合并 A16+22）。即使把 A3（B#1 备注提及）、A16（B#2 描述含"滚轮逐层"）按部分覆盖记入初版，也只能得到 32/20，得不出 33/19。**建议 B 重述口径**（例如"30 项初版覆盖 + 22 项补充覆盖（含 1 项重复评估），21 行补充矩阵"）。行数与结论分布算术本身无误 |
| 4 | **误引 A 原文**：「gradientOpacity……A 的 findings 亦确认被 web 忽略」——不成立：本文 §3.1 的「被忽略字段」清单**不含** gradientOpacity，且 A44 明确记录 web **实现了**梯度不透明度（中心差分梯度 × 梯度 LUT，`disableGradientOpacity` 可关） | B#14 实现思路末句；另「当前全为常数 1」（映射要点 6）过度概括 | 正确表述：「web 实现了梯度不透明度功能，但当前预设数据基本无实际效果」——实测 `presets-all.json` 31 个预设中 **29 个** gradientOpacity 恒为 1，例外为 **US-Fetal、uCT-Skull** 两个非 CT 预设（本次 Python 实测）；22 个 CT-* 与 5 个 MR-* 均为常数 1，故 B「对本工程 CT 预设可忽略」的结论不受影响，仅需改措辞与归因 |
| 5 | 笔误两处 | 交叉核对章节首段「unpicked-viewer-app.js」；B#12 标题「256³ vs 原始 512」 | 前者应为 `unpacked-viewer-app.js`；后者 web 预览纹理为 **256×256×160（非立方，最长边 256）**，见 `体渲染验证记录.json` 的 `texture` 字段，建议改为「最长边 256 vs 原始 512」 |

### 5.4 最终结论

- B 的 52 项映射**内容层面完备且基本忠实**：无遗漏项、无错误归宿，抽查的关键参数与两个验证记录.json 的指标引用全部准确；修正 2 处原结论（B#14 OU 校正、B-i 3D 相机）的理由充分，与本文证据一致。
- 问题清单 5 条中，#1/#2/#5 为笔误级，#3 为统计记账口径问题，#4 为一处归因错误——**均不触碰结论本身**：❌3 缺口（STL 加载、vp.json 导入及其依附项）、✅15/🛠️19/❌3 分布、建议实现顺序在修正后原样成立。
- **结论：B 的映射在修正上表 5 条（实质为 #1–#4，#5 顺手改）后，可作为复现工作量的定稿依据。** 已在上述清单中逐条给出正确值，B 只需按表更正文字，无需重做评估。
