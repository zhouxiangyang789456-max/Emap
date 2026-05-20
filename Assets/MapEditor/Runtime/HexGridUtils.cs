using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 六边形网格工具。六边形地图使用 Unity Tilemap 的<strong>偏移矩形</strong>坐标 (col, row)，
/// 即 mapWidth×mapHeight 的矩形区域，与 Unity Hex Tilemap 一致（非轴向平行四边形）。
/// </summary>
public static class HexGridUtils
{
    public enum HexOrientation { FlatTop, PointyTop }

    public const float SQRT3 = 1.73205080757f;
    private const float SQRT3_OVER_2 = 0.86602540378f;
    private const float SQRT3_OVER_3 = 0.57735026919f;
    private const float ONE_THIRD = 0.33333333333f;
    private const float TWO_THIRDS = 0.66666666667f;

    /// <summary>轴向六向邻居（仅用于轴向坐标运算，如范围圆）。</summary>
    public static readonly Vector2Int[] HexNeighbors = new Vector2Int[]
    {
        new Vector2Int(+1, -1),
        new Vector2Int(+1,  0),
        new Vector2Int( 0, +1),
        new Vector2Int(-1, +1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0, -1),
    };

    private static readonly Vector2Int[] OddRNeighborsEven = new Vector2Int[]
    {
        new(+1, 0), new(+1, -1), new(0, -1), new(-1, -1), new(-1, 0), new(0, +1),
    };

    private static readonly Vector2Int[] OddRNeighborsOdd = new Vector2Int[]
    {
        new(+1, 0), new(+1, +1), new(0, -1), new(-1, 0), new(0, +1), new(+1, +1),
    };

    // ==================== Unity Grid cellSize（外接矩形宽×高） ====================

    /// <summary>cellSize = 六边形外接圆半径 R 时，Unity Grid 的 cellSize。</summary>
    public static Vector3 GetUnityCellSize(float radius, HexOrientation orientation)
    {
        if (orientation == HexOrientation.FlatTop)
            return new Vector3(radius * 2f, radius * SQRT3, 1f);
        return new Vector3(radius * SQRT3, radius * 2f, 1f);
    }

    public static float GetRadiusFromCellSize(Vector3 cellSize, HexOrientation orientation)
        => orientation == HexOrientation.FlatTop ? cellSize.x * 0.5f : cellSize.y * 0.5f;

    // ==================== 偏移矩形 (col,row) ↔ 世界坐标（与 Unity Grid 一致） ====================

    /// <summary>Unity Grid 单元格原点（包围盒左下角，与 CellToWorld 一致）。</summary>
    public static Vector3 OffsetCellOriginToLocal(int col, int row, Vector3 cellSize)
    {
        float x = (col + row * 0.5f - ((row + (row & 1)) & 1) * 0.5f) * cellSize.x;
        float y = row * cellSize.y * TWO_THIRDS;
        return new Vector3(x, y, 0f);
    }

    /// <summary>Unity Grid 单元格中心（与 GetCellCenterWorld 一致）。</summary>
    public static Vector3 OffsetCellCenterToLocal(int col, int row, Vector3 cellSize)
    {
        Vector3 origin = OffsetCellOriginToLocal(col, row, cellSize);
        return origin + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
    }

    public static Vector3 OffsetToWorld2D(int col, int row, float radius, HexOrientation orientation)
    {
        Vector3 cellSize = GetUnityCellSize(radius, orientation);
        Vector3 local = OffsetCellCenterToLocal(col, row, cellSize);
        return new Vector3(local.x, local.y, 0f);
    }

    public static Vector3 OffsetToWorld3D(int col, int row, float radius, HexOrientation orientation)
    {
        Vector3 cellSize = GetUnityCellSize(radius, orientation);
        Vector3 local = OffsetCellCenterToLocal(col, row, cellSize);
        return new Vector3(local.x, 0f, local.y);
    }

    public static Vector3 OffsetToWorld2D(Vector2Int offset, float radius, HexOrientation orientation)
        => OffsetToWorld2D(offset.x, offset.y, radius, orientation);

