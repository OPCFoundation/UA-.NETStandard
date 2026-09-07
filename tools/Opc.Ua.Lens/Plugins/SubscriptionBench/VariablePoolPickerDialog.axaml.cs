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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Views;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Row binding for <see cref="VariablePoolPickerDialog"/>: a Variable
/// node with its display path and a <see cref="IsSelected"/> checkbox
/// state. Multi-pick UI uses the checkbox as the canonical selection
/// signal so the user can independently toggle each row without
/// shift/ctrl gestures.
/// </summary>
internal sealed partial class VariablePoolPickerItem : ObservableObject
{
    public NodeId NodeId { get; init; } = NodeId.Null;
    public string DisplayName { get; init; } = string.Empty;
    public string BrowsePath { get; init; } = string.Empty;

    [ObservableProperty]
    private bool m_isSelected;
}

/// <summary>
/// Recursive multi-pick variable browser. Walks the address space from
/// a starting node via HierarchicalReferences and shows every node with
/// <see cref="NodeClass.Variable"/> as a checkbox row. Returns the list
/// of (NodeId, DisplayName) picked on OK, or <c>null</c> on Cancel.
/// </summary>
// CA1001: m_cts is disposed in the Closed handler — the dialog is
// not held beyond its modal lifetime, so adding IDisposable to a
// Window-derived class would be overkill.
#pragma warning disable CA1001
internal sealed partial class VariablePoolPickerDialog : Window
#pragma warning restore CA1001
{
    /// <summary>Hop-depth cap to keep runaway browses bounded.</summary>
    private const int MaxDepth = 16;

    private readonly ISession m_session;
    private readonly NodeId m_root;
    private readonly ObservableCollection<VariablePoolPickerItem> m_results = new();
    private readonly CancellationTokenSource m_cts = new();
    private CheckBox? m_selectAll;
    private TextBlock? m_selectionLabel;
    private bool m_updatingSelectAll;

    public IReadOnlyList<(NodeId NodeId, string DisplayName)>? PickedItems { get; private set; }

    public VariablePoolPickerDialog(ISession session, NodeId root, string header)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        if (root.IsNull)
        {
            throw new ArgumentException("Root NodeId must not be null.", nameof(root));
        }
        m_root = root;
        InitializeComponent();
        Title = "Pick variables for Subscription Bench pool";

        var list = this.RequiredControl<ListBox>("ResultsList");
        var headerLabel = this.RequiredControl<TextBlock>("HeaderLabel");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var progress = this.RequiredControl<ProgressBar>("Progress");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        m_selectAll = this.RequiredControl<CheckBox>("SelectAllCheck");
        m_selectionLabel = this.RequiredControl<TextBlock>("SelectionLabel");

        headerLabel.Text = header;
        list.ItemsSource = m_results;

        m_selectAll.IsCheckedChanged += OnSelectAllChanged;
        m_results.CollectionChanged += (_, _) => RefreshSelectionLabel();

        ok.Click += (_, _) =>
        {
            var picked = new List<(NodeId NodeId, string DisplayName)>();
            foreach (VariablePoolPickerItem item in m_results)
            {
                if (item.IsSelected)
                {
                    picked.Add((item.NodeId, item.DisplayName));
                }
            }
            PickedItems = picked;
            Close(picked);
        };
        cancel.Click += (_, _) =>
        {
            PickedItems = null;
            Close(null);
        };
        Closed += (_, _) =>
        {
            try
            {
                m_cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            m_cts.Dispose();
        };

        Opened += async (_, _) => await RunDiscoveryAsync(status, progress).ConfigureAwait(true);
    }

    private void OnSelectAllChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (m_updatingSelectAll || m_selectAll is null)
        {
            return;
        }
        bool value = m_selectAll.IsChecked == true;
        foreach (VariablePoolPickerItem item in m_results)
        {
            item.IsSelected = value;
        }
        RefreshSelectionLabel();
    }

    private void OnItemSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VariablePoolPickerItem.IsSelected))
        {
            return;
        }
        SyncSelectAll();
        RefreshSelectionLabel();
    }

    private void SyncSelectAll()
    {
        if (m_selectAll is null || m_results.Count == 0)
        {
            return;
        }
        int selected = 0;
        foreach (VariablePoolPickerItem item in m_results)
        {
            if (item.IsSelected)
            {
                selected++;
            }
        }
        m_updatingSelectAll = true;
        try
        {
            m_selectAll.IsChecked = selected == m_results.Count
                ? true
                : (selected == 0 ? false : (bool?)null);
        }
        finally
        {
            m_updatingSelectAll = false;
        }
    }

    private void RefreshSelectionLabel()
    {
        if (m_selectionLabel is null)
        {
            return;
        }
        int selected = 0;
        foreach (VariablePoolPickerItem item in m_results)
        {
            if (item.IsSelected)
            {
                selected++;
            }
        }
        m_selectionLabel.Text = string.Format(CultureInfo.InvariantCulture,
            "{0} selected · {1} discovered",
            selected, m_results.Count);
    }

    private async Task RunDiscoveryAsync(TextBlock status, ProgressBar progress)
    {
        int visited = 0;
        int matched = 0;
        try
        {
            // BFS queue of (NodeId, ParentBrowsePath, Depth). 'seen'
            // prevents cycles.
            var queue = new Queue<(NodeId Node, string Path, int Depth)>();
            var seen = new HashSet<NodeId>();
            queue.Enqueue((m_root, string.Empty, 0));
            seen.Add(m_root);

            while (queue.Count > 0)
            {
                m_cts.Token.ThrowIfCancellationRequested();
                (NodeId node, string parentPath, int depth) = queue.Dequeue();
                visited++;
                if (visited % 25 == 0)
                {
                    UpdateStatus(status, visited, matched, queue.Count);
                }
                if (depth > MaxDepth)
                {
                    continue;
                }

                ArrayOf<BrowseDescription> browse = new BrowseDescription[]
                {
                    new BrowseDescription
                    {
                        NodeId = node,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                };
                BrowseResponse br;
                try
                {
                    br = await m_session
                        .BrowseAsync(null, null, 0, browse, m_cts.Token)
                        .ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }
                if (br.Results.Count == 0 || StatusCode.IsBad(br.Results[0].StatusCode))
                {
                    continue;
                }

                var refs = new List<ReferenceDescription>();
                foreach (ReferenceDescription r in br.Results[0].References)
                {
                    refs.Add(r);
                }
                foreach (ReferenceDescription r in refs)
                {
                    m_cts.Token.ThrowIfCancellationRequested();
                    NodeId childId = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                    if (childId.IsNull || !seen.Add(childId))
                    {
                        continue;
                    }
                    string display = r.DisplayName.IsNull
                        ? r.BrowseName.Name ?? childId.ToString()
                        : r.DisplayName.Text ?? childId.ToString();
                    string path = parentPath + "/" + display;

                    queue.Enqueue((childId, path, depth + 1));

                    if (r.NodeClass != NodeClass.Variable)
                    {
                        continue;
                    }
                    var found = new VariablePoolPickerItem
                    {
                        NodeId = childId,
                        DisplayName = display,
                        BrowsePath = path
                    };
                    found.PropertyChanged += OnItemSelectionChanged;
                    Dispatcher.UIThread.Post(() => m_results.Add(found));
                    matched++;
                }
            }
            UpdateStatus(status, visited, matched, 0);
            progress.IsIndeterminate = false;
            progress.Value = 100;
            Dispatcher.UIThread.Post(RefreshSelectionLabel);
        }
        catch (OperationCanceledException)
        {
            // dialog closed mid-walk; ignore
        }
        catch (Exception ex)
        {
            UpdateStatus(status, visited, matched, 0, $"Discovery error: {ex.Message}");
            progress.IsIndeterminate = false;
        }
    }

    private static void UpdateStatus(TextBlock status, int visited, int matched, int pending, string? extra = null)
    {
        string text = extra is null
            ? string.Format(CultureInfo.InvariantCulture,
                "Visited {0} · matched {1} variables · pending {2}", visited, matched, pending)
            : string.Format(CultureInfo.InvariantCulture,
                "Visited {0} · matched {1} variables · {2}", visited, matched, extra);
        Dispatcher.UIThread.Post(() => status.Text = text);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
