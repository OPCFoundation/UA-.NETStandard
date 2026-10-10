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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using UaLens.ViewModels;

namespace UaLens.Views;

internal sealed partial class NodeAttributesView : UserControl
{
    public NodeAttributesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Overrides clipboard writes for deterministic desktop tests.
    /// </summary>
    internal Func<string, Task>? ClipboardWriter { get; set; }

    private ListBox AttributeListControl => this.RequiredControl<ListBox>("AttributesList");

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: AttributeRow row } control &&
            e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            AttributeListControl.SelectedItem = row;
            AttributeListControl.Focus();
        }
    }

    private async void OnAttributesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !IsCopyGesture(e.KeyModifiers) ||
            AttributeListControl.SelectedItem is not AttributeRow row)
        {
            return;
        }

        e.Handled = true;
        await CopyAsync(row, e.KeyModifiers.HasFlag(KeyModifiers.Shift)).ConfigureAwait(true);
    }

    private async void OnCopyValue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyFromContextMenuAsync(includeName: false).ConfigureAwait(true);
    }

    private async void OnCopyKeyValue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyFromContextMenuAsync(includeName: true).ConfigureAwait(true);
    }

    private async Task CopyFromContextMenuAsync(bool includeName)
    {
        if (AttributeListControl.SelectedItem is not AttributeRow row)
        {
            return;
        }

        AttributeListControl.ContextMenu?.Close();
        await Task.Yield();
        await CopyAsync(row, includeName).ConfigureAwait(true);
    }

    private async Task CopyAsync(AttributeRow row, bool includeName)
    {
        string text = FormatCopyText(row, includeName);
        if (ClipboardWriter is { } writer)
        {
            await writer(text).ConfigureAwait(true);
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        await ClipboardTextWriter.SetTextAsync(clipboard, text).ConfigureAwait(true);
    }

    /// <summary>
    /// Formats an attribute row for clipboard output.
    /// </summary>
    internal static string FormatCopyText(AttributeRow row, bool includeName)
    {
        ArgumentNullException.ThrowIfNull(row);
        return includeName ? $"{row.Name}: {row.Value}" : row.Value;
    }

    private static bool IsCopyGesture(KeyModifiers modifiers)
    {
        const KeyModifiers copyModifiers = KeyModifiers.Control | KeyModifiers.Meta;
        bool hasCopyModifier = (modifiers & copyModifiers) != KeyModifiers.None;
        KeyModifiers unsupported = modifiers & ~(copyModifiers | KeyModifiers.Shift);
        return hasCopyModifier && unsupported == KeyModifiers.None;
    }
}
