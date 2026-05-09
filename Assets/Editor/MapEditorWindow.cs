using UnityEditor;
using UnityEngine;

public class MapEditorWindow : EditorWindow
{
    private int mapWidth = 10;
    private int mapHeight = 10;
    private MapResourceConfig config;

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

        // 地图参数设置面板
        GUILayout.Label("地图参数", EditorStyles.boldLabel);
        mapWidth = EditorGUILayout.IntField("宽度", mapWidth);
        mapHeight = EditorGUILayout.IntField("高度", mapHeight);

        // 地形配置
        config = (MapResourceConfig)EditorGUILayout.ObjectField("地形配置", config, typeof(MapResourceConfig), false);

        // 这里添加更多UI元素
    }
}