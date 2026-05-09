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

        // 根据通行规则自动设置颜色
        public void UpdateColor()
        {
            if (!canPass)
            {
                color = Color.red; // 阻挡
            }
            else if (blockUnit)
            {
                color = Color.yellow; // 单位阻挡
            }
            else
            {
                color = Color.green; // 可通过
            }
        }
    }

    public TerrainType[] terrainTypes;
}