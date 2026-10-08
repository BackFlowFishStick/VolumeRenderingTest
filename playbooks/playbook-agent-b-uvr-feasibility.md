# Playbook — Agent B：Easy Volume Renderer 复现可行性评估

## 目标

针对 `folder_viewer/` WebGL 查看器的功能面，逐项评估能否用 Easy Volume Renderer v1.8.0（本地包 `UnityVolumeRendering/`）在 `VolumeRendering_2022/` 工程中复现，输出能力矩阵与缺口清单。

## 前置阅读（按此顺序）

1. `AGENTS.md` —— 工作区规范与铁律
2. `kb/project-knowledge.md` —— 已验证的包行为与踩坑记录（TF 缓存、渲染模式差异、SimpleITK 等）
3. `kb/agent-a/findings-folder-viewer.md` —— Agent A 的功能清单（**若尚不存在**，先自行读 `folder_viewer/使用说明.md` 与 HTML 概览归纳功能面，并在 progress.md 记录"A 产出未就绪，功能面为自行归纳"）
4. `LocalDoc/03-核心功能详解.md`、`04-数据导入说明.md` —— 包能力速查

## 分析方法

- 对每个功能，先查包源码（`UnityVolumeRendering/Runtime/`、`Editor/`）确认有无现成 API，**用 grep 验证，不要凭印象**（例：STL 支持 → grep "stl" 应为零命中 → 缺口）。
- 已知起点（仍需你验证细节）：
  - 包**没有** STL 加载器；`StreamingAssets/STL/` 有 122 个按结构命名的网格 → 需要自研 STL 解析（二进制/ASCII 两种格式）+ Unity Mesh 展示，或评估逐结构显隐与分割标注的联动
  - `volume_rendering_presets/*.vp.json` 是 3D Slicer 风格预设 → 读 1-2 个样例总结字段结构，评估映射到包 `TransferFunction`（颜色/alpha 控制点）的可行性；注意 `kb/project-knowledge.md` 里的 TF 纹理缓存坑
  - DICOM/NRRD 运行时加载、分割叠加、切片查看器、HU 预设 **已经在 2022 工程实现**（见 `kb/project-knowledge.md` 第 7 条）——这些是"已复现"，不要重复评估
- 结论分级：✅ 直接可用 / 🛠️ 需开发（附实现思路与工作量估计：小时级/天级） / ❌ 包能力缺口（附替代方案）

## 交付物

### 1. `kb/agent-b/findings-uvr-capability.md`（主要产出）

```markdown
# UVR 复现可行性评估
## 能力矩阵
| # | folder_viewer 功能 | UVR 现状 | 结论 | 实现思路 | 工作量 |
|---|--------------------|----------|------|----------|--------|
## 缺口清单与替代方案
## .vp.json 预设格式分析
## 建议的实现顺序（按性价比排序）
```

### 2. 更新 `kb/agent-b/progress.md`

## 完成标准

- 功能矩阵覆盖 Agent A 清单的全部条目（或注明"基于自行归纳"）
- 每个结论有源码 grep/读取依据（注明文件路径）
- 缺口项都有替代方案或工作量估计
- progress.md 全部勾选或注明阻塞原因
