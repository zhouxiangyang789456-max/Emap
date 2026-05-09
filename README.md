# Unity 地形地图编辑器

> 基于 Scene 视图的网格地图编辑器 Unity 插件 —— 可视化作画、一键 JSON 序列化、2D/3D 双模式、运行时地形查询、单位放置、A* 寻路。

[![Unity](https://img.shields.io/badge/Unity-2020.3%2B-black?logo=unity)](https://unity.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## 功能一览

| 功能 | 说明 |
|------|------|
| 可视化编辑 | Scene 视图直接刷地形，绿/红/黄色块实时区分通行规则 |
| **2D/3D 双模式** | 一键切换 2D Tilemap 编辑（俯视角）或 3D Handles 编辑 |
| 多种绘制工具 | 笔刷、橡皮、矩形填充、洪水填充、拖拽连续绘制、Shift 快捷擦除 |
| 多层地图 | 支持地面/覆盖/天空等多层叠加，每层可独立选择**地形层**或**对象层** |
| **对象层（单位放置）** | 在格子上放置怪物/敌人/NPC，蓝色标记显示，支持笔刷/橡皮 |
| **单位配置管理** | 一键创建/扫描 UnitConfig 资产，多单位按钮式切换 |
| JSON 序列化 | 一键保存/加载，含版本号、地形数据 + 单位数据 |
| 地形配置表 | ScriptableObject 管理所有地形类型，自定义 Int/Float/Bool 属性 |
| 三种通行规则 | 全体可通过 / 全体不可通过 / 指定单位不可通过 |
| **精灵尺寸匹配** | 自动检测 Sprite PPU 与格子尺寸不匹配，一键修正 |
| **Tile 尺寸预设** | 一键切换 16/32/64/100/128 常用格子尺寸 |
| 运行时查询 | `GameMapGlobal.CheckUnitCanPass()` 一行代码判断通行 |
| A* 寻路 | 完整 A* 实现，4/8 方向、穿角检测、地形移动消耗 |
| Undo/Redo | Ctrl+Z/Y，增量存储，最大 50 步，支持跨层操作 |
| 编辑器持久化 | 关窗重开不丢数据（SessionState 自动保存） |
| 运行时地图生成 | 两种方案：独立 GameObject（小图）+ Tilemap（大图 >50×50） |

## 快速开始

### 1. 导入

将 `Assets` 文件夹拖入你的 Unity 项目，等待编译完成。

### 2. 创建配置表

Project 窗口右键 → **Create → 地图编辑器 → 地形资源配置表**

### 3. 创建单位配置

Project 窗口右键 → **Create → 地图编辑器 → 单位配置**

### 4. 打开编辑器

菜单栏 → **地图编辑器 → 打开地图编辑器**

### 5. 开始编辑

把配置表拖入编辑器 → 点击"扫描"加载单位 → 在 Scene 视图中点击绘制地形或放置单位

> 完整图文教程见 [操作手册.md](./操作手册.md)

## 项目结构

```
Assets/
├── Scripts/
│   ├── Map/
│   │   ├── MapResourceConfig.cs          # 地形 ScriptableObject 配置表
│   │   ├── MapJsonData.cs                # 地图 JSON 数据结构（含 MapUnitData）
│   │   ├── GameMapGlobal.cs              # 全局地形查询（静态类）
│   │   ├── RuntimeMapGenerator.cs        # 运行时地图生成（GameObject 方案）
│   │   ├── RuntimeTilemapGenerator.cs    # 运行时地图生成（Tilemap 方案）
│   │   └── MapPathfinding.cs             # A* 寻路 + 最小堆
│   └── Unit/
│       ├── UnitConfig.cs                 # 单位配置 ScriptableObject（含 prefab）
│       └── UnitTerrainLogic.cs           # 单位-地形交互逻辑
└── Editor/
    ├── MapEditor/
    │   ├── MapEditorWindow.cs            # 编辑器主窗口
    │   └── MapEditor2DSceneManager.cs    # 2D Tilemap 编辑模式管理
    └── Tests/
        └── MapEditorTests.cs             # 自动化单元测试
```

## 运行时使用

```csharp
// 1. 初始化
GameMapGlobal.MapConfig = Resources.Load<MapResourceConfig>("MapConfig");

// 2. 通行判断
bool canPass = GameMapGlobal.CheckUnitCanPass("grass", "knight");

// 3. 读地形属性
var terrain = GameMapGlobal.GetTerrain("grass");
int atkBuff = terrain.GetInt("攻击加成");

// 4. 单位进入格子
unitLogic.OnEnterGrid("grass");

// 5. A* 寻路
var path = MapPathfinding.FindPath(0, 0, 10, 10, "knight", mapData);
```

## JSON 格式

```json
{
    "formatVersion": "1.0",
    "mapName": "MyMap",
    "mapWidth": 30,
    "mapHeight": 30,
    "layerCount": 2,
    "cellDatas": [
        { "x": 0, "y": 0, "layer": 0, "terrainId": "grass" },
        { "x": 5, "y": 3, "layer": 0, "terrainId": "stone" }
    ],
    "unitDatas": [
        { "x": 10, "y": 5, "layer": 1, "unitId": "goblin" },
        { "x": 12, "y": 6, "layer": 1, "unitId": "boss" }
    ]
}
```

## 配置示例地形

| ID | 名称 | 通行规则 | 自定义属性 |
|----|------|----------|-----------|
| `grass` | 草地 | 可通过 | 攻击加成 Int=1, 移速加成 Float=0.2 |
| `stone` | 山石 | 不可通过 | — |
| `shoal` | 浅滩 | 部分不可通过（挡步兵） | 是否中毒 Bool=true |

## 键盘快捷键

| 快捷键 | 功能 |
|--------|------|
| 左键点击 | 绘制/擦除/放置单位 |
| 左键拖拽 | 连续绘制 |
| Shift + 左键 | 临时切换橡皮 |
| Ctrl+Z | 撤销 |
| Ctrl+Y / Ctrl+Shift+Z | 重做 |

## 需求

- Unity 2019.1 最低，**推荐 2020.3 LTS 或更高**
- 2D 编辑模式需要 `2D Tilemap Editor` 包（Unity 内置）

## 运行测试

1. 安装 Unity Test Framework（Window → Package Manager → 搜索 Test Framework → Install）
2. Window → General → Test Runner
3. 切换到 **EditMode** 标签
4. 点击 **Run All**

## 文档

- [操作手册](./操作手册.md) — 详细教程，从安装到寻路
- [设计文档](./地图编辑器设计文档.md) — 完整代码 + 架构说明
- [开发计划](./开发计划.md) — 开发阶段任务清单
