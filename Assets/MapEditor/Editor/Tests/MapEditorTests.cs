using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode 单元测试 —— 地形查询、JSON 序列化、通行判断
/// </summary>
public class MapEditorTests
{
    private MapResourceConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = ScriptableObject.CreateInstance<MapResourceConfig>();
        _config.terrainResources = new List<TerrainResource>
        {
            new TerrainResource { terrainId = "grass", terrainName = "草地", defaultType = DefaultTerrainType.Passable },
            new TerrainResource { terrainId = "stone", terrainName = "山石", defaultType = DefaultTerrainType.Impassable },
            new TerrainResource { terrainId = "shoal", terrainName = "浅滩", defaultType = DefaultTerrainType.UnitImpassable, blockUnitIds = new List<string> { "infantry" } },
        };
        _config.RebuildCache();
        GameMapGlobal.MapConfig = _config;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_config);
        GameMapGlobal.MapConfig = null;
    }

    // ==================== MapResourceConfig ====================

    [Test]
    public void GetTerrainById_ExistingId_ReturnsTerrain()
    {
        var result = _config.GetTerrainById("grass");
        Assert.IsNotNull(result);
        Assert.AreEqual("草地", result.terrainName);
    }

    [Test]
    public void GetTerrainById_NonExistingId_ReturnsNull()
    {
        Assert.IsNull(_config.GetTerrainById("water"));
    }

    [Test]
    public void GetTerrainById_EmptyString_ReturnsNull()
    {
        Assert.IsNull(_config.GetTerrainById(""));
        Assert.IsNull(_config.GetTerrainById(null));
    }

    [Test]
    public void ContainsId_ExistingId_ReturnsTrue()
    {
        Assert.IsTrue(_config.ContainsId("stone"));
    }

    [Test]
    public void ContainsId_NonExistingId_ReturnsFalse()
    {
        Assert.IsFalse(_config.ContainsId("fire"));
    }

    [Test]
    public void Validate_NoErrors_ReturnsEmptyList()
    {
        var errors = _config.Validate();
        Assert.AreEqual(0, errors.Count);
    }

    [Test]
    public void Validate_DuplicateId_ReportsError()
    {
        _config.terrainResources.Add(new TerrainResource { terrainId = "grass", terrainName = "重复" });
        var errors = _config.Validate();
        Assert.Greater(errors.Count, 0);
        Assert.IsTrue(errors.Exists(e => e.Contains("重复")));
    }

    [Test]
    public void Validate_EmptyId_ReportsError()
    {
        _config.terrainResources.Add(new TerrainResource { terrainId = "", terrainName = "空ID" });
        var errors = _config.Validate();
        Assert.Greater(errors.Count, 0);
    }

    // ==================== GameMapGlobal ====================

    [Test]
    public void CheckUnitCanPass_PassableTerrain_ReturnsTrue()
    {
        Assert.IsTrue(GameMapGlobal.CheckUnitCanPass("grass", "knight"));
    }

    [Test]
    public void CheckUnitCanPass_ImpassableTerrain_ReturnsFalse()
    {
        Assert.IsFalse(GameMapGlobal.CheckUnitCanPass("stone", "knight"));
    }

    [Test]
    public void CheckUnitCanPass_BlockedUnit_ReturnsFalse()
    {
        Assert.IsFalse(GameMapGlobal.CheckUnitCanPass("shoal", "infantry"));
    }

    [Test]
    public void CheckUnitCanPass_NonBlockedUnit_ReturnsTrue()
    {
        Assert.IsTrue(GameMapGlobal.CheckUnitCanPass("shoal", "cavalry"));
    }

    [Test]
    public void CheckUnitCanPass_UnknownTerrain_ReturnsTrue()
    {
        Assert.IsTrue(GameMapGlobal.CheckUnitCanPass("unknown", "knight"));
    }

    // ==================== MapJsonData ====================

    [Test]
    public void MapJsonData_SerializeDeserialize_DataPreserved()
    {
        var original = new MapJsonData
        {
            formatVersion = "1.0",
            mapName = "TestMap",
            mapWidth = 10,
            mapHeight = 10,
            layerCount = 1,
            cellDatas = new List<MapCellData>
            {
                new MapCellData { x = 0, y = 0, terrainId = "grass" },
                new MapCellData { x = 5, y = 3, terrainId = "stone" },
            }
        };

        string json = JsonUtility.ToJson(original);
        var restored = JsonUtility.FromJson<MapJsonData>(json);

        Assert.AreEqual(original.mapName, restored.mapName);
        Assert.AreEqual(original.mapWidth, restored.mapWidth);
        Assert.AreEqual(original.mapHeight, restored.mapHeight);
        Assert.AreEqual(original.formatVersion, restored.formatVersion);
        Assert.AreEqual(original.layerCount, restored.layerCount);
        Assert.AreEqual(original.cellDatas.Count, restored.cellDatas.Count);
        Assert.AreEqual(original.cellDatas[0].x, restored.cellDatas[0].x);
        Assert.AreEqual(original.cellDatas[0].y, restored.cellDatas[0].y);
        Assert.AreEqual(original.cellDatas[0].terrainId, restored.cellDatas[0].terrainId);
    }

    [Test]
    public void Validate_OutOfBoundsCell_ReportsError()
    {
        var data = new MapJsonData
        {
            mapWidth = 5,
            mapHeight = 5,
            cellDatas = new List<MapCellData>
            {
                new MapCellData { x = 10, y = 0, terrainId = "grass" } // 越界
            }
        };
        var errors = data.Validate(_config);
        Assert.Greater(errors.Count, 0);
        Assert.IsTrue(errors.Exists(e => e.Contains("超出地图范围")));
    }

    [Test]
    public void Validate_InvalidTerrainId_ReportsError()
    {
        var data = new MapJsonData
        {
            mapWidth = 10,
            mapHeight = 10,
            cellDatas = new List<MapCellData>
            {
                new MapCellData { x = 1, y = 1, terrainId = "nonexistent" }
            }
        };
        var errors = data.Validate(_config);
        Assert.Greater(errors.Count, 0);
        Assert.IsTrue(errors.Exists(e => e.Contains("不存在")));
    }

    [Test]
    public void Validate_DuplicateCell_ReportsError()
    {
        var data = new MapJsonData
        {
            mapWidth = 10,
            mapHeight = 10,
            cellDatas = new List<MapCellData>
            {
                new MapCellData { x = 3, y = 3, terrainId = "grass" },
                new MapCellData { x = 3, y = 3, terrainId = "stone" },
            }
        };
        var errors = data.Validate(_config);
        Assert.Greater(errors.Count, 0);
        Assert.IsTrue(errors.Exists(e => e.Contains("重复")));
    }

    [Test]
    public void Validate_EmptyData_NoErrors()
    {
        var data = new MapJsonData { mapWidth = 10, mapHeight = 10 };
        var errors = data.Validate(_config);
        Assert.AreEqual(0, errors.Count);
    }

    // ==================== Multi-layer ====================

    [Test]
    public void MapJsonData_MultiLayer_SerializationPreservesLayer()
    {
        var data = new MapJsonData
        {
            mapWidth = 10,
            mapHeight = 10,
            layerCount = 3,
            cellDatas = new List<MapCellData>
            {
                new MapCellData { x = 0, y = 0, layer = 0, terrainId = "grass" },
                new MapCellData { x = 0, y = 0, layer = 2, terrainId = "stone" },
            }
        };

        string json = JsonUtility.ToJson(data);
        var restored = JsonUtility.FromJson<MapJsonData>(json);

        Assert.AreEqual(3, restored.layerCount);
        Assert.AreEqual(2, restored.cellDatas.Count);
        Assert.AreEqual(0, restored.cellDatas[0].layer);
        Assert.AreEqual(2, restored.cellDatas[1].layer);
    }

    // ==================== Custom Properties ====================

    [Test]
    public void CustomProperty_GetInt_ReturnsCorrectValue()
    {
        var terrain = new TerrainResource
        {
            terrainId = "test",
            customProperties = new List<CustomProperty>
            {
                new CustomProperty { propertyName = "攻击加成", propertyType = PropertyType.Int, intValue = 5 }
            }
        };
        Assert.AreEqual(5, terrain.GetInt("攻击加成"));
    }

    [Test]
    public void CustomProperty_MissingProperty_ReturnsDefault()
    {
        var terrain = new TerrainResource { terrainId = "test" };
        Assert.AreEqual(0, terrain.GetInt("不存在"));
        Assert.AreEqual(0f, terrain.GetFloat("不存在"));
        Assert.IsFalse(terrain.GetBool("不存在"));
    }

    // ==================== A* Pathfinding ====================

    private MapJsonData CreateTestMap(int w, int h)
    {
        return new MapJsonData { mapWidth = w, mapHeight = h, layerCount = 1 };
    }

    private void FillRect(MapJsonData map, int x0, int y0, int x1, int y1, string terrainId)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                map.cellDatas.Add(new MapCellData { x = x, y = y, terrainId = terrainId });
    }

    [Test]
    public void FindPath_ClearPath_ReturnsDirectPath()
    {
        var map = CreateTestMap(5, 5);
        var path = MapPathfinding.FindPath(0, 0, 4, 4, "knight", map);
        Assert.Greater(path.Count, 0, "空旷地图应找到路径");
        Assert.AreEqual(new Vector2Int(4, 4), path[path.Count - 1], "终点应为 (4,4)");
    }

    [Test]
    public void FindPath_BlockedByImpassable_GoesAround()
    {
        var map = CreateTestMap(5, 5);
        // 在直线上放一排阻挡
        FillRect(map, 0, 2, 2, 2, "stone");

        var path = MapPathfinding.FindPath(0, 0, 4, 0, "knight", map);
        Assert.Greater(path.Count, 0, "应绕过阻挡");
        // 验证路径不经过阻挡格 (坐标 y=2 的三个格)
        foreach (var step in path)
            Assert.IsFalse(step.y == 2 && step.x <= 2, $"路径不应经过阻挡格 ({step.x},{step.y})");
    }

    [Test]
    public void FindPath_UnreachableDestination_ReturnsEmpty()
    {
        var map = CreateTestMap(5, 5);
        // 用阻挡围住终点
        FillRect(map, 0, 4, 5, 4, "stone");
        FillRect(map, 4, 0, 4, 4, "stone");

        var path = MapPathfinding.FindPath(0, 0, 4, 4, "knight", map);
        Assert.AreEqual(0, path.Count, "终点不可达应返回空列表");
    }

    [Test]
    public void FindPath_StartEqualsEnd_ReturnsEmpty()
    {
        var map = CreateTestMap(5, 5);
        var path = MapPathfinding.FindPath(2, 2, 2, 2, "knight", map);
        Assert.AreEqual(0, path.Count, "起点=终点应返回空");
    }

    [Test]
    public void FindPath_UnitSpecificBlock_BlocksCorrectUnit()
    {
        var map = CreateTestMap(5, 5);
        FillRect(map, 2, 0, 2, 4, "shoal"); // 只挡住 infantry

        var infantryPath = MapPathfinding.FindPath(0, 2, 4, 2, "infantry", map);
        Assert.AreEqual(0, infantryPath.Count, "infantry 被浅滩阻挡");

        var cavalryPath = MapPathfinding.FindPath(0, 2, 4, 2, "cavalry", map);
        Assert.Greater(cavalryPath.Count, 0, "cavalry 可通过浅滩");
    }

    [Test]
    public void FindPath_OutOfBounds_ReturnsEmpty()
    {
        var map = CreateTestMap(5, 5);
        var path = MapPathfinding.FindPath(-1, 0, 4, 4, "knight", map);
        Assert.AreEqual(0, path.Count);
        path = MapPathfinding.FindPath(0, 0, 10, 10, "knight", map);
        Assert.AreEqual(0, path.Count);
    }

    [Test]
    public void FindPath_Diagonal_CutsCorners()
    {
        var map = CreateTestMap(5, 5);
        var path = MapPathfinding.FindPath(0, 0, 4, 4, "knight", map, allowDiagonal: true);
        Assert.Greater(path.Count, 0);
    }

    [Test]
    public void FindPath_DiagonalBlockedByCorner_NoCornerCutting()
    {
        var map = CreateTestMap(3, 3);
        // 创建穿角场景：障碍在对角位置迫使走直角
        map.cellDatas.Add(new MapCellData { x = 1, y = 0, terrainId = "stone" });
        map.cellDatas.Add(new MapCellData { x = 0, y = 1, terrainId = "stone" });

        var path = MapPathfinding.FindPath(0, 0, 1, 1, "knight", map, allowDiagonal: true);
        // 不应直接斜穿 (1,0) 和 (0,1) 两个阻挡之间的缝
        foreach (var step in path)
        {
            bool isBlockedCorner = (step.x == 1 && step.y == 0) || (step.x == 0 && step.y == 1);
            Assert.IsFalse(isBlockedCorner, $"路径不应经过阻挡格 ({step.x},{step.y})");
        }
    }
}
