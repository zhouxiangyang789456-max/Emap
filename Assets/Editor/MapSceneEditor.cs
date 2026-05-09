using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class MapSceneEditor
{
    static MapSceneEditor()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        // 检查编辑器窗口是否打开
        MapEditorWindow window = EditorWindow.GetWindow<MapEditorWindow>(false, null, false);
        if (window == null) return;

        // 绘制网格
        DrawGrid();
    }

    static void DrawGrid()
    {
        // 简单的网格绘制
        Handles.color = Color.gray;
        int gridSize = 10;
        for (int i = -gridSize; i <= gridSize; i++)
        {
            Handles.DrawLine(new Vector3(i, 0, -gridSize), new Vector3(i, 0, gridSize));
            Handles.DrawLine(new Vector3(-gridSize, 0, i), new Vector3(gridSize, 0, i));
        }
    }
}