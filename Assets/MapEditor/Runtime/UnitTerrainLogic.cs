using UnityEngine;

public class UnitTerrainLogic : MonoBehaviour
{
    [Header("单位唯一 ID")]
    public string unitId;

    [Header("单位配置（可选，为空时使用回退值）")]
    public UnitConfig unitConfig;

    private int BaseAtk => unitConfig != null ? unitConfig.baseAttack : 10;
    private float BaseSpeed => unitConfig != null ? unitConfig.baseSpeed : 5f;

    public void OnEnterGrid(string terrainId)
    {
        bool canPass = GameMapGlobal.CheckUnitCanPass(terrainId, unitId);
        if (!canPass)
        {
            Debug.Log($"[单位] {unitId} 无法通过地形: {terrainId}");
            return;
        }

        var terrain = GameMapGlobal.GetTerrain(terrainId);
        if (terrain == null) return;

        int atkAdd = terrain.GetInt("攻击加成");
        float speedAdd = terrain.GetFloat("移速加成");
        bool isPoison = terrain.GetBool("是否中毒地形");

        int nowAtk = BaseAtk + atkAdd;
        float nowSpeed = BaseSpeed * (1f + speedAdd);

        Debug.Log($"[单位] {unitId} 进入 {terrainId}: 攻击 {BaseAtk}+{atkAdd}={nowAtk}, 速度 {BaseSpeed}*{1f+speedAdd}={nowSpeed}");

        if (isPoison)
        {
            Debug.Log($"[单位] {unitId} 处于中毒地形，将持续掉血");
        }
    }

    public void OnExitGrid(string terrainId)
    {
        Debug.Log($"[单位] {unitId} 离开地形: {terrainId}");
    }
}
