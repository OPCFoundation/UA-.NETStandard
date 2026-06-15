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
using Opc.Ua.Client;

namespace UaLens.Views;

/// <summary>
/// Thin modal wrapper around <see cref="ComplexValueEditor"/> for
/// editing a single nested structure value (array element or sub-field
/// of a parent struct).  Returns the committed <see cref="Variant"/>
/// (or <c>null</c> on cancel) via <see cref="Window.ShowDialog{TResult}"/>.
/// </summary>
internal sealed partial class ComplexValueElementDialog : Window
{
    public ComplexValueElementDialog(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        ManagedSession session,
        Variant initial)
    {
        InitializeComponent();
        ComplexValueEditor editor = this.RequiredControl<ComplexValueEditor>("Editor");
        editor.Initialize(dataTypeId, definition, session);
        editor.Value = initial;

        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            if (editor.TryCommit(out Variant v, out string? err))
            {
                Close(v);
            }
            else
            {
                TextBlock status = this.RequiredControl<TextBlock>("StatusLabel");
                status.Text = err ?? "Could not commit value.";
                status.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
            }
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
