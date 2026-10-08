# 决策记录（Decisions）

> 影响后续所有工作的决策按时间倒序记录在这里。Agent 有新决策时追加。

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
