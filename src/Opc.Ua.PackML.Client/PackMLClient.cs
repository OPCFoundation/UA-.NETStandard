/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// A typed OPC 30050 client borrowing a connected session. It never closes or disposes the session.
    /// </summary>
    public sealed partial class PackMLClient
    {
        /// <summary>
        /// Creates a client and registers the generated PackML structure codecs.
        /// </summary>
        public PackMLClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (!Session.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.PackMLAlarmDataType))
            {
                Session.MessageContext.Factory.Builder.AddOpcUaPackML().Commit();
            }
        }

        /// <summary>
        /// Gets the borrowed session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the connected server publishes the PackML namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.PackML) >= 0;

        /// <summary>
        /// Gets the server-qualified PackMLBaseObjectType identifier, or a null NodeId.
        /// </summary>
        public NodeId BaseObjectTypeId
        {
            get
            {
                int index = Session.NamespaceUris.GetIndex(Namespaces.PackML);
                return index < 0 ? NodeId.Null : new NodeId(ObjectTypes.PackMLBaseObjectType, (ushort)index);
            }
        }

        /// <summary>
        /// Discovers PackML units and vendor subtypes below Objects or a supplied root, with cycle detection.
        /// </summary>
        /// <param name="root">A null NodeId selects the standard Objects folder.</param>
        /// <param name="maxDepth">The maximum number of hierarchical edges traversed.</param>
        /// <param name="cancellationToken">Cancels browsing.</param>
        public async ValueTask<ArrayOf<PackMLUnit>> DiscoverAsync(
            NodeId root = default,
            int maxDepth = 32,
            CancellationToken cancellationToken = default)
        {
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupported)
            {
                return ArrayOf.Empty<PackMLUnit>();
            }
            var units = new List<PackMLUnit>();
            var visited = new HashSet<NodeId>();
            var found = new HashSet<NodeId>();
            var pending = new Queue<(NodeId Node, int Depth)>();
            pending.Enqueue((root.IsNull ? Opc.Ua.ObjectIds.ObjectsFolder : root, 0));
            var browser = new Browser(Session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (int)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            });
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (NodeId parent, int depth) = pending.Dequeue();
                if (!visited.Add(parent) || depth >= maxDepth)
                {
                    continue;
                }
                ArrayOf<NodeId> parents = [parent];
                ResultSet<ArrayOf<ReferenceDescription>> result =
                    await browser.BrowseAsync(parents, cancellationToken).ConfigureAwait(false);
                if (result.Errors.Count > 0 && StatusCode.IsBad(result.Errors[0].StatusCode))
                {
                    throw new ServiceResultException(result.Errors[0]);
                }
                if (result.Results.Count == 0)
                {
                    continue;
                }
                ArrayOf<ReferenceDescription> references = result.Results[0];
                for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
                {
                    ReferenceDescription reference = references[referenceIndex];
                    if (reference.NodeId.ServerIndex != 0)
                    {
                        continue;
                    }
                    var node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (node.IsNull || visited.Contains(node))
                    {
                        continue;
                    }
                    if (!type.IsNull && !found.Contains(node) &&
                        await Session.NodeCache.IsTypeOfAsync(type, BaseObjectTypeId, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        units.Add(new PackMLUnit(node, reference.BrowseName, reference.DisplayName, type));
                        found.Add(node);
                    }
                    pending.Enqueue((node, depth + 1));
                }
            }
            return units.ToArrayOf();
        }

        /// <summary>
        /// Reads one typed field, preserving missing fields, bad quality and both timestamps.
        /// </summary>
        /// <typeparam name="T">The field's decoded value type.</typeparam>
        public async ValueTask<PackMLValue<T>> ReadAsync<T>(
            NodeId unit,
            PackMLField<T> field,
            CancellationToken cancellationToken = default)
        {
            if (field is null)
            {
                throw new ArgumentNullException(nameof(field));
            }
            ArrayOf<string> paths = [field.Path];
            ArrayOf<NodeId> nodes = await ResolvePathsAsync(unit, paths, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
            return field.Decode(nodes[0], values[0], Session.MessageContext);
        }

        /// <summary>
        /// Reads all standard status fields and nested current states in one Read request.
        /// This is a batch read, not an atomic equipment snapshot.
        /// </summary>
        public async ValueTask<PackMLStatusSnapshot> ReadStatusAsync(
            NodeId unit,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<string> paths =
            [
                PackMLFields.Status.CurMachSpeed.Path,
                PackMLFields.Status.MachSpeed.Path,
                PackMLFields.Status.EquipmentBlocked.Path,
                PackMLFields.Status.EquipmentStarved.Path,
                PackMLFields.Status.MaterialInterlock.Path,
                PackMLFields.Status.MaterialInterlocked.Path,
                PackMLFields.Status.Parameter.Path,
                PackMLFields.Status.Product.Path,
                PackMLFields.Status.RemoteParameter.Path,
                PackMLFields.Status.StateChangeInProcess.Path,
                PackMLFields.Status.StateRequested.Path,
                PackMLFields.Status.UnitModeChangeInProcess.Path,
                PackMLFields.Status.UnitModeCurrent.Path,
                PackMLFields.Status.UnitModeRequested.Path,
                PackMLFields.Status.UnitSupportedModes.Path,
                PackMLFields.State.BaseState.Path,
                PackMLFields.State.MachineState.Path,
                PackMLFields.State.ExecuteState.Path,
                PackMLFields.State.BaseAvailableStates.Path,
                PackMLFields.State.MachineAvailableStates.Path,
                PackMLFields.State.ExecuteAvailableStates.Path,
                PackMLFields.State.BaseAvailableTransitions.Path,
                PackMLFields.State.MachineAvailableTransitions.Path,
                PackMLFields.State.ExecuteAvailableTransitions.Path
            ];
            ArrayOf<NodeId> nodes = await ResolvePathsAsync(unit, paths, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
            return new PackMLStatusSnapshot(nodes, values, Session.MessageContext);
        }

        /// <summary>
        /// Reads all standard administration fields in one Read request.
        /// </summary>
        public async ValueTask<PackMLAdminSnapshot> ReadAdminAsync(
            NodeId unit,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<string> paths =
            [
                PackMLFields.Admin.AccTimeSinceReset.Path,
                PackMLFields.Admin.Alarm.Path,
                PackMLFields.Admin.AlarmExtent.Path,
                PackMLFields.Admin.AlarmHistory.Path,
                PackMLFields.Admin.AlarmHistoryExtent.Path,
                PackMLFields.Admin.MachDesignSpeed.Path,
                PackMLFields.Admin.ModeCumulativeTime.Path,
                PackMLFields.Admin.ModeCurrentTime.Path,
                PackMLFields.Admin.Parameter.Path,
                PackMLFields.Admin.ProdConsumedCount.Path,
                PackMLFields.Admin.ProdDefectiveCount.Path,
                PackMLFields.Admin.ProdProcessedCount.Path,
                PackMLFields.Admin.StateCumulativeTime.Path,
                PackMLFields.Admin.StateCurrentTime.Path,
                PackMLFields.Admin.StopReason.Path,
                PackMLFields.Admin.StopReasonExtent.Path,
                PackMLFields.Admin.Warning.Path,
                PackMLFields.Admin.WarningExtent.Path
            ];
            ArrayOf<NodeId> nodes = await ResolvePathsAsync(unit, paths, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
            return new PackMLAdminSnapshot(nodes, values, Session.MessageContext);
        }

        /// <summary>
        /// Observes a typed field using a shared subscription. Enumeration owns only its monitored item.
        /// Absent fields fail with BadNotSupported instead of silently appearing to complete.
        /// </summary>
        /// <typeparam name="T">The field's decoded value type.</typeparam>
        public IAsyncEnumerable<PackMLValue<T>> ObserveAsync<T>(
            NodeId unit,
            PackMLField<T> field,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (field is null)
            {
                throw new ArgumentNullException(nameof(field));
            }
            return ObserveCoreAsync(unit, field, streaming, options, cancellationToken);
        }

        /// <summary>
        /// Waits for a Good-quality observed value satisfying a predicate, with a mandatory finite timeout.
        /// This never sends or retries a command and does not imply causation by an earlier command.
        /// </summary>
        /// <typeparam name="T">The field's decoded value type.</typeparam>
        public async ValueTask<PackMLValue<T>> WaitForAsync<T>(
            NodeId unit,
            PackMLField<T> field,
            Func<T, bool> predicate,
            TimeSpan timeout,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (field is null)
            {
                throw new ArgumentNullException(nameof(field));
            }
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await foreach (PackMLValue<T> value in ObserveAsync(
                    unit, field, streaming, options, timeoutSource.Token).ConfigureAwait(false))
                {
                    if (value.IsGood && predicate(value.Value))
                    {
                        return value;
                    }
                }
                timeoutSource.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The PackML observation did not reach the requested value within the timeout.");
            }
            throw new ServiceResultException(StatusCodes.BadNoData, "The PackML observation ended without a match.");
        }

        /// <summary>
        /// Observes an already validated field through the caller's shared subscription.
        /// </summary>
        /// <typeparam name="T">The field's decoded value type.</typeparam>
        private async IAsyncEnumerable<PackMLValue<T>> ObserveCoreAsync<T>(
            NodeId unit,
            PackMLField<T> field,
            IStreamingSubscription? streaming,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArrayOf<string> paths = [field.Path];
            ArrayOf<NodeId> nodes = await ResolvePathsAsync(unit, paths, cancellationToken).ConfigureAwait(false);
            if (nodes[0].IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The PackML field is not exposed.");
            }
            IStreamingSubscription subscription = streaming ??
                (Session is ManagedSession managed
                    ? managed.DefaultStreaming
                    : throw new InvalidOperationException("Supply a shared subscription for an unmanaged session."));
            await foreach (DataValueChange change in subscription
                .SubscribeDataChangesAsync(nodes[0], options, cancellationToken).ConfigureAwait(false))
            {
                yield return field.Decode(nodes[0], change.Value, Session.MessageContext);
            }
        }

        /// <summary>
        /// Resolves qualified PackML paths without assuming any namespace index.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolvePathsAsync(
            NodeId unit,
            ArrayOf<string> paths,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (unit.IsNull)
            {
                throw new ArgumentException("A PackML instance NodeId is required.", nameof(unit));
            }
            var nodes = new NodeId[paths.Count];
            int ns = Session.NamespaceUris.GetIndex(Namespaces.PackML);
            if (ns < 0)
            {
                return nodes.ToArrayOf();
            }
            var requests = new BrowsePath[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                string[] segments = paths[i].Split('/');
                var elements = new RelativePathElement[segments.Length];
                for (int j = 0; j < segments.Length; j++)
                {
                    bool standard = segments[j].Length > 0 && segments[j][0] == ':';
                    elements[j] = new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(
                            standard ? segments[j].Substring(1) : segments[j], standard ? (ushort)0 : (ushort)ns)
                    };
                }
                requests[i] = new BrowsePath
                {
                    StartingNode = unit,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                };
            }
            ArrayOf<BrowsePath> requestArray = requests.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, requestArray, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            ClientBase.ValidateResponse(response.Results, requestArray);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requestArray);
            for (int i = 0; i < nodes.Length; i++)
            {
                BrowsePathResult result = response.Results[i];
                if (StatusCode.IsGood(result.StatusCode))
                {
                    foreach (BrowsePathTarget target in result.Targets)
                    {
                        if (target.RemainingPathIndex == uint.MaxValue && target.TargetId.ServerIndex == 0)
                        {
                            nodes[i] = ExpandedNodeId.ToNodeId(target.TargetId, Session.NamespaceUris);
                            break;
                        }
                    }
                }
                else if (result.StatusCode != StatusCodes.BadNoMatch &&
                    result.StatusCode != StatusCodes.BadNodeIdUnknown &&
                    result.StatusCode != StatusCodes.BadBrowseNameInvalid)
                {
                    throw new ServiceResultException(result.StatusCode);
                }
            }
            return nodes.ToArrayOf();
        }

        /// <summary>
        /// Reads present nodes with both timestamps, retaining absent-node positions.
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> ReadValuesAsync(ArrayOf<NodeId> nodes, CancellationToken ct)
        {
            var values = new DataValue[nodes.Count];
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                values[i] = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                if (!nodes[i].IsNull)
                {
                    positions.Add(i);
                    requests.Add(new ReadValueId { NodeId = nodes[i], AttributeId = Attributes.Value });
                }
            }
            if (requests.Count > 0)
            {
                ArrayOf<ReadValueId> requestArray = requests.ToArrayOf();
                ReadResponse response = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, requestArray, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(response.ResponseHeader.ServiceResult))
                {
                    throw new ServiceResultException(response.ResponseHeader.ServiceResult);
                }
                ClientBase.ValidateResponse(response.Results, requestArray);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requestArray);
                for (int i = 0; i < positions.Count; i++)
                {
                    values[positions[i]] = response.Results[i];
                }
            }
            return values.ToArrayOf();
        }
    }
}
