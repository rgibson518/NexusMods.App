using BenchmarkDotNet.Attributes;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Sorting;
using NexusMods.Benchmarks.Interfaces;
using NexusMods.DataModel.Sorting;

namespace NexusMods.Benchmarks.Benchmarks;

[BenchmarkInfo("Sorting", "Tests how quickly it usually takes to sort the load order of X mods.")]
[MemoryDiagnoser]
public class Sorting : IBenchmark   
{
    private List<Item> _rules = null!;

    [Params(100, 1000, 2000, 5000, 10000)]
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public int NumItems { get; set; }

    [Params(Topology.Complex, Topology.Random, Topology.Chain, Topology.Star, Topology.Independent)]
    public Topology GraphType { get; set; }

    private readonly Sorter _sorter = new();
    private readonly KahnSorter _kahnSorter = new();

    [GlobalSetup]
    public void Setup()
    {
        var rules = GraphType switch
        {
            Topology.Complex => GenerateComplex(),
            Topology.Random => GenerateRandom(NumItems),
            Topology.Chain => GenerateChain(NumItems),
            Topology.Star => GenerateStar(NumItems),
            Topology.Independent => GenerateIndependent(NumItems),
            _ => throw new ArgumentOutOfRangeException()
        };

        _rules = Shuffle(rules).ToList();
    }

    private List<Item> GenerateComplex()
    {
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(e => e.ToString()).ToArray();
        var numbers = Enumerable.Range(0, NumItems).Select(e => e.ToString()).ToArray();

        var rules = new List<Item>();
        foreach (var (n, idx) in letters.Select((n, idx) => (n, idx)))
        {
            if (idx == 0)
            {
                rules.Add(new Item { Id = n, Rules = new() { new First<Item, string>() } });
            }
            else
            {
                rules.Add(new Item
                {
                    Id = n, Rules = new()
                    {
                        new First<Item, string>(),
                        new After<Item, string> { Other = letters.ElementAt(idx - 1) }
                    }
                });
            }
        }

        foreach (var (n, idx) in numbers.Select((n, idx) => (n, idx)))
        {
            if (idx == 0)
            {
                rules.Add(new Item { Id = n, Rules = new() });
            }
            else
            {
                rules.Add(new Item
                {
                    Id = n, Rules = new()
                    {
                        new After<Item, string> { Other = numbers.ElementAt(idx - 1) }
                    }
                });
            }
        }

        return rules;
    }

    private List<Item> GenerateRandom(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new Item { Id = i.ToString() }).ToList();
        var rnd = new Random(42);

        for (int i = 1; i < count; i++)
        {
            if (rnd.NextDouble() > 0.1)
            {
                items[i].Rules.Add(new After<Item, string> { Other = items[i - 1].Id });
            }

            if (rnd.NextDouble() < 0.05)
            {
                var target = rnd.Next(0, i);
                items[i].Rules.Add(new After<Item, string> { Other = items[target].Id });
            }
        }
        return items;
    }

    // A -> B -> C -> D -> ...
    private List<Item> GenerateChain(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new Item { Id = i.ToString() }).ToList();
        for (int i = 1; i < count; i++)
        {
            items[i].Rules.Add(new After<Item, string> { Other = items[i - 1].Id });
        }
        return items;
    }

    // A -> B, A -> C, A -> D, ...
    private List<Item> GenerateStar(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new Item { Id = i.ToString() }).ToList();
        for (int i = 1; i < count; i++)
        {
            if (i > 0)
                items[i].Rules.Add(new After<Item, string> { Other = "0" });
        }
        return items;
    }
    // A, B, C, D, ...
    private List<Item> GenerateIndependent(int count)
    {
        return Enumerable.Range(0, count).Select(i => new Item { Id = i.ToString() }).ToList();
    }

    [Benchmark(Baseline = true)]
    public Item[] Sort_Original()
    {
        return _sorter.Sort<Item, string>(_rules, x => x.Id, x => x.Rules).ToArray();
    }

    [Benchmark]
    public Item[] Sort_Kahn()
    {
        return _kahnSorter.Sort<Item, string>(_rules, x => x.Id, x => x.Rules).ToArray();
    }

    private IEnumerable<Item> Shuffle(List<Item> rules)
    {
        var random = new Random();
        var n = rules.Count;
        while (n > 1)
        {
            n--;
            var k = random.Next(n + 1);
            (rules[k], rules[n]) = (rules[n], rules[k]);
        }

        return rules;
    }

    public class Item : IHasEntityId<string>
    {
        public string Id { get; init; } = string.Empty;

        public List<ISortRule<Item, string>> Rules { get; init; } = new();
    }

    public enum Topology
    {
        Complex,
        Random,
        Chain,
        Star,
        Independent
    }
}
