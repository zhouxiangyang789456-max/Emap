using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class MapEditorWindow : EditorWindow
{
    private int mapWidth = 10;
    private int mapHeight = 10;
    private MapResourceConfig config;
    private Vector2 scrollPos;
    private int selectedTerrainId = 0;
    private EditMode editMode = EditMode.Brush;
    public MapJsonData mapData;

    public enum EditMode
    {
        Brush,
        Eraser
    }

    [MenuItem("地图编辑器/打开地图编辑器")]
    static void OpenWindow()
    {
        MapEditorWindow window = GetWindow<MapEditorWindow>();
        window.titleContent = new GUIContent("地图编辑器");
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("地图编辑器", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        // 地图参数设置面板
        GUILayout.Label("地图参数", EditorStyles.boldLabel);
        mapWidth = EditorGUILayout.IntField("宽度", mapWidth);
        mapHeight = EditorGUILayout.IntField("高度", mapHeight);

        // 地形配置
        config = (MapResourceConfig)EditorGUILayout.ObjectField("地形配置", config, typeof(MapResourceConfig), false);

        if (config != null)
        {
            // 编辑模式
            GUILayout.Label("编辑模式", EditorStyles.boldLabel);
            editMode = (EditMode)EditorGUILayout.EnumPopup("模式", editMode);

            // 选择地形
            string[] terrainNames = new string[config.terrainTypes.Length];
            for (int i = 0; i < config.terrainTypes.Length; i++)
            {
                terrainNames[i] = config.terrainTypes[i].name;
            }
            selectedTerrainId = EditorGUILayout.Popup("选择地形", selectedTerrainId, terrainNames);

            // 初始化地图数据
            if (mapData == null || mapData.width != mapWidth || mapData.height != mapHeight)
            {
                mapData = new MapJsonData { width = mapWidth, height = mapHeight, cells = new List<MapJsonData.TerrainCell>() };
            }

            // 地形资源列表编辑面板
            GUILayout.Label("地形资源列表", EditorStyles.boldLabel);
            if (GUILayout.Button("添加地形"))
            {
                AddTerrainType();
            }

            for (int i = 0; i < config.terrainTypes.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                config.terrainTypes[i].id = EditorGUILayout.IntField("ID", config.terrainTypes[i].id);
                config.terrainTypes[i].name = EditorGUILayout.TextField("名称", config.terrainTypes[i].name);
                config.terrainTypes[i].canPass = EditorGUILayout.Toggle("可通过", config.terrainTypes[i].canPass);
                config.terrainTypes[i].blockUnit = EditorGUILayout.Toggle("阻挡单位", config.terrainTypes[i].blockUnit);
                config.terrainTypes[i].color = EditorGUILayout.ColorField("颜色", config.terrainTypes[i].color);
                if (GUILayout.Button("删除"))
                {
                    RemoveTerrainType(i);
                    break; // 避免索引错误
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    void AddTerrainType()
    {
        var list = new List<MapResourceConfig.TerrainType>(config.terrainTypes);
        list.Add(new MapResourceConfig.TerrainType { id = list.Count, name = "新地形", color = Color.green });
        config.terrainTypes = list.ToArray();
    }

    void RemoveTerrainType(int index)
    {
        var list = new List<MapResourceConfig.TerrainType>(config.terrainTypes);
        list.RemoveAt(index);
        config.terrainTypes = list.ToArray();
    }
}