# PE Xp Helper

Project Exhibitionist 的 BepInEx 5 插件：经验倍率、加经验/升级、单项解锁、兴趣点传送、金钱、成就、统计、存档备份与恢复。一个热键呼出面板，所有功能都在面板里点。

## 功能

- **经验倍率**：1x - 10x（默认 3x），作用于 `GrantXp` 与单局结算 `CalculateRunXp`
- **加经验 / 升一级**：按钮直接写入存档，走游戏原生升级与晋升流程
- **单项解锁**：按名称搜索、筛选未解锁项，对单个物品、场景点、段位特权精确解锁
  - 物品解锁时连带解锁该物品的全部变体
  - 段位特权支持「解锁 / 锁定」双向切换
  - 中文名来自可编辑映射表，未填写的显示英文
- **临时全解锁**：仅内存生效，不写存档，关闭即恢复
- **兴趣点快速传送**：列出 POI 目的地（美术馆 / 便利店 / 商场 / 泳池 / 体育场 / 大学等），显示 `[OK]` 已解锁 / `[--]` 未发现与到访次数，点一下直接切场景
  - 走游戏自身的 `SceneTransitionManager.LoadGameplayScene`，不瞬移坐标，加载流程与正常进出门一致
  - 目的地表优先读游戏内地图 App 与进度服务的真实数据，读不到时回退到内置清单
- **金钱**：读取 / 设置银行余额（`MoneyManager.BankBalance`），改完走游戏自己的 `SaveTransactionData()` 落盘
- **成就**：列出全部成就，显示解锁状态，支持单项解锁与「全部解锁」（走 `AchievementService.Unlock`，不是改注册表）
  - 成就名内置中文翻译（39 项），搜索框支持中英文双向匹配
- **统计**：跑动距离、步数、被看到次数、游玩时长等 14 项数值，可逐项读写（走 `PlayerStatsService.Data` + `Save()`）
  - 字段名均为中文标签，如「被看到次数」「有效游玩时长（秒）」
- **备份与恢复**：一键把 `progression.json / stats.json / missions.json / poi_fast_travel.json` 快照到 `BepInEx\config\PE_XpHelper_Backups\<时间戳>\`，可一键回滚；恢复前会自动先备份当前状态
  - 支持给备份**改名**（比如「满级前」「全解锁后」），非法文件名字符会自动换成下划线

## 安装

1. 安装 BepInEx 5.4.x（x64）；
2. 把 `bin\PE_XpHelper.dll` 放进 `BepInEx\plugins\`；
3. 把 `config\pe_xphelper_names_cn.txt` 放进 `BepInEx\config\`（可选，用于中文物品名）；
4. 进入游戏后按 **Insert** 打开面板（v1.3.0 起默认键；可在 `BepInEx\config\com.dsh.pe.xphelper.cfg` 的 `PanelKey` 改成任意组合键）。

## 使用说明

- 面板分页：`经验与全局`（含金钱） / `单项解锁` / `兴趣点传送` / `统计` / `成就` / `备份与恢复`
- 除 `Insert` 外**不占用任何热键**，所有功能都在面板里点
- 物品与场景点列表只显示当前已加载的资源，进游戏或切换场景后点「刷新列表」可重新扫描
- 兴趣点传送需要**先进到一个游戏场景**（主菜单里没有 `SceneTransitionManager`），面板会显示「场景管理器：就绪/未就绪」
- 金钱、成就、统计都要求游戏场景内有对应服务实例；读不到时面板会给出提示而不是报错
- 备份恢复写的是磁盘存档，**恢复后需要重启游戏**才生效
- 修改翻译表后，关掉面板再按面板键重新打开即可生效
- 加经验与永久解锁会立即写入存档；用调试方式加分可能使本局排行榜成绩失效

## 编译

需要 .NET SDK 的 Roslyn csc 与游戏目录中的 `BepInEx\core`、`Managed` 程序集：

```
build/build.ps1 -GameDir "X:\path\to\Project Exhibitionist"
```

> `bin\PE_XpHelper.dll` 已用上述命令针对 Project Exhibitionist v1.5 实际编译（v1.4.0，27648 B）。
> 换游戏版本后需要重新编译，否则 `Assembly-CSharp` 里的类型/方法签名变化会导致补丁失效。

## 兼容性

- Project Exhibitionist v1.5（Unity 2022.3，Mono）
- BepInEx 5.4.23.6
- 面板热键默认 **Insert**，与 Dark Suite 2.6.3、PE_Trainer v1.4 的热键零重叠

## 与其它插件共存

三个插件可以同时装在同一个 BepInEx 下，代码层面无冲突（Harmony 允许多补丁共存，PE_Trainer 不用 Harmony 而走反射），但**热键必须错开**：

| 键 | PE Xp Helper（本插件） | Dark Suite 2.6.3 默认 | PE_Trainer v1.4 |
|---|---|---|---|
| F1 / F2 | — | 屏蔽警察 / 新闻组 | — |
| F3（+Shift） | — | 小地图箭头 / 罗盘 | — |
| F4 | — | SlowDetection 冻结 | — |
| F5 / F6 | — | — | 移动速度循环 / 恢复 1x |
| F7 | — | 小地图 NPC 标记 | 显示隐藏面板 |
| F8 | — | 无限体力 | 一键解锁全部 + 满级 |
| F9 | — | SlowDetection 开关 | 金钱 +999999 |
| F10 | — | SlowDetection 变慢 | 表情锁定 |
| F11 | — | SlowDetection 变快 | POI 传送 |
| F12 | — | SlowDetection HUD | — |
| End / Pause | — | 打开面板 / BossKey | — |
| **Insert** | **打开面板** | — | — |

注意两点：

- **v1.2.0 及更早版本用 F10，与 Dark Suite 的 `SlowDetection.Slower` 和 PE_Trainer 的表情锁定三者同键**，v1.3.0 起改为 Insert。
- Dark Suite 的 F7–F11 同样和 PE_Trainer 的 F5–F11 重叠，用 `tools\Apply-DarkSuiteKeymap.ps1` 一键改成 `LeftControl + F7..F11`：

```powershell
# 先看会改什么
.\tools\Apply-DarkSuiteKeymap.ps1 -GameDir "D:\path\to\Project Exhibitionist" -DryRun

