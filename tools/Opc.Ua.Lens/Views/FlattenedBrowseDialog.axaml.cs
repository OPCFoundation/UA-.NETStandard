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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Views;

/// <summary>
/// One row in <see cref="FlattenedBrowseDialog"/>: a NodeId rendered
/// by its full browse-path (e.g. <c>/Objects/Server/.../NodeName</c>).
/// </summary>
internal sealed partial class FlattenedNode : ObservableObject
{
    public NodeId NodeId { get; init; } = NodeId.Null;
    public NodeClass NodeClass { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string BrowsePath { get; init; } = string.Empty;
}

/// <summary>
/// Recursive flat browser.  Walks the address space from
/// <see cref="BrowsePickerDialog.Options.Root"/> via the configured
/// reference type and appends every node that satisfies
/// <see cref="BrowsePickerDialog.Options.AcceptPredicate"/> to a flat
/// scrollable list, growing live as the worker runs.  Closing the
/// dialog mid-walk cancels the worker via a <see cref="CancellationTokenSource"/>.
/// </summary>
// CA1001: m_cts is disposed in the Closed handler — the dialog is
// not held beyond its modal lifetime, so adding IDisposable to a
// Window-derived class would be overkill.
#pragma warning disable CA1001
internal sealed partial class FlattenedBrowseDialog : Window
#pragma warning restore CA1001
{
    /// <summary>Hop-depth cap to keep runaway browses bounded.</summary>
    private const int MaxDepth = 16;

    private readonly BrowsePickerDialog.Options m_options;
    private readonly ObservableCollection<FlattenedNode> m_results = new();
    private readonly CancellationTokenSource m_cts = new();

    public NodeId? PickedNodeId { get; private set; }
    public FlattenedNode? PickedItem { get; private set; }

    public FlattenedBrowseDialog(BrowsePickerDialog.Options options)
    {
        m_options = options ?? throw new ArgumentNullException(nameof(options));
        InitializeComponent();
        Title = "Flatten: " + options.Title;

        var list = this.RequiredControl<ListBox>("ResultsList");
        var header = this.RequiredControl<TextBlock>("HeaderLabel");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var progress = this.RequiredControl<ProgressBar>("Progress");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        header.Text = options.Header ?? $"Live flat browse under {options.Root}.";
        list.ItemsSource = m_results;
        ok.IsEnabled = false;
        list.SelectionChanged += (_, _) =>
        {
            ok.IsEnabled = list.SelectedItem is FlattenedNode;
        };

        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is FlattenedNode node)
            {
                PickedItem = node;
                PickedNodeId = node.NodeId;
                Close(node.NodeId);
                return;
            }
            Close(null);
        };
        cancel.Click += (_, _) => Close(null);
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

    private async Task RunDiscoveryAsync(TextBlock status, ProgressBar progress)
    {
        int visited = 0;
        int matched = 0;
        try
        {
            // BFS queue of (NodeId, ParentBrowsePath, Depth).  Visited
            // tracks NodeIds we've already enqueued to prevent cycles.
            var queue = new Queue<(NodeId Node, string Path, int Depth)>();
            var seen = new HashSet<NodeId>();
            queue.Enqueue((m_options.Root, string.Empty, 0));
            seen.Add(m_options.Root);

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
                        ReferenceTypeId = m_options.ReferenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                };
                BrowseResponse br;
                try
                {
                    br = await m_options.Session
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
                // Snapshot references to a local list — the ReadOnlySpan
                // enumerator can't span awaits.
                var refs = new List<ReferenceDescription>();
                foreach (ReferenceDescription r in br.Results[0].References)
                {
                    refs.Add(r);
                }
                foreach (ReferenceDescription r in refs)
                {
                    m_cts.Token.ThrowIfCancellationRequested();
                    NodeId childId = ExpandedNodeId.ToNodeId(r.NodeId, m_options.Session.NamespaceUris);
                    if (childId.IsNull || !seen.Add(childId))
                    {
                        continue;
                    }
                    string display = r.DisplayName.IsNull
                        ? r.BrowseName.Name ?? childId.ToString()
                        : r.DisplayName.Text ?? childId.ToString();
                    string path = parentPath + "/" + display;

                    queue.Enqueue((childId, path, depth + 1));

                    bool classMatches = m_options.AcceptedClasses == NodeClass.Unspecified
                        || (m_options.AcceptedClasses & r.NodeClass) != 0;
                    if (!classMatches)
                    {
                        continue;
                    }
                    bool accepted = true;
                    if (m_options.AcceptPredicate is { } pred)
                    {
                        try
                        {
                            accepted = await pred(childId, r.NodeClass).ConfigureAwait(true);
                        }
                        catch
                        {
                            accepted = false;
                        }
                    }
                    if (!accepted)
                    {
                        continue;
                    }

                    var found = new FlattenedNode
                    {
                        NodeId = childId,
                        NodeClass = r.NodeClass,
                        DisplayName = display,
                        BrowsePath = path
                    };
                    Dispatcher.UIThread.Post(() => m_results.Add(found));
                    matched++;
                }
            }
            UpdateStatus(status, visited, matched, 0);
            progress.IsIndeterminate = false;
            progress.Value = 100;
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
            ? $"Visited {visited} · matched {matched} · pending {pending}"
            : $"Visited {visited} · matched {matched} · {extra}";
        Dispatcher.UIThread.Post(() => status.Text = text);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
