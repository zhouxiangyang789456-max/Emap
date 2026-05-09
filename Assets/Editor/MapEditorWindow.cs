using UnityEditor;
using UnityEngine;

public class MapEditorWindow : EditorWindow
{
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
        // 这里添加更多UI元素
    }
}