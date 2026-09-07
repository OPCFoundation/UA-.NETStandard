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
    /// <summary>Single source of truth for the palette: 12 (R, G, B) bytes.</summary>
    private static readonly (byte R, byte G, byte B)[] s_rgb =
    [
        (0x22, 0xC5, 0x5E), // green
        (0xF5, 0x9E, 0x0B), // amber
        (0x06, 0xB6, 0xD4), // cyan
        (0xEC, 0x49, 0x99), // pink
        (0x60, 0xA5, 0xFA), // blue
        (0xFB, 0x71, 0x85), // rose
        (0xFA, 0xCC, 0x15), // yellow
        (0x4A, 0xDE, 0x80), // light green
        (0xC0, 0x84, 0xFC), // violet
        (0xF8, 0x71, 0x71), // red
        (0x2D, 0xD4, 0xBF), // teal
        (0xD9, 0xF9, 0x9D), // lime
    ];

    private static readonly IBrush[] s_palette = BuildBrushes();

    private static IBrush[] BuildBrushes()
    {
        var arr = new IBrush[s_rgb.Length];
        for (int i = 0; i < s_rgb.Length; i++)
        {
            arr[i] = new SolidColorBrush(Color.FromRgb(s_rgb[i].R, s_rgb[i].G, s_rgb[i].B));
        }
        return arr;
    }

    public static int Count => s_palette.Length;

    /// <summary>Stable Avalonia brush for a given monitored-item id.</summary>
    public static IBrush ForItemId(int id)
    {
        int n = s_palette.Length;
        int idx = ((id % n) + n) % n;
        return s_palette[idx];
    }

    /// <summary>
    /// Stable <see cref="ScottPlot.Color"/> for a given monitored-item id.
    /// Sharing this palette across the Avalonia canvas and the ScottPlot
    /// views keeps a given item the same colour everywhere.
    /// </summary>
    public static ScottPlot.Color ScottPlotForItemId(int id)
    {
        int n = s_rgb.Length;
        int idx = ((id % n) + n) % n;
        (byte r, byte g, byte b) = s_rgb[idx];
        return new ScottPlot.Color(r, g, b);
    }
}

