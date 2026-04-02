using System;
using System.Collections.Generic;
using System.Diagnostics;

record struct BvhStats(
    int NodeCount,
    int DepthMax,
    int LeafCount,
    int LeafSizeMin,
    int LeafSizeMax,
    float LeafSizeAvg,
    float LeafDepthAvg,
    float SahCost // Surface area heuristic, how many things to test (boxes and shapes) for a random ray.
);

class Bvh<T> where T : IShape
{
    private const int DivideThreshold = 8;

    /**
     * There are two types of BVH nodes:
     * - Leaf node: Contains 'shapeCount' item indices starting from 'child' in the _items array.
     * - Parent node: Contains two child nodes starting at 'child' in the _nodes array.
     * The node-type can be determined by 'shapeCount': '> 0' for leaf-node, '== 0' for parent node.
     */
    private struct Node
    {
        public AABox Bounds;
        public int Child;
        public int ShapeCount;
    }

    private struct StatsData
    {
        public int NodeCount, DepthMax, LeafCount, LeafSizeMin, LeafSizeMax, ShapeCount, DepthWeighted;
        public float SahCost;

        public static StatsData Merge(StatsData a, StatsData b) => new StatsData
        {
            NodeCount = a.NodeCount + b.NodeCount,
            LeafCount = a.LeafCount + b.LeafCount,
            LeafSizeMin = Math.Min(a.LeafSizeMin, b.LeafSizeMin),
            LeafSizeMax = Math.Max(a.LeafSizeMax, b.LeafSizeMax),
            ShapeCount = a.ShapeCount + b.ShapeCount,
            DepthMax = Math.Max(a.DepthMax, b.DepthMax),
            DepthWeighted = a.DepthWeighted + b.DepthWeighted,
            SahCost = a.SahCost + b.SahCost,
        };
    }

    private readonly IReadOnlyList<T> _shapes;
    private readonly Node[] _nodes;
    private readonly int[] _items; // Indices into the shapes collection.
    private int _nodeCount;

    public Bvh(IReadOnlyList<T> shapes)
    {
        _shapes = shapes;
        _nodes = new Node[Math.Max(shapes.Count * 2, 1)];
        _items = new int[shapes.Count];

        // Identity item -> shape mapping.
        for (int i = 0; i != shapes.Count; ++i)
            _items[i] = i;

        if (shapes.Count > 0)
        {
            int root = InsertRoot();
            if (_nodes[root].ShapeCount >= DivideThreshold)
                Subdivide(root, 0);
        }
    }

    public AABox Bounds => _nodeCount > 0 ? _nodes[0].Bounds : AABox.Inverted();

    public BvhStats GetStats()
    {
        if (_nodeCount == 0)
            return default;

        float rootSa = _nodes[0].Bounds.SurfaceArea;
        StatsData acc = GetStatsNode(0, 0, rootSa);
        return new BvhStats(
            acc.NodeCount,
            acc.DepthMax,
            acc.LeafCount,
            acc.LeafSizeMin,
            acc.LeafSizeMax,
            acc.LeafCount > 0 ? (float)acc.ShapeCount / acc.LeafCount : 0f,
            acc.ShapeCount > 0 ? (float)acc.DepthWeighted / acc.ShapeCount : 0f,
            acc.SahCost);
    }

    private StatsData GetStatsNode(int nodeIdx, int depth, float rootSA)
    {
        ref Node node = ref _nodes[nodeIdx];
        float sa = node.Bounds.SurfaceArea;
        float saRatio = rootSA > 0f ? sa / rootSA : 0f;

        if (node.ShapeCount > 0)
        {
            return new StatsData
            {
                NodeCount = 1,
                DepthMax = depth,
                LeafCount = 1,
                LeafSizeMin = node.ShapeCount,
                LeafSizeMax = node.ShapeCount,
                ShapeCount = node.ShapeCount,
                DepthWeighted = depth * node.ShapeCount,
                SahCost = saRatio * node.ShapeCount,
            };
        }

        StatsData result = StatsData.Merge(
            GetStatsNode(node.Child, depth + 1, rootSA),
            GetStatsNode(node.Child + 1, depth + 1, rootSA));

        result.NodeCount += 1;
        result.SahCost += saRatio;
        return result;
    }

