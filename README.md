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