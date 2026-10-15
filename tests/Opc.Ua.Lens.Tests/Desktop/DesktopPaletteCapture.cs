/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
 * IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using NUnit.Framework;
using Opc.Ua;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.Tests.Themes;
using UaLens.Themes;
using UaLens.Views;

namespace UaLens.Tests.Desktop
{
    /// <summary>
    /// Adds bounded UX06 evidence to an existing native dialog test without adding desktop test identities.
    /// Only synthetic samples and a temporary self-signed public certificate appear in captures.
    /// </summary>
    internal static class DesktopPaletteCapture
    {
        internal static async Task CaptureAsync(DesktopWindowScope scope, Window errorWindow, TextBlock error)
        {
            string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
            string scale = scope.Window.RenderScaling.ToString("0.##", CultureInfo.InvariantCulture);
            string directory = Path.GetFullPath(Path.Combine("TestResults", "lens-desktop", "ux06", $"{platform}-{scale}"));
            Directory.CreateDirectory(directory);
            foreach (ThemePreset theme in s_themes)
            {
                foreach (string state in s_states)
                {
                    File.Delete(Path.Combine(directory, $"{theme}-{state}.png"));
                }
            }
            string contrastPath = Path.Combine(directory, "contrast.csv");
            string separationPath = Path.Combine(directory, "series-separation.csv");
            File.Delete(contrastPath);
            File.Delete(separationPath);
            var contrasts = new StringBuilder(
                "theme,model,kind,identity,foreground,surface,background,ratio,reference_min\n");
            var separations = new StringBuilder("theme,model,slot_a,slot_b,linear_rgb_distance\n");
            ThemePreset originalTheme = ThemeManager.Current;
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=UaLens palette fixture", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            try
            {
                foreach (ThemePreset theme in s_themes)
                {
                    await ThemeManager.SetThemeAsync(theme, scope.Appearance).ConfigureAwait(true);
                    await DesktopWindowScope.FrameAsync(errorWindow).ConfigureAwait(true);
                    Assert.That(error.Text, Is.Not.Null.And.Not.Empty);
                    Color errorColor = ResourceColor("ErrorText");
                    Assert.That((error.Foreground as ISolidColorBrush)?.Color, Is.EqualTo(errorColor));
                    await DesktopCapture.SaveAsync(errorWindow, Path.Combine(directory, $"{theme}-error.png"),
                        [errorColor], error).ConfigureAwait(true);
                    var warning = new CertificateTrustDialog(
                        certificate, new ServiceResult(StatusCodes.BadCertificateUntrusted));
                    Task<TrustChoice> modal = warning.ShowDialog<TrustChoice>(errorWindow);
                    try
                    {
                        await DesktopWindowScope.FrameAsync(warning).ConfigureAwait(true);
                        TextBlock label = warning.FindControl<TextBlock>("ErrorLabel")!;
                        Color warningColor = ResourceColor("WarningText");
                        Assert.That((label.Foreground as ISolidColorBrush)?.Color, Is.EqualTo(warningColor));
                        Assert.That(label.Text, Does.Contain("failed validation"));
                        await DesktopCapture.SaveAsync(warning, Path.Combine(directory, $"{theme}-warning.png"),
                            [warningColor], label).ConfigureAwait(true);
                        DesktopWindowScope.Click(warning.FindControl<Button>("RejectButton")!);
                        Assert.That(await modal.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true),
                            Is.EqualTo(TrustChoice.Reject));
                    }
                    finally
                    {
                        warning.Close();
                    }
                }
                await CaptureSeriesAsync(scope, errorWindow, directory, contrasts, separations).ConfigureAwait(true);
                await File.WriteAllTextAsync(contrastPath, contrasts.ToString()).ConfigureAwait(true);
                await File.WriteAllTextAsync(separationPath, separations.ToString()).ConfigureAwait(true);
                TestContext.AddTestAttachment(contrastPath,
                    "Source-color contrast and severity-1 CVD simulation diagnostics.");
                TestContext.AddTestAttachment(separationPath,
                    "All 66 series pairs per theme/model; not perceptual thresholds.");
            }
            finally
            {
                await ThemeManager.SetThemeAsync(originalTheme, scope.Appearance).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(errorWindow).ConfigureAwait(true);
            }
        }

