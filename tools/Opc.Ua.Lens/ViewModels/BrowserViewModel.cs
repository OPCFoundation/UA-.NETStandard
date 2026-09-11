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
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;
using UaLens.Workspace;

namespace UaLens.ViewModels;

/// <summary>
/// Address-space view kinds, mirroring the <c>BrowseViewType</c> enum from
/// the WinForms reference client (<c>BrowseNodeCtrl.cs</c>).  Each value
/// selects a different folder root and the hierarchical reference type
/// followed when expanding children.
/// </summary>
internal enum BrowseViewKind
{
    /// <summary>
    /// Object instance hierarchy under <c>ObjectsFolder</c> (i=85).
    /// </summary>
    Objects,

    /// <summary>
    /// ObjectType hierarchy under <c>ObjectTypesFolder</c> (i=88).
    /// </summary>
    ObjectTypes,

    /// <summary>
    /// VariableType hierarchy under <c>VariableTypesFolder</c> (i=89).
    /// </summary>
    VariableTypes,

    /// <summary>
    /// DataType hierarchy under <c>DataTypesFolder</c> (i=90).
    /// </summary>
    DataTypes,

    /// <summary>
    /// ReferenceType hierarchy under <c>ReferenceTypesFolder</c> (i=91).
    /// </summary>
    ReferenceTypes,

    /// <summary>
    /// Server-defined views under <c>ViewsFolder</c> (i=87).
    /// </summary>
    Views,
}

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
internal sealed partial class BrowserViewModel : ObservableObject
{
    /// <summary>
    /// Currently-selected view kind.  Drives both the root NodeId chosen
    /// by <see cref="Reload"/> and the reference type followed when
    /// expanding children in <see cref="LoadChildrenAsync"/>.  The combo
    /// in <c>AddressSpaceView</c> rebinds the tree via
    /// <see cref="SetViewKindAsync"/> rather than relying on the setter
    /// so callers can await the rebuild.
    /// </summary>
    [ObservableProperty]
    private BrowseViewKind m_currentViewKind = BrowseViewKind.Objects;

    /// <summary>
    /// Controls whether the address-space view shows its filter row
    /// (the "View:" combo and the search box).  Hidden by default to
    /// maximize tree real estate; toggled by the address-space column
    /// header's 🔽 button, the View → Address Space → Filter / View
    /// Combo menu item, or the Ctrl+Shift+F keyboard shortcut.
    /// </summary>
    [ObservableProperty]
    private bool m_showFilters;

    private readonly ILogger m_log;
    private readonly ConnectionService? m_connection;
    private readonly Func<ISession?> m_session;
    private readonly IWorkspaceDispatcher m_dispatcher;
    private int m_treeGeneration;
    /// <summary>
    /// Last <see cref="ManagedSession"/> instance the tree was built against.
    /// The tree is only rebuilt when this changes — so transient
    /// StateChanged firings (e.g. spurious KA blips, settings reapply,
    /// tab switches that re-mirror the active adapter) don't wipe the
    /// user's expanded state.
    /// </summary>
    private ISession? m_lastSessionRef;

    public ObservableCollection<NodeViewModel> Roots { get; } = new();

