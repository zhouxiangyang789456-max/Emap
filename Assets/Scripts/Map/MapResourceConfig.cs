using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MapResourceConfig", menuName = "地图编辑器/地形资源配置表")]
public class MapResourceConfig : ScriptableObject
{
    public List<TerrainResource> terrainResources = new List<TerrainResource>();

    private Dictionary<string, TerrainResource> _cache;

    public void RebuildCache()
    {
        _cache = new Dictionary<string, TerrainResource>();
        foreach (var res in terrainResources)
        {
            if (res != null && !string.IsNullOrEmpty(res.terrainId))
            {
                string key = res.terrainId.Trim();
                if (!_cache.ContainsKey(key))
                    _cache[key] = res;
            }
        }
    }

    public TerrainResource GetTerrainById(string terrainId)
    {
        if (string.IsNullOrEmpty(terrainId)) return null;
        if (_cache == null) RebuildCache();
        _cache.TryGetValue(terrainId.Trim(), out var result);
        return result;
    }

    public bool ContainsId(string terrainId)
    {
        return GetTerrainById(terrainId) != null;
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        var seenIds = new HashSet<string>();
        for (int i = 0; i < terrainResources.Count; i++)
        {
            var t = terrainResources[i];
            if (t == null) { errors.Add($"下标 {i} 为 null"); continue; }
            if (string.IsNullOrWhiteSpace(t.terrainId))
                errors.Add($"下标 {i} 地形 ID 为空");
            else if (!seenIds.Add(t.terrainId.Trim()))
                errors.Add($"地形 ID 重复: {t.terrainId}");
            if (string.IsNullOrWhiteSpace(t.terrainName))
                errors.Add($"地形 {t.terrainId} 名称为空");
        }
        RebuildCache();
        return errors;
    }
}

[System.Serializable]
public class TerrainResource
{
    [Header("基础信息")]
    public string terrainId;
    public string terrainName;
    public Sprite terrainSprite;

    [Header("通行规则")]
    public DefaultTerrainType defaultType;

    [Header("指定阻挡的单位 ID 列表")]
    public List<string> blockUnitIds = new List<string>();

    [Header("自定义属性列表")]
    public List<CustomProperty> customProperties = new List<CustomProperty>();

    public int GetInt(string propName)
    {
        var p = FindProperty(propName, PropertyType.Int);
        return p != null ? p.intValue : 0;
    }

    public float GetFloat(string propName)
    {
        var p = FindProperty(propName, PropertyType.Float);
        return p != null ? p.floatValue : 0f;
    }

    public bool GetBool(string propName)
    {
        var p = FindProperty(propName, PropertyType.Bool);
        return p != null && p.boolValue;
    }

    private CustomProperty FindProperty(string name, PropertyType type)
    {
        foreach (var p in customProperties)
        {
            if (p != null && p.propertyName == name && p.propertyType == type)
                return p;
        }
        return null;
    }
}

public enum DefaultTerrainType
{
    Passable,
    Impassable,
    UnitImpassable
}

[System.Serializable]
public class CustomProperty
{
    public string propertyName;
    public PropertyType propertyType;
    public int intValue;
    public float floatValue;
    public bool boolValue;

    [TextArea(2, 4)]
    public string propertyEffect;
}

public enum PropertyType
{
    Int,
    Float,
    Bool
}