    public bool Overlaps(AABox box)
    {
        if (_nodeCount == 0)
            return false;

        Span<int> queue = stackalloc int[128];
        int queueCount = 1; // Insert root node (index is already zero).

        while (queueCount > 0)
        {
            ref Node node = ref _nodes[queue[--queueCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: enqueue children whose bounds overlap.
                if (_nodes[node.Child].Bounds.Overlaps(box))
                    queue[queueCount++] = node.Child;
                if (_nodes[node.Child + 1].Bounds.Overlaps(box))
                    queue[queueCount++] = node.Child + 1;
            }
            else
            {
                // Leaf node: test all shapes.
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    if (_shapes[_items[node.Child + i]].Overlaps(box))
                        return true;
                }
            }
        }
        return false;
    }

    public (RayHit Hit, int Index)? Intersect(Ray ray)
    {
        if (_nodeCount == 0)
            return null;

        Span<int> queue = stackalloc int[128];
        int queueCount = 1; // Insert root node (index is already zero).

        (RayHit Hit, int Index)? best = null;
        float bestDist = float.PositiveInfinity;

        while (queueCount > 0)
        {
            ref Node node = ref _nodes[queue[--queueCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: Test both child nodes (enqueue the closest first).
                float? tA = _nodes[node.Child].Bounds.IntersectDist(ray);
                float? tB = _nodes[node.Child + 1].Bounds.IntersectDist(ray);
                if (tA <= bestDist)
                    queue[queueCount++] = node.Child;
                if (tB <= bestDist)
                    queue[queueCount++] = node.Child + 1;
                if (tA <= bestDist && tB <= bestDist && tA < tB)
                    (queue[queueCount - 2], queue[queueCount - 1]) = (queue[queueCount - 1], queue[queueCount - 2]);
            }
            else
            {
                // Leaf node: Test all shapes in the node.
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    int idx = _items[node.Child + i];
                    if (_shapes[idx].Intersect(ray) is RayHit hit && hit.Dist < bestDist)
                    {
                        bestDist = hit.Dist;
                        best = (hit, idx);
                    }
                }
            }
        }
        return best;
    }

    public bool IntersectAny(Ray ray)
    {
        if (_nodeCount == 0)
            return false;

        Span<int> queue = stackalloc int[128];
        int queueCount = 1; // Insert root node (index is already zero).

        while (queueCount > 0)
        {
            ref Node node = ref _nodes[queue[--queueCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: enqueue children whose bounds are hit.
                if (_nodes[node.Child].Bounds.IntersectDist(ray) is not null)
                    queue[queueCount++] = node.Child;
                if (_nodes[node.Child + 1].Bounds.IntersectDist(ray) is not null)
                    queue[queueCount++] = node.Child + 1;
            }
            else
            {
                // Leaf node: return true on the first shape hit.
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    if (_shapes[_items[node.Child + i]].Intersect(ray) is not null)
                        return true;
                }
            }
        }
        return false;
    }

    public void OverlayBounds(Overlay overlay, Transform trans, int maxDepth = int.MaxValue)
    {
        OverlayBoundsNode(overlay, trans, 0, 0, maxDepth);
    }

    private void OverlayBoundsNode(Overlay overlay, Transform trans, int nodeIdx, int depth, int maxDepth)
    {
        if (depth > maxDepth)
            return;

        ref Node node = ref _nodes[nodeIdx];
        overlay.AddLineBox(node.Bounds.Transform(trans), Color.ForIndex(depth));

        if (node.ShapeCount == 0)
        {
            OverlayBoundsNode(overlay, trans, node.Child, depth + 1, maxDepth);
            OverlayBoundsNode(overlay, trans, node.Child + 1, depth + 1, maxDepth);
        }
    }

