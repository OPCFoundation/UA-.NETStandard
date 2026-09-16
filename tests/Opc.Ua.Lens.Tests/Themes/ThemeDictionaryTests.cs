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
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NUnit.Framework;
using ScottPlot.Plottables;
using UaLens.Tests.Desktop;
using UaLens.Themes;
using UaLens.Views;
using static UaLens.Tests.Themes.PaletteEvaluation;

namespace UaLens.Tests.Themes
{
    [TestFixture]
    public sealed class ThemeDictionaryTests
    {
        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Navy")]
        public void SemanticTextMeetsW3cContrastOnEveryThemeSurface(string theme)
        {
            ResourceDictionary resources = CreateTheme(theme);
            string[] surfaces = ["AppBg", "PanelBg", "SurfaceBg", "ToolbarBg", "HighlightBg", "AccentYellowBorder"];
            string[] textKeys =
            [
                "TextDim", "TextSecondary", "TextSecondaryAlt", "TextPrimary",
                "SuccessText", "WarningText", "ErrorText", "InfoText",
                "AccentBlue", "AccentCyan", "AccentGreen", "AccentYellow",
                "AccentYellowLight", "AccentRed", "AccentRedLight", "AccentPurple"
            ];
            using (Assert.EnterMultipleScope())
            {
                foreach (string surface in surfaces)
                {
                    foreach (string text in textKeys)
                    {
                        Assert.That(Contrast(GetColor(resources, text), GetColor(resources, surface)),
                            Is.GreaterThanOrEqualTo(4.5), $"{theme}: {text} on {surface}");
                    }
                }
                Assert.That(Contrast(GetColor(resources, "TextOnAccent"), GetColor(resources, "AccentBlue")),
                    Is.GreaterThanOrEqualTo(4.5), $"{theme}: primary button text");
            }
        }

        [TestCase("Light")]
        [TestCase("Dark")]
        [TestCase("Navy")]
        public void EverySeriesMeetsW3cContrastOnChartAndCanvasSurfaces(string theme)
        {
            ResourceDictionary resources = CreateTheme(theme);
            using (Assert.EnterMultipleScope())
            {
                for (int id = 0; id < ItemColors.Count; id++)
                {
                    var brush = (ISolidColorBrush)ItemColors.ForItemId(id, theme == "Light");
                    foreach (string surface in new[] { "SurfaceBg", "PanelBg", "AppBg" })
                    {
                        Assert.That(Contrast(brush.Color, GetColor(resources, surface)),
                            Is.GreaterThanOrEqualTo(3), $"{theme}: item slot {id} on {surface}");
                    }
                    Assert.That(ItemColors.ScottPlotForItemId(id, theme == "Light"),
                        Is.EqualTo(new ScottPlot.Color(brush.Color.R, brush.Color.G, brush.Color.B)));
                }
            }
        }

        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(17)]
        [TestCase(int.MaxValue)]
        public void ThemeSwitchKeepsItemIdentityAndRestoresItsColor(int id)
        {
            int slot = ItemColors.IndexForItemId(id);
            IBrush dark = ItemColors.ForItemId(id, false);
            IBrush light = ItemColors.ForItemId(id, true);
            Assert.That(ItemColors.IndexForItemId(id), Is.EqualTo(slot));
            Assert.That(light, Is.SameAs(ItemColors.ForItemId(slot, true)));
            Assert.That(dark, Is.SameAs(ItemColors.ForItemId(id, false)));
            Assert.That(dark, Is.Not.SameAs(light));
        }

        [Test]
        [Platform("Win,Linux")]
        [Explicit("Requires a dedicated real-desktop test process.")]
        [Category("LensDesktopWorkflow")]
        public Task ApplyingChartColorsKeepsDataAndZoom()
        {
            return AvaloniaDesktopTestHost.RunAsync(() =>
            {
                Application application = Application.Current!;
                application.Resources["SurfaceBg"] = Brushes.White;
                application.Resources["TextPrimary"] = Brushes.Black;
                AssertChartColors();
                return Task.CompletedTask;
            });
        }

        private static void AssertChartColors()
        {
            using var plot = new ScottPlot.Plot();
            Scatter line = plot.Add.Scatter(new double[] { 0, 1 }, new double[] { 2, 3 });
            plot.Axes.SetLimits(0, 10, -1, 5);
            ScottPlot.AxisLimits limits = plot.Axes.GetLimits();

            ChartTheme.Apply(plot);

            Assert.That(plot.GetPlottables(), Does.Contain(line));
            Assert.That(plot.Axes.GetLimits(), Is.EqualTo(limits));
            Assert.That(plot.DataBackground.Color, Is.EqualTo(ScottPlot.Colors.White));
            Assert.That(plot.Legend.FontColor, Is.EqualTo(ScottPlot.Colors.Black));
        }

        [TestCase("Light", "#fff3f4f6")]
        [TestCase("Dark", "#ff141619")]
        [TestCase("Navy", "#ff0f172a")]
        public void CompiledThemeConstructorsLoadSemanticColors(string theme, string background)
        {
            ResourceDictionary resources = theme switch
            {
                "Light" => new LightTheme(),
                "Dark" => new DarkStandardTheme(),
                _ => new DarkNavyTheme()
            };

            Assert.That(resources["AppBg"], Is.InstanceOf<ISolidColorBrush>());
            var brush = (ISolidColorBrush)resources["AppBg"]!;
            Assert.That(brush.Color, Is.EqualTo(Color.Parse(background)));
            Assert.That(resources["TextOnAccent"], Is.InstanceOf<ISolidColorBrush>());
            Assert.That(resources["TextPrimary"], Is.InstanceOf<ISolidColorBrush>());
        }

        private static ResourceDictionary CreateTheme(string theme)
        {
            return theme switch
            {
                "Light" => new LightTheme(),
                "Dark" => new DarkStandardTheme(),
                _ => new DarkNavyTheme()
            };
        }

        private static Color GetColor(ResourceDictionary resources, string key)
        {
            return ((ISolidColorBrush)resources[key]!).Color;
        }
    }
}
