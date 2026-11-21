using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The coverage matrix in <c>docs/specifications/11-reference-coverage.md</c> is a claim. This test
/// makes it a checked fact: the inventory, the totals and the documented table must agree.
/// </summary>
public sealed class ReferenceInventoryTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "M3scalinum.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("could not find the repository root from " + AppContext.BaseDirectory);
    }

    private static JsonDocument LoadInventory()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "reference-inventory.json")));

    private static List<(string Category, string Name, string Level)> Items()
    {
        using var document = LoadInventory();
        var items = new List<(string, string, string)>();

        foreach (var category in document.RootElement.GetProperty("categories").EnumerateArray())
        {
            var name = category.GetProperty("name").GetString()!;

            foreach (var item in category.GetProperty("items").EnumerateArray())
            {
                items.Add((name, item.GetProperty("name").GetString()!, item.GetProperty("level").GetString()!));
            }
        }

        return items;
    }

    [Fact]
    public void The_inventory_holds_151_items()
    {
        Assert.Equal(151, Items().Count);
    }

    [Fact]
    public void The_levels_add_up()
    {
        var items = Items();

        Assert.Equal(145, items.Count(item => item.Level == "L1"));
        Assert.Equal(6, items.Count(item => item.Level == "L2"));
        Assert.Equal(0, items.Count(item => item.Level == "L3"));
        Assert.Equal(0, items.Count(item => item.Level == "L4"));
    }

    [Fact]
    public void No_item_is_left_without_a_typed_surface()
    {
        var untyped = Items()
            .Where(item => item.Level is "L3" or "L4")
            .Select(item => $"{item.Category}/{item.Name}")
            .ToArray();

        Assert.Empty(untyped);
    }

    [Fact]
    public void No_item_is_listed_twice_within_its_category()
    {
        // `attach` exists in both Math and PDF: the same name in two modules is legitimate.
        var duplicates = Items()
            .GroupBy(item => (item.Category, item.Name))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Category}/{group.Key.Name}")
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void The_documented_totals_match_the_inventory()
    {
        var markdown = File.ReadAllText(
            Path.Combine(Root, "docs", "specifications", "11-reference-coverage.md"));

        var match = Regex.Match(
            markdown,
            @"\|\s*\*\*Total\*\*\s*\|\s*\*\*(\d+)\*\*\s*\|\s*\*\*(\d+)\*\*\s*\|\s*\*\*(\d+)\*\*\s*\|\s*\*\*(\d+)\*\*\s*\|\s*\*\*(\d+)\*\*");

        Assert.True(match.Success, "the totals row is missing from 11-reference-coverage.md");

        var items = Items();

        Assert.Equal(int.Parse(match.Groups[1].Value), items.Count);
        Assert.Equal(int.Parse(match.Groups[2].Value), items.Count(item => item.Level == "L1"));
        Assert.Equal(int.Parse(match.Groups[3].Value), items.Count(item => item.Level == "L2"));
        Assert.Equal(int.Parse(match.Groups[4].Value), items.Count(item => item.Level == "L3"));
        Assert.Equal(int.Parse(match.Groups[5].Value), items.Count(item => item.Level == "L4"));
    }

    [Fact]
    public void Every_category_in_the_inventory_appears_in_the_document()
    {
        using var document = LoadInventory();
        var markdown = File.ReadAllText(
            Path.Combine(Root, "docs", "specifications", "11-reference-coverage.md"));

        foreach (var category in document.RootElement.GetProperty("categories").EnumerateArray())
        {
            var name = category.GetProperty("name").GetString()!;
            var count = category.GetProperty("items").GetArrayLength();

            Assert.Contains($"### 4.", markdown, StringComparison.Ordinal);
            Assert.Contains($"{name} ({count})", markdown, StringComparison.Ordinal);
        }
    }
}
