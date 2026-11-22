using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The list of symbologies the package publishes and the table the specification prints are one
/// list, and this test is what keeps them one list.
/// </summary>
/// <remarks>
/// <para>
/// A symbology is a promise in three places at once: the member on <see cref="BarcodeSymbology"/>,
/// its ABI name in the native library, and the row of <c>docs/specifications/20-barcodes.md</c>
/// §8.1. Adding the member without the row documents a symbology nobody can find; adding the row
/// without the member documents one that does not exist. Enumerating both sides catches either
/// direction, and it catches the next one added, not only the state of to-day.
/// </para>
/// <para>
/// The §8.1 table has a Write and a Read column. Read is implied by membership — every row is
/// readable, which is the premise of the list — and Write is checked against
/// <see cref="Barcode.CanEncode"/> in <see cref="The_write_column_is_what_this_build_can_write"/>,
/// which needs the native library.
/// </para>
/// </remarks>
public sealed class SymbologyCoverageTests
{
    private static readonly Regex Row = new(
        @"^\|\s*(?<title>[^|]+?)\s*\|\s*`(?<name>[A-Za-z0-9-]+)`\s*\|\s*(?<write>[^\s|]+)\s*\|\s*(?<read>[^\s|]+)\s*\|",
        RegexOptions.CultureInvariant);

    [Fact]
    public void The_public_enum_and_the_specification_table_hold_the_same_symbologies()
    {
        var documented = SpecTable();

        var declared = Enum.GetValues<BarcodeSymbology>()
            .Select(symbology => symbology.ToAbiName())
            .ToHashSet(StringComparer.Ordinal);

        var missingFromTheEnum = documented.Keys.Except(declared, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var missingFromTheSpec = declared.Except(documented.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missingFromTheEnum.Count == 0,
            $"the specification's §8.1 table documents symbologies the enum does not declare: "
            + string.Join(", ", missingFromTheEnum));
        Assert.True(
            missingFromTheSpec.Count == 0,
            $"the enum declares symbologies the specification's §8.1 table does not document: "
            + string.Join(", ", missingFromTheSpec));
    }

    [Fact]
    public void Every_documented_symbology_says_it_can_be_read()
    {
        foreach (var (name, row) in SpecTable())
        {
            Assert.True(
                row.Read,
                $"§8.1 documents {name} as not readable, but every row of that table is a symbology "
                + "the decoder returns; a format that can only be written would need a column of its own");
        }
    }

    [Fact]
    public void The_write_column_is_what_this_build_can_write()
    {
        foreach (var (name, row) in SpecTable())
        {
            var symbology = BarcodeSymbologyNames.FromAbiName(name);
            Assert.NotNull(symbology);

            Assert.True(
                Barcode.CanEncode(symbology.Value) == row.Write,
                $"§8.1 says {name} is {(row.Write ? "writable" : "read only")}, and this build says "
                + $"{(Barcode.CanEncode(symbology.Value) ? "writable" : "read only")}");
        }
    }

    /// <summary>Reads the §8.1 table: ABI name to its Write and Read cells.</summary>
    private static Dictionary<string, (bool Write, bool Read)> SpecTable()
    {
        var rows = new Dictionary<string, (bool Write, bool Read)>(StringComparer.Ordinal);
        var inTable = false;

        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "docs", "specifications", "20-barcodes.md")))
        {
            if (line.StartsWith("| Symbology | ABI name", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (!inTable)
            {
                continue;
            }

            var match = Row.Match(line);

            if (!match.Success)
            {
                if (line.StartsWith("| ---", StringComparison.Ordinal)
                    || line.StartsWith("|---", StringComparison.Ordinal))
                {
                    // The header's separator row.
                    continue;
                }

                // The table ends at the first line that is not a row: §8.1 is followed by prose.
                break;
            }

            rows[match.Groups["name"].Value] = (
                Write: match.Groups["write"].Value == "✓",
                Read: match.Groups["read"].Value == "✓");
        }

        Assert.True(rows.Count > 0, "the §8.1 symbology table was not found in 20-barcodes.md");
        return rows;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "specifications")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find docs/specifications above {AppContext.BaseDirectory}");
    }
}
