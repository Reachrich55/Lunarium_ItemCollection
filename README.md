# Lunarium Item Collection Mod

《星之旅Lunarium》的非官方全收集地图Mod，支持读取本地存档后解析尚未收集的物品。

## 玩家指南

### 功能介绍

- 在游戏地图上显示未收集的道具位置。
- 标记数字对应[全收集指南](guide/)编号。
- 鼠标悬停标记可查看道具名称和未收集判定依据。
- 左上角列出当前地图的全部未收集道具，切换地图后会自动更新。
- 只读取游戏内存中的存档数据，不会修改存档。

本 Mod 不会解锁尚未探索的地图，也不会清除游戏原有的地图迷雾。只有游戏已经加载到 Atlas 中的地图区域才会显示标记。

### 快速开始

1. 为《Lunarium》安装 x64 版 [MelonLoader](https://github.com/LavaGang/MelonLoader/releases/latest)；本项目使用 `0.7.3` 构建和测试。
2. 从本项目的 [Releases 页面](../../releases/latest) 下载最新的 `LunariumItemCollectionMod-v*.zip`。
3. 解压 ZIP，把其中的 `Mods` 文件夹复制到游戏根目录并允许合并。
4. 从 Steam 启动游戏、载入存档，按 `F8` 启用 Mod，再打开地图。

### 详细安装教程

#### 1. 找到游戏目录

在 Steam 游戏库中右键《Lunarium》，选择“管理” → “浏览本地文件”。默认安装位置通常类似：

```text
D:\Steam\steamapps\common\Lunarium
```

安装前请先确认原版游戏可以正常启动，然后关闭游戏。

#### 2. 安装 MelonLoader

1. 下载并运行 MelonLoader 官方安装器。
2. 在安装器中选择《Lunarium》的 `Lunarium.exe`。
3. 选择 x64 版本并完成安装。
4. 确认游戏根目录中出现 `MelonLoader` 和 `Mods` 文件夹。
5. 首次安装后建议先启动一次游戏，让 MelonLoader 完成初始化，然后退出游戏。

#### 3. 安装本 Mod

解压 Release ZIP 后，将整个 `Mods` 文件夹复制到游戏根目录。最终应存在：

```text
Lunarium\Mods\LunariumItemCollectionMod.dll
```

如果系统询问是否合并 `Mods` 文件夹，请选择合并；不需要覆盖其他 Mod。

#### 4. 启动与使用

1. 正常启动游戏并载入存档。
2. 按 `F8` 启用或停用收集标记。
3. 打开地图查看编号标记和左上角遗漏列表。
4. 选择其他小地图时，标记与列表会自动切换。

### 标记说明

- 菱形中的数字是[全收集指南](guide/)编号。
- 不同颜色对应不同道具类别。

### 常见问题

**按 F8 后没有显示标记**

- 确认 DLL 位于 `Lunarium\Mods`，而不是 ZIP 内的多层子目录。
- 确认 MelonLoader 已正常加载，并检查 `MelonLoader\Latest.log` 中是否出现本 Mod 的初始化信息。
- 尚未探索或尚未由游戏加载的地图块不会提前显示。

**游戏更新后标记异常**

游戏更新若修改地图、存档结构或程序集，可能需要发布适配版本。

**如何卸载**

关闭游戏后删除以下文件即可；本 Mod 不写入存档，也不会留下额外的玩家配置：

```text
Lunarium\Mods\LunariumItemCollectionMod.dll
```

## 开发者说明

### 实现架构

本项目使用 MelonLoader 的 Mono Mod 加载方式。

| 文件 | 职责 |
| --- | --- |
| `src/ItemCollectionMod.cs` | MelonLoader 生命周期、F8 输入、定时刷新和内置数据加载。 |
| `src/ProgressAnalyzer.cs` | 根据当前存档信号判断每个道具是否已收集。 |
| `src/MarkerOverlay.cs` | 将标记挂到原生 `LevelRegion`，并绘制当前地图的物品列表。 |
| `src/MarkerHover.cs` | 控制标记的悬停提示。 |
| `src/Models.cs` | 内置地图与进度数据的反序列化模型。 |
| `Data/collectibles.json` | 可见收集点及其完成信号；编译时嵌入 DLL。 |

### 构建

构建要求：

- Windows 10/11
- .NET 8 SDK。
- 已为《星之旅Lunarium》安装 MelonLoader `0.7.3`。

## 贡献

欢迎提交 Issue 和 Pull Request。

- 发现 Bug、有功能建议或疑问，请[提交 Issue](../../issues)。
- 改进代码请[提交 Pull Request](../../pulls)。

## 免责声明

- 本项目是与游戏开发商及发行商无关的非官方 Mod，仅供攻略参考和个人使用。请勿将本项目或《星之旅Lunarium》的游戏资源用于收费分发、商业宣传或其他牟利活动。
