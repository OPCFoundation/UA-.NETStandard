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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.EventView;

/// <summary>
/// AOT-strict Avalonia UserControl hosting an
/// <see cref="EventViewPlugin"/> in the main window's right pane.
/// Layout: a top filter toolbar; a 3-column body with sources on the
/// left, the event log centred, and an event-fields details TreeView
/// on the right.
/// </summary>
internal sealed partial class EventViewView : UserControl
{
    public EventViewView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Per-row "×" button on a source.  Resolves the bound source from
    /// the button's <c>Tag</c> and dispatches to the view-model's
    /// RemoveSourceCommand.  Wired in code-behind because the row
    /// template's DataContext is the row, not the tab view-model, and
    /// AOT compiled bindings can't traverse to an ancestor command
    /// without a relative-source binding.
    /// </summary>
    private void OnRemoveSourceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn
            && btn.Tag is EventSourceVm src
            && DataContext is EventViewPlugin vm)
        {
            vm.RemoveSourceCommand.Execute(src);
        }
    }
}
