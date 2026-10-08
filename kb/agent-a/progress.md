# Agent A 进度 — folder_viewer 功能分析

> 每完成一项把 `[ ]` 改为 `[x]`；新增工作追加到末尾。阻塞原因写在对应项下方。

## 状态：交叉核对完成（2026-10-08），分析任务全部收尾

## 步骤清单

- [x] 1. 读 `folder_viewer/使用说明.md`，提炼作者声明的功能面
- [x] 2. 读 `folder_viewer/体渲染验证记录.json` 与 `验证记录.json`，提取已验证功能与参数
- [x] 3. 预览截图 —— **部分受阻**：当前分析模型不支持图片输入，4 张 PNG 未能目视核对；UI 布局改为从 `unpacked-viewer-ui.html`（style+DOM）推断，功能定位不受影响（见 findings「附：信息缺口」）
- [x] 4. 分析 `文件夹影像模型查看器.html`：
  - [x] 4.1 技术栈：dicom-parser 1.8.12 + Three.js（minified build ≥ r150）；双层架构（外层选择页 + 内嵌 VIEWER_HTML 字符串经 iframe srcdoc 加载）
  - [x] 4.2 数据加载链路：DICOM 扫描/分组/校验/HU 重建（picker.js readVolume）、STL 二进制+ASCII 解析（readSTL）、预设导入与 normalizePreset 校验
  - [x] 4.3 体渲染实现：GLSL3 ShaderMaterial 光线投射（BackSide 盒子、Data3DTexture R32F、8192 项 1D TF LUT、1536 步上限、合成/MIP 双模式），app.js:64–75
  - [x] 4.4 交互控件逐个列举：共 52 项功能，全部控件已定位到函数（findings 功能清单 A–F 表）
  - [x] 4.5 裁切/切面/窗宽窗位：3 正交 + 1 自由切面裁切（clippingPlanes + shader clips[4] 同步）、WW/WL 滑动/数字/预设三入口、自由切面 320×320 三线性重切片
- [x] 5. 归纳 `.vp.json` 格式契约（Slicer volume-property-schema v1.0.0；只取 volumeProperties[0].components[0]；控制点 6/4 元展开；midpoint=0.5/sharpness=0 强制；忽略字段清单；presets-all.json 汇总格式；同名去重原生优先）
- [x] 6. 写出 `findings-folder-viewer.md`（按 playbook 模板）
- [x] 7. 与 Agent B 交叉核对映射表（B 完成后执行，记录核对结论）
  - **核对结论（2026-10-08）**：逐条核对 B 的覆盖映射 + 抽查补充矩阵描述 + 机器复核两个验证记录.json 与 CT-Bones.vp.json。52 项全部有归宿（无遗漏、无缺失行），关键事实（count=32、STL 61、52 项 6 组、schema v1.0.0、`-MIP` 后缀）与 B 引用一致，JSON 指标值 11 项逐一相符。发现 4 处需 B 修正：① A46 映射行号误写为 B-b（应为 B-o）；② A28 重复计数（已并入 B#6 却又列入补充清单，补充矩阵无对应行）；③ 「33 覆盖/19 遗漏」口径与自身表格不符（实际 30 初版覆盖/22 仅补充覆盖；行数 16+21=37 与 ✅15/🛠️19/❌3=37 算术正确）；④ B#14 误引「A 确认 gradientOpacity 被 web 忽略」（A44 记录 web 已实现该功能；且 31 预设中 2 个非常数）。详见 findings 末节。**结论：B 的映射在修正上述记账/表述问题后可作为复现工作量定稿依据；❌3 缺口、结论分布与实现顺序均不受影响。**

## 交付物

- [x] `kb/agent-a/findings-folder-viewer.md`
- [x] 解包源码产物（供 Agent B 交叉核对，引用行号基于这些文件）：
  - `kb/agent-a/unpacked-viewer.html`（内层查看器完整 HTML）
  - `kb/agent-a/unpacked-viewer-ui.html`（内层 UI 结构）
  - `kb/agent-a/unpacked-viewer-app.js`（内层主程序 86 行，含 shader）
  - `kb/agent-a/unpacked-picker.js`（外层选择页 JS）

## 新发现的工作项 / 给 B 的提示

- [x] 体渲染 alpha 的 unit distance 校正（`alpha=1−pow(1−a, dt·1000/unitDistance)`）需确认 Easy Volume Renderer DVR 是否已做，未做则是 shader 改造点（findings §四.3）——**B 已回答（B#14 修正）**：包 DVR shader 已有同构公式（`VolumeRendering.hlsl:321-323`，指数由 `_SamplingRateMultiplier` 决定），复现只需把指数换成 `dt·1000/unitDistance` + 传参，工作量降为 1 天（可选优化）。
- [x] 裁切平面同步进体渲染 shader（最多 4 平面收紧 ray 区间）需确认包内 DVR 是否支持裁切——**B 已回答（B#13 ✅）**：包现成 `SlicingPlane.cs`/`CutoutBox`/`CrossSectionManager`，无需开发；"反转保留方向"= 平面法线取反。
- [x] `-MIP` 预设名后缀 → 自动切换投射模式的约定需在 Unity 侧复刻（若复现预设切换）——**B 已回答（B#9 ✅）**：预设名含 "-MIP" 调 `SetRenderMode(MIP)`，两行 UI 逻辑，随 #10 预设导入器一起做。
- [x] Unity 端验收可直接复用两个验证记录.json 的量化指标（findings §四.10）——**B 已回答**：B 的复用性评估逐项引用了这些指标（本次核对确认全部引用值准确），并给出落地建议 = Editor 校验脚本（B-t，1 天），输出与验证记录同构 JSON。
- [ ] （可选）若需要目视核对 4 张预览 PNG 的视觉效果，需由支持图片输入的会话补充确认——B 未涉及，维持待办（不阻塞任何结论）。

## 阻塞 / 备注

- 图片输入不受支持导致步骤 3 降级完成（以源码推断 UI 布局替代），不构成后续阻塞。
- Three.js 精确小版本无法从 minified 代码提取（REVISION 被压缩改名），仅确定 ≥ r150（版权 2010–2023），不影响结论。
