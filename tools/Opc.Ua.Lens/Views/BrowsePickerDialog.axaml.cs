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
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Views;

/// <summary>
/// Single VM node displayed in <see cref="BrowsePickerDialog"/>'s TreeView.
/// Children are browsed lazily the first time the user expands the node.
/// </summary>
internal sealed partial class BrowsePickerNode : ObservableObject
{
    public NodeId NodeId { get; }
    public NodeClass NodeClass { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool m_isExpanded;

    [ObservableProperty]
    private bool m_isSelectable;

    public bool ChildrenLoaded { get; set; }

    public ObservableCollection<BrowsePickerNode> Children { get; } = new();

    public BrowsePickerNode(NodeId nodeId, NodeClass nodeClass, string displayName, bool isSelectable)
    {
        NodeId = nodeId;
        NodeClass = nodeClass;
        DisplayName = displayName;
        m_isSelectable = isSelectable;
        // Placeholder child so the TreeView shows an expand arrow even
        // before we've browsed the real children.  Replaced on first
        // expansion by the actual children (or an empty list).
        Children.Add(s_placeholder);
    }

    /// <summary>Single shared sentinel for "expand me to load children".</summary>
    private static readonly BrowsePickerNode s_placeholder = new(NodeId.Null, NodeClass.Unspecified, "…", false);
}

/// <summary>
/// Generic node-picker dialog: browses the address space rooted at a
/// given NodeId, filtered by <see cref="NodeClass"/> mask and an
/// optional <see cref="ReferenceTypeId"/>, and returns the user's
/// selection.  Used by:
/// <list type="bullet">
/// <item>Performance "Pick target…" fallback when no Variable/Method
///   is selected in the main address-space tree.</item>
/// <item>EventView "+Add Source" fallback when no Object/View with
///   <c>SubscribeToEvents</c> is selected.</item>
/// <item>EventView "Filter…" event-type chooser (always shown; rooted
///   at <see cref="ObjectTypeIds.BaseEventType"/>).</item>
/// </list>
/// </summary>
internal sealed partial class BrowsePickerDialog : Window
{
    internal sealed record Options(
        ISession Session,
        NodeId Root,
        string Title,
        NodeClass AcceptedClasses,
        NodeId? ReferenceTypeId = null,
        Func<NodeId, NodeClass, Task<bool>>? AcceptPredicate = null,
        string? Header = null);

    private readonly Options m_options;

    public NodeId? PickedNodeId { get; private set; }
    public string PickedDisplay { get; private set; } = string.Empty;
    public NodeClass PickedNodeClass { get; private set; }

    public BrowsePickerDialog(Options options)
    {
        m_options = options ?? throw new ArgumentNullException(nameof(options));
        InitializeComponent();
        Title = options.Title;

        var tree = this.RequiredControl<TreeView>("Tree");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var header = this.RequiredControl<TextBlock>("HeaderLabel");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var flatten = this.RequiredControl<Button>("FlattenButton");

        header.Text = options.Header ?? $"Pick a node under {options.Root}.";

        var root = new BrowsePickerNode(
            options.Root,
            NodeClass.Unspecified,
            options.Root.ToString(),
            isSelectable: false);
        tree.ItemsSource = new[] { root };

        // Kick off the first browse so the user sees children immediately.
        root.IsExpanded = true;
        _ = LoadChildrenAsync(root);

        // Lazy-load on expand. Each BrowsePickerNode publishes IsExpanded
        // changes through CommunityToolkit's generated setter.
        root.PropertyChanged += async (_, e) => await OnExpansionAsync(root, e.PropertyName).ConfigureAwait(true);

        tree.SelectionChanged += async (_, _) => await UpdateOkAsync(tree, ok, status).ConfigureAwait(true);

        ok.Click += (_, _) =>
        {
            if (tree.SelectedItem is BrowsePickerNode node && node.IsSelectable)
            {
                PickedNodeId = node.NodeId;
                PickedNodeClass = node.NodeClass;
                PickedDisplay = node.DisplayName;
                Close(node.NodeId);
                return;
            }
            Close(null);
        };
        cancel.Click += (_, _) => Close(null);
        flatten.Click += async (_, _) =>
        {
            var dlg = new FlattenedBrowseDialog(m_options);
            NodeId? picked = await dlg.ShowDialog<NodeId?>(this).ConfigureAwait(true);
            if (picked.HasValue && !picked.Value.IsNull)
            {
                PickedNodeId = picked.Value;
                PickedNodeClass = dlg.PickedItem?.NodeClass ?? NodeClass.Unspecified;
                PickedDisplay = dlg.PickedItem?.DisplayName ?? picked.Value.ToString();
                Close(picked.Value);
            }
        };
        status.Text = "Browsing…";
        ok.IsEnabled = false;
    }

    private async Task OnExpansionAsync(BrowsePickerNode node, string? propertyName)
    {
        if (propertyName != nameof(BrowsePickerNode.IsExpanded))
        {
            return;
        }
        if (node.IsExpanded)
        {
            await LoadChildrenAsync(node).ConfigureAwait(true);
        }
    }

    private async Task LoadChildrenAsync(BrowsePickerNode parent)
    {
        if (parent.ChildrenLoaded)
        {
            return;
        }
        parent.ChildrenLoaded = true;
        parent.Children.Clear();
        try
        {
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = parent.NodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = m_options.ReferenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await m_options.Session
                .BrowseAsync(null, null, 0, browse, CancellationToken.None)
                .ConfigureAwait(true);
            if (br.Results.Count == 0 || StatusCode.IsBad(br.Results[0].StatusCode))
            {
                return;
            }
            // Snapshot to a local list so iteration doesn't span awaits
            // (References enumerator is a ref-struct).
            var refs = new System.Collections.Generic.List<ReferenceDescription>();
            foreach (ReferenceDescription r in br.Results[0].References)
            {
                refs.Add(r);
            }
            foreach (ReferenceDescription r in refs)
            {
                NodeId childId = ExpandedNodeId.ToNodeId(r.NodeId, m_options.Session.NamespaceUris);
                if (childId.IsNull)
                {
                    continue;
                }
                bool classMatches = m_options.AcceptedClasses == NodeClass.Unspecified
                    || (m_options.AcceptedClasses & r.NodeClass) != 0;
                string display = r.DisplayName.IsNull
                    ? r.BrowseName.Name ?? childId.ToString()
                    : r.DisplayName.Text ?? childId.ToString();
                bool selectable = classMatches;
                if (selectable && m_options.AcceptPredicate is { } pred)
                {
                    try
                    {
                        selectable = await pred(childId, r.NodeClass).ConfigureAwait(true);
                    }
                    catch
                    {
                        selectable = false;
                    }
                }
                var child = new BrowsePickerNode(childId, r.NodeClass, display, selectable);
                child.PropertyChanged += async (_, e) => await OnExpansionAsync(child, e.PropertyName).ConfigureAwait(true);
                parent.Children.Add(child);
            }
        }
        catch (Exception ex)
        {
            // Surface in status; tree just shows no children.
            var status = this.RequiredControl<TextBlock>("StatusLabel");
            status.Text = $"Browse failed: {ex.Message}";
        }
    }

    private Task UpdateOkAsync(TreeView tree, Button ok, TextBlock status)
    {
        if (tree.SelectedItem is BrowsePickerNode node && node.IsSelectable)
        {
            ok.IsEnabled = true;
            status.Text = $"Selected: {node.DisplayName}  ·  {node.NodeId}";
        }
        else
        {
            ok.IsEnabled = false;
            status.Text = "Pick an eligible node (highlighted) to enable OK.";
        }
        return Task.CompletedTask;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
