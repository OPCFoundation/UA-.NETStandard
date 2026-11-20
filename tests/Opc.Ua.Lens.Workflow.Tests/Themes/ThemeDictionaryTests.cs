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

        [Test]
        [Platform("Win,Linux")]
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
    }
}
