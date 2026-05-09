using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 基于 Unity Tilemap 的运行时地图生成器（推荐用于 >50×50 的大型地图）。
/// 性能远优于每格一个 GameObject 的方案。
/// </summary>
public class RuntimeTilemapGenerator : MonoBehaviour
{
    [Header("配置")]
    public MapResourceConfig config;
    public TextAsset mapJson;

    [Header("Tilemap 引用（场景中预置或自动创建）")]
    public Tilemap tilemap;

    [Header("可选：按通行规则分层的 Tilemap")]
    public Tilemap passableTilemap;
    public Tilemap impassableTilemap;

    private MapJsonData _loadedData;

    private void Awake()
    {
        GenerateMap();
    }

    [ContextMenu("生成地图")]
    public void GenerateMap()
    {
        ClearAllTilemaps();

        GameMapGlobal.MapConfig = config;
        if (config == null) { Debug.LogError("[RuntimeTilemapGenerator] 配置为空！"); return; }
        if (mapJson == null) { Debug.LogError("[RuntimeTilemapGenerator] JSON 为空！"); return; }

        _loadedData = JsonUtility.FromJson<MapJsonData>(mapJson.text);
        if (_loadedData == null) { Debug.LogError("[RuntimeTilemapGenerator] JSON 解析失败！"); return; }

        // 自动创建 Tilemap（如果未手动指定）
        if (tilemap == null)
        {
            var go = new GameObject("GeneratedTilemap");
            go.transform.SetParent(transform);
            tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
        }

        // 分批设置 Tile（避免单帧卡顿）
        int count = 0;
        foreach (var cell in _loadedData.cellDatas)
        {
            var terrain = config.GetTerrainById(cell.terrainId);
            if (terrain?.terrainSprite == null) continue;

            Tile tile = CreateTileFromTerrain(terrain);
            Vector3Int pos = new Vector3Int(cell.x, cell.y, cell.layer);

            // 根据通行规则决定放到哪个 Tilemap
            var targetMap = terrain.defaultType switch
            {
                DefaultTerrainType.Impassable when impassableTilemap != null => impassableTilemap,
                _ => tilemap
            };
            targetMap.SetTile(pos, tile);
            count++;
        }

        Debug.Log($"[RuntimeTilemapGenerator] 地图生成完毕: {_loadedData.mapWidth}×{_loadedData.mapHeight}, {count} tiles");
    }

    /// <summary>从地形资源创建 Tile</summary>
    private Tile CreateTileFromTerrain(TerrainResource terrain)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = terrain.terrainSprite;
        tile.color = terrain.defaultType switch
        {
            DefaultTerrainType.Passable => Color.white,
            DefaultTerrainType.Impassable => new Color(1f, 0.7f, 0.7f),
            DefaultTerrainType.UnitImpassable => new Color(1f, 1f, 0.7f),
            _ => Color.white
        };
        tile.name = terrain.terrainId;
        return tile;
    }

    [ContextMenu("清除 Tilemap")]
    public void ClearAllTilemaps()
    {
        if (tilemap != null) tilemap.ClearAllTiles();
        if (passableTilemap != null) passableTilemap.ClearAllTiles();
        if (impassableTilemap != null) impassableTilemap.ClearAllTiles();
    }

    /// <summary>获取某格子的地形 ID（从 Tilemap 查询）</summary>
    public string GetTerrainIdAt(int x, int y, int layer = 0)
    {
        Vector3Int pos = new Vector3Int(x, y, layer);
        TileBase tile = tilemap.GetTile(pos);
        if (tile != null) return tile.name;
        if (impassableTilemap != null)
        {
            tile = impassableTilemap.GetTile(pos);
            if (tile != null) return tile.name;
        }
        return null;
    }
}
