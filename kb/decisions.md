# 决策记录（Decisions）

> 影响后续所有工作的决策按时间倒序记录在这里。Agent 有新决策时追加。

## 2026-10-09

- **D-008 解除 STL 搁置、采用 pb_Stl 拷源码方案**（分支 `feat/stl-loader`）：用户决策解除 D-007 搁置。调研（kb/stl-package-research.md）确认现成方案 pb_Stl（MIT，github.com/karl-/pb_Stl）可覆盖解析器全部工作，选**拷源码进工程**（非 git 依赖，MIT 允许、可直接改造），自研工作量 2~3 天压缩到约 1 天。关键技术结论（源码调研，详见 StlModelLoader.cs 注释）：
  1. 包的 SimpleITK 导入器做了 `DICOMOrient("RSA")`，dataset 轴为 R/S/A（非 NRRD 原始 LPS），`rotation` 字段恒 identity；
  2. 体渲染 shader 以容器局部 ±0.5 空间采样（uvw = vertexLocal + 0.5，无轴翻转）；
  3. STL 对齐链：LPS → RSA 体素索引（i_R=(nL−1)−(x−ox)/sx，j_S=(z−oz)/sz，k_A=(nP−1)−(y−oy)/sy）→ 容器局部 ((idx+0.5)/dim−0.5)；LPS 参数从 Chest_CT.nrrd ASCII 头解析（包不保留空间信息）；映射经分割质心 vs STL 质心互校（胸骨 <1mm）；
  4. 顶点烘入网格而非挂 Transform（容器 scale 为非均匀米制缩放，会破坏法线）。

## 2026-10-08

- **D-007 STL 功能搁置、vp.json 预设优先**：包对 STL 无任何支持（grep 零命中，B findings #5/#6/#7，工作量 2~3 天/1 天），用户决策**暂且搁置 STL**，实现顺序调整为：先完成 vp.json 预设导入（B#10+#9，分支 `feat/vp-json-preset-importer`），STL 待其验证合并后由主 Agent 单独排期。已同步标注 AGENTS.md §3/§4/§5，禁止 Agent 自行启动 STL 实现任务。（注：vp.json 导入器的实现决策见 D-006，随功能分支合并入档。）

### 实现期（vp.json 分支）

- **D-006 vp.json 预设导入器的三个口径选择**（分支 `feat/vp-json-preset-importer`，`SlicerPresetLibrary.cs`）：
  1. **归一化分母用数据集实际值域**（`dataset.GetMin/MaxDataValue()`，如 Chest -2048~1828），不用预设 `effectiveRange`——否则控制点无法落到包 TF 纹理的 [0,1] 纹理空间；
  2. **端点钳制语义**：显式补 (0, 首值) 与 (1, 末值) 控制点，复刻 Slicer 对控制点区间外的取端点行为；不补的话包 `GenerateTexture()` 会注入默认 (0, alpha=0)/(1, alpha=1)/(0/1, Color.white)，超出预设值域部分外观错误（骨骼全白全不透明）；
  3. **渲染模式跟随预设名**：`-MIP` 后缀自动切 MIP、普通预设切回 DVR（复刻 web A40 行为；只有 DVR 采样 TF alpha，kb 1.2）。
  另：阈值偏移（web vrShift）实现为控制点 HU 平移（`hu + shift` 后归一化）；gradientOpacity/lighting/isoSurfaceValues 不映射（依据 B#14 与 A 交叉核对结论），待后续 #14 可选项再评估。
  **实测补充（2026-10-08 首轮验证）**：Slicer 导出的预设含双精度尾差重复控制点（x 与 x·(1+ε)，如 uCT-Bone-16bit 的 33600 / 33600.00000000001；DTI-FA-Brain 的 0 / 2.2e-308），两点的颜色/alpha 完全相同——解析按 float32 读取后二者自然重合，用 `Mathf.Approximately` 归并为一点（保留后者），仅 x 真回退才拒绝。原"严格递增否则拒绝"的校验因此放宽，否则 DTI-FA-Brain、uCT-Bone-16bit/8bit 三个预设被误拒（29/32 → 修正后 32/32）。

## 2026-09-29

- **D-005 启用 Git 版本控制与分支工作流**：仓库位于工作区根（main 分支）。子 Agent 在 `agent-x/<任务名>` 等分支上开发与测试，**合并权在主 Agent**（--no-ff 合并、合并前检查、冲突回询实现者）；safe.directory 例外已配置；Unity 标准忽略规则已在 .gitignore。详见 AGENTS.md 第 7 节。
- **D-003 开发重心迁移**：Unity 6 工程（`VolumeRenderingSample/`）冻结，**后续所有改动只在 `VolumeRendering_2022/` 进行**。Unity 6 工程仅作历史参考，不再同步、不再编译验证。
- **D-002 开启多 Agent 协作**：Agent A 负责 folder_viewer 功能分析，Agent B 负责 UVR 复现可行性评估；工作流与产出物见 `AGENTS.md` 与 `playbooks/`。
- **D-001 新数据纳入**：`StreamingAssets/STL/`（122 个结构网格）与 `StreamingAssets/volume_rendering_presets/`（3D Slicer 风格 .vp.json）纳入工程数据资产，作为复现 folder_viewer 功能的输入。

## 2026-09-20（迁移期，简要）

- 迁移 Unity6 → 2022 工程：脚本/数据/manifest/输入设置（inputsystem 1.7.0 + activeInputHandler=1）；源脚本加双版本宏保持 Unity6 可编译（后随 D-003 冻结）。
- SimpleITK 在 2022 工程独立启用（D-004 前提：NRRD 导入必需）。

## 2026-09-17~28（初建期，详见 git 式历史对话，此处仅记关键）

- 初始评估：mrb 需解包（包不支持 .mrb）；lung DICOM 直接可用。
- 教学版预设路线：DVR 专用、CT 本体 alpha 全零、仅分割色显示（注意 1.1 节 TF 缓存坑）。
- 切片查看器走自定义 UI 着色器（SliceUI.shader）而非复用包矩阵式切片着色器。
