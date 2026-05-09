using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MapEditorWindow : EditorWindow
{
    // ===== 配置引用 =====
    private MapResourceConfig _config;

    // ===== 地图参数 =====
    private string _mapName = "NewMap";
    private int _mapWidth = 30;
    private int _mapHeight = 30;
    private float _cellSize = 1f;

    // ===== 编辑状态 =====
    private enum EditMode { Brush, Eraser, RectangleFill, FloodFill }
    private EditMode _editMode = EditMode.Brush;
    private int _selectTerrainIndex = 0;

    // ===== 多层地图数据 =====
    private int _currentLayer = 0;
    private List<Dictionary<Vector2Int, string>> _allLayers = new List<Dictionary<Vector2Int, string>> { new Dictionary<Vector2Int, string>() };

    private Dictionary<Vector2Int, string> _mapGrid
    {
        get
        {
            while (_allLayers.Count <= _currentLayer)
                _allLayers.Add(new Dictionary<Vector2Int, string>());
            return _allLayers[_currentLayer];
        }
        set
        {
            while (_allLayers.Count <= _currentLayer)
                _allLayers.Add(new Dictionary<Vector2Int, string>());
            _allLayers[_currentLayer] = value;
        }
    }

    // ===== 拖拽绘制 =====
    private Vector2Int _lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
    private bool _isPainting = false;

    // ===== 矩形填充 =====
    private Vector2Int _rectStart;
    private bool _rectDragging = false;

    // ===== 悬停预览 =====
    private Vector2Int _hoveredCell = new Vector2Int(int.MinValue, int.MinValue);
    private Vector2Int _previousHoveredCell = new Vector2Int(int.MinValue, int.MinValue);

    // ===== Undo/Redo =====
    private const int MAX_HISTORY = 50;
    private Stack<UndoOperation> _undoStack = new Stack<UndoOperation>();
    private Stack<UndoOperation> _redoStack = new Stack<UndoOperation>();
    private UndoOperation _currentOp;

    // ===== 状态持久化 =====
    private const string SESSION_KEY = "MapEditor_LastGrid";
    private bool _restored;

    // ===== 滚动 =====
    private Vector2 _scrollPos;

    // ===== Undo 数据结构 =====
    private class UndoOperation
    {
        public Dictionary<Vector2Int, string> changedCells = new Dictionary<Vector2Int, string>();
        public HashSet<Vector2Int> removedCells = new HashSet<Vector2Int>();
    }

    // ============================================================
    //  窗口生命周期
    // ============================================================

    [MenuItem("地图编辑器/打开地图编辑器")]
    public static void OpenMapEditor()
    {
        var window = GetWindow<MapEditorWindow>("地形地图编辑器");
        window.minSize = new Vector2(320, 420);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        RestoreState();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SaveState();
    }

    private void SaveState()
    {
        var data = new MapJsonData { mapName = _mapName, mapWidth = _mapWidth, mapHeight = _mapHeight, layerCount = _allLayers.Count };
        for (int layer = 0; layer < _allLayers.Count; layer++)
            foreach (var kv in _allLayers[layer])
                data.cellDatas.Add(new MapCellData { x = kv.Key.x, y = kv.Key.y, layer = layer, terrainId = kv.Value });
        SessionState.SetString(SESSION_KEY, JsonUtility.ToJson(data));
    }

    private void RestoreState()
    {
        if (_restored) return;
        string json = SessionState.GetString(SESSION_KEY, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var data = JsonUtility.FromJson<MapJsonData>(json);
            _mapName = data.mapName;
            _mapWidth = Mathf.Max(1, data.mapWidth);
            _mapHeight = Mathf.Max(1, data.mapHeight);
            _allLayers.Clear();
            int maxLayer = Mathf.Max(1, data.layerCount);
            for (int i = 0; i < maxLayer; i++)
                _allLayers.Add(new Dictionary<Vector2Int, string>());
            _currentLayer = 0;
            foreach (var cell in data.cellDatas)
            {
                int layer = Mathf.Clamp(cell.layer, 0, _allLayers.Count - 1);
                _allLayers[layer][new Vector2Int(cell.x, cell.y)] = cell.terrainId;
            }
        }
        catch { }
        _restored = true;
    }

    // ============================================================
    //  主 OnGUI
    // ============================================================

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("==== 地形地图编辑器 ====", EditorStyles.boldLabel);

        _config = EditorGUILayout.ObjectField("地形资源配置表", _config, typeof(MapResourceConfig), false) as MapResourceConfig;
        if (_config == null)
        {
            EditorGUILayout.HelpBox("请先创建地形资源配置表：右键 Project → Create → 地图编辑器 → 地形资源配置表", MessageType.Warning);
            return;
        }

        DrawMapSetting();
        DrawEditMode();
        DrawTerrainSelect();
        DrawOperateBtn();
        DrawTerrainConfigList();
        HandleShortcuts();
    }

    // ============================================================
    //  地图参数
    // ============================================================

    private void DrawMapSetting()
    {
        GUILayout.Space(5);
        EditorGUILayout.LabelField("地图基础参数", EditorStyles.boldLabel);
        _mapName = EditorGUILayout.TextField("地图名称", _mapName);
        _mapWidth = Mathf.Max(1, EditorGUILayout.IntField("地图宽度（格子）", _mapWidth));
        _mapHeight = Mathf.Max(1, EditorGUILayout.IntField("地图高度（格子）", _mapHeight));
        _cellSize = Mathf.Max(0.1f, EditorGUILayout.FloatField("格子尺寸", _cellSize));
        int totalCells = 0;
        foreach (var layer in _allLayers) totalCells += layer.Count;
        EditorGUILayout.HelpBox($"当前地图: {_mapWidth}×{_mapHeight}, 总格子 {totalCells} | 层 {_currentLayer}: {_mapGrid.Count} 格", MessageType.Info);

        // 层切换
        EditorGUILayout.LabelField("层级管理", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ 上层", GUILayout.Width(60)) && _currentLayer > 0)
            _currentLayer--;
        _currentLayer = EditorGUILayout.IntSlider("当前编辑层", _currentLayer, 0, _allLayers.Count - 1);
        if (GUILayout.Button("下层 ▶", GUILayout.Width(60)) && _currentLayer < _allLayers.Count - 1)
            _currentLayer++;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 添加新层"))
            _allLayers.Add(new Dictionary<Vector2Int, string>());
        GUI.enabled = _allLayers.Count > 1;
        if (GUILayout.Button($"- 删除当前层 ({_allLayers[_currentLayer].Count} 格)"))
        {
            if (EditorUtility.DisplayDialog("确认删除", $"删除第 {_currentLayer} 层（含 {_allLayers[_currentLayer].Count} 个格子）？", "删除", "取消"))
            {
                _allLayers.RemoveAt(_currentLayer);
                if (_allLayers.Count == 0) _allLayers.Add(new Dictionary<Vector2Int, string>());
                _currentLayer = Mathf.Min(_currentLayer, _allLayers.Count - 1);
                Repaint();
            }
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    // ============================================================
    //  编辑模式
    // ============================================================

    private void DrawEditMode()
    {
        EditorGUILayout.LabelField("编辑模式", EditorStyles.boldLabel);
        _editMode = (EditMode)EditorGUILayout.EnumPopup("当前模式", _editMode);
    }

    // ============================================================
    //  地形选择（Popup 下拉）
    // ============================================================

    private void DrawTerrainSelect()
    {
        if (_config.terrainResources.Count == 0)
        {
            EditorGUILayout.HelpBox("地形资源列表为空，请先添加地形", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("选择要绘制的地形", EditorStyles.boldLabel);
        string[] names = new string[_config.terrainResources.Count];
        for (int i = 0; i < _config.terrainResources.Count; i++)
        {
            var t = _config.terrainResources[i];
            names[i] = t != null ? $"[{t.terrainId}] {t.terrainName}" : $"<null 下标{i}>";
        }
        _selectTerrainIndex = Mathf.Clamp(_selectTerrainIndex, 0, _config.terrainResources.Count - 1);
        _selectTerrainIndex = EditorGUILayout.Popup("选中地形", _selectTerrainIndex, names);

        var sel = _config.terrainResources[_selectTerrainIndex];
        EditorGUILayout.LabelField("当前选中", $"ID: {sel.terrainId} | {sel.terrainName} | 通行: {sel.defaultType}");
    }

    // ============================================================
    //  操作按钮
    // ============================================================

    private void DrawOperateBtn()
    {
        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空全部地图", GUILayout.Height(30)))
        {
            foreach (var layer in _allLayers)
                layer.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
        }
        if (GUILayout.Button("保存地图为 JSON", GUILayout.Height(30)))
            SaveMapToJson();
        if (GUILayout.Button("从 JSON 加载地图", GUILayout.Height(30)))
            LoadMapFromJson();
        EditorGUILayout.EndHorizontal();

        // Undo / Redo 按钮
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = _undoStack.Count > 0;
        if (GUILayout.Button($"↩ 撤销 ({_undoStack.Count})", GUILayout.Height(25)))
            PerformUndo();
        GUI.enabled = _redoStack.Count > 0;
        if (GUILayout.Button($"↪ 重做 ({_redoStack.Count})", GUILayout.Height(25)))
            PerformRedo();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);
    }

    // ============================================================
    //  地形配置列表
    // ============================================================

    private void DrawTerrainConfigList()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        EditorGUILayout.LabelField("地形资源配置列表", EditorStyles.boldLabel);

        if (GUILayout.Button("+ 添加新地形"))
        {
            _config.terrainResources.Add(new TerrainResource());
            EditorUtility.SetDirty(_config);
        }

        // 校验按钮
        if (GUILayout.Button("校验配置"))
        {
            var errors = _config.Validate();
            if (errors.Count == 0)
                EditorUtility.DisplayDialog("校验通过", "配置无问题", "确定");
            else
                EditorUtility.DisplayDialog("校验发现 " + errors.Count + " 个问题", string.Join("\n", errors.Take(15)), "确定");
        }

        for (int i = 0; i < _config.terrainResources.Count; i++)
        {
            var terrain = _config.terrainResources[i];
            if (terrain == null) continue;

            EditorGUILayout.BeginVertical("Box");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"地形 下标{i}", EditorStyles.boldLabel);
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("删除此地形", GUILayout.Width(80)))
            {
                _config.terrainResources.RemoveAt(i);
                EditorUtility.SetDirty(_config);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            terrain.terrainId = EditorGUILayout.TextField("唯一 ID", terrain.terrainId);
            terrain.terrainName = EditorGUILayout.TextField("显示名称", terrain.terrainName);
            terrain.terrainSprite = EditorGUILayout.ObjectField("预览图标", terrain.terrainSprite, typeof(Sprite), false) as Sprite;
            terrain.defaultType = (DefaultTerrainType)EditorGUILayout.EnumPopup("通行规则", terrain.defaultType);

            if (terrain.defaultType == DefaultTerrainType.UnitImpassable)
            {
                EditorGUILayout.LabelField("禁止通行的单位 ID 列表");
                EditorGUI.indentLevel++;
                for (int j = 0; j < terrain.blockUnitIds.Count; j++)
                {
                    EditorGUILayout.BeginHorizontal();
                    terrain.blockUnitIds[j] = EditorGUILayout.TextField($"单位 {j}", terrain.blockUnitIds[j]);
                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        terrain.blockUnitIds.RemoveAt(j);
                        EditorUtility.SetDirty(_config);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ 添加禁止单位 ID"))
                {
                    terrain.blockUnitIds.Add("");
                    EditorUtility.SetDirty(_config);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.LabelField("自定义属性");
            EditorGUI.indentLevel++;
            for (int j = 0; j < terrain.customProperties.Count; j++)
            {
                var prop = terrain.customProperties[j];
                if (prop == null) continue;

                EditorGUILayout.BeginVertical("Box");
                EditorGUILayout.BeginHorizontal();
                prop.propertyName = EditorGUILayout.TextField("属性名", prop.propertyName);
                prop.propertyType = (PropertyType)EditorGUILayout.EnumPopup(prop.propertyType, GUILayout.Width(60));
                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    terrain.customProperties.RemoveAt(j);
                    EditorUtility.SetDirty(_config);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                switch (prop.propertyType)
                {
                    case PropertyType.Int:
                        prop.intValue = EditorGUILayout.IntField("数值", prop.intValue);
                        break;
                    case PropertyType.Float:
                        prop.floatValue = EditorGUILayout.FloatField("数值", prop.floatValue);
                        break;
                    case PropertyType.Bool:
                        prop.boolValue = EditorGUILayout.Toggle("是否启用", prop.boolValue);
                        break;
                }
                prop.propertyEffect = EditorGUILayout.TextArea(prop.propertyEffect, GUILayout.Height(36));
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ 添加自定义属性"))
            {
                terrain.customProperties.Add(new CustomProperty());
                EditorUtility.SetDirty(_config);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }

    // ============================================================
    //  快捷键
    // ============================================================

    private void HandleShortcuts()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        if (e.control && e.keyCode == KeyCode.Z && !e.shift)
        {
            PerformUndo();
            e.Use();
        }
        if ((e.control && e.keyCode == KeyCode.Y) || (e.control && e.keyCode == KeyCode.Z && e.shift))
        {
            PerformRedo();
            e.Use();
        }
    }

    // ============================================================
    //  Scene 视图
    // ============================================================

    private void OnSceneGUI(SceneView sceneView)
    {
        if (_config == null) return;
        DrawGridGizmos();
        DrawHoverPreview();
        HandleMouseInput();
    }

    private void DrawGridGizmos()
    {
        float camDist = 50f;
        if (SceneView.lastActiveSceneView?.camera != null)
            camDist = Vector3.Distance(SceneView.lastActiveSceneView.camera.transform.position, Vector3.zero);

        int gridStep = 1;
        if (camDist > 100f) gridStep = 10;
        else if (camDist > 50f) gridStep = 5;
        else if (camDist > 20f) gridStep = 2;

        float w = _mapWidth * _cellSize;
        float h = _mapHeight * _cellSize;

        // 网格线
        Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        for (int x = 0; x <= _mapWidth; x += gridStep)
        {
            Handles.DrawLine(new Vector3(x * _cellSize, 0, 0), new Vector3(x * _cellSize, 0, h));
        }
        for (int y = 0; y <= _mapHeight; y += gridStep)
        {
            Handles.DrawLine(new Vector3(0, 0, y * _cellSize), new Vector3(w, 0, y * _cellSize));
        }

        // 地形色块
        foreach (var kv in _mapGrid)
        {
            Vector2Int cell = kv.Key;
            string tid = kv.Value;
            var terrain = _config.GetTerrainById(tid);

            Color col = Color.gray;
            if (terrain != null)
            {
                switch (terrain.defaultType)
                {
                    case DefaultTerrainType.Passable: col = Color.green; break;
                    case DefaultTerrainType.Impassable: col = Color.red; break;
                    case DefaultTerrainType.UnitImpassable: col = Color.yellow; break;
                }
            }

            Handles.color = new Color(col.r, col.g, col.b, 0.4f);
            Vector3 center = new Vector3(cell.x * _cellSize + _cellSize * 0.5f, 0, cell.y * _cellSize + _cellSize * 0.5f);
            Handles.CubeHandleCap(0, center, Quaternion.identity, _cellSize * 0.9f, EventType.Repaint);
        }

        // 原点标识
        Handles.color = Color.cyan;
        Vector3 origin = new Vector3(0, 0.15f, 0);
        Handles.DrawLine(origin, origin + Vector3.right * 0.5f);
        Handles.DrawLine(origin, origin + Vector3.forward * 0.5f);
        Handles.Label(origin + new Vector3(0.2f, 0, 0.2f), "原点 (0,0)");
    }

    // ============================================================
    //  悬停预览
    // ============================================================

    private void DrawHoverPreview()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        int cx = Mathf.FloorToInt(worldPos.x / _cellSize);
        int cy = Mathf.FloorToInt(worldPos.z / _cellSize);
        if (cx < 0 || cx >= _mapWidth || cy < 0 || cy >= _mapHeight) return;

        _hoveredCell = new Vector2Int(cx, cy);

        Color previewColor;
        float alpha;
        EditMode activeMode = e.shift ? EditMode.Eraser : _editMode;

        if (activeMode == EditMode.Eraser)
        {
            previewColor = Color.red;
            alpha = 0.35f;
        }
        else
        {
            if (_selectTerrainIndex < _config.terrainResources.Count)
            {
                switch (_config.terrainResources[_selectTerrainIndex].defaultType)
                {
                    case DefaultTerrainType.Passable: previewColor = Color.green; break;
                    case DefaultTerrainType.Impassable: previewColor = Color.red; break;
                    case DefaultTerrainType.UnitImpassable: previewColor = Color.yellow; break;
                    default: previewColor = Color.gray; break;
                }
            }
            else previewColor = Color.gray;
            alpha = 0.3f;
        }

        Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, alpha);
        Vector3 center = new Vector3(cx * _cellSize + _cellSize * 0.5f, 0.05f, cy * _cellSize + _cellSize * 0.5f);
        Handles.CubeHandleCap(0, center, Quaternion.identity, _cellSize * 0.95f, EventType.Repaint);

        // 边框
        Handles.color = new Color(1f, 1f, 1f, 0.6f);
        Vector3[] corners =
        {
            new Vector3(cx * _cellSize, 0.06f, cy * _cellSize),
            new Vector3((cx + 1) * _cellSize, 0.06f, cy * _cellSize),
            new Vector3((cx + 1) * _cellSize, 0.06f, (cy + 1) * _cellSize),
            new Vector3(cx * _cellSize, 0.06f, (cy + 1) * _cellSize),
            new Vector3(cx * _cellSize, 0.06f, cy * _cellSize),
        };
        Handles.DrawAAPolyLine(2f, corners);

        if (_hoveredCell != _previousHoveredCell)
        {
            _previousHoveredCell = _hoveredCell;
            SceneView.RepaintAll();
        }

        // Tooltip（含坐标）
        Handles.BeginGUI();
        string tooltip;
        if (activeMode == EditMode.Eraser)
        {
            tooltip = _mapGrid.ContainsKey(_hoveredCell) ? $"🗑 擦除 [{cx},{cy}]" : $"（空）[{cx},{cy}]";
        }
        else
        {
            var t = _config.terrainResources.Count > _selectTerrainIndex ? _config.terrainResources[_selectTerrainIndex] : null;
            tooltip = t != null ? $"🖌 {t.terrainName} ({t.terrainId}) @ [{cx},{cy}]" : $"无 @ [{cx},{cy}]";
        }
        var style = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
        var content = new GUIContent(tooltip);
        Vector2 size = style.CalcSize(content);
        GUI.Box(new Rect(e.mousePosition.x + 20, e.mousePosition.y - 10, size.x + 10, 22), content, style);
        Handles.EndGUI();
    }

    // ============================================================
    //  鼠标输入（支持拖拽 + 矩形填充 + Flood Fill + Shift 擦除）
    // ============================================================

    private void HandleMouseInput()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        int cx = Mathf.FloorToInt(worldPos.x / _cellSize);
        int cy = Mathf.FloorToInt(worldPos.z / _cellSize);
        if (cx < 0 || cx >= _mapWidth || cy < 0 || cy >= _mapHeight) return;

        Vector2Int cellPos = new Vector2Int(cx, cy);
        EditMode activeMode = e.shift ? EditMode.Eraser : _editMode;

        switch (activeMode)
        {
            case EditMode.Brush:
            case EditMode.Eraser:
                HandleBrushOrErase(e, cellPos, activeMode);
                break;
            case EditMode.RectangleFill:
                HandleRectFill(e, cellPos);
                break;
            case EditMode.FloodFill:
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_selectTerrainIndex < _config.terrainResources.Count)
                    {
                        BeginUndoOp();
                        DoFloodFill(cellPos, _config.terrainResources[_selectTerrainIndex].terrainId);
                        EndUndoOp();
                        Repaint();
                    }
                    e.Use();
                }
                break;
        }
    }

    private void HandleBrushOrErase(Event e, Vector2Int cellPos, EditMode mode)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _isPainting = true;
            _lastPaintedCell = cellPos;
            BeginUndoOp();
            PaintCell(cellPos, mode);
            EndUndoOp();
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
        {
            if (cellPos != _lastPaintedCell)
            {
                _lastPaintedCell = cellPos;
                BeginUndoOp();
                PaintCell(cellPos, mode);
                EndUndoOp();
                Repaint();
            }
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _isPainting = false;
            e.Use();
        }
    }

    private void HandleRectFill(Event e, Vector2Int cellPos)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _rectStart = cellPos;
            _rectDragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _rectDragging)
        {
            // 预览矩形（通过重绘 Scene 视图）
            SceneView.RepaintAll();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _rectDragging)
        {
            _rectDragging = false;
            if (_selectTerrainIndex < _config.terrainResources.Count)
            {
                BeginUndoOp();
                FillRect(_rectStart, cellPos, _config.terrainResources[_selectTerrainIndex].terrainId);
                EndUndoOp();
                Repaint();
            }
            e.Use();
        }
    }

    // ============================================================
    //  绘制操作
    // ============================================================

    private void PaintCell(Vector2Int cellPos, EditMode mode)
    {
        if (mode == EditMode.Brush)
        {
            if (_selectTerrainIndex < _config.terrainResources.Count)
            {
                string tid = _config.terrainResources[_selectTerrainIndex].terrainId;
                RecordChange(cellPos);
                _mapGrid[cellPos] = tid;
            }
        }
        else if (mode == EditMode.Eraser)
        {
            if (_mapGrid.ContainsKey(cellPos))
            {
                RecordChange(cellPos);
                _mapGrid.Remove(cellPos);
            }
        }
    }

    private void FillRect(Vector2Int a, Vector2Int b, string terrainId)
    {
        int x0 = Mathf.Clamp(Mathf.Min(a.x, b.x), 0, _mapWidth - 1);
        int x1 = Mathf.Clamp(Mathf.Max(a.x, b.x), 0, _mapWidth - 1);
        int y0 = Mathf.Clamp(Mathf.Min(a.y, b.y), 0, _mapHeight - 1);
        int y1 = Mathf.Clamp(Mathf.Max(a.y, b.y), 0, _mapHeight - 1);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                var pos = new Vector2Int(x, y);
                RecordChange(pos);
                _mapGrid[pos] = terrainId;
            }
        }
    }

    private void DoFloodFill(Vector2Int start, string fillTerrainId)
    {
        if (!_mapGrid.TryGetValue(start, out string targetId)) return;
        if (targetId == fillTerrainId) return;

        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(start);
        visited.Add(start);
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (_mapGrid.TryGetValue(p, out string id) && id == targetId)
            {
                RecordChange(p);
                _mapGrid[p] = fillTerrainId;
                foreach (var d in dirs)
                {
                    var n = p + d;
                    if (!visited.Add(n)) continue;
                    if (n.x < 0 || n.x >= _mapWidth || n.y < 0 || n.y >= _mapHeight) continue;
                    queue.Enqueue(n);
                }
            }
        }
    }

    // ============================================================
    //  Undo/Redo（增量存储）
    // ============================================================

    private void BeginUndoOp()
    {
        _currentOp = new UndoOperation();
    }

    private void RecordChange(Vector2Int cellPos)
    {
        if (_currentOp == null) return;
        if (_currentOp.changedCells.ContainsKey(cellPos) || _currentOp.removedCells.Contains(cellPos))
            return;

        if (_mapGrid.TryGetValue(cellPos, out string oldVal))
            _currentOp.changedCells[cellPos] = oldVal;
        else
            _currentOp.removedCells.Add(cellPos);
    }

    private void EndUndoOp()
    {
        if (_currentOp == null) return;
        if (_currentOp.changedCells.Count > 0 || _currentOp.removedCells.Count > 0)
        {
            _undoStack.Push(_currentOp);
            if (_undoStack.Count > MAX_HISTORY)
            {
                var list = new List<UndoOperation>(_undoStack);
                list.RemoveAt(list.Count - 1);
                list.Reverse();
                _undoStack = new Stack<UndoOperation>(list);
            }
            _redoStack.Clear();
        }
        _currentOp = null;
    }

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        var op = _undoStack.Pop();

        // 保存当前值到 redo
        var redo = new UndoOperation();
        foreach (var kv in op.changedCells)
        {
            if (_mapGrid.TryGetValue(kv.Key, out string v))
                redo.changedCells[kv.Key] = v;
            else
                redo.removedCells.Add(kv.Key);
        }
        foreach (var pos in op.removedCells)
        {
            if (_mapGrid.TryGetValue(pos, out string v))
                redo.changedCells[pos] = v;
        }
        _redoStack.Push(redo);

        // 还原
        foreach (var kv in op.changedCells)
            _mapGrid[kv.Key] = kv.Value;
        foreach (var pos in op.removedCells)
            _mapGrid.Remove(pos);

        Repaint();
        Debug.Log($"[Undo] 撤销完成，剩余: Undo {_undoStack.Count}, Redo {_redoStack.Count}");
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        var op = _redoStack.Pop();

        // 保存当前值到 undo
        var undoOp = new UndoOperation();
        foreach (var kv in op.changedCells)
        {
            if (_mapGrid.TryGetValue(kv.Key, out string v))
                undoOp.changedCells[kv.Key] = v;
            else
                undoOp.removedCells.Add(kv.Key);
        }
        foreach (var pos in op.removedCells)
        {
            if (_mapGrid.TryGetValue(pos, out string v))
                undoOp.changedCells[pos] = v;
        }
        _undoStack.Push(undoOp);

        // 重做
        foreach (var kv in op.changedCells)
            _mapGrid[kv.Key] = kv.Value;
        foreach (var pos in op.removedCells)
            _mapGrid.Remove(pos);

        Repaint();
        Debug.Log($"[Redo] 重做完成，剩余: Undo {_undoStack.Count}, Redo {_redoStack.Count}");
    }

    // ============================================================
    //  JSON 导入导出（含校验）
    // ============================================================

    private void SaveMapToJson()
    {
        MapJsonData data = new MapJsonData
        {
            mapName = _mapName,
            mapWidth = _mapWidth,
            mapHeight = _mapHeight,
            layerCount = _allLayers.Count
        };
        // 遍历所有层
        for (int layer = 0; layer < _allLayers.Count; layer++)
        {
            foreach (var kv in _allLayers[layer])
                data.cellDatas.Add(new MapCellData { x = kv.Key.x, y = kv.Key.y, layer = layer, terrainId = kv.Value });
        }

        string path = EditorUtility.SaveFilePanel("保存地图 JSON", "Assets", _mapName, "json");
        if (string.IsNullOrEmpty(path)) return;

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("保存成功", $"地图已保存到:\n{path}\n{_allLayers.Count} 层, {data.cellDatas.Count} 格", "确定");
    }

    private void LoadMapFromJson()
    {
        string path = EditorUtility.OpenFilePanel("选择地图 JSON", "Assets", "json");
        if (string.IsNullOrEmpty(path)) return;

        string json = File.ReadAllText(path);
        MapJsonData data = JsonUtility.FromJson<MapJsonData>(json);

        // 校验
        var errors = data.Validate(_config);
        if (errors.Count > 0)
        {
            string msg = "JSON 数据存在问题:\n" + string.Join("\n", errors.Take(10));
            if (errors.Count > 10) msg += $"\n... 还有 {errors.Count - 10} 个问题";
            if (!EditorUtility.DisplayDialog("加载警告", msg, "仍然加载", "取消"))
                return;
        }

        if (data.formatVersion != "1.0")
            Debug.LogWarning($"[MapEditor] JSON 版本 {data.formatVersion}，期望 1.0");

        _mapName = data.mapName;
        _mapWidth = Mathf.Max(1, data.mapWidth);
        _mapHeight = Mathf.Max(1, data.mapHeight);
        _undoStack.Clear();
        _redoStack.Clear();

        // 按层分布数据
        int maxLayer = Mathf.Max(1, data.layerCount);
        _allLayers.Clear();
        for (int i = 0; i < maxLayer; i++)
            _allLayers.Add(new Dictionary<Vector2Int, string>());
        _currentLayer = 0;

        foreach (var cell in data.cellDatas)
        {
            if (cell.x < 0 || cell.x >= _mapWidth || cell.y < 0 || cell.y >= _mapHeight)
                continue;
            int layer = Mathf.Clamp(cell.layer, 0, _allLayers.Count - 1);
            _allLayers[layer][new Vector2Int(cell.x, cell.y)] = cell.terrainId;
        }

        Repaint();
        EditorUtility.DisplayDialog("加载成功", $"已加载: {data.mapName}\n{_mapWidth}×{_mapHeight}\n{_allLayers.Count} 层, {data.cellDatas.Count} 格", "确定");
    }
}
