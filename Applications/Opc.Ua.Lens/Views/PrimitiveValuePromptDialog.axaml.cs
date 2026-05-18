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
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Simple modal one-line text prompt used by <see cref="EditArrayDialog"/>
/// to edit a primitive array element.  Parses the entered text via
/// <see cref="VariantParser"/> using the supplied element DataType and
/// returns the parsed <see cref="Variant"/> (or <c>null</c> on cancel /
/// parse failure).
/// </summary>
internal sealed partial class PrimitiveValuePromptDialog : Window
{
    public PrimitiveValuePromptDialog(NodeId elementDataType, Variant initial)
    {
        InitializeComponent();
        BuiltInType bi = TypeInfo.GetBuiltInType(elementDataType);
        this.RequiredControl<TextBlock>("HeaderLabel").Text = $"Edit element  ({bi})";
        this.RequiredControl<TextBlock>("HintLabel").Text =
            $"Element DataType: {elementDataType}.  Enter a value in invariant culture; "
            + "press OK to commit.";

        TextBox box = this.RequiredControl<TextBox>("ValueText");
        TextBlock status = this.RequiredControl<TextBlock>("StatusLabel");
        box.Text = FormatInitial(initial);

        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            string txt = box.Text?.Trim() ?? string.Empty;
            if (!VariantParser.TryParse(elementDataType, ValueRanks.Scalar, txt,
                out Variant parsed, out string? err))
            {
                status.Text = $"Parse error: {err}";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                return;
            }
            Close(parsed);
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private static string FormatInitial(Variant v)
    {
        if (v.IsNull)
        {
            return string.Empty;
        }
        object? raw = v.AsBoxedObject();
        return raw switch
        {
            null => string.Empty,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString() ?? string.Empty
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
