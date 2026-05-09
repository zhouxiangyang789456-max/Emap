using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class MapEditorWindow : EditorWindow
{
    private int mapWidth = 10;
    private int mapHeight = 10;
    private MapResourceConfig config;
    private Vector2 scrollPos;
    private int selectedTerrainId = 0;
    private EditMode editMode = EditMode.Brush;
    public MapJsonData mapData;
    private string savePath = "Assets/map.json";

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

            // JSON 保存/加载
            GUILayout.Label("保存/加载", EditorStyles.boldLabel);
            savePath = EditorGUILayout.TextField("文件路径", savePath);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存"))
            {
                SaveMap();
            }
            if (GUILayout.Button("加载"))
            {
                LoadMap();
            }
            EditorGUILayout.EndHorizontal();

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

    void SaveMap()
    {
        string json = JsonUtility.ToJson(mapData, true);
        File.WriteAllText(savePath, json);
        AssetDatabase.Refresh();
        Debug.Log("地图已保存到 " + savePath);
    }

    void LoadMap()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            mapData = JsonUtility.FromJson<MapJsonData>(json);
            mapWidth = mapData.width;
            mapHeight = mapData.height;
            Debug.Log("地图已加载从 " + savePath);
        }
        else
        {
            Debug.LogError("文件不存在: " + savePath);
        }
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