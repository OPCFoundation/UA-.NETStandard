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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.ViewModels;

/// <summary>
/// Hierarchical view model backing the address-space TreeView. Roots are
/// Objects (i=85) and Server (i=2253); children are loaded lazily on
/// <see cref="NodeViewModel.IsExpanded"/> change.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's <c>TreeView</c> only renders an expand chevron when the bound
/// <c>ItemsSource</c> already contains at least one item.  A pure-lazy model
/// where <see cref="NodeViewModel.Children"/> is empty until expand never
/// shows a chevron, so the user cannot drill past the first level.  We work
/// around this by populating every newly-created <see cref="NodeViewModel"/>
/// with a single sentinel placeholder child; on first expand we replace the
/// placeholder with real children fetched from the server.  If the browse
/// returns nothing the placeholder is simply cleared and the chevron
/// disappears, which is also the correct outcome for leaf Variables /
/// Methods.
/// </para>
/// </remarks>
internal sealed class BrowserViewModel : ObservableObject
{
    private readonly ITelemetryContext m_telemetry;
    private readonly ILogger m_log;
    private readonly ConnectionService m_connection;
    /// <summary>
    /// Last <see cref="ManagedSession"/> instance the tree was built against.
    /// The tree is only rebuilt when this changes — so transient
    /// StateChanged firings (e.g. spurious KA blips, settings reapply,
    /// tab switches that re-mirror the active adapter) don't wipe the
    /// user's expanded state.
    /// </summary>
    private object? m_lastSessionRef;

    public ObservableCollection<NodeViewModel> Roots { get; } = new();

    public BrowserViewModel(ITelemetryContext telemetry, ConnectionService connection)
    {
        m_telemetry = telemetry;
        m_log = telemetry.CreateLogger("Browser");
        m_connection = connection;
        m_connection.StateChanged += () => Dispatcher.UIThread.Post(OnConnectionStateChanged);
    }

    /// <summary>
    /// State-changed bridge: rebuild the tree only when the underlying
    /// <see cref="ManagedSession"/> reference actually changes (connected
    /// to a different session, or disconnected).  No-op state-changes
    /// (the connection stays the same session) leave the tree alone so
    /// the user's expansion / scroll / selection state is preserved.
    /// </summary>
    private void OnConnectionStateChanged()
    {
        object? cur = m_connection.Session;
        if (ReferenceEquals(cur, m_lastSessionRef))
        {
            return;
        }
        m_lastSessionRef = cur;
        Reload();
    }

    /// <summary>
    /// Refreshes the address-space tree from scratch — clears the existing
    /// roots and re-issues their initial browse against the live session.
    /// Bound to the "↻ Refresh" button at the top-right of the address-space
    /// panel.  Auto-invoked by <see cref="OnConnectionStateChanged"/> when
    /// the live session reference changes.
    /// </summary>
    internal void Reload()
    {
        Roots.Clear();
        if (m_connection is { IsConnected: true, Session: { } })
        {
            // Single root: i=84 (RootFolder).  Children (Objects, Types,
            // Views, ...) load lazily on expand.
            var root = new NodeViewModel(this, NodeId.Null, ObjectIds.RootFolder, "◉ Root", NodeClass.Object);
            Roots.Add(root);
            // Auto-expand the root so the user immediately sees Objects/Types/Views.
            root.IsExpanded = true;
        }
        m_lastSessionRef = m_connection.Session;
    }

