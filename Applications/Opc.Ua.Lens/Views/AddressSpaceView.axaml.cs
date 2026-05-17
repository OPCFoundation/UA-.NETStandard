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
using System.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Visibility policy for the address-space context-menu items, supplied
/// by the host (<c>MainWindow</c>) so the user-control doesn't reach
/// into <see cref="MainViewModel"/> directly.
/// </summary>
internal interface IContextMenuPolicy
{
    /// <summary>Returns whether each menu entry should be visible.</summary>
    ContextMenuVisibility Inspect(NodeViewModel node);
}

internal sealed record ContextMenuVisibility(
    bool CanAdd,
    bool CanAddRecursive,
    bool CanCall,
    bool CanWrite,
    bool CanReadHistory,
    bool CanShowEvents,
    bool CanPerf,
    bool CanExportValue);

internal sealed partial class AddressSpaceView : UserControl
{
    public event Action<NodeViewModel>? NodeSelected;
    public event Action<NodeViewModel>? AddItemRequested;
    public event Action<NodeViewModel>? AddRecursivelyRequested;
    public event Action<NodeViewModel>? CallMethodRequested;
    public event Action<NodeViewModel>? WriteValueRequested;
    public event Action<NodeViewModel>? ReadHistoryRequested;
    public event Action<NodeViewModel>? ShowEventsRequested;
    public event Action<NodeViewModel>? PerfRequested;
    public event Action<NodeViewModel>? ExportValueRequested;
    public event Action<NodeViewModel?>? FindByPathRequested;

    /// <summary>
    /// Visibility policy for the context-menu entries.  Set once by the
    /// host before the menu first opens.
    /// </summary>
    public IContextMenuPolicy? ContextMenuPolicy { get; set; }

    private string m_lastSearch = string.Empty;
    private NodeViewModel? m_lastSearchHit;

