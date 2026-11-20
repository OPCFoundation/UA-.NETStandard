/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    public sealed partial class ThemeDictionaryTests
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
    }
}
