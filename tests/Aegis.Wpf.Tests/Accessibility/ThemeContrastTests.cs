using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests.Accessibility;

public sealed class ThemeContrastTests
{
    private static readonly string[] SurfaceKeys =
    [
        "Brush.Ink",
        "Brush.Panel",
        "Brush.PanelElevated"
    ];

    [Fact]
    public void TextAndNonCriticalStatusTokens_MeetWcagAaForNormalText()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();
            var foregroundKeys = new[]
            {
                "Brush.TextPrimary",
                "Brush.TextMuted",
                "Brush.StatusBlocker",
                "Brush.StatusWarning",
                "Brush.StatusCompliant"
            };

            foreach (var foregroundKey in foregroundKeys)
            {
                foreach (var surfaceKey in SurfaceKeys)
                {
                    var ratio = ContrastRatio(GetColor(theme, foregroundKey), GetColor(theme, surfaceKey));

                    ratio.Should().BeGreaterThanOrEqualTo(
                        4.5,
                        $"{foregroundKey} must remain readable on {surfaceKey}");
                }
            }
        });
    }

    [Fact]
    public void CriticalStatusOnElevatedPanel_RemainsAnExplicitKnownContrastGap()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();
            var ratio = ContrastRatio(
                GetColor(theme, "Brush.StatusCritical"),
                GetColor(theme, "Brush.PanelElevated"));

            ratio.Should().BeLessThan(
                4.5,
                "W0 records the existing critical-status contrast gap for correction in a later production checkpoint");
            ratio.Should().BeApproximately(4.29, 0.02);
        });
    }

    [Fact]
    public void LineToken_IsDecorativeAndMustNotBeUsedAsTheOnlyControlBoundary()
    {
        RunOnSta(() =>
        {
            var theme = LoadTheme();

            foreach (var surfaceKey in SurfaceKeys)
            {
                var ratio = ContrastRatio(GetColor(theme, "Brush.Line"), GetColor(theme, surfaceKey));
                ratio.Should().BeLessThan(
                    3.0,
                    "the existing line token is suitable for decorative separators, not sole control boundaries");
            }

            theme.Contains("Brush.ControlBorder").Should().BeFalse(
                "a dedicated control-boundary token is a tracked accessibility gap");
            theme.Contains("Brush.FocusRing").Should().BeFalse(
                "a dedicated focus token is a tracked accessibility gap");
        });
    }

    private static ResourceDictionary LoadTheme()
    {
        _ = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        return new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Aegis.Wpf;component/Themes/Aegistheme.xaml",
                UriKind.Absolute)
        };
    }

    private static Color GetColor(ResourceDictionary dictionary, string key) =>
        dictionary[key] is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"Theme resource '{key}' is not a SolidColorBrush.");

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R)
            + 0.7152 * Linearize(color.G)
            + 0.0722 * Linearize(color.B);
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
