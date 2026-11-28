using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Aegis.Wpf.Views.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests.Accessibility;

public sealed class ArchitectureNavigationAccessibilityTests
{
    [Fact]
    public void ArchitectureViewSelector_ExposesAnAccessibleNativeControl()
    {
        RunOnSta(() =>
        {
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            if (!application.Resources.MergedDictionaries.Any(dictionary =>
                    dictionary.Source?.OriginalString.Contains("Aegistheme.xaml", StringComparison.Ordinal) == true))
            {
                application.Resources.MergedDictionaries.Add(LoadTheme());
            }

            var view = new ArchitectureWorkspaceView();

            view.Measure(new Size(1280, 900));
            view.Arrange(new Rect(0, 0, 1280, 900));
            view.UpdateLayout();

            var selector = GetVisualDescendants(view)
                .OfType<ComboBox>()
                .Should()
                .ContainSingle("the architecture workspace must expose one secondary view selector")
                .Which;

            selector.MinHeight.Should().BeGreaterThanOrEqualTo(38);
            selector.IsEditable.Should().BeFalse();
            selector.IsTextSearchEnabled.Should().BeTrue();
            selector.IsTabStop.Should().BeTrue();
            selector.ItemContainerStyle.Should().NotBeNull();

            AutomationProperties.GetName(selector).Should().Be("Architecture view");
            AutomationProperties.GetHelpText(selector).Should().NotBeNullOrWhiteSpace();

            var automationPeer = UIElementAutomationPeer.CreatePeerForElement(selector);
            automationPeer.Should().NotBeNull();
            automationPeer!.GetName().Should().Be("Architecture view");

            var label = GetVisualDescendants(view)
                .OfType<Label>()
                .Should()
                .ContainSingle("the selector must have one explicit visible label")
                .Which;

            label.Content.Should().Be("View");
            label.Target.Should().BeSameAs(selector);

            var itemsSourceBinding = BindingOperations.GetBinding(
                selector,
                ItemsControl.ItemsSourceProperty);
            itemsSourceBinding.Should().NotBeNull();
            itemsSourceBinding!.Path.Path.Should().Be("SelectedSection.Tabs");

            var selectedItemBinding = BindingOperations.GetBinding(
                selector,
                Selector.SelectedItemProperty);
            selectedItemBinding.Should().NotBeNull();
            selectedItemBinding!.Path.Path.Should().Be("SelectedTab");
            selectedItemBinding.Mode.Should().Be(BindingMode.TwoWay);
        });
    }

    private static ResourceDictionary LoadTheme() =>
        new()
        {
            Source = new Uri(
                "pack://application:,,,/Aegis.Wpf;component/Themes/Aegistheme.xaml",
                UriKind.Absolute)
        };

    private static IEnumerable<DependencyObject> GetVisualDescendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);

        while (pending.TryPop(out var current))
        {
            yield return current;

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = childCount - 1; index >= 0; index--)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
