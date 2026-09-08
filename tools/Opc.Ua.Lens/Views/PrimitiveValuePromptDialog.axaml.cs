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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using UaLens.StructuredValues;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Simple modal one-line text prompt used by <see cref="EditArrayDialog"/>
/// to edit a primitive array element.  Parses the entered text via
/// <see cref="VariantParser"/> using the supplied element DataType and
/// exposes the parsed Variant only after a successful commit.
/// </summary>
internal sealed partial class PrimitiveValuePromptDialog : Window
{
    public PrimitiveValuePromptDialog(BuiltInType elementType, Variant initial)
    {
        InitializeComponent();
        this.RequiredControl<TextBlock>("HeaderLabel").Text = $"Edit element  ({elementType})";
        this.RequiredControl<TextBlock>("HintLabel").Text =
            $"Element type: {elementType}. Enter a value in invariant culture; "
            + "press OK to commit.";

        TextBox box = this.RequiredControl<TextBox>("ValueText");
        TextBlock status = this.RequiredControl<TextBlock>("StatusLabel");
        box.Text = StructuredScalarValue.Format(initial);

        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            string txt = box.Text ?? string.Empty;
            if (!StructuredScalarValue.TryParse(elementType, txt, initial,
                out Variant parsed, out string? err))
            {
                status.Text = $"Parse error: {err}";
                status.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
                return;
            }
            Result = parsed;
            WasCommitted = true;
            Close();
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();
    }

    public Variant Result { get; private set; }

    public bool WasCommitted { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
