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
    internal enum EditMode { Brush, Eraser, RectangleFill, FloodFill }
    internal enum LayerType { Terrain, Object }
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

    // ===== 地形列表折叠状态 =====
    private HashSet<int> _expandedTerrains = new HashSet<int>();

    // ===== 新建地形临时输入 =====
    private bool _showNewTerrainForm;
    private string _newTerrainId = "";
    private string _newTerrainName = "";
    private DefaultTerrainType _newTerrainType = DefaultTerrainType.Passable;
    private Sprite _newTerrainSprite;

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
        var data = new MapJsonData { mapName = _mapName, mapWidth = _mapWidth, mapHeight = _mapHeight, layerCount = _allLayers.Count };
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
        GUILayout.Space(10);
        EditorGUILayout.LabelField("==== 地形地图编辑器 ====", EditorStyles.boldLabel);

        _config = EditorGUILayout.ObjectField("地形资源配置表", _config, typeof(MapResourceConfig), false) as MapResourceConfig;
        if (_config == null)
        {
            EditorGUILayout.HelpBox("请先创建地形资源配置表：右键 Project → Create → 地图编辑器 → 地形资源配置表", MessageType.Warning);
            return;
        }

        // 单位配置管理
        EditorGUILayout.LabelField("单位配置管理", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("每个 UnitConfig 资产代表一种敌人/怪物/NPC。创建多个即可配置多种单位。", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 创建新单位配置", GUILayout.Height(22)))
        {
            string path = EditorUtility.SaveFilePanelInProject("创建单位配置", "UnitConfig", "asset", "选择保存位置");
            if (!string.IsNullOrEmpty(path))
            {
                var newConfig = CreateInstance<UnitConfig>();
                newConfig.unitId = System.IO.Path.GetFileNameWithoutExtension(path);
                newConfig.unitName = newConfig.unitId;
                AssetDatabase.CreateAsset(newConfig, path);
                AssetDatabase.SaveAssets();
                _unitConfigs.Add(newConfig);
                _selectUnitIndex = _unitConfigs.Count - 1;
            }
        }
        if (GUILayout.Button("扫描已存在的配置", GUILayout.Height(22)))
        {
            _unitConfigs.Clear();
            _unitConfigs.AddRange(
                AssetDatabase.FindAssets("t:UnitConfig")
                    .Select(guid => AssetDatabase.LoadAssetAtPath<UnitConfig>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(c => c != null));
        }
        EditorGUILayout.EndHorizontal();

        // 拖入区
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("拖入配置", GUILayout.Width(80));
        var newCfg = EditorGUILayout.ObjectField(null, typeof(UnitConfig), false) as UnitConfig;
        if (newCfg != null && !_unitConfigs.Contains(newCfg))
            _unitConfigs.Add(newCfg);
        EditorGUILayout.EndHorizontal();

        if (_unitConfigs.Count > 0)
        {
            EditorGUILayout.LabelField($"  已加载 {_unitConfigs.Count} 种单位：", EditorStyles.miniLabel);
            // 横向排列显示所有单位
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _unitConfigs.Count; i++)
            {
                var uc = _unitConfigs[i];
                if (uc == null) continue;
                string btnLabel = $"{uc.unitName}\n({uc.unitId})";
                if (i == _selectUnitIndex) GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button(btnLabel, GUILayout.Width(80), GUILayout.Height(36)))
                    _selectUnitIndex = i;
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox("尚未加载任何单位配置。请点击\"创建新单位配置\"或\"扫描\"加载。", MessageType.Warning);
        }

        DrawMapSetting();
        DrawDimensionToggle();

        // 单位配置列表（对象层时需要）
        if (!IsCurrentLayerTerrain && _unitConfigs.Count == 0)
        {
            EditorGUILayout.HelpBox("请拖入 UnitConfig 资产以选择要放置的单位", MessageType.Warning);
        }

        DrawEditMode();
        if (IsCurrentLayerTerrain)
            DrawTerrainSelect();
        else
            DrawUnitSelect();
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

        // 常用尺寸预设
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("快速预设", GUILayout.Width(60));
        float[] presets = { 0.16f, 0.32f, 0.64f, 1f, 1.28f };
        string[] presetLabels = { "16", "32", "64", "100", "128" };
        for (int i = 0; i < presets.Length; i++)
        {
            if (GUILayout.Button(presetLabels[i], GUILayout.Width(38), GUILayout.Height(18)))
            {
                _cellSize = presets[i];
                OnDimensionChanged();
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
        EditorGUILayout.HelpBox($"当前地图: {_mapWidth}×{_mapHeight}, 总格子 {totalCells} | 层 {_currentLayer} [{layerTypeLabel}]: {currentCount} 格", MessageType.Info);

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
        if (_config == null || _config.terrainResources.Count == 0) return;
        if (_selectTerrainIndex >= _config.terrainResources.Count) return;

        var terrain = _config.terrainResources[_selectTerrainIndex];
        if (terrain?.terrainSprite == null) return;

        Sprite sprite = terrain.terrainSprite;
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

    private static readonly string[] EditModeNames = { "笔刷", "橡皮", "矩形填充", "洪水填充" };

    private void DrawEditMode()
    {
        EditorGUILayout.LabelField("编辑模式", EditorStyles.boldLabel);
        int modeIndex = (int)_editMode;

        // 对象层只支持笔刷和橡皮
        if (!IsCurrentLayerTerrain)
        {
            string[] objectModeNames = { "笔刷（放置单位）", "橡皮（移除单位）" };
            // 映射: 0=Brush->0, 1=Eraser->1, 2=RectangleFill->0, 3=FloodFill->0
            if (modeIndex > 1) modeIndex = 0;
            modeIndex = EditorGUILayout.Popup("当前模式", modeIndex, objectModeNames);
        }
        else
        {
            modeIndex = EditorGUILayout.Popup("当前模式", modeIndex, EditModeNames);
        }
        _editMode = (EditMode)Mathf.Clamp(modeIndex, 0, EditModeNames.Length - 1);
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

        // 过滤掉 null 的地形项
        var validList = new List<int>();
        string[] names = new string[_config.terrainResources.Count];
        for (int i = 0; i < _config.terrainResources.Count; i++)
        {
            var t = _config.terrainResources[i];
            if (t == null)
            {
                names[i] = $"(null 下标{i})";
            }
            else
            {
                string idStr = string.IsNullOrEmpty(t.terrainId) ? "???" : t.terrainId;
                string nameStr = string.IsNullOrEmpty(t.terrainName) ? "未命名" : t.terrainName;
                names[i] = $"[{idStr}] {nameStr}";
                validList.Add(i);
            }
        }

        // 确保选中索引指向有效的 terrain
        if (validList.Count > 0 && !validList.Contains(_selectTerrainIndex))
            _selectTerrainIndex = validList[0];
        _selectTerrainIndex = Mathf.Clamp(_selectTerrainIndex, 0, _config.terrainResources.Count - 1);

        EditorGUILayout.LabelField("选择要绘制的地形", EditorStyles.boldLabel);
        _selectTerrainIndex = EditorGUILayout.Popup("选中地形", _selectTerrainIndex, names);

        var sel = _config.terrainResources[_selectTerrainIndex];
        if (sel != null)
        {
            string passLabel = sel.defaultType switch
            {
                DefaultTerrainType.Passable => "可通行",
                DefaultTerrainType.Impassable => "不可通行",
                DefaultTerrainType.UnitImpassable => "单位不可通行",
                _ => "未知"
            };
            EditorGUILayout.LabelField("当前选中", $"ID: {sel.terrainId} | {sel.terrainName} | 通行: {passLabel}");
        }
    }

    // ============================================================
    //  单位选择（对象层）
    // ============================================================

    private void DrawUnitSelect()
    {
        if (_unitConfigs.Count == 0) return;

        EditorGUILayout.LabelField("选择要放置的单位", EditorStyles.boldLabel);
        string[] names = new string[_unitConfigs.Count];
        for (int i = 0; i < _unitConfigs.Count; i++)
        {
            var u = _unitConfigs[i];
            names[i] = u != null ? $"[{u.unitId}] {u.unitName}" : $"<null 下标{i}>";
        }
        _selectUnitIndex = Mathf.Clamp(_selectUnitIndex, 0, _unitConfigs.Count - 1);
        _selectUnitIndex = EditorGUILayout.Popup("选中单位", _selectUnitIndex, names);

        var sel = _unitConfigs[_selectUnitIndex];
        if (sel != null)
        {
            EditorGUILayout.LabelField("当前选中", $"ID: {sel.unitId} | {sel.unitName}");
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
        EditorGUILayout.LabelField("地形资源配置列表", EditorStyles.boldLabel);

        // ========== 创建区域 ==========
        EditorGUILayout.BeginVertical("Box");
        if (!_showNewTerrainForm)
        {
            if (GUILayout.Button("+ 创建新地形", GUILayout.Height(25)))
                _showNewTerrainForm = true;
        }
        else
        {
            EditorGUILayout.LabelField("新建地形", EditorStyles.boldLabel);
            _newTerrainId = EditorGUILayout.TextField("地形 ID（必填）", _newTerrainId);
            _newTerrainName = EditorGUILayout.TextField("显示名称", _newTerrainName);
            _newTerrainType = (DefaultTerrainType)EditorGUILayout.EnumPopup("通行规则", _newTerrainType);
            _newTerrainSprite = EditorGUILayout.ObjectField("预览图标（可选）", _newTerrainSprite, typeof(Sprite), false) as Sprite;

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("确认创建", GUILayout.Height(22)))
            {
                if (string.IsNullOrWhiteSpace(_newTerrainId))
                {
                    EditorUtility.DisplayDialog("创建失败", "地形 ID 不能为空", "确定");
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
                        terrainSprite = _newTerrainSprite
                    };
                    _config.terrainResources.Add(terrain);
                    EditorUtility.SetDirty(_config);
                    _selectTerrainIndex = _config.terrainResources.Count - 1;
                    // 重置创建表单
                    _newTerrainId = "";
                    _newTerrainName = "";
                    _newTerrainType = DefaultTerrainType.Passable;
                    _newTerrainSprite = null;
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
            string headerLabel = $"[{idDisplay}] {nameDisplay}";
            if (terrain.terrainSprite != null)
                headerLabel += " (有图标)";

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
        else if (!IsCurrentLayerTerrain)
        {
            previewColor = new Color(0.3f, 0.5f, 1f);
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

        // Tooltip
        Handles.BeginGUI();
        string tooltip;
        if (activeMode == EditMode.Eraser)
        {
            bool hasData = IsCurrentLayerTerrain
                ? _mapGrid.ContainsKey(_hoveredCell)
                : _unitGrid.ContainsKey(_hoveredCell);
            tooltip = hasData ? $"🗑 擦除 [{cx},{cy}]" : $"（空）[{cx},{cy}]";
        }
        else if (!IsCurrentLayerTerrain)
        {
            var u = _unitConfigs.Count > _selectUnitIndex ? _unitConfigs[_selectUnitIndex] : null;
            tooltip = u != null ? $"👤 {u.unitName} ({u.unitId}) @ [{cx},{cy}]" : $"无 @ [{cx},{cy}]";
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
        if (IsCurrentLayerTerrain)
            PaintTerrainCell(cellPos, mode);
        else
            PaintUnitCell(cellPos, mode);
    }

    private void PaintTerrainCell(Vector2Int cellPos, EditMode mode)
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

    private void PaintUnitCell(Vector2Int cellPos, EditMode mode)
    {
        if (mode == EditMode.Brush)
        {
            if (_selectUnitIndex < _unitConfigs.Count)
            {
                string uid = _unitConfigs[_selectUnitIndex].unitId;
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
            layerCount = _allLayers.Count
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
    internal List<Dictionary<Vector2Int, string>> AllLayers => _allLayers;
    internal EditMode CurrentEditMode => _editMode;
    internal string SelectedTerrainId => _config != null && _selectTerrainIndex < _config.terrainResources.Count ? _config.terrainResources[_selectTerrainIndex].terrainId : "";

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
    internal string GetUnitNameById(string unitId)
    {
        foreach (var uc in _unitConfigs)
            if (uc != null && uc.unitId == unitId) return uc.unitName;
        return unitId;
    }

    internal void RepaintWindow() => Repaint();
}

public enum MapEditDimension { ThreeD, TwoD }
