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
    float SahCost // Surface area heuristic, estimate cost of things to test (boxes and shapes) for a random ray.
);

class Bvh<T, THit> where T : IShape<THit> where THit : unmanaged, IShapeHit
{
    private readonly int _splitBinCount;

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

    private struct NodeStats
    {
        public int NodeCount, DepthMax, LeafCount, LeafSizeMin, LeafSizeMax, ShapeCount, DepthWeighted;
        public float SahCost;

        public static NodeStats Merge(NodeStats a, NodeStats b) => new NodeStats
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

    private readonly T[] _shapes;
    private readonly Node[] _nodes;
    private readonly int[] _items; // Indices into the shapes collection.
    private int _nodeCount;
    private readonly float _sahCostTraverse;
    private readonly float _sahCostIntersect;

    public Bvh(IReadOnlyList<T> shapes, float sahCostTraverse = 1f, float sahCostIntersect = 1f, int splitBinCount = 8)
    {
        _sahCostTraverse = sahCostTraverse;
        _sahCostIntersect = sahCostIntersect;
        _splitBinCount = splitBinCount;
        _shapes = shapes is T[] arr ? arr : [.. shapes];
        _nodes = new Node[Math.Max(shapes.Count * 2, 1)];
        _items = new int[shapes.Count];

        // Identity item -> shape mapping.
        for (int i = 0; i != shapes.Count; ++i)
            _items[i] = i;

        if (shapes.Count > 0)
        {
            Subdivide(InsertRoot(), 0);
        }
    }

    public AABox Bounds => _nodeCount > 0 ? _nodes[0].Bounds : AABox.Inverted();

    public BvhStats GetStats()
    {
        if (_nodeCount == 0)
            return default;

        float rootSa = _nodes[0].Bounds.SurfaceArea;
        NodeStats acc = GetStatsNode(0, 0, rootSa);
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

    private NodeStats GetStatsNode(int nodeIdx, int depth, float rootSA)
    {
        ref Node node = ref _nodes[nodeIdx];
        float sa = node.Bounds.SurfaceArea;
        float saRatio = rootSA > 0f ? sa / rootSA : 0f;

        if (node.ShapeCount > 0)
        {
            return new NodeStats
            {
                NodeCount = 1,
                DepthMax = depth,
                LeafCount = 1,
                LeafSizeMin = node.ShapeCount,
                LeafSizeMax = node.ShapeCount,
                ShapeCount = node.ShapeCount,
                DepthWeighted = depth * node.ShapeCount,
                SahCost = SahLeaf(saRatio, node.ShapeCount),
            };
        }

        NodeStats result = NodeStats.Merge(
            GetStatsNode(node.Child, depth + 1, rootSA),
            GetStatsNode(node.Child + 1, depth + 1, rootSA));

        result.NodeCount += 1;
        result.SahCost += _sahCostTraverse * saRatio;
        return result;
    }

    public bool Overlaps(AABox box, Counters counters)
    {
        if (_nodeCount == 0)
            return false;

        long[] counterData = counters.GetLocalData();

        Span<int> queue = stackalloc int[128];
        int queueCount = 1; // Insert root node (index is already zero).

        while (queueCount > 0)
        {
            ref Node node = ref _nodes[queue[--queueCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: enqueue children whose bounds overlap.
                counterData[(int)Counters.Type.BvhOverlapNode] += 2;
                if (_nodes[node.Child].Bounds.Overlaps(box))
                    queue[queueCount++] = node.Child;
                if (_nodes[node.Child + 1].Bounds.Overlaps(box))
                    queue[queueCount++] = node.Child + 1;
            }
            else
            {
                // Leaf node: test all shapes.
                counterData[(int)Counters.Type.BvhOverlapShape] += node.ShapeCount;
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    if (_shapes[_items[node.Child + i]].Overlaps(box))
                        return true;
                }
            }
        }
        return false;
    }

    public (THit Hit, int Index)? Intersect(Ray ray, Counters counters)
    {
        if (_nodeCount == 0)
            return null;

        long[] counterData = counters.GetLocalData();

        Span<(int Idx, float T)> queue = stackalloc (int, float)[128];
        int queueCount = 1; // Insert root node (index is already zero).

        (THit Hit, int Index)? best = null;
        float bestDist = float.PositiveInfinity;

        while (queueCount > 0)
        {
            var (nodeIdx, tNode) = queue[--queueCount];
            if (tNode >= bestDist)
                continue; // Prune: a closer hit was already found.

            ref Node node = ref _nodes[nodeIdx];
            if (node.ShapeCount == 0)
            {
                // Parent node: Test both child nodes (enqueue the closest first).
                counterData[(int)Counters.Type.BvhIntersectNode] += 2;
                float? tA = _nodes[node.Child].Bounds.IntersectDist(ray);
                float? tB = _nodes[node.Child + 1].Bounds.IntersectDist(ray);
                if (tA <= bestDist)
                    queue[queueCount++] = (node.Child, tA!.Value);
                if (tB <= bestDist)
                    queue[queueCount++] = (node.Child + 1, tB!.Value);
                if (tA <= bestDist && tB <= bestDist && tA < tB)
                    (queue[queueCount - 2], queue[queueCount - 1]) = (queue[queueCount - 1], queue[queueCount - 2]);
            }
            else
            {
                // Leaf node: Test all shapes in the node.
                counterData[(int)Counters.Type.BvhIntersectShape] += node.ShapeCount;
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    int idx = _items[node.Child + i];
                    if (_shapes[idx].Intersect(ray) is THit hit && hit.Dist < bestDist)
                    {
                        bestDist = hit.Dist;
                        best = (hit, idx);
                    }
                }
            }
        }
        return best;
    }

