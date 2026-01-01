using System;
using System.Diagnostics;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Sorting;
using NexusMods.DataModel.Sorting;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Tests;

public class SortTests
{
    private readonly ITestOutputHelper _output;

    public SortTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CyclicDependency()
    {
        var items = new List<Item>()
        {
            new() { Id = "A", Rules = [new After<Item, string>() { Other = "B" }] },
            new() { Id = "B", Rules = [new After<Item, string>() { Other = "A" }] },
        };

        var act = () => new KahnSorter().Sort<Item, string>(items, x => x.Id, x => x.Rules).ToArray();

        act.Should().Throw<InvalidOperationException>().WithMessage("Cyclic dependency detected");
    }

    [Fact]
    public void MissingItem()
    {
        var items = new List<Item>()
        {
            new() { Id = "B", Rules = [
                new After<Item, string>() { Other = "A" },
                new Before<Item, string>() { Other = "C" },
            ]},
            new() { Id = "D", Rules = [new After<Item, string>() { Other = "B" }]},
            new() { Id = "E", Rules = [new After<Item, string>() { Other = "A" }]},
        };

        var act = () => new KahnSorter().Sort(items, x => x.Id, x => x.Rules).ToArray();

        // NOTE(erri120): This is completely misleading but that's the exception we currently get for missing items
        act.Should().Throw<InvalidOperationException>().WithMessage("Cyclic dependency detected");
    }

    [Fact]
    public void FirstItemsComeFirst()
    {
        var data = new List<Item>
        {
            new() {Id = "B", Rules = new()},
            new()
            {Id = "A", Rules = new()
            {
                new First<Item, string>()
            }},
        };

        new Sorter().Sort<Item, string>(data, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .Should().Equal("A", "B");
    }

    [Fact]
    public void FirstAndLast()
    {
        var data = new List<Item>
        {
            new() { Id = "B", Rules = [] },
            new() { Id = "C", Rules = [] },
            new() { Id = "D", Rules = [ new Last<Item, string>()] },
            new() { Id = "E", Rules = [] },
            new() { Id = "F", Rules = [] },
            new() { Id = "G", Rules = [] },
            new() { Id = "A", Rules = [ new First<Item, string>()] },
        };

        var res = new Sorter().Sort<Item, string>(data, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .ToArray();

        res.First().Should().Be("A");
        res.Last().Should().Be("D");
    }

    [Fact]
    public void BeforeAndAfterWorks()
    {
        var data = new List<Item>
        {
            new() {Id = "B", Rules = new()},
            new()
            {Id = "A", Rules = new()
            {
                new Before<Item, string> { Other = "B" }
            }},
            new()
            {Id = "C", Rules = new()
            {
                new After<Item, string> { Other = "B" }
            }}
        };

        new Sorter().Sort<Item, string>(data, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .Should().Equal("A", "B", "C");
    }

    [Fact]
    public void LargeComplexCollectionsCanBeSorted()
    {
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(e => e.ToString()).ToArray();
        var numbers = Enumerable.Range(0, 10000).Select(e => e.ToString()).ToArray();

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
                        new After<Item, string> { Other = letters.ElementAt(idx - 1)}
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
                        new After<Item, string> { Other = numbers.ElementAt(idx - 1)}
                    }
                });
            }
        }

        rules = Shuffle(rules).ToList();

        new Sorter().Sort<Item, string>(rules, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .Should().Equal(letters.Concat(numbers));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(50000)]
    public void ComparePerformance(int count)
    {
        // Warmup
        var warmupItems = GenerateData(10);
        new Sorter().Sort(warmupItems, x => x.Id, x => x.Rules).ToList();
        new KahnSorter().Sort(warmupItems, x => x.Id, x => x.Rules).ToList();

        var items = GenerateData(count);

        var sw = Stopwatch.StartNew();
        var sorterResult = new Sorter().Sort(items, x => x.Id, x => x.Rules).ToList();
        sw.Stop();
        var sorterTime = sw.ElapsedMilliseconds;

        sw.Restart();
        var kahnResult = new KahnSorter().Sort(items, x => x.Id, x => x.Rules).ToList();
        sw.Stop();
        var kahnTime = sw.ElapsedMilliseconds;

        _output.WriteLine($"Count: {count} | Sorter: {sorterTime}ms | KahnSorter: {kahnTime}ms");

        sorterResult.Should().HaveCount(count);
        kahnResult.Should().HaveCount(count);

        // Verify correctness
        VerifySort(sorterResult);
        VerifySort(kahnResult);
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

    private List<Item> GenerateData(int count)
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

        return Shuffle(items).ToList();
    }

    private void VerifySort(List<Item> sortedItems)
    {
        var indexMap = sortedItems.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);

        foreach (var item in sortedItems)
        {
            var myIndex = indexMap[item.Id];
            foreach (var rule in item.Rules)
            {
                if (rule is After<Item, string> after && indexMap.TryGetValue(after.Other, out var otherIndex))
                {
                    myIndex.Should().BeGreaterThan(otherIndex, $"{item.Id} must be after {after.Other}");
                }
            }
        }
    }


    public class Item : IHasEntityId<string>
    {
        public string Id { get; init; } = string.Empty;

        public List<ISortRule<Item, string>> Rules { get; set; } = new();
    }
}
