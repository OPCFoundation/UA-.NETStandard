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
/// Vector icon set for the address-space tree, indexed by
/// <see cref="NodeClass"/>. Defined as <see cref="StreamGeometry"/>
/// resources rather than .png/.ico raster assets so the icons stay
/// crisp at any DPI and need no asset management — the visual effect
/// the user asked for is "real icons that reflect semantics", which
/// vector Paths deliver equivalently to PNGs.
/// </summary>
/// <remarks>
/// <para>Geometry conventions: every icon is authored in a 16×16 logical
/// coordinate box. Concrete instances (Object / Variable / Method / View)
/// are filled; type definitions (ObjectType / VariableType / DataType /
/// ReferenceType) are stroked-only or have a hollow centre to read as
/// "template" rather than "value".</para>
/// <para>Colour conventions: Object → blue, Variable → green, Method →
/// amber, ReferenceType → violet, DataType → cyan, View → yellow. The
/// matching *Type variants share the colour with the instance kind so
/// the eye can pair them.</para>
/// </remarks>
internal static class NodeClassIcons
{
    /// <summary>
    /// Geometry per node class. Lookup is keyed by <see cref="NodeClass"/>
    /// — unknown / unspecified classes return <c>null</c>, which renders
    /// no Path (the row TextBlock still shows).
    /// </summary>
    public static IReadOnlyDictionary<NodeClass, Geometry> Geometries { get; }
        = new Dictionary<NodeClass, Geometry>
        {
            // Object: filled rounded square — concrete instance.
            [NodeClass.Object] = StreamGeometry.Parse(
                "M3,2 L13,2 A1,1 0 0 1 14,3 L14,13 A1,1 0 0 1 13,14 L3,14 A1,1 0 0 1 2,13 L2,3 A1,1 0 0 1 3,2 Z"),
            // ObjectType: same square outline, gridded (4 quadrants) — template.
            [NodeClass.ObjectType] = StreamGeometry.Parse(
                "M3,2 L13,2 A1,1 0 0 1 14,3 L14,13 A1,1 0 0 1 13,14 L3,14 A1,1 0 0 1 2,13 L2,3 A1,1 0 0 1 3,2 Z M2,8 L14,8 M8,2 L8,14"),
            // Variable: filled circle — concrete value.
            [NodeClass.Variable] = StreamGeometry.Parse(
                "M8,2 A6,6 0 1,1 8,14 A6,6 0 1,1 8,2 Z"),
            // VariableType: ring (circle with hollow centre) — template.
            [NodeClass.VariableType] = StreamGeometry.Parse(
                "M8,2 A6,6 0 1,1 8,14 A6,6 0 1,1 8,2 Z M8,6 A2,2 0 1,1 8,10 A2,2 0 1,1 8,6 Z"),
            // Method: right-pointing play triangle — invocation.
            [NodeClass.Method] = StreamGeometry.Parse(
                "M4,3 L13,8 L4,13 Z"),
            // ReferenceType: bidirectional arrow — linkage / relation.
            [NodeClass.ReferenceType] = StreamGeometry.Parse(
                "M1,8 L4,5 L4,7 L12,7 L12,5 L15,8 L12,11 L12,9 L4,9 L4,11 Z"),
            // DataType: stacked rows / spec — schema definition.
            [NodeClass.DataType] = StreamGeometry.Parse(
                "M3,3 L13,3 L13,13 L3,13 Z M5,6 L11,6 M5,8 L11,8 M5,10 L11,10"),
            // View: eye with pupil — visibility / scope.
            [NodeClass.View] = StreamGeometry.Parse(
                "M1,8 Q8,2 15,8 Q8,14 1,8 Z M8,5 A3,3 0 1,1 8,11 A3,3 0 1,1 8,5 Z"),
        };

    /// <summary>
    /// Foreground brush per node class. The brush is used for both fill
    /// (instance kinds) and stroke (type kinds) — see
    /// <see cref="IsFilled"/> for the semantic choice.
    /// </summary>
    public static IReadOnlyDictionary<NodeClass, IBrush> Brushes { get; }
        = new Dictionary<NodeClass, IBrush>
        {
            [NodeClass.Object] = new SolidColorBrush(Color.Parse("#38BDF8")),       // sky-400
            [NodeClass.ObjectType] = new SolidColorBrush(Color.Parse("#7DD3FC")),   // sky-300
            [NodeClass.Variable] = new SolidColorBrush(Color.Parse("#10B981")),     // emerald-500
            [NodeClass.VariableType] = new SolidColorBrush(Color.Parse("#6EE7B7")), // emerald-300
            [NodeClass.Method] = new SolidColorBrush(Color.Parse("#F59E0B")),       // amber-500
            [NodeClass.ReferenceType] = new SolidColorBrush(Color.Parse("#A78BFA")),// violet-400
            [NodeClass.DataType] = new SolidColorBrush(Color.Parse("#22D3EE")),     // cyan-400
            [NodeClass.View] = new SolidColorBrush(Color.Parse("#FACC15")),         // yellow-400
        };

    /// <summary>True if the icon renders filled (instance kinds); false for type kinds (stroke-only).</summary>
    public static bool IsFilled(NodeClass cls) => cls is
        NodeClass.Object or NodeClass.Variable or NodeClass.Method or NodeClass.View;

    /// <summary>Singleton transparent brush for the un-filled half.</summary>
    public static IBrush Transparent { get; } = Avalonia.Media.Brushes.Transparent;
}

/// <summary>Maps a <see cref="NodeClass"/> to its <see cref="Geometry"/>.</summary>
internal sealed class NodeClassToGeometryConverter : IValueConverter
{
    public static readonly NodeClassToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is NodeClass cls && NodeClassIcons.Geometries.TryGetValue(cls, out Geometry? g)
            ? g
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="NodeClass"/> to its fill brush (transparent for type kinds).</summary>
internal sealed class NodeClassToFillConverter : IValueConverter
{
    public static readonly NodeClassToFillConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeClass cls)
        {
            return NodeClassIcons.Transparent;
        }
        if (!NodeClassIcons.IsFilled(cls))
        {
            return NodeClassIcons.Transparent;
        }
        return NodeClassIcons.Brushes.TryGetValue(cls, out IBrush? b) ? b : NodeClassIcons.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="NodeClass"/> to its stroke brush.</summary>
internal sealed class NodeClassToStrokeConverter : IValueConverter
{
    public static readonly NodeClassToStrokeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeClass cls)
        {
            return NodeClassIcons.Transparent;
        }
        return NodeClassIcons.Brushes.TryGetValue(cls, out IBrush? b) ? b : NodeClassIcons.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