    public AddressSpaceView()
    {
        InitializeComponent();
        var tree = this.FindControl<TreeView>("Tree");
        var menu = this.FindControl<ContextMenu>("NodeMenu");
        var miAdd = this.FindControl<MenuItem>("MenuAddItem");
        var miAddRec = this.FindControl<MenuItem>("MenuAddRecursive");
        var miCall = this.FindControl<MenuItem>("MenuCallMethod");
        var miWrite = this.FindControl<MenuItem>("MenuWriteValue");
        var miReadHistory = this.FindControl<MenuItem>("MenuReadHistory");
        var miShowEvents = this.FindControl<MenuItem>("MenuShowEvents");
        var miPerf = this.FindControl<MenuItem>("MenuPerf");
        var miExportValue = this.FindControl<MenuItem>("MenuExportValue");
        var miFindByPath = this.FindControl<MenuItem>("MenuFindByPath");
        var search = this.FindControl<TextBox>("SearchBox");
        var viewKindCombo = this.FindControl<ComboBox>("ViewKindCombo");
        if (tree is null || menu is null || miAdd is null || miAddRec is null
            || miCall is null || miWrite is null
            || miReadHistory is null || miShowEvents is null || miPerf is null
            || miExportValue is null || miFindByPath is null
            || search is null || viewKindCombo is null)
        {
            return;
        }

        WireViewKindCombo(viewKindCombo);

        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                NodeSelected?.Invoke(n);
            }
        };

        // Set per-item visibility just before the menu opens so it
        // reflects the currently-selected node.
        menu.Opening += (_, _) =>
        {
            if (tree.SelectedItem is not NodeViewModel n || ContextMenuPolicy is null)
            {
                miAdd.IsVisible = miAddRec.IsVisible = miCall.IsVisible = miWrite.IsVisible = false;
                miReadHistory.IsVisible = miShowEvents.IsVisible = miPerf.IsVisible = false;
                miExportValue.IsVisible = false;
                miFindByPath.IsVisible = true;
                return;
            }
            ContextMenuVisibility v = ContextMenuPolicy.Inspect(n);
            miAdd.IsVisible = v.CanAdd;
            miAddRec.IsVisible = v.CanAddRecursive;
            miCall.IsVisible = v.CanCall;
            miWrite.IsVisible = v.CanWrite;
            miReadHistory.IsVisible = v.CanReadHistory;
            miShowEvents.IsVisible = v.CanShowEvents;
            miPerf.IsVisible = v.CanPerf;
            miExportValue.IsVisible = v.CanExportValue;
            miFindByPath.IsVisible = true;
        };

        miAdd.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                AddItemRequested?.Invoke(n);
            }
        };
        miAddRec.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                AddRecursivelyRequested?.Invoke(n);
            }
        };
        miCall.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                CallMethodRequested?.Invoke(n);
            }
        };
        miWrite.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                WriteValueRequested?.Invoke(n);
            }
        };
        miReadHistory.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                ReadHistoryRequested?.Invoke(n);
            }
        };
        miShowEvents.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                ShowEventsRequested?.Invoke(n);
            }
        };
        miPerf.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                PerfRequested?.Invoke(n);
            }
        };
        miExportValue.Click += (_, _) =>
        {
            if (tree.SelectedItem is NodeViewModel n)
            {
                ExportValueRequested?.Invoke(n);
            }
        };
        miFindByPath.Click += (_, _) =>
        {
            // Find-by-path works without a selection too (defaults to ObjectsFolder).
            FindByPathRequested?.Invoke(tree.SelectedItem as NodeViewModel);
        };

        // Browse search — Enter starts a fresh search; F3 jumps to next match.
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                DoSearch(search.Text ?? string.Empty, fromBeginning: true);
            }
            else if (e.Key == Avalonia.Input.Key.F3)
            {
                e.Handled = true;
                DoSearch(search.Text ?? string.Empty, fromBeginning: false);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.F3)
            {
                e.Handled = true;
                DoSearch(search.Text ?? string.Empty, fromBeginning: false);
            }
        };
    }

    /// <summary>
    /// Depth-limited DFS over the already-loaded tree nodes filtering by
    /// BrowseName / NodeId substring (case-insensitive).  When
    /// <paramref name="fromBeginning"/> is true the search restarts at
    /// the root and stops at the first hit; otherwise it continues from
    /// the last found node so F3 walks through matches.
    /// </summary>
    private void DoSearch(string query, bool fromBeginning)
    {
        var tree = this.FindControl<TreeView>("Tree");
        if (tree is null || tree.ItemsSource is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }
        bool fresh = fromBeginning || query != m_lastSearch;
        m_lastSearch = query;
        NodeViewModel? skipUntil = fresh ? null : m_lastSearchHit;
        bool seenSkip = skipUntil is null;
        foreach (object root in tree.ItemsSource)
        {
            if (root is NodeViewModel n && Walk(n, query, ref seenSkip, skipUntil) is { } hit)
            {
                m_lastSearchHit = hit;
                hit.IsExpanded = true;
                tree.SelectedItem = hit;
                NodeSelected?.Invoke(hit);
                return;
            }
        }
        // No more matches — wrap to start next time.
        m_lastSearchHit = null;
    }

    private static NodeViewModel? Walk(NodeViewModel n, string q, ref bool seenSkip, NodeViewModel? skipUntil)
    {
        if (seenSkip)
        {
            string text = n.Text ?? string.Empty;
            string id = n.NodeId.ToString() ?? string.Empty;
            if (text.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return n;
            }
        }
        else if (ReferenceEquals(n, skipUntil))
        {
            seenSkip = true;
        }
        foreach (NodeViewModel child in n.Children)
        {
            if (child.IsPlaceholder)
            {
                continue;
            }

            NodeViewModel? hit = Walk(child, q, ref seenSkip, skipUntil);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    /// <summary>
    /// Populates the View-kind combo with all <see cref="BrowseViewKind"/>
    /// values, mirrors the bound <see cref="BrowserViewModel.CurrentViewKind"/>
    /// into the selected item whenever the <see cref="UserControl.DataContext"/>
    /// changes, and forwards user selections via
    /// <see cref="BrowserViewModel.SetViewKindAsync"/>.  Re-entry is
    /// suppressed via a sentinel so the programmatic resync we do on
    /// DataContext arrival does not fire another SetViewKindAsync.
    /// </summary>
    private void WireViewKindCombo(ComboBox combo)
    {
        combo.ItemsSource = Enum.GetValues<BrowseViewKind>();
        bool syncing = false;

        void SyncFromDataContext()
        {
            if (DataContext is BrowserViewModel vm)
            {
                syncing = true;
                try
                {
                    combo.SelectedItem = vm.CurrentViewKind;
                }
                finally
                {
                    syncing = false;
                }
            }
        }

        SyncFromDataContext();
        DataContextChanged += (_, _) => SyncFromDataContext();

        combo.SelectionChanged += async (_, _) =>
        {
            if (syncing)
            {
                return;
            }
            if (DataContext is BrowserViewModel vm
                && combo.SelectedItem is BrowseViewKind kind
                && kind != vm.CurrentViewKind)
            {
                await vm.SetViewKindAsync(kind, CancellationToken.None).ConfigureAwait(false);
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