    public bool IntersectAny(Ray ray, Counters counters)
    {
        if (_nodeCount == 0)
            return false;

        long[] counterData = counters.GetLocalData();

        Span<int> queue = stackalloc int[128];
        int queueCount = 1; // Insert root node (index is already zero).

        while (queueCount > 0)
        {
            ref Node node = ref _nodes[queue[--queueCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: enqueue children whose bounds are hit (closest first).
                counterData[(int)Counters.Type.BvhIntersectNode] += 2;
                float? tA = _nodes[node.Child].Bounds.IntersectDist(ray);
                float? tB = _nodes[node.Child + 1].Bounds.IntersectDist(ray);
                if (tA is not null)
                    queue[queueCount++] = node.Child;
                if (tB is not null)
                    queue[queueCount++] = node.Child + 1;
                if (tA is not null && tB is not null && tA < tB)
                    (queue[queueCount - 2], queue[queueCount - 1]) = (queue[queueCount - 1], queue[queueCount - 2]);
            }
            else
            {
                // Leaf node: return true on the first shape hit.
                counterData[(int)Counters.Type.BvhIntersectShape] += node.ShapeCount;
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    if (_shapes[_items[node.Child + i]].IntersectAny(ray))
                        return true;
                }
            }
        }
        return false;
    }

    public void Intersect(ReadOnlySpan<Ray> rays, Span<(THit Hit, int Index)?> hits, Counters counters)
    {
        Debug.Assert(rays.Length == hits.Length);
        hits.Clear();

        if (_nodeCount == 0)
            return;

        long[] counterData = counters.GetLocalData();

        Span<float> bestDists = stackalloc float[rays.Length];
        bestDists.Fill(float.PositiveInfinity);

        Span<THit?> shapeHits = stackalloc THit?[rays.Length];

        Span<int> stack = stackalloc int[128];
        int stackCount = 1; // Insert root node (index is already zero).

        while (stackCount > 0)
        {
            ref Node node = ref _nodes[stack[--stackCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: descend children if any ray hits their bounds.
                counterData[(int)Counters.Type.BvhIntersectNode] += 2;
                bool anyA = false, anyB = false;
                for (int r = 0; r != rays.Length; ++r)
                {
                    if (!anyA)
                        anyA |= _nodes[node.Child].Bounds.IntersectDist(rays[r]) is float tA && tA < bestDists[r];
                    if (!anyB)
                        anyB |= _nodes[node.Child + 1].Bounds.IntersectDist(rays[r]) is float tB && tB < bestDists[r];
                    if (anyA && anyB)
                        break;
                }
                if (anyA)
                    stack[stackCount++] = node.Child;
                if (anyB)
                    stack[stackCount++] = node.Child + 1;
            }
            else
            {
                // Leaf node: test all shapes against all rays.
                counterData[(int)Counters.Type.BvhIntersectShape] += node.ShapeCount * rays.Length;
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    int idx = _items[node.Child + i];
                    _shapes[idx].Intersect(rays, shapeHits);
                    for (int r = 0; r != rays.Length; ++r)
                    {
                        if (shapeHits[r] is THit hit && hit.Dist < bestDists[r])
                        {
                            bestDists[r] = hit.Dist;
                            hits[r] = (hit, idx);
                        }
                    }
                }
            }
        }
    }

