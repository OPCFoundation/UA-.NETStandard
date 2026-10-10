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
    public sealed partial class ThemeDictionaryTests
    {

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