    internal async Task LoadChildrenAsync(NodeViewModel node)
    {
        if (node.ChildrenLoaded || node.IsPlaceholder)
        {
            return;
        }
        node.ChildrenLoaded = true;

        if (m_connection.Session is not { } session)
        {
            return;
        }
        try
        {
            // Two BrowseDescriptions per node, issued in a single round-trip:
            //   1. Aggregates (HasComponent / HasOrderedComponent / HasProperty
            //                  / HasHistoricalConfiguration via subtype walk)
            //   2. Organizes (no subtypes — preserves folder navigation under
            //                 the standard Objects folder, where children are
            //                 linked via Organizes rather than HasComponent).
            // Their union is what we render. We deduplicate by absolute NodeId
            // so a child reachable through both refs (rare but possible) only
            // shows once.  HasNotifier / HasEventSource are intentionally
            // excluded — they cause duplicates on event-emitting servers.
            // Browse with `Aggregates` (IncludeSubtypes=true) — covers
            // HasComponent / HasOrderedComponent / HasProperty /
            // HasHistoricalConfiguration via subtype walk — AND `Organizes`
            // (no subtypes) so folder-style nodes (RootFolder → Objects /
            // Types / Views, the standard Objects folder, …) navigate
            // correctly.  Two BrowseDescriptions in one round-trip; results
            // are deduplicated by absolute NodeId.  HasNotifier /
            // HasEventSource are intentionally excluded — they cause
            // duplicates on event-emitting servers.
            ArrayOf<BrowseDescription> descriptions = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = node.NodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
                    ResultMask = (uint)BrowseResultMask.All
                },
                new BrowseDescription
                {
                    NodeId = node.NodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            var refs = new List<ReferenceDescription>();
            var continuationPoints = new List<ByteString>();
            BrowseResponse resp = await session.BrowseAsync(null, null, 0, descriptions, default).ConfigureAwait(false);
            foreach (BrowseResult br in resp.Results)
            {
                if (StatusCode.IsBad(br.StatusCode))
                {
                    continue;
                }
                refs.AddRange(br.References);
                if (br.ContinuationPoint.Length > 0)
                {
                    continuationPoints.Add(br.ContinuationPoint);
                }
            }
            // Drain continuation points until both browse results are complete.
            while (continuationPoints.Count > 0)
            {
                ArrayOf<ByteString> nextCps = continuationPoints.ToArray();
                BrowseNextResponse next = await session.BrowseNextAsync(null, false, nextCps, default).ConfigureAwait(false);
                continuationPoints.Clear();
                foreach (BrowseResult br in next.Results)
                {
                    if (StatusCode.IsBad(br.StatusCode))
                    {
                        continue;
                    }
                    refs.AddRange(br.References);
                    if (br.ContinuationPoint.Length > 0)
                    {
                        continuationPoints.Add(br.ContinuationPoint);
                    }
                }
            }

            var seen = new HashSet<NodeId>();
            var children = new List<NodeViewModel>(refs.Count);
            foreach (ReferenceDescription r in refs)
            {
                if (r.NodeId.IsNull || r.NodeId.IsAbsolute)
                {
                    continue;
                }
                NodeId child = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                if (child.IsNull || !seen.Add(child))
                {
                    continue;
                }
                string name = !r.DisplayName.IsNull
                    ? r.DisplayName.Text ?? string.Empty
                    : (!r.BrowseName.IsNull ? r.BrowseName.Name ?? string.Empty : (child.ToString() ?? string.Empty));
                children.Add(new NodeViewModel(this, node.NodeId, child, $"{Glyph(r.NodeClass)} {name}", r.NodeClass));
            }

            Action apply = () =>
            {
                node.Children.Clear();              // remove the placeholder (if any)
                foreach (NodeViewModel c in children)
                {
                    node.Children.Add(c);
                }
                node.HasItems = node.Children.Count > 0;
            };
            await PostToUiAsync(apply).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Browse failed for {NodeId}", node.NodeId);
            // Make sure the placeholder doesn't linger if browse failed.
            await PostToUiAsync(() =>
            {
                node.Children.Clear();
                node.HasItems = false;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Browses a node's HasComponent (no subtypes — explicitly excludes
    /// HasProperty) child Variables and returns a flat list.  Used by the
    /// "add all children" path on Object selections.
    /// </summary>
    public async Task<IReadOnlyList<(NodeId NodeId, string DisplayName)>> GetChildVariablesAsync(
        NodeId parent, CancellationToken ct = default)
    {
        if (m_connection.Session is not { } session || parent.IsNull)
        {
            return Array.Empty<(NodeId, string)>();
        }
        try
        {
            ArrayOf<BrowseDescription> descriptions = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = parent,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse resp = await session.BrowseAsync(null, null, 0, descriptions, ct).ConfigureAwait(false);
            var refs = new List<ReferenceDescription>();
            ByteString cp = ByteString.Empty;
            if (resp.Results.Count > 0 && !StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                refs.AddRange(resp.Results[0].References);
                cp = resp.Results[0].ContinuationPoint;
            }
            while (cp.Length > 0)
            {
                ArrayOf<ByteString> cps = new ByteString[] { cp };
                BrowseNextResponse next = await session.BrowseNextAsync(null, false,
                    cps, ct).ConfigureAwait(false);
                cp = ByteString.Empty;
                if (next.Results.Count > 0 && !StatusCode.IsBad(next.Results[0].StatusCode))
                {
                    refs.AddRange(next.Results[0].References);
                    cp = next.Results[0].ContinuationPoint;
                }
            }

            var seen = new HashSet<NodeId>();
            var list = new List<(NodeId, string)>(refs.Count);
            foreach (ReferenceDescription r in refs)
            {
                if (r.NodeId.IsNull || r.NodeId.IsAbsolute)
                {
                    continue;
                }
                NodeId child = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                if (child.IsNull || !seen.Add(child))
                {
                    continue;
                }
                string name = !r.DisplayName.IsNull
                    ? r.DisplayName.Text ?? string.Empty
                    : (!r.BrowseName.IsNull ? r.BrowseName.Name ?? string.Empty : (child.ToString() ?? string.Empty));
                list.Add((child, name));
            }
            return list;
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "GetChildVariablesAsync failed for {NodeId}", parent);
            return Array.Empty<(NodeId, string)>();
        }
    }

    /// <summary>
    /// Resolves one or more relative-path strings against
    /// <paramref name="startingNode"/> via <c>TranslateBrowsePathsToNodeIds</c>.
    /// Each input string follows the OPC UA <c>RelativePath</c> grammar
    /// (e.g. <c>"/Objects/Server/ServerStatus.CurrentTime"</c>).
    /// </summary>
    /// <param name="startingNode">Anchor node for each path; <see cref="Opc.Ua.NodeId.Null"/> uses ObjectsFolder.</param>
    /// <param name="relativePaths">One path per row; blank rows are skipped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Per input path, a tuple of the (parsed-or-null) status code and the
    /// resolved matching NodeIds. Path-parse failures yield
    /// <see cref="StatusCodes.BadSyntaxError"/> with an empty match list;
    /// service-level failures yield the server-reported status; success
    /// yields <see cref="StatusCodes.Good"/>.
    /// </returns>
    public async Task<IReadOnlyList<(string Path, StatusCode Status, IReadOnlyList<NodeId> Matches)>>
        ResolveBrowsePathsAsync(
            NodeId startingNode,
            IReadOnlyList<string> relativePaths,
            CancellationToken ct = default)
    {
        if (m_connection.Session is not { } session || relativePaths.Count == 0)
        {
            return Array.Empty<(string, StatusCode, IReadOnlyList<NodeId>)>();
        }
        NodeId anchor = startingNode.IsNull ? ObjectIds.ObjectsFolder : startingNode;
        var rows = new List<(string, StatusCode, IReadOnlyList<NodeId>)>(relativePaths.Count);
        var live = new List<(int Index, string Path, BrowsePath Browse)>(relativePaths.Count);
        for (int i = 0; i < relativePaths.Count; i++)
        {
            string raw = (relativePaths[i] ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                rows.Add((raw, StatusCodes.Good, Array.Empty<NodeId>()));
                continue;
            }
            try
            {
                var bp = new BrowsePath
                {
                    StartingNode = anchor,
                    RelativePath = Opc.Ua.RelativePath.Parse(raw, session.TypeTree)
                };
                rows.Add((raw, StatusCodes.Good, Array.Empty<NodeId>()));
                live.Add((i, raw, bp));
            }
            catch (Exception ex)
            {
                m_log.LogDebug(ex, "Parse RelativePath '{Path}' failed.", raw);
                rows.Add((raw, StatusCodes.BadSyntaxError, Array.Empty<NodeId>()));
            }
        }
        if (live.Count == 0)
        {
            return rows;
        }
        try
        {
            var browsePaths = new ArrayOf<BrowsePath>();
            foreach ((_, _, BrowsePath bp) in live)
            {
                browsePaths = browsePaths.AddItem(bp);
            }
            TranslateBrowsePathsToNodeIdsResponse resp = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct).ConfigureAwait(false);
            for (int j = 0; j < live.Count && j < resp.Results.Count; j++)
            {
                int idx = live[j].Index;
                BrowsePathResult r = resp.Results[j];
                var matches = new List<NodeId>();
                if (r.Targets is { Count: > 0 } tgts)
                {
                    foreach (BrowsePathTarget t in tgts)
                    {
                        NodeId mapped = ExpandedNodeId.ToNodeId(t.TargetId, session.NamespaceUris);
                        if (!mapped.IsNull)
                        {
                            matches.Add(mapped);
                        }
                    }
                }
                rows[idx] = (live[j].Path, r.StatusCode, matches);
            }
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "TranslateBrowsePathsToNodeIds failed.");
            for (int j = 0; j < live.Count; j++)
            {
                int idx = live[j].Index;
                rows[idx] = (live[j].Path, new StatusCode(StatusCodes.BadCommunicationError.Code), Array.Empty<NodeId>());
            }
        }
        return rows;
    }


    /// <summary>
    /// Marshals an action onto the Avalonia UI thread when an
    /// <see cref="Avalonia.Application"/> is running; otherwise (e.g. headless
    /// validators like <c>--testtree</c>) runs it inline so the production
    /// code path doesn't dead-lock.
    /// </summary>
    private static Task PostToUiAsync(Action a)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            a();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(a).GetTask();
    }