    public static Vector3 OffsetToWorld3D(Vector2Int offset, float radius, HexOrientation orientation)
        => OffsetToWorld3D(offset.x, offset.y, radius, orientation);

    public static Vector2Int WorldToOffset2D(Vector3 worldPos, float radius, HexOrientation orientation)
    {
        Vector3 cellSize = GetUnityCellSize(radius, orientation);
        // worldPos 为单元格中心时，先还原为 Unity 单元格原点
        Vector3 origin = worldPos - new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        float rowF = origin.y / (cellSize.y * TWO_THIRDS);
        int row = Mathf.RoundToInt(rowF);
        float colF = origin.x / cellSize.x
                     - rowF * 0.5f
                     + ((row + (row & 1)) & 1) * 0.5f;
        return new Vector2Int(Mathf.RoundToInt(colF), row);
    }

    public static Vector2Int WorldToOffset3D(Vector3 worldPos, float radius, HexOrientation orientation)
    {
        Vector3 flat = new Vector3(worldPos.x, worldPos.z, 0f);
        return WorldToOffset2D(flat, radius, orientation);
    }

    public static bool IsInOffsetRect(int col, int row, int mapWidth, int mapHeight)
        => col >= 0 && col < mapWidth && row >= 0 && row < mapHeight;

    public static List<Vector2Int> GetOffsetNeighbors(Vector2Int offset, HexOrientation orientation)
    {
        // Unity Hex Tilemap 使用 odd-r；平顶/尖顶仅影响 cellSize 比例与绘制角度
        var dirs = (offset.y & 1) == 0 ? OddRNeighborsEven : OddRNeighborsOdd;
        var result = new List<Vector2Int>(6);
        foreach (var d in dirs)
            result.Add(offset + d);
        return result;
    }

    public static List<Vector2Int> GetOffsetNeighborsInBounds(Vector2Int offset, int mapWidth, int mapHeight,
        HexOrientation orientation)
    {
        var result = new List<Vector2Int>(6);
        foreach (var n in GetOffsetNeighbors(offset, orientation))
        {
            if (IsInOffsetRect(n.x, n.y, mapWidth, mapHeight))
                result.Add(n);
        }
        return result;
    }

    public static int OffsetDistance(Vector2Int a, Vector2Int b, HexOrientation orientation)
    {
        Vector2Int aq = OffsetToAxial(a, orientation);
        Vector2Int bq = OffsetToAxial(b, orientation);
        return HexDistance(aq, bq);
    }

    // ==================== 偏移 ↔ 轴向（范围查询等仍可用轴向） ====================

    public static Vector2Int OffsetToAxial(Vector2Int offset, HexOrientation orientation)
    {
        int q = offset.x;
        int r = offset.y - ((offset.x - (offset.x & 1)) >> 1);
        return new Vector2Int(q, r);
    }

    public static Vector2Int AxialToOffset(Vector2Int axial, HexOrientation orientation)
    {
        int col = axial.x;
        int row = axial.y + ((axial.x - (axial.x & 1)) >> 1);
        return new Vector2Int(col, row);
    }

    // ==================== 轴向坐标（范围、旧公式兼容） ====================

    public static Vector3 HexToWorld3D(int q, int r, float cellSize, HexOrientation orientation)
    {
        Vector2Int offset = AxialToOffset(new Vector2Int(q, r), orientation);
        return OffsetToWorld3D(offset, cellSize, orientation);
    }

    public static Vector3 HexToWorld3D(Vector2Int hex, float cellSize, HexOrientation orientation)
        => HexToWorld3D(hex.x, hex.y, cellSize, orientation);

    public static Vector3 HexToWorld2D(int q, int r, float cellSize, HexOrientation orientation)
    {
        Vector2Int offset = AxialToOffset(new Vector2Int(q, r), orientation);
        return OffsetToWorld2D(offset, cellSize, orientation);
    }

    public static Vector3 HexToWorld2D(Vector2Int hex, float cellSize, HexOrientation orientation)
        => HexToWorld2D(hex.x, hex.y, cellSize, orientation);

