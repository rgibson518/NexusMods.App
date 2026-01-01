using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Sorting;
using NexusMods.DataModel.Sorting;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Tests;

public class KahnSortTests
{
    private readonly ITestOutputHelper _output;

    public KahnSortTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CyclicDependency_Throws()
    {
        var items = new List<Item>()
        {
            new() { Id = "A", Rules = [new After<Item, string>() { Other = "B" }] },
            new() { Id = "B", Rules = [new After<Item, string>() { Other = "A" }] },
        };

        var act = () => new KahnSorter().Sort(items, x => x.Id, x => x.Rules).ToArray();

        act.Should().Throw<InvalidOperationException>().WithMessage("Cyclic dependency detected");
    }

    [Fact]
    public void MissingDependency_Throws()
    {
        var items = new List<Item>()
        {
            new() { Id = "B", Rules = [new After<Item, string> { Other = "A" }] },
            new() { Id = "C", Rules = [new Before<Item, string> { Other = "D" }] },
        };

        var act = () => new KahnSorter().Sort(items, x => x.Id, x => x.Rules).ToArray();

        act.Should().Throw<InvalidOperationException>().WithMessage("Cyclic dependency detected");
    }

    [Fact]
    public void FirstAndLastRules_OrderCorrectly()
    {
        var items = new List<Item>
        {
            new() { Id = "C", Rules = [] },
            new() { Id = "D", Rules = [new Last<Item, string>()] },
            new() { Id = "A", Rules = [new First<Item, string>()] },
            new() { Id = "E", Rules = [new Last<Item, string>()] },
            new() { Id = "B", Rules = [new First<Item, string>()] },
            new() { Id = "F", Rules = [] },
        };

        var result = new KahnSorter().Sort(items, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .ToArray();

        result.Should().Equal("A", "B", "C", "F", "D", "E");
    }

    [Fact]
    public void ExplicitDependencies_OrderCorrectly()
    {
        var items = new List<Item>
        {
            new() { Id = "B", Rules = [new After<Item, string> { Other = "A" }, new Before<Item, string> { Other = "C" }] },
            new() { Id = "A", Rules = [new Before<Item, string> { Other = "B" }] },
            new() { Id = "C", Rules = [new After<Item, string> { Other = "B" }] },
            new() { Id = "D", Rules = [new Before<Item, string> { Other = "B" }] },
            new() { Id = "E", Rules = [] },
        };

        var result = new KahnSorter().Sort(items, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .ToArray();

        result.Should().Equal("A", "D", "E", "B", "C");
    }

    [Fact]
    public void EdgeCases_HandleEmptyAndSingle()
    {
        var sorter = new KahnSorter();
        
        sorter.Sort(new List<Item>(), x => x.Id, x => x.Rules).Should().BeEmpty();
        
        var single = new List<Item> { new() { Id = "A" } };
        sorter.SortWithEnumerable(single, x => x.Id, x => x.Rules).Select(x => x.Id).Should().Equal("A");
    }

    [Fact]
    public void LargeComplexCollectionsCanBeSorted()
    {
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(e => e.ToString()).ToArray();
        var numbers = Enumerable.Range(0, 10000).Select(e => e.ToString()).ToArray();

        var rules = new List<Item>();
        foreach (var (n, idx) in letters.Select((n, idx) => (n, idx)))
        {
            var r = new List<ISortRule<Item, string>> { new First<Item, string>() };
            if (idx > 0)
                r.Add(new After<Item, string> { Other = letters[idx - 1] });
            
            rules.Add(new Item { Id = n, Rules = r });
        }

        foreach (var (n, idx) in numbers.Select((n, idx) => (n, idx)))
        {
            var r = new List<ISortRule<Item, string>>();
            if (idx > 0)
                r.Add(new After<Item, string> { Other = numbers[idx - 1] });
            
            rules.Add(new Item { Id = n, Rules = r });
        }

        rules = Shuffle(rules).ToList();

        new KahnSorter().Sort<Item, string>(rules, x => x.Id, x => x.Rules)
            .Select(i => i.Id)
            .Should().Equal(letters.Concat(numbers));
    }

    private IEnumerable<Item> Shuffle(List<Item> rules)
    {
        var random = new Random(42);
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

        public List<ISortRule<Item, string>> Rules { get; set; } = new();
    }
}
