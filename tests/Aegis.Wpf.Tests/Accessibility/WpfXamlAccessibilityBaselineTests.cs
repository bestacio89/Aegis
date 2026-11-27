using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests.Accessibility;

public sealed class WpfXamlAccessibilityBaselineTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ApplicationXaml_MatchesTheApprovedAccessibilityBaseline()
    {
        var files = Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "ApplicationXaml"),
                "*.xaml",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        files.Should().HaveCount(20, "the baseline covers every application XAML file present at W0");

        var documents = files
            .Select(path => new XamlDocument(path, XDocument.Load(path)))
            .ToArray();

        var hardCodedColors = documents
            .Where(document => !document.RelativePath.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(document => Regex.Matches(File.ReadAllText(document.Path), "#[0-9A-Fa-f]{3,8}\\b").Count);

        var smallFontDeclarations = documents.Sum(document =>
            document.Document.Descendants()
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName == "FontSize")
                    .Select(attribute => attribute.Value)
                    .Concat(element.Name.LocalName == "Setter"
                        && string.Equals((string?)element.Attribute("Property"), "FontSize", StringComparison.Ordinal)
                            ? [(string?)element.Attribute("Value") ?? string.Empty]
                            : []))
                .Count(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                    && size <= 11));

        var fixedLargeDimensions = documents.Sum(document =>
            document.Document.Descendants()
                .SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName is "Width" or "Height")
                    .Select(attribute => attribute.Value)
                    .Concat(element.Name.LocalName == "Setter"
                        && ((string?)element.Attribute("Property") is "Width" or "Height")
                            ? [(string?)element.Attribute("Value") ?? string.Empty]
                            : []))
                .Count(value => Regex.IsMatch(value, @"^\d{3,4}(?:\.0+)?$", RegexOptions.CultureInvariant)));

        var automationMetadata = documents.Sum(document =>
            document.Document.Descendants()
                .SelectMany(element => element.Attributes())
                .Count(attribute => attribute.Name.LocalName.StartsWith("AutomationProperties.", StringComparison.Ordinal)));

        hardCodedColors.Should().Be(0, "all application colors must be defined by the shared theme token dictionary");
        smallFontDeclarations.Should().BeLessThanOrEqualTo(
            89,
            "W1 establishes a minimum 12-DIP type scale for the 19 declarations in its approved production scope");
        fixedLargeDimensions.Should().BeLessThanOrEqualTo(96, "fixed large dimensions must not spread while responsive layout is addressed");
        automationMetadata.Should().BeGreaterThanOrEqualTo(23, "existing SystemLandscape automation metadata must not be removed");
    }

    [Fact]
    public void FocusVisualSuppression_IsLimitedToTheTwoKnownStylesAwaitingW1()
    {
        var files = Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "ApplicationXaml"),
            "*.xaml",
            SearchOption.AllDirectories);

        var suppressedStyles = files
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "Style"
                    && element.Elements().Any(child =>
                        child.Name.LocalName == "Setter"
                        && string.Equals((string?)child.Attribute("Property"), "FocusVisualStyle", StringComparison.Ordinal)
                        && string.Equals((string?)child.Attribute("Value"), "{x:Null}", StringComparison.Ordinal)))
                .Select(style => (string?)style.Attribute(XamlNamespace + "Key") ?? string.Empty))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        suppressedStyles.Should().Equal(
            new[]
            {
                "ArchitectureSectionItem",
                "ArchitectureViewItem"
            },
            "W0 records the two known focus-style suppressions; W2 must replace them with visible focus treatment");
    }

    private sealed record XamlDocument(string Path, XDocument Document)
    {
        public string RelativePath => Path[(Path.IndexOf("ApplicationXaml", StringComparison.Ordinal)
            + "ApplicationXaml".Length)..];
    }
}
