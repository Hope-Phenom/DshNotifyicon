# dsh-notify-hook

dsh 通知增强插件：在每一轮回答结束（`turn/end`）时向 stdout 输出一行结构化通知，供 DshNotifyicon 解析并展示托盘通知/执行外部命令。

## 启用条件

- 由 DshNotifyicon 启动 dsh 时，会自动注入 `DSH_NOTIFY_ENABLED=1`；
- 手动运行 dsh 时不会输出，避免干扰。

## 可选环境变量

| 变量 | 说明 |
|---|---|
| `DSH_NOTIFY_ENABLED` | `1` 时启用输出 |
| `DSH_NOTIFY_INCLUDE_SUBAGENTS` | `1` 时子代理/子任务的 `turn/end` 也输出 |

## 输出格式

```text
DSH_NOTIFY {"event":"turn-end","sessionId":"...","parentSessionId":null,"title":"...","turn":1,"reason":"completed","durationMs":1234}
```

`title` 来自会话日志中的最新 `session/title` 事件；如果没有标题则为空字符串。
