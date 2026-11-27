using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Aegis.Wpf.ViewModels.Architecture;
using Aegis.Wpf.Views.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests.Accessibility;

public sealed class SystemLandscapeAutomationTests
{
    [Fact]
    public void InteractiveSystemLandscapeControls_ExposeNonEmptyAutomationNames()
    {
        RunOnSta(() =>
        {
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            application.Resources.MergedDictionaries.Add(LoadTheme());

            var viewModel = new SystemLandscapeViewModel();
            viewModel.TopologyNodes.Add(new SystemLandscapeTopologyNodeViewModel
            {
                Id = "w0-test-node",
                Name = "Payments",
                Category = "Logical region",
                Detail = "Representative topology node used to exercise the node template."
            });

            var view = new SystemLandscapeView
            {
                DataContext = viewModel
            };

            view.Measure(new Size(1280, 900));
            view.Arrange(new Rect(0, 0, 1280, 900));
            view.UpdateLayout();

            var interactiveControls = GetVisualDescendants(view)
                .OfType<Control>()
                .Where(control => control is Button or ComboBox or ListBox)
                .ToArray();

            interactiveControls.Should().NotBeEmpty(
                "the view must realize its authored buttons, filters, lists, and representative topology node");

            foreach (var control in interactiveControls)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                peer.Should().NotBeNull(
                    $"the {control.GetType().Name} control must expose a WPF automation peer");

                peer!.GetName().Should().NotBeNullOrWhiteSpace(
                    $"the authored {control.GetType().Name} control must expose an accessible name");
            }
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