# 确认后执行（自动备份 com.darksuite.pe.cfg）
.\tools\Apply-DarkSuiteKeymap.ps1 -GameDir "D:\path\to\Project Exhibitionist"
```

PE_Trainer v1.4 的 F5–F11 是硬编码在 DLL 里的，改不了；不想让它占键就别装它自带的 `PE_Trainer.dll`，只用它的 exe 界面改存档即可。

> 注意：`com.darksuite.pe.cfg` 里的**配置项名**取决于装的是原版还是汉化版——本仓库的汉化 DLL 会把项名也译成中文
> （`SlowDetection.Hotkeys` 段下是「更慢 / 更快 / 切换两阶段」，而不是 `Slower / Faster / ToggleTwoPhase`）。
> 脚本按中英双表匹配，两种版本都能正确识别；同一段里的数值项（如「调整步长」）不会被误改。

另外还有两处非热键的重叠值得注意：

- **移动速度会相乘**：Dark Suite 的 `SpeedTweak` 与 PE_Trainer 的 F5 都改玩家速度，同时开容易飞出去；
- **存档覆盖**：PE_Trainer 是游戏外改 `GameData\Runtime\*.json` 与注册表，必须完全退出游戏后使用，否则会用它读到的旧快照覆盖你在游戏内刚取得的进度。

## 更新日志

- **1.5.2**：成就名（39 项）与统计字段名（14 项）内置中文翻译，搜索支持中英文双向匹配
- **1.5.1**：备份支持改名（面板内点「改名」直接编辑，非法字符自动替换）
- **1.5.0**：新增「统计」「成就」「备份与恢复」三个页签，「经验与全局」里加金钱栏——把 PE_Trainer v1.4 有而我们没有的功能全部搬进面板，仍然只有 `Insert` 一个热键
- **1.4.0**：新增「兴趣点传送」页签（移植自 PE_Trainer v1.4 的 F11 面板实现，改为面板内页签，不额外占用热键）
- **1.3.0**：面板热键改为可配置（`PanelKey`，默认 `Insert`）；修正 README 中「与 Dark Suite 的 F10 不冲突」的错误描述；新增 `tools\Apply-DarkSuiteKeymap.ps1`
- **1.2.0**：XP 倍率、加经验 / 升级、单项解锁、临时全解锁、中文名映射表

## 免责声明

仅用于单机调试与学习，使用后产生的存档变化由使用者自行负责。

## Dark Suite 2.6.3 简体中文（汉化 DLL）

`dark-suite-cn/` 是本仓库维护的 Dark Suite 汉化版本。

- **原插件**：Dark Suite 2.6.3（作者 **DoomerDGR8**，发布在 AyakaMods：https://ayakamods.com/mods/dark-suite.5714/ ；BepInEx 5 插件，GUID `com.darksuite.pe`）——本仓库不包含原版安装包，请从原贴下载原版
- **汉化方式**：Mono.Cecil 读取原版 DLL，扫描全部 1228 条 `ldstr`，按「类型 + 方法 + 原文」三元组精确定位，替换 294 条界面字符串（共 308 处），不反编译、不重编
- **本仓库的额外修复**：
  - `SlowDetection` 的反射字段名 `CooldownTime` 曾被误译成「冷却时间」，导致每帧 `NullReferenceException`，已还原为 `CooldownTime`
  - 运行时拼接的英文标题（`SmoothMotion` 等配置段名经 `Nice()` 生成）通过面板文本出口映射补译
  - 开关的 `ON/OFF` 改为「开/关」
- **文件**：
  - `DarkSuite.dll`：BepInEx 5 版本，安装到 `BepInEx\plugins\DarkSuite\`
  - `translations_cn.json`：294 条替换映射
  - `汉化说明.txt`：原汉化说明

版权归 Dark Suite 原作者所有；此汉化仅用于学习与自用分享，请勿用于售卖，并支持原作者。