    public static Vector2Int WorldToHex3D(Vector3 worldPos, float cellSize, HexOrientation orientation)
        => OffsetToAxial(WorldToOffset3D(worldPos, cellSize, orientation), orientation);

    public static Vector2Int WorldToHex2D(Vector3 worldPos, float cellSize, HexOrientation orientation)
        => OffsetToAxial(WorldToOffset2D(worldPos, cellSize, orientation), orientation);

    // ==================== 立方坐标圆整 ====================

    private static Vector2Int CubeRound(float q, float r)
    {
        float cx = q;
        float cz = r;
        float cy = -cx - cz;

        int rx = Mathf.RoundToInt(cx);
        int ry = Mathf.RoundToInt(cy);
        int rz = Mathf.RoundToInt(cz);

        float dx = Mathf.Abs(rx - cx);
        float dy = Mathf.Abs(ry - cy);
        float dz = Mathf.Abs(rz - cz);

        if (dx > dy && dx > dz)
            rx = -ry - rz;
        else if (dy > dz)
            ry = -rx - rz;

        return new Vector2Int(rx, rz);
    }

    // ==================== 六边形顶点 ====================

    public static Vector3[] GetHexCorners2D(Vector3 center, float radius, HexOrientation orientation)
        => GetHexOutlineCorners2D(center, GetUnityCellSize(radius, orientation), orientation);

    /// <summary>
    /// 按 Unity Hex 单元格外接矩形 (cellSize.x × cellSize.y) 生成顶点，与 Tilemap 蜂窝对齐。
    /// </summary>
    public static Vector3[] GetHexOutlineCorners2D(Vector3 center, Vector3 cellSize, HexOrientation orientation)
    {
        float hw = cellSize.x * 0.5f;
        float hh = cellSize.y * 0.5f;
        if (orientation == HexOrientation.PointyTop)
        {
            return new[]
            {
                center + new Vector3(0f, hh, 0f),
                center + new Vector3(hw, hh * 0.5f, 0f),
                center + new Vector3(hw, -hh * 0.5f, 0f),
                center + new Vector3(0f, -hh, 0f),
                center + new Vector3(-hw, -hh * 0.5f, 0f),
                center + new Vector3(-hw, hh * 0.5f, 0f),
            };
        }

        return new[]
        {
            center + new Vector3(hw, 0f, 0f),
            center + new Vector3(hw * 0.5f, hh, 0f),
            center + new Vector3(-hw * 0.5f, hh, 0f),
            center + new Vector3(-hw, 0f, 0f),
            center + new Vector3(-hw * 0.5f, -hh, 0f),
            center + new Vector3(hw * 0.5f, -hh, 0f),
        };
    }

    public static Vector3[] GetHexOutlineCorners3D(Vector3 center, Vector3 cellSize, HexOrientation orientation)
    {
        Vector3[] c2 = GetHexOutlineCorners2D(Vector3.zero, cellSize, orientation);
        var result = new Vector3[6];
        for (int i = 0; i < 6; i++)
            result[i] = center + new Vector3(c2[i].x, 0f, c2[i].y);
        return result;
    }

    public static Vector3[] GetHexCorners2D(Vector3 center, Grid grid, HexOrientation orientation)
        => GetHexOutlineCorners2D(center, grid.cellSize, orientation);

    public static Vector3[] GetHexCorners3D(Vector3 center, float radius, HexOrientation orientation)
        => GetHexOutlineCorners3D(center, GetUnityCellSize(radius, orientation), orientation);

    // ==================== 距离与邻接 ====================

    public static int HexDistance(Vector2Int a, Vector2Int b)
    {
        int ax = a.x, az = a.y, ay = -ax - az;
        int bx = b.x, bz = b.y, by = -bx - bz;
        return (Mathf.Abs(ax - bx) + Mathf.Abs(ay - by) + Mathf.Abs(az - bz)) / 2;
    }

    public static bool IsAdjacent(Vector2Int a, Vector2Int b)
        => HexDistance(a, b) == 1;

