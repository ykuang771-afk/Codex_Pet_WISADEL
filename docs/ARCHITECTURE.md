# 项目架构

```text
Codex 桌面应用
├─ pets/wisadel-rest/       v2 原生宠物：pet.json + APNG 图集 + WebP 静态备用
└─ wisadel-companion 插件   可选；与原生宠物分开运行
   ├─ MCP server.py         统计查询、设置、面板 HTML
   ├─ Hooks hook.py         SessionStart/UserPromptSubmit/Stop/Interrupt 事件
   ├─ Companion.exe         Windows Raw Input、宠物位置与悬停绘制
   ├─ usage_ledger.py       本地用量账本与去重
   └─ %LOCALAPPDATA%/WisadelCompanion/  私有计数、偏好和账本
```

## 数据流

1. Codex 原生宠物只读取 `pet.json` 指向的图集，按九种标准动作及 v2 的十六个方向画格播放。这里的 `spritesheet.png` 是 APNG，图集的每个时间点共享播放时钟。
2. 插件的 MCP 服务启动配套程序，并向 Codex 提供统计工具及面板。Hooks 可加快任务事件响应，仍需用户在 Codex 中信任。
3. Windows 配套程序从 Raw Input 只取键/鼠按下事件做计数；UI Automation 只取浮动宠物及其按钮的边界，让悬停面板跟随。计数数据留在本机。
4. 用量账本读取 Codex 已保存的请求用量元数据，写入本机 SQLite。额度查询经本机 Codex app-server 读取当前账号额度。两者都不会改变宠物图集的原生状态机。

## 仓库各目录

- `pets/`：可直接安装的 `wisadel-rest` v2 原生宠物。三个文件必须一起复制。
- `stickers/`：当前内嵌版用到的 7 个原始 GIF。
- `frames/native/`：把原始动作处理为 192×208 画格后的 235 张逐帧 PNG。`idle_rest` 对应当前宠物的 idle 行；`running-left` 是向右移动逐帧镜像；`jumping` 的 15 帧中有 5 幅原画和 10 幅只调整位置的中间帧。
- `frames/look-directions/`：从 v2 静态图集的第 9–10 行提取的 16 张方向画格，角度按顺时针排序，0° 向上、90° 向右。
- `plugins/wisadel-companion/`：插件清单、C# 与 Python 源码、已编译 Windows 可执行文件、面板和插件技能。
- `.agents/plugins/marketplace.json`：Codex 本地插件市场清单。安装器把它和插件复制到用户数据目录后注册。
- `scripts/install.ps1`：检查版本、备份旧包、复制宠物与注册插件。`-DryRun` 不写入。
- `docs/`：帧数、预览与素材来源索引。

## 版本与兼容性

v2 图集为 8 列 11 行：标准状态 0–8 行，方向 9–10 行；`spriteVersionNumber` 必须是 2。版本和实际高度不符会导致加载失败。原生状态由 Codex 决定，单靠素材无法控制状态停留时间、强制从头播放或实现跨状态淡入淡出。插件是 Windows 本地辅助程序；宠物本身可以独立安装。