        private static async Task CaptureSeriesAsync(
            DesktopWindowScope scope,
            Window owner,
            string directory,
            StringBuilder contrasts,
            StringBuilder separations)
        {
            MonitoredItemConfig[] items = [.. Enumerable.Range(0, ItemColors.Count).Select(slot => new MonitoredItemConfig
            {
                Id = ItemColors.Count + slot,
                NodeId = new NodeId($"palette-{slot}", 0),
                DisplayName = $"Slot {slot:D2} / item {ItemColors.Count + slot}"
            })];
            var events = Channel.CreateBounded<NotificationEvent>(ItemColors.Count * 2048);
            using var view = new ScottPlotView();
            view.Bind(events.Reader, items, AnimationMode.Signal);
            AvaPlot chart = view.FindControl<AvaPlot>("Plot")!;
            DataStreamer[] series = [.. chart.Plot.GetPlottables().OfType<DataStreamer>()];
            Assert.That(series, Has.Length.EqualTo(ItemColors.Count));
            int sampleCount = series[0].Data.Data.Length;
            Assert.That(sampleCount, Is.InRange(2, 2048));
            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int sample = 0; sample < sampleCount; sample++)
            {
                for (int slot = 0; slot < items.Length; slot++)
                {
                    Assert.That(events.Writer.TryWrite(new NotificationEvent(
                        NotificationKind.DataChange, items[slot].Id, 1, (uint)sample + 1,
                        timestamp.AddMilliseconds(sample * 25),
                        (slot * 2) + (0.3 * Math.Sin(sample * 0.05)),
                        items[slot].DisplayName, items[slot].NodeId.ToString())), Is.True);
                }
            }
            events.Writer.Complete();
            var window = new Window
            {
                Content = view,
                Title = "UaLens UX06: all monitor palette slots (synthetic samples)",
                Width = 1000,
                Height = 660
            };
            Task modal = window.ShowDialog(owner);
            try
            {
                var timer = Stopwatch.StartNew();
                while (series.Any(streamer => streamer.CountTotal < sampleCount))
                {
                    Assert.That(timer.Elapsed, Is.LessThan(DesktopApplication.Timeout),
                        "The native monitor did not consume the bounded sample fixture.");
                    await DesktopWindowScope.FrameAsync(window).ConfigureAwait(true);
                }
                view.ApplyXZoom(1);
                chart.Plot.Axes.SetLimits(0, sampleCount - 1, -1, ItemColors.Count * 2);
                ScottPlot.AxisLimits limits = chart.Plot.Axes.GetLimits();
                double[][] samples = [.. series.Select(streamer => streamer.Data.Data.ToArray())];
                foreach (ThemePreset theme in s_themes)
                {
                    await ThemeManager.SetThemeAsync(theme, scope.Appearance).ConfigureAwait(true);
                    await DesktopWindowScope.FrameAsync(window).ConfigureAwait(true);
                    Color[] colors = [.. items.Select(item => ((ISolidColorBrush)ItemColors.ForItemId(item.Id)).Color)];
                    for (int slot = 0; slot < items.Length; slot++)
                    {
                        Assert.That(ItemColors.IndexForItemId(items[slot].Id), Is.EqualTo(slot));
                        Assert.That(chart.Plot.GetPlottables().ElementAt(slot), Is.SameAs(series[slot]));
                        Assert.That(series[slot].Color, Is.EqualTo(ItemColors.ScottPlotForItemId(items[slot].Id)));
                        Assert.That(series[slot].LegendItems.Single().LabelText, Is.EqualTo(items[slot].DisplayName));
                        Assert.That(series[slot].LegendItems.Single().LineColor, Is.EqualTo(series[slot].Color));
                        Assert.That(series[slot].CountTotal, Is.EqualTo(sampleCount));
                        Assert.That(series[slot].Data.Data, Is.EqualTo(samples[slot]));
                    }
                    Assert.That(chart.Plot.Axes.GetLimits(), Is.EqualTo(limits));
                    chart.Refresh();
                    await DesktopCapture.SaveAsync(window, Path.Combine(directory, $"{theme}-all-series.png"),
                        [.. colors]).ConfigureAwait(true);
                    AppendEvaluation(theme, colors, contrasts, separations);
                }
            }
            finally
            {
                window.Close();
                await modal.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            }
        }

        private static void AppendEvaluation(
            ThemePreset theme,
            Color[] colors,
            StringBuilder contrasts,
            StringBuilder separations)
        {
            foreach (ColorVisionModel model in Enum.GetValues<ColorVisionModel>())
            {
                foreach (string text in s_text)
                {
                    foreach (string surface in s_textSurfaces)
                    {
                        AppendContrast(theme, model, "text", text, ResourceColor(text), surface, 4.5, contrasts);
                    }
                }
                for (int slot = 0; slot < colors.Length; slot++)
                {
                    foreach (string surface in s_seriesSurfaces)
                    {
                        AppendContrast(theme, model, "series", $"slot-{slot:D2}", colors[slot], surface, 3, contrasts);
                    }
                    for (int other = slot + 1; other < colors.Length; other++)
                    {
                        double distance = PaletteEvaluation.LinearRgbDistance(
                            PaletteEvaluation.Simulate(colors[slot], model),
                            PaletteEvaluation.Simulate(colors[other], model));
                        separations.Append(CultureInfo.InvariantCulture, $"{theme},{model},{slot},{other},{distance:F6}\n");
                    }
                }
            }
        }

        private static void AppendContrast(
            ThemePreset theme,
            ColorVisionModel model,
            string kind,
            string identity,
            Color foreground,
            string surface,
            double minimum,
            StringBuilder report)
        {
            foreground = PaletteEvaluation.Simulate(foreground, model);
            Color background = PaletteEvaluation.Simulate(ResourceColor(surface), model);
            double ratio = PaletteEvaluation.Contrast(foreground, background);
            report.Append(CultureInfo.InvariantCulture,
                $"{theme},{model},{kind},{identity},{foreground},{surface},{background},{ratio:F6},{minimum:F1}\n");
            if (model == ColorVisionModel.Normal)
            {
                Assert.That(ratio, Is.GreaterThanOrEqualTo(minimum), $"{theme}: {identity} on {surface}");
            }
        }

        private static Color ResourceColor(string key)
        {
            Color color = ThemeManager.GetColor(key, Colors.Transparent);
            Assert.That(color.A, Is.EqualTo(255), $"Required opaque theme resource {key} is missing.");
            return color;
        }

        private static readonly ThemePreset[] s_themes =
            [ThemePreset.Light, ThemePreset.DarkStandard, ThemePreset.DarkNavy];

        private static readonly string[] s_states = ["error", "warning", "all-series"];
        private static readonly string[] s_text = ["ErrorText", "WarningText", "SuccessText", "InfoText"];

        private static readonly string[] s_textSurfaces =
            ["AppBg", "PanelBg", "SurfaceBg", "ToolbarBg", "HighlightBg", "AccentYellowBorder"];

        private static readonly string[] s_seriesSurfaces = ["AppBg", "PanelBg", "SurfaceBg"];
    }
}
