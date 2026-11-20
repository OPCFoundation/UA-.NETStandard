// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using ScottPlot;

namespace UaLens.Themes
{
    /// <summary>
    /// Applies semantic chart colors without replacing data, axes, or zoom state.
    /// </summary>
    internal static class ChartTheme
    {
        public static bool IsLight
        {
            get
            {
                Avalonia.Media.Color surface = ThemeManager.GetColor("SurfaceBg", Avalonia.Media.Colors.White);
                return ((surface.R * 299) + (surface.G * 587) + (surface.B * 114)) > 128000;
            }
        }

        public static void Apply(Plot plot)
        {
            ArgumentNullException.ThrowIfNull(plot);
            Color background = GetColor("SurfaceBg", Avalonia.Media.Colors.White);
            Color foreground = GetColor("TextPrimary", Avalonia.Media.Colors.Black);
            Color border = GetColor("PanelBorder", Avalonia.Media.Colors.Gray);
            plot.FigureBackground.Color = background;
            plot.DataBackground.Color = background;
            plot.Axes.Color(foreground);
            foreach (IAxis axis in plot.Axes.GetAxes())
            {
                axis.MajorTickStyle.Color = foreground;
                axis.MinorTickStyle.Color = foreground;
                axis.FrameLineStyle.Color = border;
                axis.Label.ForeColor = foreground;
                axis.TickLabelStyle.ForeColor = foreground;
            }
            plot.Grid.MajorLineColor = border;
            plot.Grid.MinorLineColor = border;
            plot.Legend.BackgroundColor = background;
            plot.Legend.FontColor = foreground;
            plot.Legend.OutlineColor = border;
        }

        private static Color GetColor(string key, Avalonia.Media.Color fallback)
        {
            Avalonia.Media.Color color = ThemeManager.GetColor(key, fallback);
            return new Color(color.R, color.G, color.B, color.A);
        }
    }
}
