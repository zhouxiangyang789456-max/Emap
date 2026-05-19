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
    private GridShape _gridShape = GridShape.Square;
    private int _hexFillRange = 1;

    // ===== 编辑状态 =====
    internal enum EditMode { Brush, Eraser, RectangleFill, FloodFill, HexRangeFill }
    internal enum LayerType { Terrain, Object }
    internal enum GridShape { Square, HexFlatTop, HexPointyTop }
    private EditMode _editMode = EditMode.Brush;
    private int _selectTerrainIndex = 0;
    private int _selectUnitIndex = 0;
    private List<UnitConfig> _unitConfigs = new List<UnitConfig>();

    // ===== 多层地图数据 =====
    private int _currentLayer = 0;
    private List<LayerType> _layerTypes = new List<LayerType> { LayerType.Terrain };
    private List<Dictionary<Vector2Int, string>> _allLayers = new List<Dictionary<Vector2Int, string>> { new Dictionary<Vector2Int, string>() };
    private List<Dictionary<Vector2Int, string>> _allUnitLayers = new List<Dictionary<Vector2Int, string>> { new Dictionary<Vector2Int, string>() };

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

    private Dictionary<Vector2Int, string> _unitGrid
    {
        get
        {
            while (_allUnitLayers.Count <= _currentLayer)
                _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
            return _allUnitLayers[_currentLayer];
        }
        set
        {
            while (_allUnitLayers.Count <= _currentLayer)
                _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
            _allUnitLayers[_currentLayer] = value;
        }
    }

    private bool IsCurrentLayerTerrain => _currentLayer < _layerTypes.Count
        ? _layerTypes[_currentLayer] == LayerType.Terrain
        : true;

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

    private enum EditorTab { MapEdit, ResourceManage }
    private EditorTab _currentTab = EditorTab.MapEdit;

    private static readonly string[] PredefinedTags = { "地形", "单位", "建筑" };

    private enum TileShape { Rectangle, Hexagon }

    private struct TileSizePreset
    {
        public string label;
        public int pixelWidth;
        public int pixelHeight;
    }

    private static readonly TileSizePreset[] SquarePresets = new[]
    {
        new TileSizePreset { label = "32×32",   pixelWidth = 32,  pixelHeight = 32 },
        new TileSizePreset { label = "64×64",   pixelWidth = 64,  pixelHeight = 64 },
        new TileSizePreset { label = "128×128", pixelWidth = 128, pixelHeight = 128 },
        new TileSizePreset { label = "256×256", pixelWidth = 256, pixelHeight = 256 },
    };

    private static readonly TileSizePreset[] HexPresets = new[]
    {
        new TileSizePreset { label = "64×74",   pixelWidth = 64,  pixelHeight = 74 },
        new TileSizePreset { label = "128×148", pixelWidth = 128, pixelHeight = 148 },
        new TileSizePreset { label = "256×296", pixelWidth = 256, pixelHeight = 296 },
    };

    private static float SquarePresetToCellSize(TileSizePreset p) => p.pixelWidth / 100f;
    private static float HexPresetToRadius(TileSizePreset p) => p.pixelHeight / 200f;

    // ===== 地形列表折叠状态 =====
    private HashSet<int> _expandedTerrains = new HashSet<int>();

    // ===== 新建地形临时输入 =====
    private bool _showNewTerrainForm;
    private string _newTerrainId = "";
    private string _newTerrainName = "";
    private DefaultTerrainType _newTerrainType = DefaultTerrainType.Passable;
    private Sprite _newTerrainSprite;
    private GameObject _newTerrainPrefab;
    private TileShape _newTileShape = TileShape.Rectangle;
    private int _newTileSizeIndex = 2;
    private string _newResourceTag = "地形";
    private string _customTagInput = "";
    private bool _useCustomTag = false;
    private int _newUnitAttack = 10;
    private float _newUnitSpeed = 5f;
    private int _newUnitHealth = 100;

    // ===== Undo 数据结构 =====
    private class UndoOperation
    {
        public int layer;
        public bool isTerrainLayer;
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
        _scene2D?.Cleanup();
        SaveState();
    }

    private void SaveState()
    {
        var data = new MapJsonData
        {
            mapName = _mapName,
            mapWidth = _mapWidth,
            mapHeight = _mapHeight,
            layerCount = _allLayers.Count,
            cellSize = _cellSize,
            gridShape = _gridShape == GridShape.Square ? "Square" : "Hex",
            hexOrientation = _gridShape == GridShape.HexPointyTop ? "PointyTop" : "FlatTop"
        };
        for (int layer = 0; layer < _allLayers.Count; layer++)
            foreach (var kv in _allLayers[layer])
                data.cellDatas.Add(new MapCellData { x = kv.Key.x, y = kv.Key.y, layer = layer, terrainId = kv.Value });
        for (int layer = 0; layer < _allUnitLayers.Count; layer++)
            foreach (var kv in _allUnitLayers[layer])
                data.unitDatas.Add(new MapUnitData { x = kv.Key.x, y = kv.Key.y, layer = layer, unitId = kv.Value });
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
            if (data.cellSize > 0) _cellSize = data.cellSize;
            if (!string.IsNullOrEmpty(data.gridShape))
                _gridShape = data.gridShape == "Hex"
                    ? (data.hexOrientation == "PointyTop" ? GridShape.HexPointyTop : GridShape.HexFlatTop)
                    : GridShape.Square;

            _allLayers.Clear();
            _allUnitLayers.Clear();
            _layerTypes.Clear();
            int maxLayer = Mathf.Max(1, data.layerCount);
            for (int i = 0; i < maxLayer; i++)
            {
                _allLayers.Add(new Dictionary<Vector2Int, string>());
                _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
                _layerTypes.Add(LayerType.Terrain);
            }
            _currentLayer = 0;
            foreach (var cell in data.cellDatas)
            {
                int layer = Mathf.Clamp(cell.layer, 0, _allLayers.Count - 1);
                _allLayers[layer][new Vector2Int(cell.x, cell.y)] = cell.terrainId;
            }
            if (data.unitDatas != null)
            {
                foreach (var unit in data.unitDatas)
                {
                    int layer = Mathf.Clamp(unit.layer, 0, _allUnitLayers.Count - 1);
                    _allUnitLayers[layer][new Vector2Int(unit.x, unit.y)] = unit.unitId;
                    if (_allLayers[layer].Count == 0)
                        _layerTypes[layer] = LayerType.Object;
                }
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
        GUILayout.Space(5);
        EditorGUILayout.LabelField("==== 地形地图编辑器 ====", EditorStyles.boldLabel);

        _config = EditorGUILayout.ObjectField("地形资源配置表", _config, typeof(MapResourceConfig), false) as MapResourceConfig;
        if (_config == null)
        {
            EditorGUILayout.HelpBox("请先创建地形资源配置表：右键 Project → Create → 地图编辑器 → 地形资源配置表", MessageType.Warning);
            return;
        }

        GUILayout.Space(4);
        _currentTab = (EditorTab)GUILayout.Toolbar((int)_currentTab, new[] { "地图编辑", "资源管理" });
        GUILayout.Space(6);

        switch (_currentTab)
        {
            case EditorTab.MapEdit:
                DrawTab_MapEdit();
                break;
            case EditorTab.ResourceManage:
                DrawTab_ResourceManage();
                break;
        }

        HandleShortcuts();
    }

    // ============================================================
    //  Tab: 地图编辑
    // ============================================================

    private void DrawTab_MapEdit()
    {
        DrawMapSetting();
        DrawDimensionToggle();

        if (!IsCurrentLayerTerrain && !_config.terrainResources.Any(t => t != null && t.resourceTag == "单位"))
        {
            EditorGUILayout.HelpBox("请先在「资源管理」页签中创建标签为\"单位\"的资源", MessageType.Warning);
        }

        DrawEditMode();
        if (IsCurrentLayerTerrain)
            DrawTerrainSelect();
        else
            DrawUnitSelect();
        DrawOperateBtn();
    }

    // ============================================================
    //  Tab: 资源管理
    // ============================================================

    private void DrawTab_ResourceManage()
    {
        DrawTerrainConfigList();
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

        // 网格形状选择
        EditorGUILayout.LabelField("网格形状", EditorStyles.boldLabel);
        var newShape = (GridShape)EditorGUILayout.EnumPopup("网格类型", _gridShape);
        if (newShape != _gridShape)
        {
            OnGridShapeChanged(newShape);
        }

        if (_gridShape != GridShape.Square)
        {
            EditorGUILayout.HelpBox(
                "六边形模式：x=列(col)，y=行(row)，矩形 mapWidth×mapHeight 蜂窝（与 Unity Hex Tilemap 一致）。\n" +
                "若网格线仍有空隙，请切换到 3D 再切回 2D，或修改格子尺寸以重建 Grid。",
                MessageType.Info);
        }

        string cellSizeLabel = _gridShape == GridShape.Square
            ? "格子尺寸（正方形边长）"
            : "格子尺寸（六边形外接圆半径）";
        EditorGUI.BeginChangeCheck();
        _cellSize = Mathf.Max(0.1f, EditorGUILayout.FloatField(cellSizeLabel, _cellSize));
        if (EditorGUI.EndChangeCheck() && _dimension == MapEditDimension.TwoD)
            _scene2D?.EnsureGridConfigured();

        // 尺寸预设
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("快速预设", GUILayout.Width(60));
        if (_gridShape != GridShape.Square)
        {
            for (int i = 0; i < HexPresets.Length; i++)
            {
                if (GUILayout.Button(HexPresets[i].label, GUILayout.Width(68), GUILayout.Height(22)))
                {
                    _cellSize = HexPresetToRadius(HexPresets[i]);
                    OnDimensionChanged();
                }
            }
        }
        else
        {
            for (int i = 0; i < SquarePresets.Length; i++)
            {
                if (GUILayout.Button(SquarePresets[i].label, GUILayout.Width(68), GUILayout.Height(22)))
                {
                    _cellSize = SquarePresetToCellSize(SquarePresets[i]);
                    OnDimensionChanged();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        // 精灵尺寸参考
        DrawSpriteSizeReference();

        int totalCells = 0;
        foreach (var layer in _allLayers) totalCells += layer.Count;
        foreach (var layer in _allUnitLayers) totalCells += layer.Count;
        string layerTypeLabel = IsCurrentLayerTerrain ? "地形" : "对象";
        int currentCount = IsCurrentLayerTerrain ? _mapGrid.Count : _unitGrid.Count;
        string coordLabel = _gridShape == GridShape.Square ? "" : " (坐标: 列/行)";
        EditorGUILayout.HelpBox($"当前地图: {_mapWidth}×{_mapHeight}, 总格子 {totalCells} | 层 {_currentLayer} [{layerTypeLabel}]: {currentCount} 格{coordLabel}", MessageType.Info);

        if (_gridShape != GridShape.Square)
        {
            var bounds = HexGridUtils.GetMapBoundsWorld2D(_mapWidth, _mapHeight, _cellSize, CurrentHexOrientation);
            float approxWidth = bounds.max.x - bounds.min.x;
            float approxHeight = bounds.max.y - bounds.min.y;
            EditorGUILayout.HelpBox(
                $"六边形矩形地图: {_mapWidth} 列 × {_mapHeight} 行 = {_mapWidth * _mapHeight} 格\n" +
                $"物理尺寸约: {approxWidth:F1} × {approxHeight:F1} 单位\n" +
                $"坐标: 列∈[0,{_mapWidth - 1}], 行∈[0,{_mapHeight - 1}]（与 Unity Hex Tilemap 一致）",
                MessageType.Info);
        }

        // 层切换
        EditorGUILayout.LabelField("层级管理", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ 上层", GUILayout.Width(60)) && _currentLayer > 0)
            _currentLayer--;
        _currentLayer = EditorGUILayout.IntSlider("当前编辑层", _currentLayer, 0, _allLayers.Count - 1);
        if (GUILayout.Button("下层 ▶", GUILayout.Width(60)) && _currentLayer < _allLayers.Count - 1)
            _currentLayer++;

        // 层类型标签
        string typeText = IsCurrentLayerTerrain ? "[地形层]" : "[对象层]";
        GUILayout.Label(typeText, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 添加地形层"))
        {
            _allLayers.Add(new Dictionary<Vector2Int, string>());
            _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
            _layerTypes.Add(LayerType.Terrain);
            Repaint();
        }
        if (GUILayout.Button("+ 添加对象层"))
        {
            _allLayers.Add(new Dictionary<Vector2Int, string>());
            _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
            _layerTypes.Add(LayerType.Object);
            Repaint();
        }
        GUI.enabled = _allLayers.Count > 1;
        int delCount = IsCurrentLayerTerrain ? _mapGrid.Count : _unitGrid.Count;
        if (GUILayout.Button($"- 删除当前层 ({delCount} 格)"))
        {
            if (EditorUtility.DisplayDialog("确认删除", $"删除第 {_currentLayer} 层 [{typeText}]（含 {delCount} 个格子）？", "删除", "取消"))
            {
                _allLayers.RemoveAt(_currentLayer);
                _allUnitLayers.RemoveAt(_currentLayer);
                _layerTypes.RemoveAt(_currentLayer);
                if (_allLayers.Count == 0)
                {
                    _allLayers.Add(new Dictionary<Vector2Int, string>());
                    _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
                    _layerTypes.Add(LayerType.Terrain);
                }
                _currentLayer = Mathf.Min(_currentLayer, _allLayers.Count - 1);
                Repaint();
                OnCellDataChanged();
            }
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    // ============================================================
    //  精灵尺寸参考
    // ============================================================

    private void DrawSpriteSizeReference()
    {
        if (_config == null || _selectedTerrain == null) return;

        var terrainSprite = _selectedTerrain.terrainSprite;
        if (terrainSprite == null) return;

        Sprite sprite = terrainSprite;
        float ppu = sprite.pixelsPerUnit;
        float spriteW = sprite.rect.width;
        float spriteH = sprite.rect.height;
        float worldW = spriteW / ppu;
        float worldH = spriteH / ppu;

        EditorGUILayout.LabelField(
            $"精灵参考: {spriteW}×{spriteH}px, PPU={ppu}, 世界尺寸={worldW:F2}×{worldH:F2}",
            EditorStyles.miniLabel);

        if (Mathf.Abs(worldW - _cellSize) > 0.001f || Mathf.Abs(worldH - _cellSize) > 0.001f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(
                $"精灵世界尺寸({worldW:F2})与格子尺寸({_cellSize:F2})不匹配！",
                MessageType.Warning);
            if (GUILayout.Button("自动匹配", GUILayout.Width(70), GUILayout.Height(38)))
            {
                _cellSize = worldW;
                OnDimensionChanged();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ============================================================
    //  维度切换
    // ============================================================

    private void DrawDimensionToggle()
    {
        EditorGUILayout.LabelField("编辑维度", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _dimension = (MapEditDimension)EditorGUILayout.EnumPopup("维度模式", _dimension);
        string toggleLabel = _dimension == MapEditDimension.TwoD ? "切换到 3D 编辑" : "切换到 2D 编辑";
        if (GUILayout.Button(toggleLabel, GUILayout.Width(130)))
        {
            _dimension = _dimension == MapEditDimension.ThreeD ? MapEditDimension.TwoD : MapEditDimension.ThreeD;
            OnDimensionChanged();
        }
        EditorGUILayout.EndHorizontal();

        if (_dimension == MapEditDimension.TwoD)
        {
            EditorGUILayout.HelpBox(
                "2D 编辑模式：请在 Scene 视图中切换到 2D 视角（点击 Scene 视图右上角的 2D 按钮），"
                + "或将摄像机对准 Z 轴方向以获得最佳编辑体验。",
                MessageType.Info);
        }
        GUILayout.Space(5);
    }

    // ============================================================
    //  编辑模式
    // ============================================================

    private string[] GetEditModeNames()
    {
        if (_gridShape != GridShape.Square)
            return new[] { "笔刷", "橡皮", "菱形填充", "洪水填充", "范围填充" };
        else
            return new[] { "笔刷", "橡皮", "矩形填充", "洪水填充" };
    }

    private void DrawEditMode()
    {
        EditorGUILayout.LabelField("编辑模式", EditorStyles.boldLabel);
        int modeIndex = (int)_editMode;

        if (!IsCurrentLayerTerrain)
        {
            string[] objectModeNames = { "笔刷（放置单位）", "橡皮（移除单位）" };
            if (modeIndex > 1) modeIndex = 0;
            modeIndex = EditorGUILayout.Popup("当前模式", modeIndex, objectModeNames);
        }
        else
        {
            string[] names = GetEditModeNames();
            if (modeIndex >= names.Length) modeIndex = 0;
            modeIndex = EditorGUILayout.Popup("当前模式", modeIndex, names);
        }
        _editMode = (EditMode)Mathf.Clamp(modeIndex, 0, _gridShape != GridShape.Square ? 4 : 3);

        if (_gridShape != GridShape.Square && _editMode == EditMode.HexRangeFill)
        {
            _hexFillRange = EditorGUILayout.IntSlider("填充范围", _hexFillRange, 0, 10);
        }
    }

    // ============================================================
    //  地形选择（Popup 下拉）
    // ============================================================

    private void DrawTerrainSelect()
    {
        // 筛选非单位标签的地形资源
        var terrainList = _config.terrainResources
            .Where(t => t != null && t.resourceTag != "单位").ToList();
        if (terrainList.Count == 0)
        {
            EditorGUILayout.HelpBox("没有可用的地形资源，请先在「资源管理」中创建。", MessageType.Warning);
            return;
        }

        // 过滤掉 null 的地形项
        var validList = new List<int>();
        string[] names = new string[terrainList.Count];
        for (int i = 0; i < terrainList.Count; i++)
        {
            var t = terrainList[i];
            if (t == null)
            {
                names[i] = $"(null 下标{i})";
            }
            else
            {
                string idStr = string.IsNullOrEmpty(t.terrainId) ? "???" : t.terrainId;
                string nameStr = string.IsNullOrEmpty(t.terrainName) ? "未命名" : t.terrainName;
                string tagStr = string.IsNullOrEmpty(t.resourceTag) ? "" : $" [{t.resourceTag}]";
                names[i] = $"[{idStr}] {nameStr}{tagStr}";
                validList.Add(i);
            }
        }

        // 确保选中索引指向有效的 terrain
        if (validList.Count > 0 && !validList.Contains(_selectTerrainIndex))
            _selectTerrainIndex = validList[0];
        _selectTerrainIndex = Mathf.Clamp(_selectTerrainIndex, 0, terrainList.Count - 1);

        EditorGUILayout.LabelField("选择要绘制的地形", EditorStyles.boldLabel);
        _selectTerrainIndex = EditorGUILayout.Popup("选中地形", _selectTerrainIndex, names);

        if (_selectTerrainIndex < terrainList.Count)
        {
            var sel = terrainList[_selectTerrainIndex];
            if (sel != null)
            {
                string passLabel = sel.defaultType switch
                {
                    DefaultTerrainType.Passable => "可通行",
                    DefaultTerrainType.Impassable => "不可通行",
                    DefaultTerrainType.UnitImpassable => "单位不可通行",
                    _ => "未知"
                };
                EditorGUILayout.LabelField("当前选中", $"ID: {sel.terrainId} | {sel.terrainName} | 标签: {sel.resourceTag} | 通行: {passLabel}");
            }
        }
    }

    // ============================================================
    //  单位选择（对象层）
    // ============================================================

    private void DrawUnitSelect()
    {
        // 从 TerrainResource 中筛选标签="单位"的资源
        var unitResources = _config.terrainResources
            .Where(t => t != null && t.resourceTag == "单位").ToList();
        if (unitResources.Count == 0)
        {
            EditorGUILayout.HelpBox("没有标签为\"单位\"的资源。请在「资源管理」中创建。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("选择要放置的单位", EditorStyles.boldLabel);
        string[] names = new string[unitResources.Count];
        for (int i = 0; i < unitResources.Count; i++)
        {
            var u = unitResources[i];
            names[i] = $"[{u.terrainId}] {u.terrainName}  ATK:{u.unitAttack} SPD:{u.unitSpeed} HP:{u.unitHealth}";
        }
        _selectUnitIndex = Mathf.Clamp(_selectUnitIndex, 0, unitResources.Count - 1);
        _selectUnitIndex = EditorGUILayout.Popup("选中单位", _selectUnitIndex, names);

        var sel = unitResources[_selectUnitIndex];
        if (sel != null)
        {
            EditorGUILayout.LabelField("当前选中", $"ID: {sel.terrainId} | {sel.terrainName} | ATK:{sel.unitAttack} SPD:{sel.unitSpeed} HP:{sel.unitHealth}");
            if (sel.prefab != null)
                EditorGUILayout.ObjectField("预制体", sel.prefab, typeof(GameObject), false);
            else
                EditorGUILayout.HelpBox("此单位未关联预制体", MessageType.Warning);
        }
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
            foreach (var layer in _allLayers) layer.Clear();
            foreach (var layer in _allUnitLayers) layer.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            OnCellDataChanged();
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
        EditorGUILayout.LabelField("资源配置列表", EditorStyles.boldLabel);

        // ========== 创建区域 ==========
        EditorGUILayout.BeginVertical("Box");
        if (!_showNewTerrainForm)
        {
            if (GUILayout.Button("+ 创建新资源", GUILayout.Height(25)))
                _showNewTerrainForm = true;
        }
        else
        {
            EditorGUILayout.LabelField("新建资源", EditorStyles.boldLabel);

            var newPrefab = EditorGUILayout.ObjectField("从预制体导入 (可选)", _newTerrainPrefab, typeof(GameObject), false) as GameObject;
            if (newPrefab != _newTerrainPrefab)
            {
                _newTerrainPrefab = newPrefab;
                if (_newTerrainPrefab != null)
                {
                    string prefabName = _newTerrainPrefab.name;
                    _newTerrainId = prefabName.ToLower().Replace(" ", "_").Trim();
                    _newTerrainName = prefabName;
                    _newTerrainSprite = ExtractSpriteFromPrefab(_newTerrainPrefab);
                    if (_newTerrainSprite == null)
                        Debug.LogWarning($"[地图编辑器] 预制体 \"{prefabName}\" 没有 SpriteRenderer，请手动设置预览图标。");
                }
            }

            // 资源标签
            EditorGUILayout.LabelField("资源标签", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < PredefinedTags.Length; i++)
            {
                bool selected = !_useCustomTag && _newResourceTag == PredefinedTags[i];
                GUI.backgroundColor = selected ? Color.cyan : Color.white;
                if (GUILayout.Button(PredefinedTags[i], GUILayout.Height(22)))
                {
                    _newResourceTag = PredefinedTags[i];
                    _useCustomTag = false;
                }
                GUI.backgroundColor = Color.white;
            }
            if (GUILayout.Button(_useCustomTag ? "✓ 自定义" : "+ 自定义", GUILayout.Width(80), GUILayout.Height(22)))
            {
                _useCustomTag = !_useCustomTag;
                if (_useCustomTag) _newResourceTag = _customTagInput;
                else _newResourceTag = "地形";
            }
            EditorGUILayout.EndHorizontal();

            if (_useCustomTag)
            {
                _customTagInput = EditorGUILayout.TextField("自定义标签", _customTagInput);
                if (!string.IsNullOrWhiteSpace(_customTagInput))
                    _newResourceTag = _customTagInput.Trim();
            }

            // 瓦片形状 + 大小预设
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("瓦片形状", GUILayout.Width(70));
            _newTileShape = (TileShape)EditorGUILayout.EnumPopup(_newTileShape);
            EditorGUILayout.EndHorizontal();

            var presets = _newTileShape == TileShape.Hexagon ? HexPresets : SquarePresets;
            if (_newTileSizeIndex >= presets.Length) _newTileSizeIndex = presets.Length - 1;
            string[] sizeLabels = new string[presets.Length];
            for (int i = 0; i < presets.Length; i++) sizeLabels[i] = presets[i].label;
            _newTileSizeIndex = EditorGUILayout.Popup("瓦片大小", _newTileSizeIndex, sizeLabels);

            var selPreset = presets[_newTileSizeIndex];
            if (_newTileShape == TileShape.Hexagon)
            {
                float r = HexPresetToRadius(selPreset);
                EditorGUILayout.HelpBox($"六边形外接圆半径: {r:F2} | 精灵尺寸: {selPreset.pixelWidth}×{selPreset.pixelHeight} px", MessageType.Info);
            }
            else
            {
                float cs = SquarePresetToCellSize(selPreset);
                EditorGUILayout.HelpBox($"格子边长: {cs:F2} | 精灵尺寸: {selPreset.pixelWidth}×{selPreset.pixelHeight} px", MessageType.Info);
            }

            // 根据标签显示不同字段
            if (_newResourceTag == "单位")
            {
                _newUnitAttack = EditorGUILayout.IntField("攻击力", _newUnitAttack);
                _newUnitSpeed = EditorGUILayout.FloatField("速度", _newUnitSpeed);
                _newUnitHealth = EditorGUILayout.IntField("生命值", _newUnitHealth);
            }
            else
            {
                _newTerrainType = (DefaultTerrainType)EditorGUILayout.EnumPopup("通行规则", _newTerrainType);
            }

            _newTerrainId = EditorGUILayout.TextField("资源 ID（必填）", _newTerrainId);
            _newTerrainName = EditorGUILayout.TextField("显示名称", _newTerrainName);
            _newTerrainType = (DefaultTerrainType)EditorGUILayout.EnumPopup("通行规则", _newTerrainType);
            _newTerrainSprite = EditorGUILayout.ObjectField("预览图标（可选）", _newTerrainSprite, typeof(Sprite), false) as Sprite;

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("确认创建", GUILayout.Height(22)))
            {
                if (string.IsNullOrWhiteSpace(_newTerrainId))
                {
                    EditorUtility.DisplayDialog("创建失败", "资源 ID 不能为空", "确定");
                }
                else if (_config.ContainsId(_newTerrainId))
                {
                    EditorUtility.DisplayDialog("创建失败", $"ID \"{_newTerrainId}\" 已存在", "确定");
                }
                else
                {
                    var terrain = new TerrainResource
                    {
                        terrainId = _newTerrainId.Trim(),
                        terrainName = string.IsNullOrWhiteSpace(_newTerrainName) ? _newTerrainId.Trim() : _newTerrainName.Trim(),
                        defaultType = _newTerrainType,
                        terrainSprite = _newTerrainSprite,
                        prefab = _newTerrainPrefab,
                        resourceTag = _newResourceTag,
                        unitAttack = _newUnitAttack,
                        unitSpeed = _newUnitSpeed,
                        unitHealth = _newUnitHealth
                    };
                    _config.terrainResources.Add(terrain);
                    EditorUtility.SetDirty(_config);
                    _selectTerrainIndex = _config.terrainResources.Count - 1;

                    // 根据瓦片形状联动格子尺寸
                    var cPresets = _newTileShape == TileShape.Hexagon ? HexPresets : SquarePresets;
                    if (_newTileShape == TileShape.Hexagon && _gridShape != GridShape.Square)
                    {
                        _cellSize = HexPresetToRadius(cPresets[_newTileSizeIndex]);
                        OnDimensionChanged();
                    }
                    else if (_newTileShape == TileShape.Rectangle && _gridShape == GridShape.Square)
                    {
                        _cellSize = SquarePresetToCellSize(cPresets[_newTileSizeIndex]);
                        OnDimensionChanged();
                    }

                    // 重置创建表单
                    _newTerrainId = "";
                    _newTerrainName = "";
                    _newTerrainType = DefaultTerrainType.Passable;
                    _newTerrainSprite = null;
                    _newTerrainPrefab = null;
                    _newTileShape = TileShape.Rectangle;
                    _newTileSizeIndex = 2;
                    _newResourceTag = "地形";
                    _customTagInput = "";
                    _useCustomTag = false;
                    _newUnitAttack = 10;
                    _newUnitSpeed = 5f;
                    _newUnitHealth = 100;
                    _showNewTerrainForm = false;
                    Repaint();
                }
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("取消", GUILayout.Height(22)))
            {
                _showNewTerrainForm = false;
                _newTerrainId = "";
                _newTerrainName = "";
                _newTerrainSprite = null;
                _newTerrainPrefab = null;
                _newTileShape = TileShape.Rectangle;
                _newTileSizeIndex = 2;
                _newResourceTag = "地形";
                _customTagInput = "";
                _useCustomTag = false;
                _newUnitAttack = 10;
                _newUnitSpeed = 5f;
                _newUnitHealth = 100;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        // ========== 校验按钮 ==========
        if (GUILayout.Button("校验配置", GUILayout.Height(22)))
        {
            var errors = _config.Validate();
            if (errors.Count == 0)
                EditorUtility.DisplayDialog("校验通过", "配置无问题", "确定");
            else
                EditorUtility.DisplayDialog("校验发现 " + errors.Count + " 个问题", string.Join("\n", errors.Take(15)), "确定");
        }

        if (_config.terrainResources.Count == 0)
        {
            EditorGUILayout.HelpBox("还没有地形资源，请使用上方的创建按钮添加", MessageType.Info);
        }

        // ========== 地形列表（Foldout 折叠） ==========
        for (int i = 0; i < _config.terrainResources.Count; i++)
        {
            var terrain = _config.terrainResources[i];
            if (terrain == null) continue;

            bool isExpanded = _expandedTerrains.Contains(i);

            // Foldout 标题行
            EditorGUILayout.BeginHorizontal("Box");

            // 通行规则颜色标记
            Color typeColor = terrain.defaultType switch
            {
                DefaultTerrainType.Passable => Color.green,
                DefaultTerrainType.Impassable => Color.red,
                DefaultTerrainType.UnitImpassable => new Color(1f, 0.8f, 0.2f),
                _ => Color.gray
            };
            var oldColor = GUI.color;
            GUI.color = typeColor;
            EditorGUILayout.LabelField("■", GUILayout.Width(16));
            GUI.color = oldColor;

            // 标题文本
            string idDisplay = string.IsNullOrEmpty(terrain.terrainId) ? "???" : terrain.terrainId;
            string nameDisplay = string.IsNullOrEmpty(terrain.terrainName) ? "未命名" : terrain.terrainName;
            string tagStr = string.IsNullOrEmpty(terrain.resourceTag) ? "" : $" <{terrain.resourceTag}>";
            string headerLabel = $"[{idDisplay}] {nameDisplay}{tagStr}";
            if (terrain.terrainSprite != null)
                headerLabel += " (有图标)";
            if (terrain.resourceTag == "单位")
                headerLabel += $" ATK:{terrain.unitAttack} SPD:{terrain.unitSpeed} HP:{terrain.unitHealth}";

            bool newExpanded = EditorGUILayout.Foldout(isExpanded, headerLabel, true);
            if (newExpanded != isExpanded)
            {
                if (newExpanded) _expandedTerrains.Add(i);
                else _expandedTerrains.Remove(i);
            }

            // 删除按钮
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("刪除", GUILayout.Width(40)))
            {
                if (EditorUtility.DisplayDialog("确认删除", $"删除地形 \"{terrain.terrainName}\" (ID: {terrain.terrainId})？", "删除", "取消"))
                {
                    _config.terrainResources.RemoveAt(i);
                    _expandedTerrains.Remove(i);
                    EditorUtility.SetDirty(_config);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // 展开内容
            if (isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical("Box");

                terrain.terrainId = EditorGUILayout.TextField("唯一 ID", terrain.terrainId);
                terrain.terrainName = EditorGUILayout.TextField("显示名称", terrain.terrainName);
                terrain.terrainSprite = EditorGUILayout.ObjectField("预览图标", terrain.terrainSprite, typeof(Sprite), false) as Sprite;

                var displayedPrefab = EditorGUILayout.ObjectField("关联预制体", terrain.prefab, typeof(GameObject), false) as GameObject;
                if (displayedPrefab != terrain.prefab)
                {
                    terrain.prefab = displayedPrefab;
                    EditorUtility.SetDirty(_config);
                }

                EditorGUILayout.LabelField("标签", terrain.resourceTag);
                if (terrain.resourceTag == "单位")
                {
                    terrain.unitAttack = EditorGUILayout.IntField("攻击力", terrain.unitAttack);
                    terrain.unitSpeed = EditorGUILayout.FloatField("速度", terrain.unitSpeed);
                    terrain.unitHealth = EditorGUILayout.IntField("生命值", terrain.unitHealth);
                }

                // 精灵尺寸检测
                if (terrain.terrainSprite != null)
                {
                    Sprite sp = terrain.terrainSprite;
                    float ppu = sp.pixelsPerUnit;
                    float spW = sp.rect.width;
                    float spH = sp.rect.height;
                    float worldW = spW / ppu;
                    float worldH = spH / ppu;

                    EditorGUILayout.LabelField(
                        $"  → {spW}×{spH}px, PPU={ppu}, 世界={worldW:F2}×{worldH:F2}",
                        EditorStyles.miniLabel);

                    if (Mathf.Abs(worldW - _cellSize) > 0.001f || Mathf.Abs(worldH - _cellSize) > 0.001f)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.HelpBox(
                            $"⚠ 精灵世界尺寸({worldW:F2}) ≠ 格子尺寸({_cellSize:F2})",
                            MessageType.Warning);
                        if (GUILayout.Button("以此为准", GUILayout.Width(70), GUILayout.Height(38)))
                        {
                            _cellSize = worldW;
                            OnDimensionChanged();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

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
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }

    // ============================================================
    //  网格形状切换
    // ============================================================

    private void OnGridShapeChanged(GridShape newShape)
    {
        if (_gridShape == newShape) return;

        bool proceed = EditorUtility.DisplayDialog(
            "切换网格形状",
            "切换网格形状将清空当前地图数据。是否继续？",
            "确定", "取消");

        if (!proceed) return;

        _gridShape = newShape;
        foreach (var layer in _allLayers) layer.Clear();
        foreach (var layer in _allUnitLayers) layer.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        if (_editMode == EditMode.RectangleFill || _editMode == EditMode.HexRangeFill)
            _editMode = EditMode.Brush;
        if (_dimension == MapEditDimension.TwoD)
            _scene2D?.RebuildScene();
        Repaint();
        SceneView.RepaintAll();
    }

    private HexGridUtils.HexOrientation CurrentHexOrientation =>
        _gridShape == GridShape.HexFlatTop
            ? HexGridUtils.HexOrientation.FlatTop
            : HexGridUtils.HexOrientation.PointyTop;

    private bool IsHexMode => _gridShape != GridShape.Square;

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
        if (_dimension == MapEditDimension.TwoD)
        {
            _scene2D?.OnSceneGUI(sceneView);
            return;
        }
        DrawGridGizmos();
        DrawHoverPreview();
        HandleMouseInput();
    }

    private void DrawGridGizmos()
    {
        if (IsHexMode)
        {
            DrawHexGridGizmos();
            return;
        }

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

            Color col = GetTerrainDisplayColor(terrain);
            Handles.color = new Color(col.r, col.g, col.b, 0.4f);
            Vector3 center = new Vector3(cell.x * _cellSize + _cellSize * 0.5f, 0, cell.y * _cellSize + _cellSize * 0.5f);
            Handles.CubeHandleCap(0, center, Quaternion.identity, _cellSize * 0.9f, EventType.Repaint);
        }

        // 对象层：绘制单位标记
        if (!IsCurrentLayerTerrain)
        {
            foreach (var kv in _unitGrid)
            {
                Vector2Int cell = kv.Key;
                string uid = kv.Value;
                Handles.color = new Color(0.3f, 0.5f, 1f, 0.7f);
                Vector3 center = new Vector3(cell.x * _cellSize + _cellSize * 0.5f, 0.15f, cell.y * _cellSize + _cellSize * 0.5f);
                Handles.SphereHandleCap(0, center, Quaternion.identity, _cellSize * 0.4f, EventType.Repaint);
                Handles.Label(center + Vector3.up * 0.3f, uid);
            }
        }

        // 原点标识
        Handles.color = Color.cyan;
        Vector3 origin = new Vector3(0, 0.15f, 0);
        Handles.DrawLine(origin, origin + Vector3.right * 0.5f);
        Handles.DrawLine(origin, origin + Vector3.forward * 0.5f);
        Handles.Label(origin + new Vector3(0.2f, 0, 0.2f), "原点 (0,0)");
    }

    // ============================================================
    //  六边形 3D 网格渲染
    // ============================================================

    private void DrawHexGridGizmos()
    {
        float camDist = 50f;
        if (SceneView.lastActiveSceneView?.camera != null)
            camDist = Vector3.Distance(SceneView.lastActiveSceneView.camera.transform.position, Vector3.zero);

        int step = 1;
        if (camDist > 100f) step = 4;
        else if (camDist > 50f) step = 2;

        var orient = CurrentHexOrientation;
        float radius = _cellSize;

        // 地图边界（矩形）
        var bounds = HexGridUtils.GetMapBoundsWorld3D(_mapWidth, _mapHeight, radius, orient);
        Handles.color = new Color(1f, 1f, 1f, 0.4f);
        Vector3 b0 = new Vector3(bounds.min.x, 0.05f, bounds.min.z);
        Vector3 b1 = new Vector3(bounds.max.x, 0.05f, bounds.min.z);
        Vector3 b2 = new Vector3(bounds.max.x, 0.05f, bounds.max.z);
        Vector3 b3 = new Vector3(bounds.min.x, 0.05f, bounds.max.z);
        Handles.DrawAAPolyLine(2f, new[] { b0, b1, b2, b3, b0 });

        // 网格线 (LOD)
        Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        for (int row = 0; row < _mapHeight; row += step)
        {
            for (int col = 0; col < _mapWidth; col += step)
            {
                Vector3 center = HexGridUtils.OffsetToWorld3D(col, row, radius, orient);
                Vector3[] corners = HexGridUtils.GetHexOutlineCorners3D(
                    center, HexGridUtils.GetUnityCellSize(radius, orient), orient);
                Vector3[] closed = new Vector3[7];
                System.Array.Copy(corners, closed, 6);
                closed[6] = corners[0];
                Handles.DrawAAPolyLine(1.5f, closed);
            }
        }

        // 地形色块
        foreach (var kv in _mapGrid)
        {
            Vector2Int cell = kv.Key;
            string tid = kv.Value;
            var terrain = _config.GetTerrainById(tid);
            Color col = GetTerrainDisplayColor(terrain);
            Handles.color = new Color(col.r, col.g, col.b, 0.4f);
            Vector3 center = HexGridUtils.OffsetToWorld3D(cell, radius, orient);
            Handles.DrawSolidDisc(center, Vector3.up, radius * 0.85f);
        }

        // 对象层单位标记
        if (!IsCurrentLayerTerrain)
        {
            foreach (var kv in _unitGrid)
            {
                Vector2Int cell = kv.Key;
                string uid = kv.Value;
                Vector3 pos = HexGridUtils.OffsetToWorld3D(cell, radius, orient);
                pos.y = 0.5f;
                Handles.color = new Color(0.3f, 0.5f, 1f, 0.6f);
                Vector3[] corners = HexGridUtils.GetHexCorners3D(pos, radius * 0.6f, orient);
                Vector3[] closed = new Vector3[7];
                System.Array.Copy(corners, closed, 6);
                closed[6] = corners[0];
                Handles.DrawAAConvexPolygon(closed);
                Handles.Label(pos + Vector3.up * 0.5f, uid);
            }
        }

        // 原点特殊高亮
        Handles.color = new Color(1f, 1f, 0f, 0.5f);
        Vector3 originCenter = HexGridUtils.OffsetToWorld3D(0, 0, radius, orient);
        Vector3[] oCorners = HexGridUtils.GetHexOutlineCorners3D(
            originCenter, HexGridUtils.GetUnityCellSize(radius, orient), orient);
        Vector3[] oClosed = new Vector3[7];
        System.Array.Copy(oCorners, oClosed, 6);
        oClosed[6] = oCorners[0];
        Handles.DrawAAPolyLine(3f, oClosed);
        Handles.Label(originCenter + Vector3.up * 0.3f, "原点 (0,0)",
            new GUIStyle() { normal = { textColor = Color.yellow } });
    }

    private static Color GetTerrainDisplayColor(TerrainResource terrain)
    {
        if (terrain == null) return Color.gray;
        return terrain.defaultType switch
        {
            DefaultTerrainType.Passable => Color.green,
            DefaultTerrainType.Impassable => Color.red,
            DefaultTerrainType.UnitImpassable => Color.yellow,
            _ => Color.gray
        };
    }

    private static Sprite ExtractSpriteFromPrefab(GameObject prefab)
    {
        if (prefab == null) return null;
        var sr = prefab.GetComponent<SpriteRenderer>();
        return sr != null ? sr.sprite : null;
    }

    // ============================================================
    //  悬停预览
    // ============================================================

    private void DrawHoverPreview()
    {
        if (IsHexMode)
        {
            DrawHexHoverPreview();
            return;
        }

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
        else if (!IsCurrentLayerTerrain)
        {
            previewColor = new Color(0.3f, 0.5f, 1f);
            alpha = 0.35f;
        }
        else
        {
            if (_selectTerrainIndex < _config.terrainResources.Count)
            {
                var t = _selectedTerrain;
                previewColor = GetTerrainDisplayColor(t);
            }
            else previewColor = Color.gray;
            alpha = 0.3f;
        }

        Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, alpha);
        if (IsCurrentLayerTerrain)
        {
            Vector3 center = new Vector3(cx * _cellSize + _cellSize * 0.5f, 0.05f, cy * _cellSize + _cellSize * 0.5f);
            Handles.CubeHandleCap(0, center, Quaternion.identity, _cellSize * 0.95f, EventType.Repaint);
        }
        else
        {
            Vector3 center = new Vector3(cx * _cellSize + _cellSize * 0.5f, 0.1f, cy * _cellSize + _cellSize * 0.5f);
            Handles.SphereHandleCap(0, center, Quaternion.identity, _cellSize * 0.4f, EventType.Repaint);
        }

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

        DrawSquareTooltip(e, activeMode, _hoveredCell);
    }

    // ============================================================
    //  方形悬停 Tooltip
    // ============================================================

    private void DrawSquareTooltip(Event e, EditMode activeMode, Vector2Int cell)
    {
        Handles.BeginGUI();
        string tooltip;
        if (activeMode == EditMode.Eraser)
        {
            bool hasData = IsCurrentLayerTerrain
                ? _mapGrid.ContainsKey(cell)
                : _unitGrid.ContainsKey(cell);
            tooltip = hasData ? $"擦除 [{cell.x},{cell.y}]" : $"(空) [{cell.x},{cell.y}]";
        }
        else if (!IsCurrentLayerTerrain)
        {
            var units = _config.terrainResources.Where(t => t != null && t.resourceTag == "单位").ToList();
            var u = _selectUnitIndex < units.Count ? units[_selectUnitIndex] : null;
            tooltip = u != null ? $"{u.terrainName} ({u.terrainId}) @ [{cell.x},{cell.y}]" : $"无 @ [{cell.x},{cell.y}]";
        }
        else
        {
            var t = _selectedTerrain;
            tooltip = t != null ? $"{t.terrainName} ({t.terrainId}) @ [{cell.x},{cell.y}]" : $"无 @ [{cell.x},{cell.y}]";
        }
        var style = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
        var content = new GUIContent(tooltip);
        Vector2 size = style.CalcSize(content);
        GUI.Box(new Rect(e.mousePosition.x + 20, e.mousePosition.y - 10, size.x + 10, 22), content, style);
        Handles.EndGUI();
    }

    // ============================================================
    //  六边形悬停预览
    // ============================================================

    private void DrawHexHoverPreview()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        Vector2Int cell = HexGridUtils.WorldToOffset3D(worldPos, _cellSize, CurrentHexOrientation);
        if (!HexGridUtils.IsInOffsetRect(cell.x, cell.y, _mapWidth, _mapHeight)) return;

        _hoveredCell = cell;

        EditMode activeMode = e.shift ? EditMode.Eraser : _editMode;
        Color previewColor;
        float alpha;

        if (activeMode == EditMode.Eraser)
        {
            previewColor = Color.red;
            alpha = 0.35f;
        }
        else if (!IsCurrentLayerTerrain)
        {
            previewColor = new Color(0.3f, 0.5f, 1f);
            alpha = 0.35f;
        }
        else
        {
            var terrain = _selectedTerrain;
            previewColor = GetTerrainDisplayColor(terrain);
            alpha = 0.3f;
        }

        Vector3 center = HexGridUtils.OffsetToWorld3D(cell, _cellSize, CurrentHexOrientation);

        // 范围填充预览
        if (activeMode == EditMode.HexRangeFill)
        {
            var cells = HexGridUtils.GetOffsetHexagonsInRange(
                cell, _hexFillRange, _mapWidth, _mapHeight, CurrentHexOrientation);
            Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.15f);
            foreach (var c in cells)
            {
                Vector3 cc = HexGridUtils.OffsetToWorld3D(c, _cellSize, CurrentHexOrientation);
                Handles.DrawSolidDisc(cc, Vector3.up, _cellSize * 0.82f);
            }
        }

        // 六边形填充
        Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, alpha);
        Handles.DrawSolidDisc(center, Vector3.up, _cellSize * 0.82f);

        // 白色边框
        Vector3[] corners = HexGridUtils.GetHexOutlineCorners3D(
            center, HexGridUtils.GetUnityCellSize(_cellSize, CurrentHexOrientation), CurrentHexOrientation);
        Vector3[] closed = new Vector3[7];
        System.Array.Copy(corners, closed, 6);
        closed[6] = corners[0];
        Handles.color = new Color(1f, 1f, 1f, 0.6f);
        Handles.DrawAAPolyLine(3f, closed);

        if (_hoveredCell != _previousHoveredCell)
        {
            _previousHoveredCell = _hoveredCell;
            SceneView.RepaintAll();
        }

        DrawHexTooltip(e, cell, activeMode);
    }

    private void DrawHexTooltip(Event e, Vector2Int cell, EditMode activeMode)
    {
        Handles.BeginGUI();
        string tooltip;
        if (activeMode == EditMode.Eraser)
        {
            tooltip = _mapGrid.ContainsKey(cell)
                ? $"擦除 [{cell.x},{cell.y}]"
                : $"(空) [{cell.x},{cell.y}]";
        }
        else
        {
            var terrain = _selectedTerrain;
            tooltip = terrain != null
                ? $"{terrain.terrainName} ({terrain.terrainId}) @ [{cell.x},{cell.y}]"
                : $"无选中地形 @ [{cell.x},{cell.y}]";
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
        if (IsHexMode)
        {
            HandleHexMouseInput();
            return;
        }

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
                        DoFloodFill(cellPos, _selectedTerrain.terrainId);
                        EndUndoOp();
                        Repaint();
                    }
                    e.Use();
                }
                break;
        }
    }

    // ============================================================
    //  六边形鼠标输入
    // ============================================================

    private void HandleHexMouseInput()
    {
        Event e = Event.current;
        if (e == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 worldPos = ray.GetPoint(dist);
        Vector2Int cell = HexGridUtils.WorldToOffset3D(worldPos, _cellSize, CurrentHexOrientation);
        if (!HexGridUtils.IsInOffsetRect(cell.x, cell.y, _mapWidth, _mapHeight)) return;

        EditMode activeMode = e.shift ? EditMode.Eraser : _editMode;

        switch (activeMode)
        {
            case EditMode.Brush:
            case EditMode.Eraser:
                HandleHexBrushOrErase(e, cell, activeMode);
                break;
            case EditMode.RectangleFill:
                HandleHexRhombusFill(e, cell);
                break;
            case EditMode.FloodFill:
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_selectTerrainIndex < _config.terrainResources.Count)
                    {
                        BeginUndoOp();
                        HexFloodFill(cell, _selectedTerrain.terrainId);
                        EndUndoOp();
                        Repaint();
                    }
                    e.Use();
                }
                break;
            case EditMode.HexRangeFill:
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_selectTerrainIndex < _config.terrainResources.Count)
                    {
                        BeginUndoOp();
                        HexRangeFill(cell, _hexFillRange, _selectedTerrain.terrainId);
                        EndUndoOp();
                        Repaint();
                    }
                    e.Use();
                }
                break;
        }
    }

    private void HandleHexBrushOrErase(Event e, Vector2Int hex, EditMode mode)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _isPainting = true;
            _lastPaintedCell = hex;
            BeginUndoOp();
            PaintCell(hex, mode);
            EndUndoOp();
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _isPainting)
        {
            if (hex != _lastPaintedCell)
            {
                _lastPaintedCell = hex;
                BeginUndoOp();
                PaintCell(hex, mode);
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

    private void HandleHexRhombusFill(Event e, Vector2Int hex)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _rectStart = hex;
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
            if (_selectTerrainIndex < _config.terrainResources.Count)
            {
                BeginUndoOp();
                HexFillRhombus(_rectStart, hex, _selectedTerrain.terrainId);
                EndUndoOp();
                Repaint();
            }
            e.Use();
        }
    }

    // ============================================================
    //  六边形填充方法
    // ============================================================

    private void HexFillRhombus(Vector2Int a, Vector2Int b, string terrainId)
    {
        int q0 = Mathf.Clamp(Mathf.Min(a.x, b.x), 0, _mapWidth - 1);
        int q1 = Mathf.Clamp(Mathf.Max(a.x, b.x), 0, _mapWidth - 1);
        int r0 = Mathf.Clamp(Mathf.Min(a.y, b.y), 0, _mapHeight - 1);
        int r1 = Mathf.Clamp(Mathf.Max(a.y, b.y), 0, _mapHeight - 1);

        for (int q = q0; q <= q1; q++)
        {
            for (int r = r0; r <= r1; r++)
            {
                var pos = new Vector2Int(q, r);
                RecordChange(pos);
                _mapGrid[pos] = terrainId;
            }
        }
    }

    private void HexFloodFill(Vector2Int start, string fillTerrainId)
    {
        if (!_mapGrid.TryGetValue(start, out string targetId)) return;
        if (targetId == fillTerrainId) return;

        const int MAX_FILL = 100000;
        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            if (visited.Count > MAX_FILL)
            {
                Debug.LogWarning($"六边形洪水填充已超过 {MAX_FILL} 格，中断操作");
                break;
            }

            Vector2Int current = queue.Dequeue();
            if (_mapGrid.TryGetValue(current, out string id) && id == targetId)
            {
                RecordChange(current);
                _mapGrid[current] = fillTerrainId;

                var neighbors = HexGridUtils.GetOffsetNeighborsInBounds(
                    current, _mapWidth, _mapHeight, CurrentHexOrientation);
                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }
        }
    }

    internal void HexRangeFill(Vector2Int center, int range, string terrainId)
    {
        var cells = HexGridUtils.GetOffsetHexagonsInRange(
            center, range, _mapWidth, _mapHeight, CurrentHexOrientation);
        foreach (var c in cells)
        {
            RecordChange(c);
            _mapGrid[c] = terrainId;
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
                FillRect(_rectStart, cellPos, _selectedTerrain.terrainId);
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
        if (IsCurrentLayerTerrain)
            PaintTerrainCell(cellPos, mode);
        else
            PaintUnitCell(cellPos, mode);
    }

    private void PaintTerrainCell(Vector2Int cellPos, EditMode mode)
    {
        if (mode == EditMode.Brush)
        {
            var terrainList = _config.terrainResources
                .Where(t => t != null && t.resourceTag != "单位").ToList();
            if (_selectTerrainIndex < terrainList.Count)
            {
                string tid = terrainList[_selectTerrainIndex].terrainId;
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

    private void PaintUnitCell(Vector2Int cellPos, EditMode mode)
    {
        if (mode == EditMode.Brush)
        {
            var unitResources = _config.terrainResources
                .Where(t => t != null && t.resourceTag == "单位").ToList();
            if (_selectUnitIndex < unitResources.Count)
            {
                string uid = unitResources[_selectUnitIndex].terrainId;
                RecordChange(cellPos);
                _unitGrid[cellPos] = uid;
            }
        }
        else if (mode == EditMode.Eraser)
        {
            if (_unitGrid.ContainsKey(cellPos))
            {
                RecordChange(cellPos);
                _unitGrid.Remove(cellPos);
            }
        }
    }

    internal void FillRect(Vector2Int a, Vector2Int b, string terrainId)
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

    internal void DoFloodFill(Vector2Int start, string fillTerrainId)
    {
        if (IsHexMode)
        {
            HexFloodFill(start, fillTerrainId);
            return;
        }

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

    internal void BeginUndoOp()
    {
        _currentOp = new UndoOperation
        {
            layer = _currentLayer,
            isTerrainLayer = IsCurrentLayerTerrain
        };
    }

    internal void RecordChange(Vector2Int cellPos)
    {
        if (_currentOp == null) return;
        if (_currentOp.changedCells.ContainsKey(cellPos) || _currentOp.removedCells.Contains(cellPos))
            return;

        var grid = _currentOp.isTerrainLayer ? _mapGrid : _unitGrid;
        if (grid.TryGetValue(cellPos, out string oldVal))
            _currentOp.changedCells[cellPos] = oldVal;
        else
            _currentOp.removedCells.Add(cellPos);
    }

    internal void EndUndoOp()
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

        // 切换到操作所属层
        int prevLayer = _currentLayer;
        _currentLayer = op.layer;

        var grid = op.isTerrainLayer ? _mapGrid : _unitGrid;

        // 保存当前值到 redo
        var redo = new UndoOperation { layer = op.layer, isTerrainLayer = op.isTerrainLayer };
        foreach (var kv in op.changedCells)
        {
            if (grid.TryGetValue(kv.Key, out string v))
                redo.changedCells[kv.Key] = v;
            else
                redo.removedCells.Add(kv.Key);
        }
        foreach (var pos in op.removedCells)
        {
            if (grid.TryGetValue(pos, out string v))
                redo.changedCells[pos] = v;
        }
        _redoStack.Push(redo);

        // 还原
        foreach (var kv in op.changedCells)
            grid[kv.Key] = kv.Value;
        foreach (var pos in op.removedCells)
            grid.Remove(pos);

        _currentLayer = prevLayer;
        Repaint();
        OnCellDataChanged();
        Debug.Log($"[Undo] 撤销完成，剩余: Undo {_undoStack.Count}, Redo {_redoStack.Count}");
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        var op = _redoStack.Pop();

        int prevLayer = _currentLayer;
        _currentLayer = op.layer;

        var grid = op.isTerrainLayer ? _mapGrid : _unitGrid;

        // 保存当前值到 undo
        var undoOp = new UndoOperation { layer = op.layer, isTerrainLayer = op.isTerrainLayer };
        foreach (var kv in op.changedCells)
        {
            if (grid.TryGetValue(kv.Key, out string v))
                undoOp.changedCells[kv.Key] = v;
            else
                undoOp.removedCells.Add(kv.Key);
        }
        foreach (var pos in op.removedCells)
        {
            if (grid.TryGetValue(pos, out string v))
                undoOp.changedCells[pos] = v;
        }
        _undoStack.Push(undoOp);

        // 重做
        foreach (var kv in op.changedCells)
            grid[kv.Key] = kv.Value;
        foreach (var pos in op.removedCells)
            grid.Remove(pos);

        _currentLayer = prevLayer;
        Repaint();
        OnCellDataChanged();
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
            layerCount = _allLayers.Count,
            cellSize = _cellSize,
            gridShape = _gridShape == GridShape.Square ? "Square" : "Hex",
            hexOrientation = _gridShape == GridShape.HexPointyTop ? "PointyTop" : "FlatTop"
        };
        // 遍历所有层：地形 → cellDatas, 单位 → unitDatas
        for (int layer = 0; layer < _allLayers.Count; layer++)
        {
            foreach (var kv in _allLayers[layer])
                data.cellDatas.Add(new MapCellData { x = kv.Key.x, y = kv.Key.y, layer = layer, terrainId = kv.Value });
        }
        for (int layer = 0; layer < _allUnitLayers.Count; layer++)
        {
            foreach (var kv in _allUnitLayers[layer])
                data.unitDatas.Add(new MapUnitData { x = kv.Key.x, y = kv.Key.y, layer = layer, unitId = kv.Value });
        }

        string path = EditorUtility.SaveFilePanel("保存地图 JSON", "Assets", _mapName, "json");
        if (string.IsNullOrEmpty(path)) return;

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();
        int totalUnits = 0;
        foreach (var ul in _allUnitLayers) totalUnits += ul.Count;
        EditorUtility.DisplayDialog("保存成功", $"地图已保存到:\n{path}\n{_allLayers.Count} 层, {data.cellDatas.Count} 地形格, {totalUnits} 单位", "确定");
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

        // 网格形状匹配检查
        string currentShape = _gridShape == GridShape.Square ? "Square" : "Hex";
        string loadedShape = string.IsNullOrEmpty(data.gridShape) ? "Square" : data.gridShape;
        if (currentShape != loadedShape)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "网格形状不匹配",
                $"当前编辑器为 [{currentShape}] 网格，JSON 文件为 [{loadedShape}] 网格。\n" +
                "加载后编辑器将自动切换到 JSON 的网格形状。是否继续？",
                "确定", "取消");
            if (!proceed) return;

            _gridShape = loadedShape == "Hex"
                ? (data.hexOrientation == "PointyTop" ? GridShape.HexPointyTop : GridShape.HexFlatTop)
                : GridShape.Square;
        }

        _mapName = data.mapName;
        _mapWidth = Mathf.Max(1, data.mapWidth);
        _mapHeight = Mathf.Max(1, data.mapHeight);
        if (data.cellSize > 0) _cellSize = data.cellSize;
        _undoStack.Clear();
        _redoStack.Clear();

        // 按层分布数据
        int maxLayer = Mathf.Max(1, data.layerCount);
        _allLayers.Clear();
        _allUnitLayers.Clear();
        _layerTypes.Clear();
        for (int i = 0; i < maxLayer; i++)
        {
            _allLayers.Add(new Dictionary<Vector2Int, string>());
            _allUnitLayers.Add(new Dictionary<Vector2Int, string>());
            _layerTypes.Add(LayerType.Terrain); // 默认地形层
        }
        _currentLayer = 0;

        foreach (var cell in data.cellDatas)
        {
            if (cell.x < 0 || cell.x >= _mapWidth || cell.y < 0 || cell.y >= _mapHeight)
                continue;
            int layer = Mathf.Clamp(cell.layer, 0, _allLayers.Count - 1);
            _allLayers[layer][new Vector2Int(cell.x, cell.y)] = cell.terrainId;
        }

        // 加载单位数据，并标记对应层为对象层
        if (data.unitDatas != null)
        {
            foreach (var unit in data.unitDatas)
            {
                if (unit.x < 0 || unit.x >= _mapWidth || unit.y < 0 || unit.y >= _mapHeight)
                    continue;
                int layer = Mathf.Clamp(unit.layer, 0, _allUnitLayers.Count - 1);
                _allUnitLayers[layer][new Vector2Int(unit.x, unit.y)] = unit.unitId;
                // 如果该层有单位数据，标记为对象层（但若该层也有地形数据，不覆盖）
                if (_allLayers[layer].Count == 0 && _layerTypes[layer] == LayerType.Terrain)
                    _layerTypes[layer] = LayerType.Object;
            }
        }

        Repaint();
        OnCellDataChanged();
        int totalUnits = 0;
        foreach (var ul in _allUnitLayers) totalUnits += ul.Count;
        EditorUtility.DisplayDialog("加载成功", $"已加载: {data.mapName}\n{_mapWidth}×{_mapHeight}\n{_allLayers.Count} 层, {data.cellDatas.Count} 地形格, {totalUnits} 单位", "确定");
    }

    // ============================================================
    //  2D/3D 模式切换
    // ============================================================

    private MapEditDimension _dimension = MapEditDimension.ThreeD;
    private MapEditor2DSceneManager _scene2D;

    private void OnDimensionChanged()
    {
        if (_dimension == MapEditDimension.TwoD)
        {
            if (_scene2D == null) _scene2D = new MapEditor2DSceneManager(this);
            _scene2D.Setup();
        }
        else
        {
            _scene2D?.Cleanup();
            // 恢复 3D 透视相机
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = false;
                SceneView.lastActiveSceneView.orthographic = false;
                SceneView.lastActiveSceneView.Repaint();
            }
        }
        SceneView.RepaintAll();
    }

    internal void OnCellDataChanged()
    {
        if (_dimension == MapEditDimension.TwoD)
            _scene2D?.SyncAllTiles();
    }

    // ============================================================
    //  供 MapEditor2DSceneManager 调用的 internal 访问器
    // ============================================================

    internal MapResourceConfig Config => _config;
    internal int MapWidth => _mapWidth;
    internal int MapHeight => _mapHeight;
    internal float CellSize => _cellSize;
    internal int CurrentLayer => _currentLayer;
    internal GridShape CurrentGridShape => _gridShape;
    internal bool IsHexModeInternal => IsHexMode;
    internal HexGridUtils.HexOrientation CurrentHexOrientationInternal => CurrentHexOrientation;
    internal List<Dictionary<Vector2Int, string>> AllLayers => _allLayers;
    internal EditMode CurrentEditMode => _editMode;
    internal string SelectedTerrainId => _selectedTerrain != null ? _selectedTerrain.terrainId : "";

    private TerrainResource _selectedTerrain
    {
        get
        {
            var list = _config?.terrainResources?.Where(t => t != null && t.resourceTag != "单位").ToList();
            if (list == null || list.Count == 0) return null;
            if (_selectTerrainIndex < 0 || _selectTerrainIndex >= list.Count) return null;
            return list[_selectTerrainIndex];
        }
    }

    internal string GetCellTerrain(int x, int y, int layer)
    {
        while (_allLayers.Count <= layer) _allLayers.Add(new Dictionary<Vector2Int, string>());
        return _allLayers[layer].TryGetValue(new Vector2Int(x, y), out string id) ? id : null;
    }

    internal void SetCellTerrain(int x, int y, int layer, string terrainId)
    {
        while (_allLayers.Count <= layer) _allLayers.Add(new Dictionary<Vector2Int, string>());
        _allLayers[layer][new Vector2Int(x, y)] = terrainId;
    }

    internal void RemoveCellTerrain(int x, int y, int layer)
    {
        while (_allLayers.Count <= layer) _allLayers.Add(new Dictionary<Vector2Int, string>());
        _allLayers[layer].Remove(new Vector2Int(x, y));
    }

    internal bool IsCurrentLayerTerrainInternal => IsCurrentLayerTerrain;
    internal Dictionary<Vector2Int, string> CurrentUnitGrid => _unitGrid;
    internal int HexFillRange => _hexFillRange;
    internal string GetUnitNameById(string unitId)
    {
        var res = _config.terrainResources.Find(t => t != null && t.terrainId == unitId);
        if (res != null) return res.terrainName;
        foreach (var uc in _unitConfigs)
            if (uc != null && uc.unitId == unitId) return uc.unitName;
        return unitId;
    }

    internal void RepaintWindow() => Repaint();
}

public enum MapEditDimension { ThreeD, TwoD }
