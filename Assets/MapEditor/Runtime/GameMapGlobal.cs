using System.Collections.Generic;

public static class GameMapGlobal
{
    public static MapResourceConfig MapConfig;

    // ===== 基础查询 =====

    public static TerrainResource GetTerrain(string terrainId)
    {
        if (MapConfig == null)
        {
            UnityEngine.Debug.LogError("[GameMapGlobal] MapConfig 未初始化，请先赋值！");
            return null;
        }
        return MapConfig.GetTerrainById(terrainId);
    }

    public static bool CheckUnitCanPass(string terrainId, string unitId)
    {
        var terrain = GetTerrain(terrainId);
        if (terrain == null) return true;

        switch (terrain.defaultType)
        {
            case DefaultTerrainType.Passable:
                return true;
            case DefaultTerrainType.Impassable:
                return false;
            case DefaultTerrainType.UnitImpassable:
                if (terrain.blockUnitIds == null) return true;
                return !terrain.blockUnitIds.Contains(unitId);
            default:
                return true;
        }
    }

    // ===== 多层地图查询 =====

    public static TerrainResource GetTerrainAt(int x, int y, int layer, MapJsonData mapData)
    {
        if (mapData == null) return null;
        foreach (var cell in mapData.cellDatas)
        {
            if (cell.x == x && cell.y == y && cell.layer == layer)
                return GetTerrain(cell.terrainId);
        }
        return null;
    }

    public static bool CheckUnitCanPassAt(int x, int y, int layer, string unitId, MapJsonData mapData)
    {
        var terrain = GetTerrainAt(x, y, layer, mapData);
        if (terrain == null) return true;
        return CheckUnitCanPass(terrain.terrainId, unitId);
    }

    public static int GetHighestBlockingLayer(int x, int y, string unitId, MapJsonData mapData)
    {
        if (mapData == null) return 0;
        for (int layer = 0; layer < mapData.layerCount; layer++)
        {
            if (!CheckUnitCanPassAt(x, y, layer, unitId, mapData))
                return layer;
        }
        return mapData.layerCount - 1;
    }
}
