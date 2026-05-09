using UnityEngine;

[CreateAssetMenu(fileName = "MapResourceConfig", menuName = "Map Editor/Resource Config")]
public class MapResourceConfig : ScriptableObject
{
    [System.Serializable]
    public class TerrainType
    {
        public int id;
        public string name;
        public bool canPass = true;
        public bool blockUnit = false;
        public Color color = Color.green;
        // 自定义属性可以在这里添加
    }

    public TerrainType[] terrainTypes;
}