    /// <summary>
    /// Reads the <see cref="Attributes.EventNotifier"/> attribute for an Object
    /// or View node.  Returns the byte value (a bitmask of
    /// <see cref="EventNotifiers"/>) or <c>null</c> if the read failed.
    /// </summary>
    public async Task<byte?> GetEventNotifierAsync(NodeId nodeId, CancellationToken ct = default)
    {
        if (m_connection.Session is not { } session)
        {
            return null;
        }
        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.EventNotifier
                }
            ];
            ReadResponse resp = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, ids, ct).ConfigureAwait(false);
            if (resp.Results.Count == 0)
            {
                return null;
            }
            DataValue dv = resp.Results[0];
            if (StatusCode.IsBad(dv.StatusCode))
            {
                return null;
            }
            if (dv.WrappedValue.TryGetValue(out byte b))
            {
                return b;
            }
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "EventNotifier read failed for {NodeId}", nodeId);
        }
        return null;
    }

    /// <summary>
    /// ◉ Object · ◇ ObjectType · ○ Variable · ◎ VariableType · ▶ Method
    /// ◦ ReferenceType · □ DataType · ▣ View
    /// </summary>
    private static string Glyph(NodeClass cls) => cls switch
    {
        NodeClass.Object => "\u25C9",
        NodeClass.ObjectType => "\u25C7",
        NodeClass.Variable => "\u25CB",
        NodeClass.VariableType => "\u25CE",
        NodeClass.Method => "\u25B6",
        NodeClass.ReferenceType => "\u25E6",
        NodeClass.DataType => "\u25A1",
        NodeClass.View => "\u25A3",
        _ => "?"
    };
}

