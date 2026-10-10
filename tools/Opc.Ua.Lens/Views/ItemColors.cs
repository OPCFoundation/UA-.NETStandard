/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Avalonia.Media;
using UaLens.Themes;

namespace UaLens.Views;

/// <summary>
/// 12-colour palette used to give every monitored item a stable, distinct
/// colour in the AnimationCanvas (Dots-mode lanes and Bars-mode segments)
/// and in the ScottPlot views (Signal / Histogram / Heatmap).  Keyed by
/// <c>ItemId</c> so a given item retains its colour across add/remove
/// operations of other items.  Wraps around when more items than palette
/// slots are subscribed.
/// </summary>
internal static class ItemColors
{
    public static int Count => s_dark.Length;

    /// <summary>
    /// Stable palette slot for a given monitored-item identity.
    /// </summary>
    public static int IndexForItemId(int id)
    {
        return ((id % Count) + Count) % Count;
    }

    /// <summary>
    /// Theme-aware Avalonia brush without changing the item's palette slot.
    /// </summary>
    public static IBrush ForItemId(int id)
    {
        return ForItemId(id, ChartTheme.IsLight);
    }

    public static IBrush ForItemId(int id, bool isLight)
    {
        return (isLight ? s_lightBrushes : s_darkBrushes)[IndexForItemId(id)];
    }

    /// <summary>
    /// Stable <see cref="ScottPlot.Color"/> for a given monitored-item id.
    /// Sharing this palette across the Avalonia canvas and the ScottPlot
    /// views keeps a given item the same colour everywhere.
    /// </summary>
    public static ScottPlot.Color ScottPlotForItemId(int id)
    {
        return ScottPlotForItemId(id, ChartTheme.IsLight);
    }

    public static ScottPlot.Color ScottPlotForItemId(int id, bool isLight)
    {
        Color color = (isLight ? s_light : s_dark)[IndexForItemId(id)];
        return new ScottPlot.Color(color.R, color.G, color.B);
    }

    private static IBrush[] BuildBrushes(Color[] colors)
    {
        var brushes = new IBrush[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            brushes[i] = new SolidColorBrush(colors[i]);
        }
        return brushes;
    }

    private static readonly Color[] s_dark =
    [
        Color.FromRgb(0x22, 0xC5, 0x5E),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x06, 0xB6, 0xD4),
        Color.FromRgb(0xEC, 0x49, 0x99),
        Color.FromRgb(0x60, 0xA5, 0xFA),
        Color.FromRgb(0xFB, 0x71, 0x85),
        Color.FromRgb(0xFA, 0xCC, 0x15),
        Color.FromRgb(0x4A, 0xDE, 0x80),
        Color.FromRgb(0xC0, 0x84, 0xFC),
        Color.FromRgb(0xF8, 0x71, 0x71),
        Color.FromRgb(0x2D, 0xD4, 0xBF),
        Color.FromRgb(0xD9, 0xF9, 0x9D)
    ];

    private static readonly Color[] s_light =
    [
        Color.FromRgb(0x16, 0x65, 0x34),
        Color.FromRgb(0x92, 0x40, 0x0E),
        Color.FromRgb(0x0E, 0x74, 0x90),
        Color.FromRgb(0xBE, 0x18, 0x5D),
        Color.FromRgb(0x1D, 0x4E, 0xD8),
        Color.FromRgb(0xBE, 0x12, 0x3C),
        Color.FromRgb(0x85, 0x4D, 0x0E),
        Color.FromRgb(0x15, 0x80, 0x3D),
        Color.FromRgb(0x7E, 0x22, 0xCE),
        Color.FromRgb(0xB9, 0x1C, 0x1C),
        Color.FromRgb(0x0F, 0x76, 0x6E),
        Color.FromRgb(0x4D, 0x7C, 0x0F)
    ];

    private static readonly IBrush[] s_darkBrushes = BuildBrushes(s_dark);
    private static readonly IBrush[] s_lightBrushes = BuildBrushes(s_light);
}
