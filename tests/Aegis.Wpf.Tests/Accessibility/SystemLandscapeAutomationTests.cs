using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
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

            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri("/Aegis.Wpf;component/Themes/Aegistheme.xaml", UriKind.Relative)
                });

            var view = new SystemLandscapeView
            {
                DataContext = new SystemLandscapeViewModel()
            };

            view.Measure(new Size(1280, 900));
            view.Arrange(new Rect(0, 0, 1280, 900));
            view.UpdateLayout();

            var rootPeer = UIElementAutomationPeer.CreatePeerForElement(view);
            rootPeer.Should().NotBeNull("the view must expose a WPF automation peer");

            var interactivePeers = GetDescendants(rootPeer!)
                .Where(peer => peer.GetAutomationControlType() is
                    AutomationControlType.Button
                    or AutomationControlType.ComboBox
                    or AutomationControlType.List)
                .ToArray();

            interactivePeers.Should().NotBeEmpty("the representative view must expose its interactive controls");

            foreach (var peer in interactivePeers)
            {
                peer.GetName().Should().NotBeNullOrWhiteSpace(
                    $"the {peer.GetAutomationControlType()} control must expose an accessible name");
            }
        });
    }

    private static IEnumerable<AutomationPeer> GetDescendants(AutomationPeer root)
    {
        var visited = new HashSet<AutomationPeer>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<AutomationPeer>();
        pending.Push(root);

        while (pending.TryPop(out var peer))
        {
            if (!visited.Add(peer))
            {
                continue;
            }

            yield return peer;

            var children = peer.GetChildren();
            if (children is null)
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
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
