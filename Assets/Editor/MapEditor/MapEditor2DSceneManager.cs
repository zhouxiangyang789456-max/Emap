using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapEditor2DSceneManager
{
    private MapEditorWindow _owner;

    // 场景对象（不保存到场景文件）
    private GameObject _containerGO;
    private Grid _grid;
    private Tilemap _tilemap;

    // Tile 缓存
    private Dictionary<string, Tile> _tileCache = new Dictionary<string, Tile>();

    // 全局尺寸不匹配警告（每个会话只弹一次）
    private static bool _mismatchWarningShown = false;

    // 交互状态
    private Vector3Int _hoveredCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    private Vector3Int _lastPaintedCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    private bool _isPainting = false;
    private Vector3Int _rectStart;
    private bool _rectDragging = false;

    public MapEditor2DSceneManager(MapEditorWindow owner)
    {
        _owner = owner;
    }

    // ============================================================
    //  生命周期
    // ============================================================

    public void Setup()
    {
        if (_containerGO != null) return;

        var config = _owner.Config;
        if (config == null) return;

        // 容器
        _containerGO = new GameObject("MapEditor2D");
        _containerGO.hideFlags = HideFlags.DontSave;

        // Grid
        _grid = _containerGO.AddComponent<Grid>();
        _grid.cellLayout = GridLayout.CellLayout.Rectangle;
        _grid.cellSize = new Vector3(_owner.CellSize, _owner.CellSize, 1f);

        // Tilemap 子对象
        var tilemapGO = new GameObject("Tilemap_Layer");
        tilemapGO.transform.SetParent(_containerGO.transform);
        tilemapGO.transform.localPosition = Vector3.zero;
        _tilemap = tilemapGO.AddComponent<Tilemap>();
        var renderer = tilemapGO.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = 0;

        // 构建 Tile 缓存
        BuildTileCache();

        // 同步数据
        SyncAllTiles();

        // 全局精灵尺寸匹配检测
        CheckSpriteSizeMismatches();

        // 自动设置 Scene 视图为 2D 俯视角
        if (SceneView.lastActiveSceneView != null)
        {
            var sv = SceneView.lastActiveSceneView;
            sv.in2DMode = true;
            sv.orthographic = true;

            float w = _owner.MapWidth * _owner.CellSize;
            float h = _owner.MapHeight * _owner.CellSize;

            // 俯视角：相机从正上方垂直向下看 XY 平面
            sv.rotation = Quaternion.identity;
            // 视口中心对准地图中心
            sv.pivot = new Vector3(w * 0.5f, h * 0.5f, 0f);
            // 自动计算 orthographic size 以完整显示地图（留 10% 边距）
            float aspect = sv.camera != null ? sv.camera.aspect : 1.6f;
            float vertSize = h * 0.55f;
            float horzSize = (w * 0.55f) / aspect;
            sv.size = Mathf.Max(vertSize, horzSize, 5f);

            sv.Repaint();
        }

        Debug.Log("[MapEditor2D] 2D 编辑模式已启动");
    }

    public void Cleanup()
    {
        if (_containerGO != null)
        {
            Object.DestroyImmediate(_containerGO);
            _containerGO = null;
        }
        _grid = null;
        _tilemap = null;

        foreach (var tile in _tileCache.Values)
        {
            if (tile != null) Object.DestroyImmediate(tile);
        }
        _tileCache.Clear();

        Debug.Log("[MapEditor2D] 2D 编辑模式已清理");
    }

    // ============================================================
    //  精灵尺寸匹配检测
    // ============================================================

    private void CheckSpriteSizeMismatches()
    {
        if (_mismatchWarningShown) return;

        var config = _owner.Config;
        if (config == null) return;

        float cellSize = _owner.CellSize;
        var mismatches = new List<string>();

        foreach (var terrain in config.terrainResources)
        {
            if (terrain?.terrainSprite == null) continue;
            float worldW = terrain.terrainSprite.rect.width / terrain.terrainSprite.pixelsPerUnit;
            if (Mathf.Abs(worldW - cellSize) > 0.001f)
            {
                string label = string.IsNullOrEmpty(terrain.terrainName) ? terrain.terrainId : terrain.terrainName;
                mismatches.Add($"  [{terrain.terrainId}] {label}: 精灵={worldW:F2}, 格子={cellSize:F2}");
            }
        }

        if (mismatches.Count > 0)
        {
            _mismatchWarningShown = true;
            string msg = $"检测到 {mismatches.Count} 个地形精灵尺寸与格子尺寸({cellSize:F2})不匹配:\n\n"
                       + string.Join("\n", mismatches.Take(10))
                       + "\n\n建议: 在编辑器中将格子尺寸设为精灵世界尺寸，或在 Unity 中调整精灵 PPU 导入设置。";
            if (mismatches.Count > 10)
                msg += $"\n... 还有 {mismatches.Count - 10} 个未列出";
            EditorUtility.DisplayDialog("精灵尺寸不匹配", msg, "知道了");
        }
    }

    // ============================================================
    //  Tile 缓存
    // ============================================================

    private void BuildTileCache()
    {
        foreach (var tile in _tileCache.Values)
            if (tile != null) Object.DestroyImmediate(tile);
        _tileCache.Clear();

        var config = _owner.Config;
        if (config == null) return;

        foreach (var terrain in config.terrainResources)
        {
            if (terrain == null || terrain.terrainSprite == null) continue;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = terrain.terrainSprite;
            tile.color = terrain.defaultType switch
            {
                DefaultTerrainType.Passable => Color.white,
                DefaultTerrainType.Impassable => new Color(1f, 0.7f, 0.7f),
                DefaultTerrainType.UnitImpassable => new Color(1f, 1f, 0.7f),
                _ => Color.white
            };
            tile.name = terrain.terrainId;
            _tileCache[terrain.terrainId] = tile;
        }
    }

    // ============================================================
    //  数据 ↔ Tilemap 同步
    // ============================================================

    public void SyncAllTiles()
    {
        if (_tilemap == null) return;
        _tilemap.ClearAllTiles();

        var layers = _owner.AllLayers;
        for (int layer = 0; layer < layers.Count; layer++)
        {
            foreach (var kv in layers[layer])
            {
                if (_tileCache.TryGetValue(kv.Value, out Tile tile))
                {
                    _tilemap.SetTile(new Vector3Int(kv.Key.x, kv.Key.y, layer), tile);
                }
            }
        }
    }

    public void SyncTile(int x, int y, int layer, string terrainId)
    {
        if (_tilemap == null) return;
        if (_tileCache.TryGetValue(terrainId, out Tile tile))
            _tilemap.SetTile(new Vector3Int(x, y, layer), tile);
    }

    public void RemoveTile(int x, int y, int layer)
    {
        if (_tilemap == null) return;
        _tilemap.SetTile(new Vector3Int(x, y, layer), null);
    }

    private void SyncRect(Vector3Int a, Vector3Int b)
    {
        if (_tilemap == null) return;
        int x0 = Mathf.Min(a.x, b.x);
        int x1 = Mathf.Max(a.x, b.x);
        int y0 = Mathf.Min(a.y, b.y);
        int y1 = Mathf.Max(a.y, b.y);
        int layer = _owner.CurrentLayer;

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                string tid = _owner.GetCellTerrain(x, y, layer);
                if (string.IsNullOrEmpty(tid))
                    _tilemap.SetTile(new Vector3Int(x, y, layer), null);
                else if (_tileCache.TryGetValue(tid, out Tile tile))
                    _tilemap.SetTile(new Vector3Int(x, y, layer), tile);
            }
        }
    }

    // ============================================================
    //  Scene 视图
    // ============================================================

    public void OnSceneGUI(SceneView sceneView)
    {
        if (_tilemap == null) return;
        DrawGridLines();
        DrawTerrainHighlights();
        DrawUnitMarkers();
        DrawHoverPreview();
        HandleMouseInput();
    }

    // ============================================================
    //  网格线（XY 平面）
    // ============================================================

    private void DrawGridLines()
    {
        float camDist = 50f;
        if (SceneView.lastActiveSceneView?.camera != null)
        {
            camDist = Vector3.Distance(
                SceneView.lastActiveSceneView.camera.transform.position,
                Vector3.zero);
        }

        int gridStep = 1;
        if (camDist > 100f) gridStep = 10;
        else if (camDist > 50f) gridStep = 5;
        else if (camDist > 20f) gridStep = 2;

        float w = _owner.MapWidth * _owner.CellSize;
        float h = _owner.MapHeight * _owner.CellSize;

        Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        for (int x = 0; x <= _owner.MapWidth; x += gridStep)
        {
            Handles.DrawLine(
                new Vector3(x * _owner.CellSize, 0, 0),
                new Vector3(x * _owner.CellSize, h, 0));
        }
        for (int y = 0; y <= _owner.MapHeight; y += gridStep)
        {
            Handles.DrawLine(
                new Vector3(0, y * _owner.CellSize, 0),
                new Vector3(w, y * _owner.CellSize, 0));
        }

        // 原点标识
        Handles.color = Color.cyan;
        Vector3 origin = new Vector3(0, 0, -0.1f);
        Handles.DrawLine(origin, origin + Vector3.right * 0.5f);
        Handles.DrawLine(origin, origin + Vector3.up * 0.5f);
        Handles.Label(origin + new Vector3(0.2f, 0.2f, 0), "原点 (0,0)");
    }

    // ============================================================
    //  地形高亮（非 Tilemap 的空格用灰色方块标识）
    // ============================================================

    private void DrawTerrainHighlights()
    {
        // 网格中的地形已由 Tilemap 渲染，此处不额外绘制。
        // 但为了显示没有 Tile 的格子的边框，在密网格时跳过。
        int totalCells = _owner.MapWidth * _owner.MapHeight;
        if (totalCells > 2500) return; // 超过 50x50 时跳过以提高性能
    }

    // ============================================================
    //  对象层单位标记
    // ============================================================

    private void DrawUnitMarkers()
    {
        if (_owner.IsCurrentLayerTerrainInternal) return;

        var unitGrid = _owner.CurrentUnitGrid;
        if (unitGrid.Count == 0) return;

        float cs = _owner.CellSize;
        foreach (var kv in unitGrid)
        {
            Vector2Int cell = kv.Key;
            string uid = kv.Value;
            string name = _owner.GetUnitNameById(uid);

            Vector3 center = new Vector3(cell.x * cs + cs * 0.5f, cell.y * cs + cs * 0.5f, -0.08f);
            Handles.color = new Color(0.3f, 0.5f, 1f, 0.6f);
            Handles.DrawSolidDisc(center, Vector3.forward, cs * 0.35f);
            Handles.color = Color.white;
            Handles.Label(center + new Vector3(0, -cs * 0.15f, 0), $"{name}\n[{cell.x},{cell.y}]");
        }
    }

    // ============================================================
    //  悬停预览
    // ============================================================

    private void DrawHoverPreview()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(new Vector3(0, 0, -1), Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        int cx = Mathf.FloorToInt(worldPos.x / _owner.CellSize);
        int cy = Mathf.FloorToInt(worldPos.y / _owner.CellSize);
        if (cx < 0 || cx >= _owner.MapWidth || cy < 0 || cy >= _owner.MapHeight) return;

        _hoveredCell = new Vector3Int(cx, cy, _owner.CurrentLayer);

        Color previewColor;
        var activeMode = e.shift ? MapEditorWindow.EditMode.Eraser : _owner.CurrentEditMode;

        if (activeMode == MapEditorWindow.EditMode.Eraser)
        {
            previewColor = Color.red;
        }
        else
        {
            var config = _owner.Config;
            string selId = _owner.SelectedTerrainId;
            var terrain = config.GetTerrainById(selId);
            if (terrain != null)
            {
                previewColor = terrain.defaultType switch
                {
                    DefaultTerrainType.Passable => Color.green,
                    DefaultTerrainType.Impassable => Color.red,
                    DefaultTerrainType.UnitImpassable => Color.yellow,
                    _ => Color.gray
                };
            }
            else previewColor = Color.gray;
        }

        Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.35f);
        Vector3[] corners =
        {
            new Vector3(cx * _owner.CellSize, cy * _owner.CellSize, -0.05f),
            new Vector3((cx + 1) * _owner.CellSize, cy * _owner.CellSize, -0.05f),
            new Vector3((cx + 1) * _owner.CellSize, (cy + 1) * _owner.CellSize, -0.05f),
            new Vector3(cx * _owner.CellSize, (cy + 1) * _owner.CellSize, -0.05f),
        };
        Handles.DrawSolidRectangleWithOutline(corners,
            new Color(previewColor.r, previewColor.g, previewColor.b, 0.3f),
            new Color(1f, 1f, 1f, 0.6f));

        // 矩形填充拖拽预览
        if (_rectDragging && activeMode == MapEditorWindow.EditMode.RectangleFill)
        {
            DrawRectPreview(_rectStart, _hoveredCell, previewColor);
        }

        // Tooltip
        Handles.BeginGUI();
        string tooltip;
        string cellTerrain = _owner.GetCellTerrain(cx, cy, _owner.CurrentLayer);
        if (activeMode == MapEditorWindow.EditMode.Eraser)
        {
            tooltip = !string.IsNullOrEmpty(cellTerrain)
                ? $"Eraser [{cx},{cy}] <- {cellTerrain}"
                : $"(空) [{cx},{cy}]";
        }
        else
        {
            var terrain = _owner.Config.GetTerrainById(_owner.SelectedTerrainId);
            tooltip = terrain != null
                ? $"Brush {terrain.terrainName} ({terrain.terrainId}) @ [{cx},{cy}]"
                : $"无 [{cx},{cy}]";
        }
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft
        };
        style.normal.textColor = Color.white;
        var content = new GUIContent(tooltip);
        Vector2 size = style.CalcSize(content);
        GUI.Box(new Rect(e.mousePosition.x + 20, e.mousePosition.y - 10, size.x + 10, 22), content, style);
        Handles.EndGUI();
    }

    private void DrawRectPreview(Vector3Int a, Vector3Int b, Color color)
    {
        int x0 = Mathf.Min(a.x, b.x);
        int x1 = Mathf.Max(a.x, b.x);
        int y0 = Mathf.Min(a.y, b.y);
        int y1 = Mathf.Max(a.y, b.y);

        Handles.color = new Color(color.r, color.g, color.b, 0.15f);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                Vector3[] corners =
                {
                    new Vector3(x * _owner.CellSize, y * _owner.CellSize, -0.06f),
                    new Vector3((x + 1) * _owner.CellSize, y * _owner.CellSize, -0.06f),
                    new Vector3((x + 1) * _owner.CellSize, (y + 1) * _owner.CellSize, -0.06f),
                    new Vector3(x * _owner.CellSize, (y + 1) * _owner.CellSize, -0.06f),
                };
                Handles.DrawSolidRectangleWithOutline(corners, color, Color.white);
            }
        }
    }

    // ============================================================
    //  鼠标输入
    // ============================================================

    private void HandleMouseInput()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(new Vector3(0, 0, -1), Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        int cx = Mathf.FloorToInt(worldPos.x / _owner.CellSize);
        int cy = Mathf.FloorToInt(worldPos.y / _owner.CellSize);
        if (cx < 0 || cx >= _owner.MapWidth || cy < 0 || cy >= _owner.MapHeight) return;

        Vector3Int tilePos = new Vector3Int(cx, cy, _owner.CurrentLayer);
        Vector2Int modelPos = new Vector2Int(cx, cy);
        var activeMode = e.shift ? MapEditorWindow.EditMode.Eraser : _owner.CurrentEditMode;

        switch (activeMode)
        {
            case MapEditorWindow.EditMode.Brush:
            case MapEditorWindow.EditMode.Eraser:
                HandleBrushOrErase(e, tilePos, modelPos, activeMode);
                break;
            case MapEditorWindow.EditMode.RectangleFill:
                HandleRectFill(e, tilePos, modelPos);
                break;
            case MapEditorWindow.EditMode.FloodFill:
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    string fillId = _owner.SelectedTerrainId;
                    if (!string.IsNullOrEmpty(fillId))
                    {
                        _owner.BeginUndoOp();
                        _owner.DoFloodFill(modelPos, fillId);
                        _owner.EndUndoOp();
                        SyncAllTiles();
                        _owner.RepaintWindow();
                    }
                    e.Use();
                }
                break;
        }
    }

    private void HandleBrushOrErase(Event e, Vector3Int tilePos, Vector2Int modelPos, MapEditorWindow.EditMode mode)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _isPainting = true;
            _lastPaintedCell = tilePos;
            _owner.BeginUndoOp();
            PaintOneCell(modelPos, tilePos, mode);
            _owner.EndUndoOp();
            _owner.RepaintWindow();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
        {
            if (tilePos != _lastPaintedCell)
            {
                _lastPaintedCell = tilePos;
                _owner.BeginUndoOp();
                PaintOneCell(modelPos, tilePos, mode);
                _owner.EndUndoOp();
                _owner.RepaintWindow();
            }
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _isPainting = false;
            e.Use();
        }
    }

    private void PaintOneCell(Vector2Int modelPos, Vector3Int tilePos, MapEditorWindow.EditMode mode)
    {
        int layer = _owner.CurrentLayer;
        if (mode == MapEditorWindow.EditMode.Eraser)
        {
            _owner.RecordChange(modelPos);
            _owner.RemoveCellTerrain(modelPos.x, modelPos.y, layer);
            Undo.RecordObject(_tilemap, "Erase Tile");
            RemoveTile(tilePos.x, tilePos.y, layer);
        }
        else
        {
            string terrainId = _owner.SelectedTerrainId;
            if (string.IsNullOrEmpty(terrainId)) return;
            _owner.RecordChange(modelPos);
            _owner.SetCellTerrain(modelPos.x, modelPos.y, layer, terrainId);
            Undo.RecordObject(_tilemap, "Paint Tile");
            SyncTile(tilePos.x, tilePos.y, layer, terrainId);
        }
    }

    private void HandleRectFill(Event e, Vector3Int tilePos, Vector2Int modelPos)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _rectStart = tilePos;
            _rectDragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _rectDragging)
        {
            SceneView.RepaintAll();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _rectDragging)
        {
            _rectDragging = false;
            string fillId = _owner.SelectedTerrainId;
            if (!string.IsNullOrEmpty(fillId))
            {
                _owner.BeginUndoOp();
                _owner.FillRect(
                    new Vector2Int(_rectStart.x, _rectStart.y),
                    new Vector2Int(tilePos.x, tilePos.y),
                    fillId);
                _owner.EndUndoOp();
                Undo.RecordObject(_tilemap, "Rectangle Fill");
                SyncRect(_rectStart, tilePos);
                _owner.RepaintWindow();
            }
            e.Use();
        }
    }
}
