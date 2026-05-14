/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.ViewModels;

/// <summary>
/// View model backing the per-node references panel.  When the address-space
/// tree selection changes, <see cref="LoadAsync"/> issues a single
/// <c>BrowseDirection.Both</c> browse against the root <c>References</c>
/// reference type (IncludeSubtypes=true) and renders every link the node
/// participates in — forward and inverse — as a <see cref="ReferenceRow"/>.
/// Keeps a per-load <see cref="CancellationTokenSource"/> so a fast tree
/// selection change cancels in-flight work.
/// </summary>
internal sealed partial class ReferencesViewModel : ObservableObject, IDisposable
{
    private readonly ConnectionService m_connection;
    private readonly ILogger m_log;
    private CancellationTokenSource? m_cts;

    public ObservableCollection<ReferenceRow> Rows { get; } = new();

    [ObservableProperty]
    private string m_header = "(no node selected)";

    public ReferencesViewModel(ITelemetryContext telemetry, ConnectionService connection)
    {
        m_connection = connection;
        m_log = telemetry.CreateLogger("References");
    }

    public void Clear()
    {
        m_cts?.Cancel();
        Rows.Clear();
        Header = "(no node selected)";
    }

    public async Task LoadAsync(NodeId nodeId, NodeClass nodeClass)
    {
        m_cts?.Cancel();
        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;

        Header = $"{Glyph(nodeClass)} {nodeId}  ({nodeClass})";
        Rows.Clear();

        if (m_connection.Session is not { } session)
        {
            Rows.Add(new ReferenceRow("·", "(disconnected)", string.Empty, string.Empty, string.Empty));
            return;
        }

        try
        {
            ArrayOf<BrowseDescription> descriptions = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Both,
                    ReferenceTypeId = ReferenceTypeIds.References,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
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
                BrowseNextResponse next = await session.BrowseNextAsync(null, false, cps, ct).ConfigureAwait(false);
                cp = ByteString.Empty;
                if (next.Results.Count > 0 && !StatusCode.IsBad(next.Results[0].StatusCode))
                {
                    refs.AddRange(next.Results[0].References);
                    cp = next.Results[0].ContinuationPoint;
                }
            }
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // Resolve reference-type browse names from a single batched read so
            // we don't have to look every reference up individually.
            var refTypeIds = new HashSet<NodeId>();
            foreach (ReferenceDescription r in refs)
            {
                if (!r.ReferenceTypeId.IsNull)
                {
                    refTypeIds.Add(r.ReferenceTypeId);
                }
            }
            var refTypeNames = new Dictionary<NodeId, string>();
            if (refTypeIds.Count > 0)
            {
                var idList = new List<ReadValueId>(refTypeIds.Count);
                foreach (NodeId rt in refTypeIds)
                {
                    idList.Add(new ReadValueId { NodeId = rt, AttributeId = Attributes.BrowseName });
                }
                ReadResponse rtRead = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    new ArrayOf<ReadValueId>(idList.ToArray()), ct).ConfigureAwait(false);
                int i = 0;
                foreach (NodeId rt in refTypeIds)
                {
                    if (i < rtRead.Results.Count
                        && !StatusCode.IsBad(rtRead.Results[i].StatusCode)
                        && rtRead.Results[i].WrappedValue.TryGetValue(out QualifiedName qn))
                    {
                        refTypeNames[rt] = qn.Name ?? rt.ToString() ?? string.Empty;
                    }
                    else
                    {
                        refTypeNames[rt] = rt.ToString() ?? string.Empty;
                    }
                    i++;
                }
            }

            foreach (ReferenceDescription r in refs)
            {
                string direction = r.IsForward ? "→" : "←";
                string refType = refTypeNames.TryGetValue(r.ReferenceTypeId, out string? n)
                    ? n
                    : r.ReferenceTypeId.ToString() ?? string.Empty;
                string target = r.NodeId.ToString() ?? string.Empty;
                string targetName = !r.DisplayName.IsNull
                    ? r.DisplayName.Text ?? string.Empty
                    : (!r.BrowseName.IsNull ? r.BrowseName.Name ?? string.Empty : string.Empty);
                Rows.Add(new ReferenceRow(direction, refType, target, targetName, r.NodeClass.ToString()));
            }
            if (Rows.Count == 0)
            {
                Rows.Add(new ReferenceRow("·", "(no references)", string.Empty, string.Empty, string.Empty));
            }
        }
        catch (OperationCanceledException)
        {
            // Selection moved on — drop the partial list.
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Failed to browse references for {NodeId}", nodeId);
            Rows.Add(new ReferenceRow("!", "(browse failed)", ex.Message, string.Empty, string.Empty));
        }
    }

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

    public void Dispose()
    {
        m_cts?.Cancel();
        m_cts?.Dispose();
        m_cts = null;
    }
}

internal sealed record ReferenceRow(
    string Direction, string ReferenceType, string TargetNodeId,
    string TargetBrowseName, string TargetNodeClass);