    public BrowserViewModel(
        ITelemetryContext telemetry,
        ConnectionService connection,
        IWorkspaceDispatcher? dispatcher = null)
        : this(telemetry, () => connection.CurrentSession, dispatcher ??
            (Avalonia.Application.Current is null
                ? InlineWorkspaceDispatcher.Instance
                : new AvaloniaWorkspaceDispatcher()))
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_connection.StateChanged += () => Dispatcher.UIThread.Post(OnConnectionStateChanged);
    }

    public BrowserViewModel(
        ITelemetryContext telemetry,
        Func<ISession?> session,
        IWorkspaceDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        m_log = telemetry.CreateLogger("Browser");
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
        ISession? cur = m_session();
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
    /// the live session reference changes.  Uses <see cref="CurrentViewKind"/>
    /// to choose the root folder.
    /// </summary>
    internal void Reload()
    {
        m_dispatcher.VerifyAccess();
        m_treeGeneration++;
        Roots.Clear();
        if (m_session() is not null && (m_connection is null || m_connection.IsConnected))
        {
            (NodeId rootId, string rootLabel) = GetRootSpec(CurrentViewKind);
            // Children load lazily on expand via LoadChildrenAsync, which
            // uses CurrentViewKind to pick the reference type to follow.
            var root = new NodeViewModel(this, NodeId.Null, rootId, rootLabel, NodeClass.Object);
            Roots.Add(root);
            // Auto-expand the root so the user immediately sees its
            // children (Objects / Types / Views, etc.).
            root.IsExpanded = true;
        }
        m_lastSessionRef = m_session();
    }

    /// <summary>
    /// Switches the address-space view to <paramref name="kind"/>, clears
    /// the existing roots, and re-loads from the new root with the
    /// reference type associated with <paramref name="kind"/>.  No-op if
    /// the view kind is unchanged.
    /// </summary>
    public Task SetViewKindAsync(BrowseViewKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CurrentViewKind == kind)
        {
            return Task.CompletedTask;
        }
        CurrentViewKind = kind;
        return PostToUiAsync(Reload);
    }

    /// <summary>
    /// Maps a <see cref="BrowseViewKind"/> to its root NodeId and the
    /// human-readable label shown at the top of the tree.
    /// </summary>
    private static (NodeId RootId, string Label) GetRootSpec(BrowseViewKind kind) => kind switch
    {
        BrowseViewKind.Objects => (ObjectIds.RootFolder, "Root"),
        BrowseViewKind.ObjectTypes => (ObjectIds.ObjectTypesFolder, "ObjectTypes"),
        BrowseViewKind.VariableTypes => (ObjectIds.VariableTypesFolder, "VariableTypes"),
        BrowseViewKind.DataTypes => (ObjectIds.DataTypesFolder, "DataTypes"),
        BrowseViewKind.ReferenceTypes => (ObjectIds.ReferenceTypesFolder, "ReferenceTypes"),
        BrowseViewKind.Views => (ObjectIds.ViewsFolder, "Views"),
        _ => (ObjectIds.ObjectsFolder, "Objects"),
    };

    internal async Task LoadChildrenAsync(NodeViewModel node)
    {
        m_dispatcher.VerifyAccess();
        if (node.ChildrenLoaded || node.IsPlaceholder || node.IsLoading)
        {
            return;
        }
        node.IsLoading = true;
        node.LoadError = string.Empty;
        int generation = m_treeGeneration;
        ISession? session = m_session();
        var refs = new List<ReferenceDescription>();
        var continuationPoints = new List<ByteString>();
        try
        {
            if (session is null)
            {
                throw new ServiceResultException(StatusCodes.BadNotConnected, "Reconnect, then retry this node.");
            }
            // Build the BrowseDescription(s) to issue against this node.
            // For instance views (Objects, Views) we follow Aggregates+Organizes
            // so folder-style navigation works.  For type views (ObjectTypes,
            // VariableTypes, DataTypes, ReferenceTypes) we follow HasSubtype
            // and constrain the NodeClass to the matching type class so the
            // tree only surfaces type nodes.  Results are deduplicated by
            // absolute NodeId so a child reachable through both refs (rare
            // but possible) only shows once.  HasNotifier / HasEventSource
            // are intentionally excluded — they cause duplicates on
            // event-emitting servers.
            ArrayOf<BrowseDescription> descriptions = BuildBrowseDescriptions(node.NodeId);

            BrowseResponse resp = await session.BrowseAsync(null, null, 0, descriptions, default).ConfigureAwait(false);
            CollectPage(resp.Results, descriptions.Count);
            // Drain continuation points until both browse results are complete.
            while (continuationPoints.Count > 0)
            {
                ArrayOf<ByteString> nextCps = continuationPoints.ToArray();
                BrowseNextResponse next = await session.BrowseNextAsync(null, false, nextCps, default).ConfigureAwait(false);
                continuationPoints.Clear();
                for (int i = 0; i < nextCps.Count; i++)
                {
                    if (i >= next.Results.Count || StatusCode.IsBad(next.Results[i].StatusCode))
                    {
                        continuationPoints.Add(nextCps[i]);
                    }
                }
                CollectPage(next.Results, nextCps.Count);
            }

            await PostToUiAsync(() =>
            {
                if (generation == m_treeGeneration && ReferenceEquals(session, m_session()))
                {
                    ApplyChildren();
                    node.ChildrenLoaded = true;
                    node.HasItems = node.Children.Count > 0;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BrowserViewModelLog.BrowseFailed(m_log, ex, node.NodeId);
            await PostToUiAsync(() =>
            {
                if (generation == m_treeGeneration && ReferenceEquals(session, m_session()))
                {
                    ApplyChildren();
                    node.LoadError = refs.Count == 0
                        ? $"Browse failed: {ex.Message}"
                        : $"Incomplete browse ({refs.Count} references): {ex.Message}";
                    node.EnsureRetryPlaceholder();
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            if (session is not null && continuationPoints.Count > 0)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await session.BrowseNextAsync(null, true, continuationPoints.ToArray(), cleanup.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    BrowserViewModelLog.BrowseFailed(m_log, ex, node.NodeId);
                }
            }
            await PostToUiAsync(() => node.IsLoading = false).ConfigureAwait(false);
        }

        void CollectPage(ArrayOf<BrowseResult> results, int expectedCount)
        {
            foreach (BrowseResult result in results)
            {
                if (result.ContinuationPoint.Length > 0)
                {
                    continuationPoints.Add(result.ContinuationPoint);
                }
                if (!StatusCode.IsBad(result.StatusCode))
                {
                    refs.AddRange(result.References);
                }
            }
            if (results.Count != expectedCount)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Incomplete browse response.");
            }
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode);
                }
            }
        }

        void ApplyChildren()
        {
            node.Children.Clear();
            var seen = new HashSet<NodeId>();
            foreach (ReferenceDescription reference in refs)
            {
                if (reference.NodeId.IsNull || reference.NodeId.IsAbsolute || session is null)
                {
                    continue;
                }
                NodeId child = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (child.IsNull || !seen.Add(child))
                {
                    continue;
                }
                string name = reference.DisplayName.Text ?? reference.BrowseName.Name ?? child.ToString();
                node.Children.Add(new NodeViewModel(this, node.NodeId, child, name, reference.NodeClass));
            }
        }
    }

    /// <summary>
    /// Builds the <see cref="BrowseDescription"/> set used by
    /// <see cref="LoadChildrenAsync"/> for the supplied node, choosing the
    /// reference type(s) to follow from <see cref="CurrentViewKind"/>:
    /// <list type="bullet">
    ///   <item>Objects / Views → <c>Aggregates</c> (with subtypes) ∪ <c>Organizes</c>
    ///     so folder-style navigation works under <c>ObjectsFolder</c> and
    ///     <c>ViewsFolder</c>.</item>
    ///   <item>ObjectTypes / VariableTypes / DataTypes / ReferenceTypes →
    ///     <c>HasSubtype</c> (no further subtype walk) so the tree exposes
    ///     each type's direct subtype hierarchy, filtered to the matching
    ///     <see cref="NodeClass"/>.</item>
    /// </list>
    /// </summary>
    private ArrayOf<BrowseDescription> BuildBrowseDescriptions(NodeId nodeId)
    {
        switch (CurrentViewKind)
        {
            case BrowseViewKind.ObjectTypes:
                return BuildSubtypeDescriptions(nodeId, NodeClass.ObjectType);
            case BrowseViewKind.VariableTypes:
                return BuildSubtypeDescriptions(nodeId, NodeClass.VariableType);
            case BrowseViewKind.DataTypes:
                return BuildSubtypeDescriptions(nodeId, NodeClass.DataType);
            case BrowseViewKind.ReferenceTypes:
                return BuildSubtypeDescriptions(nodeId, NodeClass.ReferenceType);
            case BrowseViewKind.Objects:
            case BrowseViewKind.Views:
            default:
                // Objects/Views browsing follows three reference families
                // so the user can walk both the instance hierarchy AND
                // the type hierarchy reachable from RootFolder/Types:
                //   • Aggregates (+ subtypes) — HasComponent / HasProperty /
                //     HasHistoricalConfiguration for folder-style children;
                //   • Organizes — Types/ObjectTypes/Views folder layout
                //     (e.g. ObjectTypesFolder → BaseObjectType);
                //   • HasSubtype — type-tree navigation once the user
                //     drills into a TypeDefinition (e.g. BaseObjectType →
                //     BaseEventType → SystemEventType …).
                // NodeClassMask=0 means "any class" so type-tree nodes
                // surface alongside instance children. HasNotifier /
                // HasEventSource are intentionally excluded — they cause
                // duplicates on event-emitting servers.
                return new BrowseDescription[]
                {
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Aggregates,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                };
        }
    }

    private static ArrayOf<BrowseDescription> BuildSubtypeDescriptions(NodeId nodeId, NodeClass nodeClass) =>
        new BrowseDescription[]
        {
            new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = (uint)nodeClass,
                ResultMask = (uint)BrowseResultMask.All
            }
        };

    /// <summary>
    /// Browses a node's HasComponent (no subtypes — explicitly excludes
    /// HasProperty) child Variables and returns a flat list.  Used by the
    /// "add all children" path on Object selections.
    /// </summary>
    public async Task<IReadOnlyList<(NodeId NodeId, string DisplayName)>> GetChildVariablesAsync(
        NodeId parent, CancellationToken ct = default)
    {
        if (m_session() is not { } session || parent.IsNull)
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
            BrowserViewModelLog.ChildVariablesFailed(m_log, ex, parent);
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
        if (m_session() is not { } session || relativePaths.Count == 0)
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
                BrowserViewModelLog.RelativePathFailed(m_log, ex, raw);
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
            BrowserViewModelLog.TranslateFailed(m_log, ex);
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
    private Task PostToUiAsync(Action a)
    {
        return m_dispatcher.InvokeAsync(() =>
        {
            a();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Reads the <see cref="Attributes.EventNotifier"/> attribute for an Object
    /// or View node.  Returns the byte value (a bitmask of
    /// <see cref="EventNotifiers"/>) or <c>null</c> if the read failed.
    /// </summary>
    public async Task<byte?> GetEventNotifierAsync(NodeId nodeId, CancellationToken ct = default)
    {
        if (m_session() is not { } session)
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
            BrowserViewModelLog.EventNotifierFailed(m_log, ex, nodeId);
        }
        return null;
    }

    /// <summary>
    /// 🟦 Object · 🧩 ObjectType · 🟢 Variable · 🟣 VariableType · ⚙️ Method
    /// 🔗 ReferenceType · 🧮 DataType · 👁️ View
    /// </summary>
    private static string Glyph(NodeClass cls) => cls switch
    {
        NodeClass.Object => "🟦",
        NodeClass.ObjectType => "🧩",
        NodeClass.Variable => "🟢",
        NodeClass.VariableType => "🟣",
        NodeClass.Method => "⚙️",
        NodeClass.ReferenceType => "🔗",
        NodeClass.DataType => "🧮",
        NodeClass.View => "👁️",
        _ => "•"
    };
}

internal sealed partial class NodeViewModel : ObservableObject
{
    private readonly BrowserViewModel m_owner;

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

    [ObservableProperty]
    private bool m_isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string m_loadError = string.Empty;

    public bool HasLoadError => LoadError.Length > 0;

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
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !ChildrenLoaded && !IsPlaceholder)
        {
            _ = m_owner.LoadChildrenAsync(this);
        }
    }

    [RelayCommand]
    private Task RetryAsync() => m_owner.LoadChildrenAsync(this);

    internal void EnsureRetryPlaceholder()
    {
        if (Children.Count == 0)
        {
            Children.Add(new NodeViewModel(
                m_owner, NodeId, NodeId.Null, "Retry this node to load its children.", NodeClass.Unspecified, true));
        }
        HasItems = true;
    }
}
