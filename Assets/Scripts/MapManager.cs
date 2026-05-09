using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public MapJsonData mapData;
    public MapResourceConfig config;

    private Dictionary<Vector2Int, MapResourceConfig.TerrainType> terrainGrid = new Dictionary<Vector2Int, MapResourceConfig.TerrainType>();

    void Start()
    {
        LoadMap();
    }

    public void LoadMap()
    {
        if (mapData == null || config == null) return;

        terrainGrid.Clear();
        foreach (var cell in mapData.cells)
        {
            var terrain = System.Array.Find(config.terrainTypes, t => t.id == cell.terrainId);
            if (terrain != null)
            {
                terrainGrid[new Vector2Int(cell.x, cell.y)] = terrain;
            }
        }
    }

    public bool CanPass(Vector2Int position)
    {
        if (terrainGrid.TryGetValue(position, out var terrain))
        {
            return terrain.canPass;
        }
        return true; // 默认可通过
    }

    public bool BlocksUnit(Vector2Int position)
    {
        if (terrainGrid.TryGetValue(position, out var terrain))
        {
            return terrain.blockUnit;
        }
        return false;
    }

    public MapResourceConfig.TerrainType GetTerrain(Vector2Int position)
    {
        terrainGrid.TryGetValue(position, out var terrain);
        return terrain;
    }
}