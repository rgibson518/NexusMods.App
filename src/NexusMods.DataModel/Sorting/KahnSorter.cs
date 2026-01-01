using NexusMods.Abstractions.Loadouts.Sorting;

namespace NexusMods.DataModel.Sorting;

/// <summary>
/// A topological sorter using Kahn's algorithm with virtual nodes to handle implicit First/Last dependencies efficiently.
/// </summary>
public class KahnSorter : ISorter
{
    public IEnumerable<TItem> SortWithEnumerable<TItem, TId>(IEnumerable<TItem> items, Func<TItem, TId> idSelector, Func<TItem, IReadOnlyList<ISortRule<TItem, TId>>> ruleFn, IComparer<TId>? comparer = null) where TId : IEquatable<TId>
    {
        return Sort(items.ToList(), idSelector, ruleFn, comparer);
    }

    public IEnumerable<TItem> Sort<TItem, TId>(List<TItem> items, Func<TItem, TId> idSelector, Func<TItem, IReadOnlyList<ISortRule<TItem, TId>>> ruleFn, IComparer<TId>? comparer = null) where TId : IEquatable<TId>
    {
        return Sort<TItem, TId, List<TItem>>(items, idSelector, ruleFn, comparer);
    }

    public IEnumerable<TItem> Sort<TItem, TId, TCollection>(TCollection items, Func<TItem, TId> idSelector, Func<TItem, IReadOnlyList<ISortRule<TItem, TId>>> ruleFn, IComparer<TId>? comparer = null) where TId : IEquatable<TId> where TCollection : class, IReadOnlyList<TItem>
    {
        var count = items.Count;
        if (count == 0) return [];

        // 1. Map items to integers for O(1) array access
        var idToIndex = new Dictionary<TId, int>(count);
        var indexToItem = new TItem[count];
        
        // We will use virtual nodes for First and Last barriers.
        // Indices 0 to count-1 are real items.
        // Index count is Virtual_EndFirst.
        // Index count+1 is Virtual_StartLast.
        var virtualFirstIndex = count;
        var virtualLastIndex = count + 1;
        var totalNodes = count + 2;

        var adjacency = new List<int>[totalNodes];
        for (var i = 0; i < totalNodes; i++) adjacency[i] = new List<int>();
        
        var inDegree = new int[totalNodes];
        var isFirst = new bool[count];
        var isLast = new bool[count];
        var hasFirstItems = false;
        var hasLastItems = false;

        // 2. Build Index Map and Identify First/Last
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var id = idSelector(item);
            idToIndex[id] = i;
            indexToItem[i] = item;

            var rules = ruleFn(item);
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule is First<TItem, TId>)
                {
                    isFirst[i] = true;
                    hasFirstItems = true;
                }
                else if (rule is Last<TItem, TId>)
                {
                    isLast[i] = true;
                    hasLastItems = true;
                }
            }
        }

        // 3. Build Graph Edges
        for (var i = 0; i < count; i++)
        {
            var item = indexToItem[i];
            var rules = ruleFn(item);

            // Handle Virtual Nodes for First/Last
            if (hasFirstItems)
            {
                if (isFirst[i])
                {
                    // First -> VirtualFirst
                    AddEdge(i, virtualFirstIndex, adjacency, inDegree);
                }
                else
                {
                    // VirtualFirst -> NonFirst
                    AddEdge(virtualFirstIndex, i, adjacency, inDegree);
                }
            }

            if (hasLastItems)
            {
                if (isLast[i])
                {
                    // VirtualLast -> Last
                    AddEdge(virtualLastIndex, i, adjacency, inDegree);
                }
                else
                {
                    // NonLast -> VirtualLast
                    AddEdge(i, virtualLastIndex, adjacency, inDegree);
                }
            }

            // Handle Explicit Rules
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                switch (rule)
                {
                    case After<TItem, TId> after:
                        if (idToIndex.TryGetValue(after.Other, out var afterIdx))
                        {
                            // This item comes After 'Other' => Other -> This
                            AddEdge(afterIdx, i, adjacency, inDegree);
                        }
                        else
                        {
                            // Dependency is missing. This item cannot be sorted.
                            inDegree[i]++;
                        }
                        break;
                    case Before<TItem, TId> before:
                        if (idToIndex.TryGetValue(before.Other, out var beforeIdx))
                        {
                            // This item comes Before 'Other' => This -> Other
                            AddEdge(i, beforeIdx, adjacency, inDegree);
                        }
                        break;
                }
            }
        }

        // 4. Kahn's Algorithm
        // We use a PriorityQueue to ensure deterministic output if a comparer is provided,
        // or simply to process lower indices first for stability if no comparer.
        // However, standard Queue is O(1) push/pop, PQ is O(log N). 
        // For strict performance, Queue is better, but if we want to support the 'comparer' arg for tie-breaking:
        // The original Sorter supports 'comparer' to sort the superset.
        
        // Using a simple Queue for O(N) performance. 
        // If tie-breaking is needed, we would need a PriorityQueue.
        var queue = new Queue<int>();
        for (var i = 0; i < totalNodes; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var result = new List<TItem>(count);
        var processedCount = 0;

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            
            // If it's a real node, add to result
            if (u < count)
            {
                result.Add(indexToItem[u]);
            }
            
            processedCount++;

            var neighbors = adjacency[u];
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var n = 0; n < neighbors.Count; n++)
            {
                var v = neighbors[n];
                inDegree[v]--;
                if (inDegree[v] == 0)
                    queue.Enqueue(v);
            }
        }

        if (processedCount != totalNodes)
            throw new InvalidOperationException("Cyclic dependency detected");

        return result;
    }

    private static void AddEdge(int from, int to, List<int>[] adjacency, int[] inDegree)
    {
        adjacency[from].Add(to);
        inDegree[to]++;
    }
}