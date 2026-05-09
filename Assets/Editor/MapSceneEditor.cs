using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class MapSceneEditor
{
    static MapEditorWindow window;
    static MapJsonData mapData;

    static MapSceneEditor()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        // 获取编辑器窗口
        window = EditorWindow.GetWindow<MapEditorWindow>(false, null, false);
        if (window == null) return;

        mapData = window.mapData;
        if (mapData == null) return;

        // 绘制网格
        DrawGrid();

        // 绘制地形色块
        DrawTerrainBlocks();

        // 处理鼠标点击
        HandleMouseInput();
    }

    static void DrawGrid()
    {
        Handles.color = Color.gray;
        int gridSize = 10;
        for (int i = -gridSize; i <= gridSize; i++)
        {
            Handles.DrawLine(new Vector3(i, 0, -gridSize), new Vector3(i, 0, gridSize));
            Handles.DrawLine(new Vector3(-gridSize, 0, i), new Vector3(gridSize, 0, i));
        }
    }

    static void DrawTerrainBlocks()
    {
        if (window.config == null) return;

        foreach (var cell in mapData.cells)
        {
            var terrain = System.Array.Find(window.config.terrainTypes, t => t.id == cell.terrainId);
            if (terrain != null)
            {
                Handles.color = terrain.color;
                Handles.DrawSolidRectangleWithOutline(
                    new Rect(cell.x, cell.y, 1, 1),
                    terrain.color,
                    Color.black
                );
            }
        }
    }

    static void HandleMouseInput()
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Vector2 mousePos = Event.current.mousePosition;
            mousePos.y = SceneView.currentDrawingSceneView.camera.pixelHeight - mousePos.y;
            Ray ray = SceneView.currentDrawingSceneView.camera.ScreenPointToRay(mousePos);
            Plane plane = new Plane(Vector3.up, 0);
            float enter;
            if (plane.Raycast(ray, out enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                int x = Mathf.FloorToInt(hitPoint.x);
                int y = Mathf.FloorToInt(hitPoint.z);

                if (x >= 0 && x < mapData.width && y >= 0 && y < mapData.height)
                {
                    if (window.editMode == MapEditorWindow.EditMode.Brush)
                    {
                        // 添加或更新色块
                        var existing = mapData.cells.Find(c => c.x == x && c.y == y);
                        if (existing != null)
                        {
                            existing.terrainId = window.selectedTerrainId;
                        }
                        else
                        {
                            mapData.cells.Add(new MapJsonData.TerrainCell { x = x, y = y, terrainId = window.selectedTerrainId });
                        }
                    }
                    else if (window.editMode == MapEditorWindow.EditMode.Eraser)
                    {
                        // 删除色块
                        mapData.cells.RemoveAll(c => c.x == x && c.y == y);
                    }
                    e.Use();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}