# 维什戴尔 · Codex 内嵌宠物

> Windows 版 Codex 桌面应用的 **维什戴尔** 宠物素材与默认安装的统计助手。

## 给 Codex 的一键安装提示词

复制下面整段，发给你自己电脑上的 Codex。安装前请先看[权限说明](#权限与隐私)；安装助手会要求本机文件写入、插件注册及 Hooks 信任审核。

```text
请帮我安装 https://github.com/ykuang771-afk/Codex_Pet_WISADEL 中的维什戴尔 Codex 内嵌宠物和配套助手。先阅读仓库 README、docs/ARCHITECTURE.md 和 docs/FRAMES.md，检查当前系统是否为 Windows、Codex CLI 是否可用，并向我说明将写入 ~/.codex/pets、%LOCALAPPDATA%/WisadelCompanion，以及配套助手需要的键鼠计数、无障碍位置读取、本地 Codex 用量日志和只读额度查询权限。不要读取或上传我的聊天正文、键入内容或凭据。确认我授权这些本机权限后，克隆仓库到我选择的目录，先运行 scripts/install.ps1 -DryRun 检查，再运行 scripts/install.ps1（默认 v2 休息版及助手；如果我只要宠物则加 -PetOnly）。安装前备份已有同名宠物；遇到已安装的同名助手不要重复启动。按 Codex 界面完成 Hooks 信任审核，重启 Codex，选择“维什戴尔”，验证宠物和助手各自工作。向我说明 Windows 开机自启 Codex 可让已选宠物随应用启动；只有我明确同意时才设置开机自启。若当前 Codex 版本不支持这些包或插件，请停在兼容性检查并告诉我具体原因，不要修改 Codex 程序文件。
```

## 功能与位置

| 功能 | 内容 | 仓库路径 |
| --- | --- | --- |
| v2 内嵌宠物 | 9 种标准状态、16 个顺时针视线方向，8×11 图集；保留原 GIF 动作的 APNG 时间轴 | [`pets/wisadel-rest/`](pets/wisadel-rest/) |
| 原始贴纸 | 当前内嵌版使用的 7 个 GIF | [`stickers/`](stickers/) |
| 逐帧 PNG | 235 张标准动作画格、16 张方向画格 | [`frames/`](frames/) |
| 统计助手（默认安装，可自行关闭） | 键鼠累计、悬停面板、用量和额度、台词、MCP 面板与 Hooks | [`plugins/wisadel-companion/`](plugins/wisadel-companion/) |
| 安装与说明 | 备份后安装、架构、帧数及权限 | [`scripts/install.ps1`](scripts/install.ps1)、[`docs/`](docs/) |

宠物图集采用每格 **192×208** 像素，横向 8 格。`spritesheet.png` 为 **1536×2288** 的 APNG，`pet.json` 声明 `spriteVersionNumber: 2`。三个包文件必须同版一起安装；`spritesheet.webp` 是静态备用图，清单实际指向 PNG。帧数及状态来源见[逐帧索引](docs/FRAMES.md)。

## 动作效果预览

以下动图直接从**本仓库发布的 v2 APNG 图集**提取，按图集时间轴循环播放；淡色背景仅用于在 README 中看清透明角色。动作何时出现、持续多久由 Codex 控制，因此预览循环不代表每次状态都能在应用内完整播完。[逐帧 PNG 与原始 GIF](docs/FRAMES.md)也全部附在仓库中。

| 待机与移动 | 移动与挥手 |
| --- | --- |
| **休息待机 idle**：无任务时侧躺、眨眼和轻微活动；原画 19 帧。<br>![维什戴尔休息待机动画](docs/previews/idle.gif) | **向右移动 running-right**：拖动宠物向右时的 14 帧步态。<br>![维什戴尔向右移动动画](docs/previews/running-right.gif) |
| **向左移动 running-left**：向右移动的 14 帧逐帧镜像。<br>![维什戴尔向左移动动画](docs/previews/running-left.gif) | **挥手 waving**：Codex 进入挥手状态时的表情与动作；原画 38 帧。<br>![维什戴尔挥手动画](docs/previews/waving.gif) |
| **跳跃 jumping**：点击宠物时跳起；5 幅原画加 10 幅只调整位置的中间帧。<br>![维什戴尔跳跃动画](docs/previews/jumping.gif) | **失败 failed**：任务失败状态的夸张表情；原画 35 帧。<br>![维什戴尔失败表情动画](docs/previews/failed.gif) |
| **等待 waiting**：需要用户回答或审批时的等待表情；原画 30 帧。<br>![维什戴尔等待动画](docs/previews/waiting.gif) | **任务进行 running**：Codex 工作时的 38 帧动作。当前版本与挥手状态共用同一份“开始任务”原画。<br>![维什戴尔任务进行动画](docs/previews/running.gif) |
| **完成／复查 review**：完成阶段的表情变化；原画 32 帧。Codex 可能提前切换状态。<br>![维什戴尔完成复查动画](docs/previews/review.gif) | **视线方向 look**：16 个顺时针方向；0° 向上、90° 向右、180° 向下、270° 向左。<br>![维什戴尔十六方向视线动画](docs/previews/look-directions.gif) |

“失败”图案源自原始低额度贴纸，但**素材包自身不会监控额度或强制切换宠物状态**；额度监控由下方的助手单独完成。running 指任务运行，左右拖动对应 running-right / running-left。

## 实现的功能

**Codex 内嵌宠物**

- 安装清单与 8×11 图集后，在 Codex 宠物选择器中选择“维什戴尔”即可显示。9 种标准状态和 16 个方向画格都在同一个 v2 图集中。
- 待机、左右拖动、挥手、点击跳跃、失败、等待、任务进行和完成阶段都有对应动画；实际状态转换、播放起点和持续时间由 Codex 原生组件决定。
- 原 GIF 动作保存在 APNG 时间轴中；[动作逐帧](frames/native/)有 235 张 PNG，[方向逐帧](frames/look-directions/)有 16 张 PNG，可单独查看或核对。

**默认安装的维什戴尔统计助手（Windows，可后续关闭）**

- **键鼠计数与悬停面板：**统计键盘按下次数及鼠标左、右、中、侧键次数，长按自动重复不重复计数。鼠标在宠物上停留约 200 毫秒后显示面板；移开、拖动或宠物隐藏时隐藏，并跟随宠物位置。只记录次数，不记录输入文字。
- **额度与用量：**显示 5 小时和每周剩余额度、重置时间、最近更新时间；5 小时额度严格低于 5% 时每个重置周期提示一次。读取本机已完成请求的 Token，用本机时间 04:00 为每日分界；美元是按 API 单价折算的估值，不是订阅账单。额度过期或读取失败时不会伪装成实时值。
- **任务台词与 Hooks：**SessionStart、UserPromptSubmit、Stop、Interrupt 四类 Hooks 辅助识别任务事件，开始和完成时显示对应台词，其他台词间隔随机出现；中断不播放完成台词。Hooks 需要 Codex 信任审核，MCP 启动路径不依赖 Hooks。
- **统计面板与设置：**可对 Codex 说“显示维什戴尔统计面板”，查看累计计数与状态；可暂停计数、关闭悬停面板或关闭助手自动启动。宠物图集可以独立使用。

以下两图使用**模拟数字和示例台词**，仅展示助手界面，不是任何用户的真实统计：

| 悬停计数、额度与 Token 面板 | 任务台词气泡 |
| --- | --- |
| ![维什戴尔助手的悬停统计面板示意图](docs/previews/companion-badge.png) | ![维什戴尔助手的任务台词气泡示意图](docs/previews/companion-speech.png) |
## 安装

要求：Windows、支持本地宠物的 Codex 桌面版。统计助手还需要 Codex CLI 和 Python 3；仓库内附已从源码构建的 Windows 配套程序，源码见插件目录。当前版本的本地宠物和插件接口将来可能变化，安装时应先检查本机 Codex 是否仍支持。

```powershell
git clone https://github.com/ykuang771-afk/Codex_Pet_WISADEL.git
cd Codex_Pet_WISADEL
.\scripts\install.ps1 -DryRun
.\scripts\install.ps1
```

PowerShell 如果拦截本次脚本，可在**当前进程**执行 `Set-ExecutionPolicy -Scope Process Bypass` 后重试；无需修改系统级策略。脚本默认安装 v2 宠物及当前统计助手；仅安装宠物使用 `-PetOnly`。安装器将已有同名宠物或插件源码先备份到 `%LOCALAPPDATA%/WisadelCompanion/setup-backups/`，不清空计数与用量历史。安装后在 Codex 中选择“维什戴尔”，切换一次宠物或重启以刷新缓存；助手安装后重启 Codex，并按界面提示审核 Hooks。后续可通过插件设置关闭自动启动或取消安装。

**手动只装宠物：**把 `pets/wisadel-rest/` 中的 `pet.json`、`spritesheet.png`、`spritesheet.webp` 三个文件一起复制到 `%CODEX_HOME%/pets/wisadel-rest/`；没有设置 `CODEX_HOME` 时用 `%USERPROFILE%/.codex/pets/wisadel-rest/`。先备份原目标目录。

### 宠物怎么启动

1. 安装完成后**打开 Codex 桌面应用**。宠物由 Codex 自身加载，单独打开图集文件不会出现宠物。
2. 打开 Codex 的宠物选择器，选择 **“维什戴尔”**（包目录名是 `wisadel-rest`）。首次选择后应出现浮动宠物。已有同名宠物缓存时，先切换到其他宠物再切回来；仍未显示就重启 Codex。
3. 统计助手随 Codex 插件启动。重启 Codex 后完成 Hooks 信任审核；悬停宠物可查看计数与额度，也可以对 Codex 说“显示维什戴尔统计面板”。如果不需要助手，可在插件设置中关闭自动启动或卸载插件，宠物本身仍可使用。

**可选开机自启：**如果电脑内存够用，建议在 Windows **设置 → 应用 → 启动** 中启用 Codex。这样 Windows 登录后 Codex 会启动，已选择的宠物也会随 Codex 加载；无需另为宠物设置启动项。如果启动列表没有 Codex，可把 Codex 的应用快捷方式放入用户的“启动”文件夹（`Win + R` 输入 `shell:startup`），并先验证快捷方式能正常打开 Codex。电脑资源紧张时保持手动启动即可。

统计助手作为独立 Codex 插件注册，不是宠物图集的一部分。安装器把插件复制到 `%LOCALAPPDATA%/WisadelCompanion/marketplace/`，为本机 Python 生成 MCP 与 Hooks 启动配置，再通过 `codex plugin marketplace add`、`codex plugin add` 注册。若同名助手已经安装，脚本会跳过插件以避免重复计数。插件运行与完整架构见[架构说明](docs/ARCHITECTURE.md)。

## 权限与隐私

| 组件 | 所需权限 | 用途 |
| --- | --- | --- |
| 内嵌宠物 | 写入用户的 Codex `pets` 目录 | 加载角色动画；无需键鼠或会话读取权限 |
| 安装器 | 写入 `%LOCALAPPDATA%/WisadelCompanion/`，调用 `codex plugin` | 保存插件副本、备份并注册本地插件 |
| 键鼠计数 | Windows Raw Input 全局按键和鼠标按下事件 | 只累计次数；不保存按键文字、组合内容或剪贴板 |
| 悬停跟随 | Windows UI Automation 和窗口位置 | 识别 Codex 浮动宠物边界；不扫描聊天正文 |
| Token 账本与台词 | 只读本机 Codex 会话/用量日志，写入本机数据库和短期事件 | 统计已完成请求、判断任务事件；不复制提示词或回答 |
| 额度显示 | 使用现有登录状态进行 Codex app-server 只读额度查询 | 显示剩余额度；不保存登录凭据，不发起模型请求 |
| Hooks | 需要 Codex 的信任审核 | 触发任务台词及助手启动；不信任 Hooks 时 MCP 启动路径仍可用 |

所有计数、账本与偏好保存在本机 `%LOCALAPPDATA%/WisadelCompanion/`。美元数是按公开 API 单价计算的**估值**，不是 ChatGPT 订阅账单；无公开单价的模型只显示 Token。插件不把数据上传到本仓库。使用第三方实现的说明见 [`THIRD_PARTY_NOTICES.md`](plugins/wisadel-companion/THIRD_PARTY_NOTICES.md)。

## 验证与已知边界

- 安装前可检查 `pets/wisadel-rest/pet.json` 的版本为 2，图片为 1536×2288。
- 动画预览见 [`docs/contact-sheet-extended.png`](docs/contact-sheet-extended.png) 和 [`docs/look-directions.png`](docs/look-directions.png)；独立逐帧图片可逐张检查。
- 助手的本地测试脚本在 [`plugins/wisadel-companion/scripts/`](plugins/wisadel-companion/scripts/)；编译命令是 `build.ps1`。仓库打包前已运行构建、安装器 dry run 和素材检查。实际悬停、Hooks 和额度显示还取决于安装电脑与 Codex 版本。
- Codex 控制原生状态切换和持续时间；素材不能让完成动作强制播完，也不能保证切换状态时从 APNG 第 1 帧重播。助手仅适用于 Windows，不修改 Codex 二进制。

## 授权

仓库中的**代码和文档采用 [MIT 许可](LICENSE)**；第三方来源另见插件的声明。**角色图片、GIF、图集、帧图和图标保留版权**，详见 [图片素材条款](ASSET_LICENSE.md)。允许按本说明下载并在自己的 Codex 中安装使用图片；改编、再分发或商业使用需另获权利人许可。
