# PE Xp Helper

Project Exhibitionist 的 BepInEx 5 插件：经验倍率、加经验/升级、单项解锁（物品 / 场景点 / 段位特权）。

## 功能

- **经验倍率**：1x - 10x（默认 3x），作用于 `GrantXp` 与单局结算 `CalculateRunXp`
- **加经验 / 升一级**：按钮直接写入存档，走游戏原生升级与晋升流程
- **单项解锁**：按名称搜索、筛选未解锁项，对单个物品、场景点、段位特权精确解锁
  - 物品解锁时连带解锁该物品的全部变体
  - 段位特权支持「解锁 / 锁定」双向切换
  - 中文名来自可编辑映射表，未填写的显示英文
- **临时全解锁**：仅内存生效，不写存档，关闭即恢复

## 安装

1. 安装 BepInEx 5.4.x（x64）；
2. 把 `bin\PE_XpHelper.dll` 放进 `BepInEx\plugins\`；
3. 把 `config\pe_xphelper_names_cn.txt` 放进 `BepInEx\config\`（可选，用于中文物品名）；
4. 进入游戏后按 **F10** 打开面板。

## 使用说明

- 面板分页：`经验与全局` / `单项解锁`
- 物品与场景点列表只显示当前已加载的资源，进游戏或切换场景后点「刷新列表」可重新扫描
- 修改翻译表后，关掉面板再按 F10 重新打开即可生效
- 加经验与永久解锁会立即写入存档；用调试方式加分可能使本局排行榜成绩失效

## 编译

需要 .NET SDK 的 Roslyn csc 与游戏目录中的 `BepInEx\core`、`Managed` 程序集：

```
build/build.ps1 -GameDir "X:\path\to\Project Exhibitionist"
```

## 兼容性

- Project Exhibitionist v1.5（Unity 2022.3，Mono）
- BepInEx 5.4.23.6
- 与 Dark Suite 共存（F10 与 End 不冲突）

## 免责声明

仅用于单机调试与学习，使用后产生的存档变化由使用者自行负责。

## Dark Suite 2.6.3 简体中文（汉化 DLL）

`dark-suite-cn/` 是本仓库维护的 Dark Suite 汉化版本。

- **原插件**：Dark Suite 2.6.3（BepInEx 5 插件，插件 GUID `com.darksuite.pe`，由原作者发布；本仓库不包含原版安装包）
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