    /**
      * Insert a single root leaf-node containing all the shapes (needs at least 1 shape).
      * NOTE: Bvh needs to be empty before inserting a new root.
      * Returns the node index.
      */
    private int InsertRoot()
    {
        Debug.Assert(_nodeCount == 0);
        int index = _nodeCount++;
        ref Node node = ref _nodes[index];
        node.Bounds = AABox.Inverted();
        node.Child = 0;
        node.ShapeCount = _shapes.Count;
        for (int i = 0; i != _shapes.Count; ++i)
            node.Bounds.Encapsulate(_shapes[i].Bounds());
        return index;
    }

    /**
     * Insert a new child leaf-node covering items [itemBegin, itemBegin + itemCount) in _items.
     * NOTE: Items need to be consecutively stored in _items.
     * Returns the node index.
     */
    private int Insert(int itemBegin, int itemCount)
    {
        int index = _nodeCount++;
        ref Node node = ref _nodes[index];
        node.Bounds = AABox.Inverted();
        node.Child = itemBegin;
        node.ShapeCount = itemCount;
        for (int i = 0; i != itemCount; ++i)
            node.Bounds.Encapsulate(_shapes[_items[itemBegin + i]].Bounds());
        return index;
    }

    /**
     * Pick a plane to split the leaf-node on.
     * At the moment we just use the center of the longest axis of the node.
     */
    private (int Axis, float Pos) SplitPick(int nodeIdx)
    {
        AABox bounds = _nodes[nodeIdx].Bounds;
        Vec3 size = bounds.Size;
        int axis = 0;
        if (size.Y > size[axis])
            axis = 1;
        if (size.Z > size[axis])
            axis = 2;
        float pos = bounds.Min[axis] + size[axis] * 0.5f;
        return (axis, pos);
    }

    /**
     * Partition the leaf-node's items so all items before the returned _items index are on one side
     * of the split plane and all items from the returned index onwards are on the other side.
     */
    private int Partition(int nodeIdx, int axis, float splitPos)
    {
        ref Node node = ref _nodes[nodeIdx];
        int left = node.Child;
        int right = node.Child + node.ShapeCount - 1;
        while (true)
        {
            AABox leftBounds = _shapes[_items[left]].Bounds();
            float center = (leftBounds.Min[axis] + leftBounds.Max[axis]) * 0.5f;
            if (center < splitPos)
            {
                ++left;
                if (left > right)
                    break;
            }
            else
            {
                if (left == right)
                    break;
                // Wrong side; swap.
                (_items[left], _items[right]) = (_items[right], _items[left]);
                --right;
            }
        }
        return left;
    }

    /**
     * Subdivide the given leaf-node, if successful the node is no longer a leaf-node but contains a
     * tree of child nodes encompassing the same shapes as it did before subdividing.
     */
    private void Subdivide(int nodeIdx, int depth)
    {
        Debug.Assert(_nodes[nodeIdx].ShapeCount > 0);

        if (depth >= 64)
            return; // Depth limit reached; keep node as a leaf.

        var (axis, splitPos) = SplitPick(nodeIdx);
        int partitionIdx = Partition(nodeIdx, axis, splitPos);

        ref Node node = ref _nodes[nodeIdx];
        int countA = partitionIdx - node.Child;
        int countB = node.ShapeCount - countA;
        if (countA == 0 || countB == 0)
            return; // One of the partitions is empty; abort the subdivide.

        int childA = Insert(node.Child, countA);
        int childB = Insert(partitionIdx, countB);

        Debug.Assert(childB == childA + 1); // Children must be stored consecutively.
        node.Child = childA;
        node.ShapeCount = 0; // Node is no longer a leaf-node.

        const int maxDepth = 64;
        if (countA >= DivideThreshold && depth < maxDepth)
            Subdivide(childA, depth + 1);
        if (countB >= DivideThreshold && depth < maxDepth)
            Subdivide(childB, depth + 1);
    }
}
