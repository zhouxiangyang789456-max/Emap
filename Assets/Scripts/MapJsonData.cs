using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MapJsonData
{
    public int width;
    public int height;
    public List<TerrainCell> cells;

    [System.Serializable]
    public class TerrainCell
    {
        public int x;
        public int y;
        public int terrainId;
    }
}