internal sealed partial class NodeViewModel : ObservableObject
{
    private readonly BrowserViewModel m_owner;
    private bool m_loadStarted;

    public NodeId NodeId { get; }
    public NodeId ParentNodeId { get; }
    public NodeClass NodeClass { get; }
    internal bool IsPlaceholder { get; }

    [ObservableProperty]
    private string m_text;

    [ObservableProperty]
    private bool m_isExpanded;

    [ObservableProperty]
    private bool m_hasItems = true;

    public ObservableCollection<NodeViewModel> Children { get; } = new();
    internal bool ChildrenLoaded { get; set; }

    public NodeViewModel(BrowserViewModel owner, NodeId parent, NodeId nodeId, string text, NodeClass cls)
        : this(owner, parent, nodeId, text, cls, isPlaceholder: false)
    {
        // Seed every real node with a single sentinel child so the
        // <c>TreeView</c> renders an expand chevron.  The first expand
        // replaces it with real children via <see cref="BrowserViewModel.LoadChildrenAsync"/>.
        Children.Add(new NodeViewModel(owner, NodeId, NodeId.Null, "…", NodeClass.Unspecified, isPlaceholder: true));
    }

    private NodeViewModel(BrowserViewModel owner, NodeId parent, NodeId nodeId, string text, NodeClass cls, bool isPlaceholder)
    {
        m_owner = owner;
        ParentNodeId = parent;
        NodeId = nodeId;
        NodeClass = cls;
        m_text = text;
        IsPlaceholder = isPlaceholder;
        if (isPlaceholder)
        {
            // The placeholder is itself a tree leaf.
            m_hasItems = false;
            m_loadStarted = true;
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !m_loadStarted && !IsPlaceholder)
        {
            m_loadStarted = true;
            _ = m_owner.LoadChildrenAsync(this);
        }
    }
}
