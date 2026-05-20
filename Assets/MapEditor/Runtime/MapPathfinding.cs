using System.Collections.Generic;
using UnityEngine;

public static class MapPathfinding
{
    public static List<Vector2Int> FindPath(
        int startX, int startY,
        int endX, int endY,
        string unitId,
        MapJsonData mapData,
        bool allowDiagonal = false)
    {
        if (mapData == null) return new List<Vector2Int>();
        if (!IsInBounds(startX, startY, mapData) || !IsInBounds(endX, endY, mapData))
            return new List<Vector2Int>();
        if (!IsPassable(endX, endY, unitId, mapData))
            return new List<Vector2Int>();

        Vector2Int start = new Vector2Int(startX, startY);
        Vector2Int end = new Vector2Int(endX, endY);
        if (start == end) return new List<Vector2Int> { start };

        bool isHex = mapData.IsHex;

        int initCapacity = Mathf.Min(mapData.mapWidth * mapData.mapHeight, 512);
        var openSet = new Heap<Node>(initCapacity);
        var closedSet = new HashSet<Vector2Int>();
        var nodeCache = new Dictionary<Vector2Int, Node>();

        Node startNode = GetOrCreateNode(startX, startY, nodeCache);
        startNode.gCost = 0;
        startNode.hCost = Heuristic(startX, startY, endX, endY, mapData);
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            Node current = openSet.RemoveFirst();

            if (current.pos == end)
                return RetracePath(startNode, current);

            closedSet.Add(current.pos);

            var neighbors = isHex
                ? GetHexNeighbors(current, mapData)
                : GetSquareNeighbors(current, allowDiagonal);

            foreach (var dir in neighbors)
            {
                int nx = current.pos.x + dir.x;
                int ny = current.pos.y + dir.y;

                if (!IsInBounds(nx, ny, mapData)) continue;
                if (closedSet.Contains(new Vector2Int(nx, ny))) continue;
                if (!IsPassable(nx, ny, unitId, mapData)) continue;

                // 方形对角穿角检测
                if (!isHex && allowDiagonal && Mathf.Abs(dir.x) == 1 && Mathf.Abs(dir.y) == 1)
                {
                    if (!IsPassable(current.pos.x + dir.x, current.pos.y, unitId, mapData)) continue;
                    if (!IsPassable(current.pos.x, current.pos.y + dir.y, unitId, mapData)) continue;
                }

                Node neighbor = GetOrCreateNode(nx, ny, nodeCache);
                float moveCost = isHex ? 1f
                    : (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) == 2) ? 1.414f : 1f;

                var terrain = GameMapGlobal.GetTerrainAt(nx, ny, 0, mapData);
                if (terrain != null)
                {
                    float terrainCost = terrain.GetFloat("移动消耗");
                    if (terrainCost > 0) moveCost += terrainCost;
                }

                float newGCost = current.gCost + moveCost;
                if (newGCost < neighbor.gCost)
                {
                    neighbor.gCost = newGCost;
                    neighbor.hCost = Heuristic(nx, ny, endX, endY, mapData);
                    neighbor.parent = current;

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                    else
                        openSet.UpdateItem(neighbor);
                }
            }
        }

        return new List<Vector2Int>();
    }

    private static List<Vector2Int> GetHexNeighbors(Node current, MapJsonData mapData)
    {
        var orient = mapData.hexOrientation == "PointyTop"
            ? HexGridUtils.HexOrientation.PointyTop
            : HexGridUtils.HexOrientation.FlatTop;
        return HexGridUtils.GetOffsetNeighbors(current.pos, orient);
    }

    private static List<Vector2Int> GetSquareNeighbors(Node current, bool allowDiagonal)
    {
        var result = new List<Vector2Int>(allowDiagonal ? 8 : 4);
        result.Add(new Vector2Int(0, 1));
        result.Add(new Vector2Int(1, 0));
        result.Add(new Vector2Int(0, -1));
        result.Add(new Vector2Int(-1, 0));
        if (allowDiagonal)
        {
            result.Add(new Vector2Int(1, 1));
            result.Add(new Vector2Int(1, -1));
            result.Add(new Vector2Int(-1, -1));
            result.Add(new Vector2Int(-1, 1));
        }
        return result;
    }

    public static bool IsPassable(int x, int y, string unitId, MapJsonData mapData)
    {
        return GameMapGlobal.CheckUnitCanPassAt(x, y, 0, unitId, mapData);
    }

    private static bool IsInBounds(int x, int y, MapJsonData mapData)
    {
        return x >= 0 && x < mapData.mapWidth && y >= 0 && y < mapData.mapHeight;
    }

    private static int Heuristic(int x1, int y1, int x2, int y2, MapJsonData mapData)
    {
        if (mapData.IsHex)
        {
            var orient = mapData.hexOrientation == "PointyTop"
                ? HexGridUtils.HexOrientation.PointyTop
                : HexGridUtils.HexOrientation.FlatTop;
            return 10 * HexGridUtils.OffsetDistance(new Vector2Int(x1, y1), new Vector2Int(x2, y2), orient);
        }

        int dx = Mathf.Abs(x1 - x2);
        int dy = Mathf.Abs(y1 - y2);
        return 10 * (dx + dy) + (14 - 2 * 10) * Mathf.Min(dx, dy);
    }

    private static List<Vector2Int> RetracePath(Node startNode, Node endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Node current = endNode;
        while (current != startNode)
        {
            path.Add(current.pos);
            current = current.parent;
        }
        path.Reverse();
        return path;
    }

    private static Node GetOrCreateNode(int x, int y, Dictionary<Vector2Int, Node> cache)
    {
        var key = new Vector2Int(x, y);
        if (!cache.TryGetValue(key, out Node node))
        {
            node = new Node { pos = key };
            cache[key] = node;
        }
        return node;
    }

    private class Node : IHeapItem<Node>
    {
        public Vector2Int pos;
        public float gCost;
        public float hCost;
        public Node parent;
        public int heapIndex { get; set; }
        public float fCost => gCost + hCost;

        public int CompareTo(Node other)
        {
            int compare = fCost.CompareTo(other.fCost);
            if (compare == 0) compare = hCost.CompareTo(other.hCost);
            return compare;
        }
    }
}

