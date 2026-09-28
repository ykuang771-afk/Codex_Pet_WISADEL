# 维什戴尔统计助手

这是主仓库中的可选 Windows Codex 插件。请从[仓库安装说明](../../README.md#安装)运行安装器；安装器会为本机生成 `.mcp.json` 和 `hooks/hooks.json`，并通过 Codex CLI 注册。仓库源码不包含任何用户计数或聊天记录。

`scripts/` 含 MCP 服务、Hooks、用量账本和 Windows C# 配套程序源码；`bin/WisadelCompanion.exe` 是从该源码构建的程序，`scripts/build.ps1` 可在装有 .NET Framework 编译器的 Windows 上重建。`ui/` 是统计面板，`skills/wisadel-stats/` 是助手技能。数据保存在 `%LOCALAPPDATA%/WisadelCompanion/`，详见主 README 的权限说明。