    public void IntersectAny(ReadOnlySpan<Ray> rays, Span<bool> hits, Counters counters)
    {
        Debug.Assert(rays.Length == hits.Length);
        hits.Clear();

        if (_nodeCount == 0)
            return;

        long[] counterData = counters.GetLocalData();

        Span<bool> shapeHits = stackalloc bool[rays.Length];
        int activeCount = rays.Length;

        Span<int> stack = stackalloc int[128];
        int stackCount = 1; // Insert root node (index is already zero).

        while (stackCount > 0 && activeCount > 0)
        {
            ref Node node = ref _nodes[stack[--stackCount]];
            if (node.ShapeCount == 0)
            {
                // Parent node: descend children if any active ray hits their bounds.
                counterData[(int)Counters.Type.BvhIntersectNode] += 2;
                bool anyA = false, anyB = false;
                for (int r = 0; r != rays.Length; ++r)
                {
                    if (hits[r])
                        continue;
                    if (!anyA)
                        anyA |= _nodes[node.Child].Bounds.IntersectDist(rays[r]).HasValue;
                    if (!anyB)
                        anyB |= _nodes[node.Child + 1].Bounds.IntersectDist(rays[r]).HasValue;
                    if (anyA && anyB)
                        break;
                }
                if (anyA)
                    stack[stackCount++] = node.Child;
                if (anyB)
                    stack[stackCount++] = node.Child + 1;
            }
            else
            {
                // Leaf node: test shapes against active rays, deactivate on first hit.
                counterData[(int)Counters.Type.BvhIntersectShape] += node.ShapeCount * activeCount;
                for (int i = 0; i != node.ShapeCount; ++i)
                {
                    int idx = _items[node.Child + i];
                    _shapes[idx].IntersectAny(rays, shapeHits);
                    for (int r = 0; r != rays.Length; ++r)
                    {
                        if (!hits[r] && shapeHits[r])
                        {
                            hits[r] = true;
                            --activeCount;
                        }
                    }
                    if (activeCount == 0)
                        break;
                }
            }
        }
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
        node.ShapeCount = _shapes.Length;
        for (int i = 0; i != _shapes.Length; ++i)
            node.Bounds.Encapsulate(_shapes[_items[i]].Bounds());
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
     * Pick a split plane using the Surface Area Heuristic (SAH) with binned evaluation.
     * Divides centroid space into _splitBinCount bins per axis, evaluates all _splitBinCount-1 split
     * positions on all 3 axes, and returns the one with the lowest SAH cost.
     * Returns null if keeping the node as a leaf is cheaper than any split.
     */
    private (int Axis, float Pos)? SplitPick(int nodeIdx)
    {
        ref readonly Node node = ref _nodes[nodeIdx];
        Debug.Assert(node.ShapeCount > 0); // Needs to be a leaf node.

        int shapeBegin = node.Child;
        int shapeCount = node.ShapeCount;

        AABox centroidBounds = CentroidBounds(shapeBegin, shapeCount);

        float parentSa = node.Bounds.SurfaceArea;
        float bestCost = SahLeaf(parentSa, shapeCount);
        int bestAxis = -1;
        float bestPos = 0f;

        Span<AABox> binBounds = stackalloc AABox[_splitBinCount];
        Span<int> binCount = stackalloc int[_splitBinCount];
        Span<float> leftSa = stackalloc float[_splitBinCount - 1];
        Span<int> leftCount = stackalloc int[_splitBinCount - 1];

        for (int axis = 0; axis != 3; ++axis)
        {
            float axisCenterMin = centroidBounds.Min[axis];
            float axisCenterMax = centroidBounds.Max[axis];
            if (axisCenterMax - axisCenterMin < 1e-6f)
                continue; // All centroids are equal on this axis; skip.

            // Assign each shape to a bin by its centroid position.
            for (int b = 0; b != _splitBinCount; ++b)
            {
                binBounds[b] = AABox.Inverted();
                binCount[b] = 0;
            }
            float scale = _splitBinCount / (axisCenterMax - axisCenterMin);
            for (int i = 0; i != shapeCount; ++i)
            {
                AABox shapeBounds = _shapes[_items[shapeBegin + i]].Bounds();
                int bin = Math.Min((int)((shapeBounds.Center[axis] - axisCenterMin) * scale), _splitBinCount - 1);
                binBounds[bin].Encapsulate(shapeBounds);
                ++binCount[bin];
            }

            // Left-to-right prefix sweep: accumulate bounds and counts for the left partition.
            AABox binLeftBounds = AABox.Inverted();
            int binLeftCount = 0;
            for (int b = 0; b != (_splitBinCount - 1); ++b)
            {
                binLeftBounds.Encapsulate(binBounds[b]);
                binLeftCount += binCount[b];
                leftSa[b] = binLeftBounds.IsInverted ? 0f : binLeftBounds.SurfaceArea;
                leftCount[b] = binLeftCount;
            }

            // Right-to-left suffix sweep: accumulate bounds/counts for the right partition.
            // Pick the best split based on the combined left and right SAH cost.
            AABox binRightBounds = AABox.Inverted();
            int binRightCount = 0;
            for (int b = _splitBinCount - 1; b != 0; --b)
            {
                binRightBounds.Encapsulate(binBounds[b]);
                binRightCount += binCount[b];
                float rightSa = binRightBounds.IsInverted ? 0f : binRightBounds.SurfaceArea;

                float cost = SahSplit(parentSa, leftSa[b - 1], leftCount[b - 1], rightSa, binRightCount);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestPos = axisCenterMin + b * (axisCenterMax - axisCenterMin) / _splitBinCount;
                }
            }
        }

        return bestAxis >= 0 ? (bestAxis, bestPos) : null;
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

        (int Axis, float Pos)? split = SplitPick(nodeIdx);
        if (split is null)
            return; // SAH says keeping this node as a leaf is cheaper than any split.

        int partitionIdx = Partition(nodeIdx, split.Value.Axis, split.Value.Pos);

        ref Node node = ref _nodes[nodeIdx];
        int countA = partitionIdx - node.Child;
        int countB = node.ShapeCount - countA;
        Debug.Assert(countA > 0 && countB > 0); // Guaranteed by SAH centroid-space binning.

        int childA = Insert(node.Child, countA);
        int childB = Insert(partitionIdx, countB);

        Debug.Assert(childB == childA + 1); // Children must be stored consecutively.
        node.Child = childA;
        node.ShapeCount = 0; // Node is no longer a leaf-node.

        Subdivide(childA, depth + 1);
        Subdivide(childB, depth + 1);
    }

    private AABox CentroidBounds(int shapeBegin, int shapeCount)
    {
        AABox bounds = AABox.Inverted();
        for (int i = 0; i != shapeCount; ++i)
        {
            Vec3 center = _shapes[_items[shapeBegin + i]].Bounds().Center;
            bounds.Encapsulate(center);
        }
        return bounds;
    }

    private float SahLeaf(float sa, int count) =>
        _sahCostIntersect * sa * count;

    private float SahSplit(float parentSa, float leftSa, int leftCount, float rightSa, int rightCount) =>
        _sahCostTraverse * parentSa + SahLeaf(leftSa, leftCount) + SahLeaf(rightSa, rightCount);
}
