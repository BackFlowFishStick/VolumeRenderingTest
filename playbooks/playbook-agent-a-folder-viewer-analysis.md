# Playbook — Agent A：folder_viewer WebGL 查看器功能分析

## 目标

穷举 `C:\UnityProjs\TestVolumeRendering\folder_viewer\` 中 WebGL 影像查看器（`文件夹影像模型查看器.html`）的功能，形成结构化功能清单，作为 Agent B 做包复现可行性评估的输入。

## 输入材料（按此顺序读）

1. `folder_viewer/使用说明.md` —— 作者写的功能说明，最快的功能面概览
2. `folder_viewer/体渲染验证记录.json`、`folder_viewer/验证记录.json` —— 验证过的功能与参数记录
3. 预览截图（`体渲染肺预览.png` 等 4 张）—— 用 Read 工具直接看图，记录 UI 布局与可视效果
4. `文件夹影像模型查看器.html` —— 主程序（单文件 HTML+JS，可能很大，分段读），重点分析：
   - 使用了哪些库（`DICOM-PARSER-LICENSE.txt`、`THREE-LICENSE.txt` 表明用了 dicom-parser 和 Three.js）
   - 数据加载：文件夹选择、DICOM 解析、STL 加载、预设加载的入口函数
   - 渲染方式：Three.js 用的是体渲染（shader raycasting？）还是网格重建？关键 shader/material 代码
   - 交互功能：每个 UI 控件（slider/按钮/菜单）对应什么功能（窗宽窗位、裁切、旋转、预设切换等）
   - `volume_rendering_presets/*.vp.json` 的解析与应用逻辑（这决定预设文件格式契约）
5. `STL/` 与 `lung/` 目录 —— 只需确认数量与命名规则，不用逐个读

## 交付物

### 1. `kb/agent-a/findings-folder-viewer.md`（主要产出）

结构要求：

```markdown
# folder_viewer 功能分析
## 技术栈概览（库、渲染方式、架构）
## 功能清单
| # | 功能 | 实现要点（函数/shader/库） | 数据依赖 | 备注 |
|---|------|--------------------------|----------|------|
（逐条穷举，宁多勿漏：加载、渲染、交互、预设、导出/截图……）
## 数据格式契约
（.vp.json 的字段结构；STL 命名规则；DICOM 目录假设）
## 与 Unity 复现相关的注意点
（哪些功能依赖 Web 特有能力，哪些是通用图形学能力）
```

### 2. 更新 `kb/agent-a/progress.md`

把完成的步骤勾选；发现的额外工作追加到待办。

## 完成标准

- 功能清单覆盖 HTML 中全部 UI 控件与渲染路径（每个控件在代码里能指出对应实现位置/函数名）
- .vp.json 格式契约完整（字段、插值方式、单位）
- 结论有源码依据（引用文件与大致行号/函数名）
- progress.md 全部勾选或注明阻塞原因