    public static List<Vector2Int> GetNeighbors(Vector2Int hex)
    {
        var result = new List<Vector2Int>(6);
        foreach (var dir in HexNeighbors)
            result.Add(hex + dir);
        return result;
    }

    /// <summary>轴向邻接 + 矩形边界（仅当坐标存轴向时使用）。</summary>
    public static List<Vector2Int> GetNeighborsInBounds(Vector2Int hex, int mapWidth, int mapHeight)
    {
        var result = new List<Vector2Int>(6);
        foreach (var dir in HexNeighbors)
        {
            Vector2Int neighbor = hex + dir;
            if (neighbor.x >= 0 && neighbor.x < mapWidth && neighbor.y >= 0 && neighbor.y < mapHeight)
                result.Add(neighbor);
        }
        return result;
    }

    // ==================== 六边形范围（轴向空间） ====================

    public static List<Vector2Int> GetHexagonsInRange(Vector2Int center, int range)
    {
        var result = new List<Vector2Int>();
        for (int dq = -range; dq <= range; dq++)
        {
            int minR = Mathf.Max(-range, -dq - range);
            int maxR = Mathf.Min(range, -dq + range);
            for (int dr = minR; dr <= maxR; dr++)
                result.Add(new Vector2Int(center.x + dq, center.y + dr));
        }
        return result;
    }

    /// <summary>以偏移格为中心的六边形范围，结果过滤在矩形地图内。</summary>
    public static List<Vector2Int> GetOffsetHexagonsInRange(Vector2Int centerOffset, int range,
        int mapWidth, int mapHeight, HexOrientation orientation)
    {
        Vector2Int centerAxial = OffsetToAxial(centerOffset, orientation);
        var axialCells = GetHexagonsInRange(centerAxial, range);
        var result = new List<Vector2Int>();
        foreach (var axial in axialCells)
        {
            Vector2Int off = AxialToOffset(axial, orientation);
            if (IsInOffsetRect(off.x, off.y, mapWidth, mapHeight))
                result.Add(off);
        }
        return result;
    }

    // ==================== 视线 ====================

    public static List<Vector2Int> HexLine(Vector2Int from, Vector2Int to)
    {
        int dist = HexDistance(from, to);
        var result = new List<Vector2Int>();
        float step = 1f / Mathf.Max(dist, 1);
        for (int i = 0; i <= dist; i++)
        {
            float t = step * i;
            float ax = from.x, az = from.y, ay = -ax - az;
            float bx = to.x, bz = to.y, by = -bx - bz;
            float lx = ax + (bx - ax) * t;
            float lz = az + (bz - az) * t;
            result.Add(CubeRound(lx, lz));
        }
        return result;
    }

    // ==================== 地图边界（矩形偏移地图） ====================

    public static (Vector3 min, Vector3 max) GetMapBoundsWorld3D(int mapWidth, int mapHeight,
        float radius, HexOrientation orientation)
    {
        Vector3 min = new Vector3(float.MaxValue, 0, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, 0, float.MinValue);
        float pad = radius * 1.2f;

        Vector3 cellSize = GetUnityCellSize(radius, orientation);
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                Vector3 center = OffsetToWorld3D(col, row, radius, orientation);
                foreach (var corner in GetHexOutlineCorners3D(center, cellSize, orientation))
                {
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                }
            }
        }

        min -= new Vector3(pad, 0, pad);
        max += new Vector3(pad, 0, pad);
        return (min, max);
    }

    public static (Vector3 min, Vector3 max) GetMapBoundsWorld2D(int mapWidth, int mapHeight,
        float radius, HexOrientation orientation)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, 0);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, 0);
        float pad = radius * 1.2f;

        Vector3 cellSize = GetUnityCellSize(radius, orientation);
        for (int row = 0; row < mapHeight; row++)
        {
            for (int col = 0; col < mapWidth; col++)
            {
                Vector3 center = OffsetToWorld2D(col, row, radius, orientation);
                foreach (var corner in GetHexOutlineCorners2D(center, cellSize, orientation))
                {
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                }
            }
        }

        min -= new Vector3(pad, pad, 0);
        max += new Vector3(pad, pad, 0);
        return (min, max);
    }
}
