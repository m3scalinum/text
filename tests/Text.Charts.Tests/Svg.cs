using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>Reads the SVG a test rendered, so an assertion talks about elements and numbers.</summary>
/// <remarks>
/// <see cref="Parse"/> is itself the well-formedness check: a document that does not parse fails the
/// test that called it.
/// </remarks>
internal static class Svg
{
    internal static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    internal static XDocument Parse(string svg)
    {
        var document = XDocument.Parse(svg);

        Assert.NotNull(document.Root);
        Assert.Equal(Ns, document.Root!.Name.Namespace);

        return document;
    }

    internal static IReadOnlyList<XElement> Of(XDocument document, string name)
        => document.Descendants(Ns + name).ToArray();

    internal static IReadOnlyList<XElement> Paths(XDocument document) => Of(document, "path");

    internal static string Attribute(XElement element, string name)
    {
        var attribute = element.Attribute(name);

        Assert.True(attribute is not null, $"the <{element.Name.LocalName}> has no '{name}'");

        return attribute!.Value;
    }

    internal static double Number(XElement element, string name)
        => double.Parse(Attribute(element, name), CultureInfo.InvariantCulture);

    /// <summary>The labels of one axis: <c>middle</c> for the x ticks, <c>end</c> for the y ticks.</summary>
    internal static IReadOnlyList<string> Labels(XDocument document, string anchor)
        => document.Descendants(Ns + "text")
            .Where(element => (string?)element.Attribute("text-anchor") == anchor
                && (string?)element.Attribute("font-size") == "11")
            .Select(element => element.Value)
            .ToArray();

    /// <summary>The numbers of a path or a points attribute, in the order they were written.</summary>
    internal static IReadOnlyList<double> Numbers(string data)
        => data.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : (double?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
}
