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
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;
using UaLens.NodeSets;
using UaLens.Workspace;

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
    private readonly Func<ISession?> m_session;
    private readonly Func<NodeSetAddressSpace?>? m_offlineSource;
    private readonly IWorkspaceDispatcher m_dispatcher;
    private readonly ILogger m_log;
    private Action? m_cancelCurrent;
    private CancellationToken m_currentRequest;
    private bool m_disposed;

    public ObservableCollection<ReferenceRow> Rows { get; } = new();

    [ObservableProperty]
    private string m_header = "(no node selected)";

    public ReferencesViewModel(
        ITelemetryContext telemetry,
        ConnectionService connection,
        IWorkspaceDispatcher? dispatcher = null,
        Func<NodeSetAddressSpace?>? offlineSource = null)
        : this(telemetry, () => connection.CurrentSession, dispatcher ??
            (Avalonia.Application.Current is null
                ? InlineWorkspaceDispatcher.Instance
                : new AvaloniaWorkspaceDispatcher()))
    {
        ArgumentNullException.ThrowIfNull(connection);
        m_offlineSource = offlineSource;
    }

    public ReferencesViewModel(
        ITelemetryContext telemetry,
        Func<ISession?> session,
        IWorkspaceDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        m_log = telemetry.CreateLogger("References");
    }

    public void Clear()
    {
        m_dispatcher.VerifyAccess();
        m_cancelCurrent?.Invoke();
        m_cancelCurrent = null;
        m_currentRequest = default;
        Rows.Clear();
        Header = "(no node selected)";
    }

    public async Task LoadAsync(NodeId nodeId, NodeClass nodeClass)
    {
        m_dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_cancelCurrent?.Invoke();
        using var request = new CancellationTokenSource();
        m_cancelCurrent = request.Cancel;
        CancellationToken ct = request.Token;
        m_currentRequest = ct;

        Header = $"{Glyph(nodeClass)} {nodeId}  ({nodeClass})";
        Rows.Clear();

        try
        {
            ISession? session = m_session();
            NodeSetAddressSpace? offline = m_offlineSource?.Invoke();
            if (session is null && offline is null)
            {
                Rows.Add(new ReferenceRow("·", "(disconnected)", string.Empty, string.Empty, string.Empty));
                return;
            }
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
            BrowseResponse resp = offline is not null
                ? await offline.BrowseAsync(descriptions, ct).ConfigureAwait(false)
                : await session!.BrowseAsync(null, null, 0, descriptions, ct).ConfigureAwait(false);
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
                BrowseNextResponse next = await session!.BrowseNextAsync(null, false, cps, ct).ConfigureAwait(false);
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
                ReadResponse rtRead = offline is not null
                    ? await offline.ReadAsync([.. idList], ct).ConfigureAwait(false)
                    : await session!.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        new ArrayOf<ReadValueId>(idList.ToArray()), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
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

            var rows = new List<ReferenceRow>();
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
                rows.Add(new ReferenceRow(direction, refType, target, targetName, r.NodeClass.ToString()));
            }
            if (rows.Count == 0)
            {
                rows.Add(new ReferenceRow("·", "(no references)", string.Empty, string.Empty, string.Empty));
            }
            await PublishAsync(request, rows).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Selection moved on — drop the partial list.
        }
        catch (Exception ex)
        {
            ReferencesViewModelLog.ReferenceBrowseFailed(m_log, ex, nodeId);
            await PublishAsync(request,
                [new ReferenceRow("!", "(browse failed)", ex.Message, string.Empty, string.Empty)])
                .ConfigureAwait(false);
        }
        finally
        {
            await m_dispatcher.InvokeAsync(() =>
            {
                if (m_currentRequest == ct)
                {
                    m_cancelCurrent = null;
                    m_currentRequest = default;
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    private Task PublishAsync(CancellationTokenSource request, List<ReferenceRow> rows)
    {
        return m_dispatcher.InvokeAsync(() =>
        {
            if (!m_disposed && !request.IsCancellationRequested && m_currentRequest == request.Token)
            {
                Rows.Clear();
                foreach (ReferenceRow row in rows)
                {
                    Rows.Add(row);
                }
            }
            return Task.CompletedTask;
        });
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
        m_dispatcher.VerifyAccess();
        m_disposed = true;
        m_cancelCurrent?.Invoke();
        m_cancelCurrent = null;
        m_currentRequest = default;
    }
}

internal sealed record ReferenceRow(
    string Direction, string ReferenceType, string TargetNodeId,
    string TargetBrowseName, string TargetNodeClass);
