using System;
using UnityEngine;

[Obsolete("请使用 TerrainResource 标签=\"单位\" 替代")]
[CreateAssetMenu(fileName = "UnitConfig", menuName = "地图编辑器/单位配置")]
public class UnitConfig : ScriptableObject
{
    public string unitId;
    public string unitName;
    public GameObject prefab;
    public int baseAttack = 10;
    public float baseSpeed = 5f;
    public int baseHealth = 100;
}