public class Heap<T> where T : IHeapItem<T>
{
    private T[] _items;
    private int _count;
    private const int DEFAULT_CAPACITY = 128;

    public int Count => _count;

    public Heap(int capacity = DEFAULT_CAPACITY)
    {
        _items = new T[Mathf.Max(capacity, DEFAULT_CAPACITY)];
    }

    public void Add(T item)
    {
        if (_count >= _items.Length)
            System.Array.Resize(ref _items, _items.Length * 2);
        item.heapIndex = _count;
        _items[_count] = item;
        SortUp(item);
        _count++;
    }

    public T RemoveFirst()
    {
        T first = _items[0];
        _count--;
        _items[0] = _items[_count];
        _items[0].heapIndex = 0;
        SortDown(_items[0]);
        return first;
    }

    public bool Contains(T item) => Equals(_items[item.heapIndex], item);

    public void UpdateItem(T item) => SortUp(item);

    private void SortUp(T item)
    {
        int parent = (item.heapIndex - 1) / 2;
        while (true)
        {
            T parentItem = _items[parent];
            if (item.CompareTo(parentItem) < 0)
                Swap(item, parentItem);
            else
                break;
            parent = (item.heapIndex - 1) / 2;
        }
    }

    private void SortDown(T item)
    {
        while (true)
        {
            int left = item.heapIndex * 2 + 1;
            int right = item.heapIndex * 2 + 2;
            int swap = left;

            if (left >= _count) break;
            if (right < _count && _items[left].CompareTo(_items[right]) > 0)
                swap = right;
            if (item.CompareTo(_items[swap]) > 0)
                Swap(item, _items[swap]);
            else
                break;
        }
    }

    private void Swap(T a, T b)
    {
        _items[a.heapIndex] = b;
        _items[b.heapIndex] = a;
        (a.heapIndex, b.heapIndex) = (b.heapIndex, a.heapIndex);
    }
}

public interface IHeapItem<T> : System.IComparable<T>
{
    int heapIndex { get; set; }
}
