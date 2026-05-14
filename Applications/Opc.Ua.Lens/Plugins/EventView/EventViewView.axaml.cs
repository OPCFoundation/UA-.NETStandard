/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
