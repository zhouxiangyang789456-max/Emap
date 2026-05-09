using System;
using System.Collections.Generic;

[Serializable]
public class MapJsonData
{
    public string formatVersion = "1.0";
    public string mapName;
    public int mapWidth;
    public int mapHeight;
    public int layerCount = 1;
    public List<MapCellData> cellDatas = new List<MapCellData>();

    public List<string> Validate(MapResourceConfig config)
    {
        var errors = new List<string>();
        var seen = new HashSet<(int, int, int)>();
        for (int i = 0; i < cellDatas.Count; i++)
        {
            var c = cellDatas[i];
            if (c == null) { errors.Add($"cellDatas[{i}] 为 null"); continue; }
            if (c.x < 0 || c.x >= mapWidth || c.y < 0 || c.y >= mapHeight)
                errors.Add($"格子 ({c.x},{c.y}) 超出地图范围 ({mapWidth}×{mapHeight})");
            if (c.layer < 0 || c.layer >= layerCount)
                errors.Add($"格子 ({c.x},{c.y}) 层级 {c.layer} 超出范围 (0~{layerCount - 1})");
            if (!string.IsNullOrEmpty(c.terrainId) && config != null && !config.ContainsId(c.terrainId))
                errors.Add($"格子 ({c.x},{c.y}) 的地形 ID '{c.terrainId}' 在配置表中不存在");
            if (!seen.Add((c.x, c.y, c.layer)))
                errors.Add($"格子 ({c.x},{c.y},层{c.layer}) 坐标重复");
        }
        return errors;
    }
}

[Serializable]
public class MapCellData
{
    public int x;
    public int y;
    public int layer;
    public string terrainId;
}
