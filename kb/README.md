# kb 目录索引（Knowledge Base）

> Agent 的工作记忆：所有结论、进度、决策都落在这里，保证任何 Agent（或新会话）接手时不丢上下文。

## 文件说明

| 文件 | 用途 | 维护者 |
| --- | --- | --- |
| `project-knowledge.md` | 已验证的工程事实与踩坑记录（动代码前必读） | 主 Agent / 全体 |
| `decisions.md` | 决策记录（架构、范围、规范类决策，倒序追加） | 全体 |
| `agent-a/progress.md` | Agent A 工作进度清单 | Agent A |
| `agent-a/findings-folder-viewer.md` | folder_viewer 功能分析产出 | Agent A |
| `agent-b/progress.md` | Agent B 工作进度清单 | Agent B |
| `agent-b/findings-uvr-capability.md` | UVR 复现可行性评估产出 | Agent B |

## 使用协议

1. **开工前**：读 `AGENTS.md` → 读自己角色 playbook → 读自己的 `progress.md`（接着上次干）。
2. **收工前**：更新 `progress.md`（勾选完成项、追加新发现的待办）；把结论写进 `findings-*.md`（不要只留在对话里）。
3. **新知识**：验证出新的包行为/坑 → 追加到 `project-knowledge.md` 对应小节；范围类决策 → 追加到 `decisions.md`。
4. **交叉引用**：B 的映射表完成后要与 A 的 findings 交叉核对一次，并在双方 progress.md 记录核对状态。
