using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class ArchitectureWorkspaceViewModelTests
{
    [Fact]
    public void Constructor_GroupsEveryExistingViewIntoFivePrimarySections()
    {
        var workspace = CreateWorkspace();

        workspace.Sections.Should().HaveCount(5);
        workspace.Sections.Select(section => section.Name)
            .Should().Equal("System", "Assessment", "Dependencies", "Governance", "Reports");

        workspace.Tabs.Select(tab => tab.Name)
            .Should().Equal(
                "System Landscape",
                "Overview",
                "Dependency Graph",
                "Relationship Ledger",
                "Sections",
                "Layers",
                "Rules & Metrics",
                "Findings",
                "Reports");

        workspace.Sections.Sum(section => section.Tabs.Count)
            .Should().Be(workspace.Tabs.Count);
    }

    [Fact]
    public void Constructor_SelectsSystemLandscapeAsTheInitialDestination()
    {
        var workspace = CreateWorkspace();

        workspace.SelectedSection.Should().BeSameAs(workspace.Sections[0]);
        workspace.SelectedTab.Should().BeSameAs(workspace.Sections[0].Tabs[0]);
        workspace.ActiveContent.Should().BeSameAs(workspace.SelectedTab!.ViewModel);
        workspace.SectionViewCount.Should().Be(1);
    }

    [Fact]
    public void SelectTab_SelectsOwningSectionAndUpdatesActiveContent()
    {
        var workspace = CreateWorkspace();

        workspace.SelectTab("Relationship Ledger");

        workspace.SelectedSection!.Name.Should().Be("Dependencies");
        workspace.SelectedTab!.Name.Should().Be("Relationship Ledger");
        workspace.ActiveContent.Should().BeSameAs(workspace.SelectedTab.ViewModel);
        workspace.SectionViewCount.Should().Be(2);
    }

    [Fact]
    public void SelectTab_PreservesEachSectionsLastSelectedView()
    {
        var workspace = CreateWorkspace();

        workspace.SelectTab("Relationship Ledger");
        workspace.SelectTab("Findings");
        workspace.SelectedTab!.Name.Should().Be("Findings");

        workspace.SelectedSection = workspace.Sections.Single(section => section.Name == "Dependencies");

        workspace.SelectedTab!.Name.Should().Be("Relationship Ledger");
    }

    [Theory]
    [InlineData("Rules", "Rules & Metrics")]
    [InlineData("Rules & Findings", "Rules & Metrics")]
    [InlineData("rules & metrics", "Rules & Metrics")]
    public void SelectTab_ResolvesHistoricalAliases(string requestedName, string expectedName)
    {
        var workspace = CreateWorkspace();

        workspace.SelectTab(requestedName);

        workspace.SelectedTab!.Name.Should().Be(expectedName);
        workspace.SelectedSection!.Name.Should().Be("Governance");
    }

    [Fact]
    public void SelectTab_IgnoresUnknownOrBlankNamesWithoutChangingSelection()
    {
        var workspace = CreateWorkspace();
        var initialSection = workspace.SelectedSection;
        var initialTab = workspace.SelectedTab;
        var initialContent = workspace.ActiveContent;

        workspace.SelectTab("does not exist");
        workspace.SelectTab(" ");
        workspace.SelectTab(null!);

        workspace.SelectedSection.Should().BeSameAs(initialSection);
        workspace.SelectedTab.Should().BeSameAs(initialTab);
        workspace.ActiveContent.Should().BeSameAs(initialContent);
    }

    [Fact]
    public void SelectedTab_RejectsViewsOutsideTheSelectedSection()
    {
        var workspace = CreateWorkspace();
        var currentTab = workspace.SelectedTab;

        workspace.SelectedTab = workspace.Tabs.Single(tab => tab.Name == "Reports");

        workspace.SelectedTab.Should().BeSameAs(currentTab);
        workspace.ActiveContent.Should().BeSameAs(currentTab!.ViewModel);
    }

    [Fact]
    public void SelectedSection_RejectsSectionsNotOwnedByTheWorkspace()
    {
        var workspace = CreateWorkspace();
        var currentSection = workspace.SelectedSection;

        var foreignSection = new ArchitectureWorkspaceSection(
            "Foreign",
            "!",
            "Not part of this workspace.",
            [new ArchitectureTab("Foreign View", "!", new object())]);

        workspace.SelectedSection = foreignSection;

        workspace.SelectedSection.Should().BeSameAs(currentSection);
    }

    private static ArchitectureWorkspaceViewModel CreateWorkspace() =>
        new(
            new SystemLandscapeViewModel(),
            new ArchitectureOverviewViewModel(),
            new DependencyGraphViewModel(),
            new RelationshipLedgerViewModel(),
            new SectionDashboardViewModel(),
            new LayerDashboardViewModel(),
            new RuleDashboardViewModel(),
            new FindingsLedgerViewModel(),
            new ReportVisualizationViewModel());
}
