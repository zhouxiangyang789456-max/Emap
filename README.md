# Unity 地图编辑器

> 基于 Scene 视图的网格地图编辑器 Unity 插件 —— 支持方形/六边形双模式、统一资源标签系统、prefab 拖入即用、2D/3D 双模式、A* 寻路。

[![Unity](https://img.shields.io/badge/Unity-2020.3%2B-black?logo=unity)](https://unity.com/)

## 功能一览

| 功能 | 说明 |
|------|------|
| **方形 + 六边形双模式** | 支持方形网格 + 六边形网格（平顶/尖顶），一键切换 |
| **统一资源标签系统** | 地形/单位/建筑/自定义标签，一个表单创建所有资源 |
| **prefab 拖入即用** | 拖入 prefab → 自动提取名称/ID/精灵 → 立即可用 |
| **瓦片大小预设** | 方形：32×32 ~ 256×256；六边形：64×74 ~ 256×296 |
| **2D/3D 双模式** | 一键切换 2D Tilemap（Unity Hex Grid 原生支持）或 3D Handles |
| 多种绘制工具 | 笔刷、橡皮、矩形/菱形填充、洪水填充、六边形范围填充 |
| 多层地图 | 支持多层叠加，每层可选**地形层**或**对象层** |
| JSON 序列化 | 一键保存/加载，含网格形状、格子尺寸、地形+单位数据 |
| 三种通行规则 | 可通过 / 不可通过 / 指定单位不可通过 |
| A* 寻路 | 方形 4/8 方向 + 六边形 6 方向，含地形消耗 |
| Undo/Redo | Ctrl+Z/Y，增量存储，最大 50 步 |
| 精灵尺寸匹配 | 自动检测 Sprite PPU 与格子尺寸不匹配，一键修正 |
| 运行时地图生成 | GameObject + Tilemap 两种方案 |

## 快速开始

### 1. 导入

将 `Assets/Editor/MapEditor`、`Assets/Scripts/Map`、`Assets/Scripts/Unit` 文件夹复制到你的 Unity 项目。

### 2. 创建配置表

Project 窗口右键 → **Create → 地图编辑器 → 地形资源配置表**

### 3. 打开编辑器

菜单栏 → **地图编辑器 → 打开地图编辑器**

### 4. 创建资源

切换到**资源管理** Tab → "+ 创建新资源" → 选标签（地形/单位/建筑）→ 拖入 prefab → 确认

### 5. 开始编辑

切换到**地图编辑** Tab → 选网格形状 → 在 Scene 视图中绘制

## 项目结构

```
Assets/
├── Editor/MapEditor/
│   ├── MapEditorWindow.cs            # 编辑器主窗口（Tab、资源管理、3D渲染、鼠标输入）
│   └── MapEditor2DSceneManager.cs    # 2D Tilemap 模式管理
├── Editor/Tests/
│   └── MapEditorTests.cs             # 单元测试
├── Scripts/Map/
│   ├── HexGridUtils.cs               # 六边形网格数学库（偏移坐标）
│   ├── MapJsonData.cs                # JSON 数据模型
│   ├── MapPathfinding.cs             # A* 寻路（方形+六边形）
│   ├── MapResourceConfig.cs          # 资源配置表 + TerrainResource
│   ├── GameMapGlobal.cs              # 全局地图查询
│   ├── RuntimeMapGenerator.cs        # 运行时地图生成（GameObject）
│   └── RuntimeTilemapGenerator.cs    # 运行时地图生成（Tilemap）
└── Scripts/Unit/
    ├── UnitConfig.cs                 # 单位配置（已弃用，用标签="单位"替代）
    └── UnitTerrainLogic.cs           # 单位-地形碰撞逻辑
```

## 页签说明

| 页签 | 功能 |
|------|------|
| **地图编辑** | 地图参数、网格形状、格子尺寸、层级管理、2D/3D切换、编辑模式、绘制操作 |
| **资源管理** | 创建/编辑/删除资源，标签筛选（地形/单位/建筑），prefab 拖入导入 |

## JSON 格式

```json
{
    "formatVersion": "2.0",
    "mapName": "MyMap",
    "mapWidth": 30,
    "mapHeight": 30,
    "cellSize": 1.0,
    "gridShape": "Square",
    "hexOrientation": "FlatTop",
    "layerCount": 2,
    "cellDatas": [
        { "x": 0, "y": 0, "layer": 0, "terrainId": "grass" }
    ],
    "unitDatas": [
        { "x": 10, "y": 5, "layer": 1, "unitId": "goblin" }
    ]
}
```

| 字段 | 说明 |
|------|------|
| `gridShape` | `"Square"` 或 `"Hex"` |
| `hexOrientation` | `"FlatTop"` 或 `"PointyTop"`（仅 hex 模式） |
| `cellSize` | 格子尺寸（方形=边长，六边形=外接圆半径） |

## 运行时使用

```csharp
// 1. 初始化
GameMapGlobal.MapConfig = Resources.Load<MapResourceConfig>("MapConfig");

// 2. 通行判断
bool canPass = GameMapGlobal.CheckUnitCanPass("grass", "knight");

// 3. A* 寻路（自动适配方形/六边形）
var path = MapPathfinding.FindPath(0, 0, 10, 10, "knight", mapData);
```

## 键盘快捷键

| 快捷键 | 功能 |
|--------|------|
| 左键点击 | 绘制/擦除 |
| 左键拖拽 | 连续绘制 |
| Shift + 左键 | 临时切换橡皮 |
| Ctrl+Z | 撤销 |
| Ctrl+Y | 重做 |

## 瓦片大小预设

| 形状 | 可选尺寸 |
|------|---------|
| 矩形 | 32×32, 64×64, 128×128, 256×256 |
| 六边形 | 64×74, 128×148, 256×296 |

## 依赖

- Unity 2020.3 LTS 或更高
- 2D 模式需要 `2D Tilemap Editor`（Unity 内置）
- **无第三方依赖**

## 运行测试

1. Window → Package Manager → 安装 Test Framework
2. Window → General → Test Runner
3. EditMode → Run All
