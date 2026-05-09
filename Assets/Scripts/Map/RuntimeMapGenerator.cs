using System.Collections.Generic;
using UnityEngine;

public class RuntimeMapGenerator : MonoBehaviour
{
    [Header("配置")]
    public MapResourceConfig config;
    public TextAsset mapJson;
    public Transform mapRoot;

    [Header("渲染")]
    public Material terrainMaterial;
    public float spriteYOffset = 0.01f;

    [Header("碰撞")]
    public bool generateColliders = true;
    public PhysicMaterial blockPhysicMaterial;

    private Dictionary<Vector2Int, GameObject> _spawnedCells = new Dictionary<Vector2Int, GameObject>();
    private MapJsonData _loadedData;

    private void Awake()
    {
        GenerateMap();
    }

    [ContextMenu("生成地图")]
    public void GenerateMap()
    {
        ClearMap(immediate: true);

        GameMapGlobal.MapConfig = config;
        if (config == null) { Debug.LogError("[RuntimeMapGenerator] 配置为空！"); return; }
        if (mapJson == null) { Debug.LogError("[RuntimeMapGenerator] JSON 为空！"); return; }

        _loadedData = JsonUtility.FromJson<MapJsonData>(mapJson.text);
        if (_loadedData == null) { Debug.LogError("[RuntimeMapGenerator] JSON 解析失败！"); return; }

        if (mapRoot == null)
        {
            mapRoot = new GameObject("RuntimeMap").transform;
            mapRoot.SetParent(transform);
        }

        CreateMapFloor();

        foreach (var cell in _loadedData.cellDatas)
            SpawnCell(cell);

        Debug.Log($"[RuntimeMapGenerator] 地图生成完毕: {_loadedData.mapWidth}×{_loadedData.mapHeight}, {_loadedData.cellDatas.Count} 格");
    }

    private void CreateMapFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "MapFloor";
        floor.transform.SetParent(mapRoot);

        float scaleX = _loadedData.mapWidth / 10f;
        float scaleZ = _loadedData.mapHeight / 10f;
        floor.transform.localScale = new Vector3(scaleX, 1f, scaleZ);
        floor.transform.position = new Vector3(_loadedData.mapWidth * 0.5f, -0.01f, _loadedData.mapHeight * 0.5f);

        var renderer = floor.GetComponent<MeshRenderer>();
        if (terrainMaterial != null)
            renderer.material = terrainMaterial;

        Destroy(floor.GetComponent<Collider>());
    }

    private void SpawnCell(MapCellData cell)
    {
        var terrain = config.GetTerrainById(cell.terrainId);
        if (terrain == null)
        {
            Debug.LogWarning($"[RuntimeMapGenerator] 未知地形 ID: {cell.terrainId}");
            return;
        }

        Vector3 pos = new Vector3(cell.x + 0.5f, spriteYOffset, cell.y + 0.5f);
        GameObject cellGo = new GameObject($"Cell_{cell.x}_{cell.y}");
        cellGo.transform.SetParent(mapRoot);
        cellGo.transform.position = pos;

        if (terrain.terrainSprite != null)
        {
            var sr = cellGo.AddComponent<SpriteRenderer>();
            sr.sprite = terrain.terrainSprite;
        }

        if (generateColliders && terrain.defaultType == DefaultTerrainType.Impassable)
        {
            var col = cellGo.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            if (blockPhysicMaterial != null) col.material = blockPhysicMaterial;
        }

        var cellData = cellGo.AddComponent<TerrainCellData>();
        cellData.terrainId = cell.terrainId;
        cellData.gridX = cell.x;
        cellData.gridY = cell.y;

        _spawnedCells[new Vector2Int(cell.x, cell.y)] = cellGo;
    }

    [ContextMenu("清除地图")]
    public void ClearMap() => ClearMap(immediate: false);

    public void ClearMap(bool immediate)
    {
        if (mapRoot == null) return;

        for (int i = mapRoot.childCount - 1; i >= 0; i--)
        {
            var child = mapRoot.GetChild(i).gameObject;
            if (immediate || !Application.isPlaying)
                DestroyImmediate(child);
            else
                Destroy(child);
        }
        _spawnedCells.Clear();
    }

    public GameObject GetCell(int x, int y)
    {
        _spawnedCells.TryGetValue(new Vector2Int(x, y), out var go);
        return go;
    }
}

public class TerrainCellData : MonoBehaviour
{
    public string terrainId;
    public int gridX;
    public int gridY;

    private void OnTriggerEnter(Collider other)
    {
        var unitLogic = other.GetComponent<UnitTerrainLogic>();
        if (unitLogic != null)
            unitLogic.OnEnterGrid(terrainId);
    }

    private void OnTriggerExit(Collider other)
    {
        var unitLogic = other.GetComponent<UnitTerrainLogic>();
        if (unitLogic != null)
            unitLogic.OnExitGrid(terrainId);
    }

    public TerrainResource GetTerrain()
    {
        return GameMapGlobal.GetTerrain(terrainId);
    }
}
