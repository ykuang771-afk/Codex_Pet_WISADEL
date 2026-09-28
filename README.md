# 维什戴尔 · Codex 内嵌宠物

> Windows 版 Codex 桌面应用的 **维什戴尔·休息（v2）** 宠物素材与默认安装的统计助手。

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
