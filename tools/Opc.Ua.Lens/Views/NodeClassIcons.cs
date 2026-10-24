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

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Opc.Ua;

namespace UaLens.Views;

/// <summary>
/// Letter-tile icon set for the address-space tree, indexed by
/// <see cref="NodeClass"/>. Inspired by code-editor symbol icons
/// (VS Code / IntelliSense / Roslyn) — each NodeClass gets a small
/// rounded square with a colored background and a white letter glyph:
/// <list type="bullet">
///   <item>Object → solid <b>C</b> tile (class instance, blue)</item>
///   <item>ObjectType → outlined <b>C</b> tile (class template, light blue)</item>
///   <item>Variable → solid <b>V</b> tile (field, green)</item>
///   <item>VariableType → outlined <b>V</b> tile (field template, light green)</item>
///   <item>Method → solid <b>M</b> tile (function, violet)</item>
///   <item>ReferenceType → solid <b>R</b> tile (relation, pink)</item>
///   <item>DataType → solid <b>T</b> tile (schema, cyan)</item>
///   <item>View → solid <b>W</b> tile (scope, amber)</item>
/// </list>
/// "Outline" variants render with a transparent background + colored
/// border so the eye reads them as "templates" of the matching colour.
/// </summary>
internal static class NodeClassIcons
{
    /// <summary>
    /// One tile descriptor: letter glyph + colour pair.
    /// </summary>
    /// <param name="Letter">The 1-2 character glyph rendered in the tile centre.</param>
    /// <param name="Accent">The accent colour (solid background for instances, border for types).</param>
    /// <param name="IsOutline">When true, the tile renders with a transparent fill and a stroke; instance kinds render filled.</param>
    public sealed record Tile(string Letter, IBrush Accent, bool IsOutline);

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    /// <summary>White text for solid-fill tiles (good contrast on saturated colours).</summary>
    public static IBrush WhiteText { get; } = new SolidColorBrush(Color.Parse("#F8FAFC"));

    /// <summary>Singleton transparent brush for outline-variant fills.</summary>
    public static IBrush Transparent { get; } = Avalonia.Media.Brushes.Transparent;

    /// <summary>Letter-tile per node class.</summary>
    public static IReadOnlyDictionary<NodeClass, Tile> Tiles { get; }
        = new Dictionary<NodeClass, Tile>
        {
            [NodeClass.Object]        = new("C",  Brush("#3B82F6"), IsOutline: false), // blue-500
            [NodeClass.ObjectType]    = new("C",  Brush("#93C5FD"), IsOutline: true),  // blue-300
            [NodeClass.Variable]      = new("V",  Brush("#10B981"), IsOutline: false), // emerald-500
            [NodeClass.VariableType]  = new("V",  Brush("#6EE7B7"), IsOutline: true),  // emerald-300
            [NodeClass.Method]        = new("M",  Brush("#A855F7"), IsOutline: false), // violet-500
            [NodeClass.ReferenceType] = new("R",  Brush("#EC4899"), IsOutline: false), // pink-500
            [NodeClass.DataType]      = new("T",  Brush("#22D3EE"), IsOutline: false), // cyan-400
            [NodeClass.View]          = new("W",  Brush("#F59E0B"), IsOutline: false), // amber-500
        };
}

/// <summary>Maps a <see cref="NodeClass"/> to its tile letter glyph.</summary>
internal sealed class NodeClassToLetterConverter : IValueConverter
{
    public static readonly NodeClassToLetterConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is NodeClass cls && NodeClassIcons.Tiles.TryGetValue(cls, out NodeClassIcons.Tile? t)
            ? t.Letter
            : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="NodeClass"/> to its tile background brush.
/// Returns the accent colour for solid tiles; transparent for outline
/// tiles (the border still renders in the accent colour).
/// </summary>
internal sealed class NodeClassToFillConverter : IValueConverter
{
    public static readonly NodeClassToFillConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeClass cls
            || !NodeClassIcons.Tiles.TryGetValue(cls, out NodeClassIcons.Tile? t))
        {
            return NodeClassIcons.Transparent;
        }
        return t.IsOutline ? NodeClassIcons.Transparent : t.Accent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="NodeClass"/> to its tile border brush.</summary>
internal sealed class NodeClassToStrokeConverter : IValueConverter
{
    public static readonly NodeClassToStrokeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeClass cls
            || !NodeClassIcons.Tiles.TryGetValue(cls, out NodeClassIcons.Tile? t))
        {
            return NodeClassIcons.Transparent;
        }
        return t.Accent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="NodeClass"/> to the foreground brush for the tile
/// letter. Outline tiles use the accent colour so the letter reads
/// against the transparent background; solid tiles use white text.
/// </summary>
internal sealed class NodeClassToForegroundConverter : IValueConverter
{
    public static readonly NodeClassToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeClass cls
            || !NodeClassIcons.Tiles.TryGetValue(cls, out NodeClassIcons.Tile? t))
        {
            return NodeClassIcons.WhiteText;
        }
        return t.IsOutline ? t.Accent : NodeClassIcons.WhiteText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="NodeClass"/> to <c>true</c> when a tile exists; lets templates hide the tile for unknown kinds.</summary>
internal sealed class NodeClassToIconVisibilityConverter : IValueConverter
{
    public static readonly NodeClassToIconVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is NodeClass cls && NodeClassIcons.Tiles.ContainsKey(cls);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
