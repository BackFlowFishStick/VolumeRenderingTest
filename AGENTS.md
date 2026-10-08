# AGENTS.md — 多 Agent 协作规范（本工作区）

> 所有 Agent 在开始工作前**必须先读完本文档**，再读自己角色的 playbook 和 `kb/` 中的进度文件。

## 1. 工作区结构

| 路径 | 说明 |
| --- | --- |
| `VolumeRendering_2022/` | **当前唯一开发工程**（Unity 2022.3.13f1c1，URP 14） |
| `VolumeRenderingSample/` | Unity 6 工程，**已冻结（2026-09-29 起）**，仅作历史参考，禁止改动 |
| `UnityVolumeRendering/` | Easy Volume Renderer v1.8.0 本地包源码（`com.mlavik1.easyvolumerenderer`），被 2022 工程以 file: 引用 |
| `LocalDoc/` | 包的中文速查文档（11 篇） |
| `folder_viewer/` | 待分析的 WebGL 影像查看器（Three.js + dicom-parser 单页应用） |
| `playbooks/` | 各角色的任务手册（工作流程、交付物、完成标准） |
| `kb/` | 知识库：项目知识、决策记录、各 Agent 的进度与产出 |

## 2. 铁律

1. **所有代码改动只允许发生在 `VolumeRendering_2022/`**。Unity 6 工程、LocalDoc 一律不改。
2. `UnityVolumeRendering/` 包源码尽量不改；确需修改时，必须先在 `kb/decisions.md` 记录理由和改动点（该包被两个工程共享，虽然 Unity 6 已冻结，仍要保持可追溯）。
3. 每次工作开始：读 `kb/agent-X/progress.md` 了解自己上次进度；结束前**必须更新** `progress.md`（完成项打勾、新增待办），分析结论写入自己的 `findings-*.md`。
4. 编写代码前先读 `kb/project-knowledge.md`——里面有已踩过的坑（TF 纹理缓存、渲染模式差异、SimpleITK 等），不要重复踩。
5. Unity 编辑器对 `VolumeRendering_2022` 保持开启，**自动刷新不可靠**：改完脚本需要用户点一下编辑器窗口（或 Ctrl+R）才编译；验证编译以 `Library/ScriptAssemblies/Assembly-CSharp.dll` 的时间戳晚于源码为准。

## 3. 角色分工

| 角色 | 任务 | Playbook | 产出目录 |
| --- | --- | --- | --- |
| **Agent A（分析）** | 分析 `folder_viewer/` 的 WebGL 查看器，穷举其功能清单与实现方式 | `playbooks/playbook-agent-a-folder-viewer-analysis.md` | `kb/agent-a/` |
| **Agent B（可行性）** | 评估上述功能能否用 Easy Volume Renderer 包在 2022 工程中复现，输出能力矩阵与缺口清单 | `playbooks/playbook-agent-b-uvr-feasibility.md` | `kb/agent-b/` |

两个角色可并行；B 在 A 未完成时允许基于 `folder_viewer/使用说明.md` 和 HTML 源码自行归纳功能面，但**最终映射表必须与 A 的 findings 交叉核对一次**（在 progress.md 里记录核对状态）。

## 4. 数据资产（VolumeRendering_2022/Assets/StreamingAssets/）

| 路径 | 内容 |
| --- | --- |
| `lung/` | 320 张肺部 DICOM 序列（`fu (N).dcm`，约 512×512） |
| `Chest_Reconstruction.mrb` | 3D Slicer 打包的胸部 CT（zip），解包工具见下 |
| `Datasets/` | mrb 解包产物：`Chest_CT.nrrd`（CT 主体）、`Chest_Seg.nrrd`（TotalSegmentator 分割标注）、`ReconstructionLabels.csv`（标签表） |
| `STL/` | **122 个按解剖结构拆分的 STL 网格**（文件名 = TotalSegmentator 结构名，与分割标注同名） |
| `volume_rendering_presets/` | 3D Slicer 风格的体渲染预设（`CT-*.vp.json`），已作为 Unity 资产导入 |

## 5. 关键工程事实（详见 kb/project-knowledge.md）

- 包 v1.8.0：支持 DICOM/NRRD/NIfTI/RAW；**不支持 .mrb / .vti / .vtk / STL**；NRRD 依赖 SimpleITK（2022 工程已启用）。
- 只有 DVR 模式读取 TF 的 alpha；等值面强制不透明、MIP 完全不采样 TF。
- `CreateTransferFunction()` 会预生成默认 TF 纹理缓存，修改控制点后必须调 `GenerateTexture()`。
- 已完成功能：DICOM/NRRD 运行时加载、分割叠加（61 结构显隐/隔离）、切片查看器 UI、教学版 HU 预设、mrb 解包菜单。

## 6. 完成定义（Definition of Done）

- 代码改动：`VolumeRendering_2022` 编译零错误（以 ScriptAssemblies 时间戳验证）+ 功能在 Play 模式下人工验证通过 + 与 Unity 6 工程无关（不需要同步）。
- 分析任务：findings 文档结构完整、结论有源码/数据依据（注明文件路径）、progress.md 全部勾选。

## 7. Git 版本控制

仓库：工作区根目录（`main` 分支为可运行基线）。`.gitignore` 已配置 Unity 标准忽略规则；**注意**：仓库归属用户与当前登录用户不同，已通过 `git config --global --add safe.directory C:/UnityProjs/TestVolumeRendering` 解决，若子 Agent 仍遇 "dubious ownership" 报错，重跑该命令即可。

### 7.1 子 Agent 工作流（允许并期望开分支）

1. **开工前**：`git status` 确认工作区干净（有未提交改动先向主 Agent 报告，不要混入自己的提交）。
2. **切分支**：从最新 `main` 切出，命名规范：
   - `agent-a/<任务名>`、`agent-b/<任务名>` —— 子 Agent 任务分支
   - `feat/<功能名>`、`fix/<问题名>` —— 一般功能/修复分支
3. **小步提交**：在分支上开发，commit message 用中文写清"改了什么、为什么"；**只提交与任务相关的文件**（不要 `git add -A` 一把梭）。
4. **功能测试**：按 playbook 的完成定义自测（编译零错误 + Play 模式验证 + progress.md/findings 更新）。
5. **请求合并**：测试通过后通知主 Agent"分支 X 已就绪"，**子 Agent 不要自行合并到 main**。

### 7.2 主 Agent 合并规则

1. **合并前检查**：`git diff main..分支` 审查改动范围符合铁律（只动 `VolumeRendering_2022/` 与 kb/playbooks）；编译验证通过；对应 progress.md/findings 已更新。
2. **合并方式**：`git merge --no-ff <分支>`，保留分支历史；合并信息写明功能来源与验证结论。
3. **冲突处理**：由主 Agent 解决；冲突双方逻辑不确定时，回到实现该分支的 Agent 确认意图，不擅自取舍。
4. **合并后**：`git branch -d <分支>` 清理，并在对应 Agent 的 progress.md（或 `kb/decisions.md`，若是架构类变更）登记合并结果。
5. **基线保护**：`main` 上不允许直接改代码（热修例外需在 commit message 注明 HOTFIX）；若合并后 main 出现问题，优先 `git revert` 而不是 force push。
