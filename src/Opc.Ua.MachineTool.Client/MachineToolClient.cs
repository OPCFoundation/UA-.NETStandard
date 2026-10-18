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

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Provides bounded, subtype-aware workflows over an application-owned connected session.
    /// </summary>
    public sealed partial class MachineToolClient : IAsyncDisposable
    {
        /// <summary>
        /// Creates a client without taking ownership of the supplied session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The observation clock, or the system clock when omitted.</param>
        public MachineToolClient(
            ISession session, ITelemetryContext telemetry, TimeProvider? timeProvider = null)
            : this(session, telemetry, ownsSession: false, timeProvider)
        {
        }

        /// <summary>
        /// Creates a facade with explicit ownership; only independently created sessions may transfer ownership.
        /// </summary>
        internal MachineToolClient(
            ISession session, ITelemetryContext telemetry, bool ownsSession, TimeProvider? timeProvider = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_ownsSession = ownsSession;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            MachineToolParts available = AvailableParts;
            if (available != MachineToolParts.None)
            {
                Session.MessageContext.Factory.Builder.AddMachineToolTypeSystem(available).Commit();
            }
        }

        /// <summary>
        /// Gets the application-owned session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the published model namespaces, not a claim that their optional instance capabilities exist.
        /// </summary>
        public MachineToolParts AvailableParts =>
            (Session.NamespaceUris.GetIndex(Namespaces.MachineTool) >= 0
                ? MachineToolParts.MachineTool : MachineToolParts.None) |
            (Session.NamespaceUris.GetIndex(WMTP.Namespaces.WMTP) >= 0
                ? MachineToolParts.Wmtp : MachineToolParts.None);

        /// <summary>
        /// Cancels and drains facade-owned observations while leaving borrowed sessions intact.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (m_observationGate)
            {
                if (m_disposal != null)
                {
                    return new ValueTask(m_disposal);
                }
                Volatile.Write(ref m_disposed, 1);
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
                if (m_activeObservations == 0)
                {
                    m_observationsDrained.TrySetResult(true);
                }
            }
            _ = CompleteDisposalAsync(completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Cancels owned observations and joins their subscription cleanup before releasing an owned session.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            try
            {
                m_observationShutdown.Cancel();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            await m_observationsDrained.Task.ConfigureAwait(false);
            if (m_ownsSession)
            {
                try
                {
                    await Session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
                try
                {
                    Session.Dispose();
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            m_observationShutdown.Dispose();
            if (failures.Count == 0)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(failures.Count == 1 ? failures[0] :
                    new AggregateException("Client observation and session cleanup failed.", failures));
            }
        }

        /// <summary>
        /// Admits a bounded observation atomically with respect to facade disposal.
        /// </summary>
        private CancellationTokenSource BeginObservation(CancellationToken cancellationToken)
        {
            lock (m_observationGate)
            {
                EnsureActive();
                var operation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_observationShutdown.Token);
                m_activeObservations++;
                return operation;
            }
        }

        /// <summary>
        /// Releases observation admission only after its server subscription cleanup has completed.
        /// </summary>
        private void EndObservation()
        {
            lock (m_observationGate)
            {
                m_activeObservations--;
                if (m_activeObservations == 0 && m_disposed != 0)
                {
                    m_observationsDrained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Discovers machines and wireless peripherals through both standard discovery folders.
        /// </summary>
        /// <param name="maximumDevices">The maximum accepted inventory, from one through 4096.</param>
        /// <param name="cancellationToken">Cancels discovery and releases continuation points.</param>
        /// <returns>The unique, bounded inventory.</returns>
        public async ValueTask<ArrayOf<MachineToolDeviceEntry>> DiscoverAsync(
            int maximumDevices = 128,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (maximumDevices is < 1 or > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDevices));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<MachineToolDeviceEntry>();
            var seen = new HashSet<NodeId>();
            ArrayOf<NodeId> roots =
            [
                OptionalNode(Opc.Ua.Machinery.Objects.Machines, Opc.Ua.Machinery.Namespaces.Machinery),
                OptionalNode(Opc.Ua.Di.Objects.DeviceSet, Opc.Ua.Di.Namespaces.OpcUaDi)
            ];
            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                NodeId root = roots[rootIndex];
                if (root.IsNull)
                {
                    continue;
                }
                await foreach (ReferenceDescription reference in BrowseChildrenAsync(root, cancellationToken)
                    .ConfigureAwait(false))
                {
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (node.IsNull || type.IsNull || !seen.Add(node))
                    {
                        continue;
                    }
                    MachineToolDeviceKind? kind = await ClassifyAsync(type, cancellationToken).ConfigureAwait(false);
                    if (!kind.HasValue)
                    {
                        continue;
                    }
                    if (entries.Count == maximumDevices)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                            "The discovered inventory exceeds the requested limit of {0} devices.", maximumDevices);
                    }
                    entries.Add(new MachineToolDeviceEntry(
                        node, reference.BrowseName, reference.DisplayName, type, kind.Value));
                }
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Returns the generated advanced proxy for a machine without duplicating its method protocol.
        /// </summary>
        /// <param name="machineId">The actual machine instance identifier.</param>
        /// <returns>The generated proxy.</returns>
        public MachineToolTypeClient GetMachineProxy(NodeId machineId)
        {
            EnsureActive();
            RequireNode(machineId);
            if ((AvailableParts & MachineToolParts.MachineTool) == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The endpoint does not publish the MachineTool namespace.");
            }
            return new MachineToolTypeClient(Session, machineId, Telemetry);
        }

        /// <summary>
        /// Returns the generated peripheral proxy without requiring the MachineTool namespace.
        /// </summary>
        /// <param name="peripheralId">The wireless peripheral identifier.</param>
        /// <returns>The independent WMTP proxy.</returns>
        public WMTP.WirelessMachineToolPeripheralTypeClient GetPeripheralProxy(NodeId peripheralId)
        {
            EnsureActive();
            RequireNode(peripheralId);
            if ((AvailableParts & MachineToolParts.Wmtp) == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The endpoint does not publish the wireless peripheral namespace.");
            }
            return new WMTP.WirelessMachineToolPeripheralTypeClient(Session, peripheralId, Telemetry);
        }

        /// <summary>
        /// Reads mandatory machine monitoring and active-program samples with their original quality and time.
        /// </summary>
        /// <param name="machineId">The machine instance identifier.</param>
        /// <param name="cancellationToken">Cancels the workflow.</param>
        /// <returns>The samples; absent optional fields have DataValue.IsNull set.</returns>
        public async ValueTask<MachineToolReadSnapshot> ReadSnapshotAsync(
            NodeId machineId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<NodeId> nodes = await ResolveSnapshotNodesAsync(machineId, cancellationToken).ConfigureAwait(false);
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!nodes[index].IsNull)
                {
                    requests.Add(new ReadValueId { NodeId = nodes[index], AttributeId = Attributes.Value });
                    positions.Add(index);
                }
            }
            ArrayOf<ReadValueId> requestedValues = requests.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, requestedValues, cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requestedValues);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requestedValues);
            var samples = new DataValue[nodes.Count];
            for (int index = 0; index < positions.Count; index++)
            {
                samples[positions[index]] = response.Results[index];
            }
            return new MachineToolReadSnapshot
            {
                NodeId = machineId,
                OperationMode = samples[0],
                FeedOverride = samples[1],
                IsWarmUp = samples[2],
                ActiveProgramName = samples[3],
                ActiveProgramState = samples[4]
            };
        }

        /// <summary>
        /// Resolves the same typed sample surfaces for reads and subscriptions.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolveSnapshotNodesAsync(
            NodeId machineId, CancellationToken cancellationToken)
        {
            MachineToolTypeClient machine = GetMachineProxy(machineId);
            MonitoringTypeClient monitoring = await machine.GetMonitoringAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw MissingMandatory(BrowseNames.Monitoring);
            MachineOperationMonitoringTypeClient operation = await monitoring
                .GetMachineToolAsync(Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw MissingMandatory(BrowseNames.MachineTool);
            ProductionTypeClient production = await machine.GetProductionAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw MissingMandatory(BrowseNames.Production);
            ProductionActiveProgramTypeClient program = await production
                .GetActiveProgramAsync(Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw MissingMandatory(BrowseNames.ActiveProgram);
            ProductionProgramStateMachineTypeClient state = await program.GetStateAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw MissingMandatory(BrowseNames.State);
            ArrayOf<NodeId> operationValues = await ResolveChildNodesAsync(operation.ObjectId,
                [
                    Name(BrowseNames.OperationMode, Namespaces.MachineTool),
                    Name(BrowseNames.FeedOverride, Namespaces.MachineTool),
                    Name(BrowseNames.IsWarmUp, Namespaces.MachineTool)
                ], cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> programValues = await ResolveChildNodesAsync(
                program.ObjectId, [Name(BrowseNames.Name, Namespaces.MachineTool)], cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NodeId> stateValues = await ResolveChildNodesAsync(
                state.ObjectId, [new QualifiedName(Opc.Ua.BrowseNames.CurrentState)], cancellationToken)
                .ConfigureAwait(false);
            if (operationValues[0].IsNull || programValues[0].IsNull || stateValues[0].IsNull)
            {
                throw MissingMandatory("monitoring or active-program value");
            }
            return [operationValues[0], operationValues[1], operationValues[2], programValues[0], stateValues[0]];
        }

        /// <summary>
        /// Resolves declared variables while preserving absent-member versus failed-request distinctions.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolveChildNodesAsync(
            NodeId parent,
            ArrayOf<QualifiedName> names,
            CancellationToken cancellationToken)
        {
            var paths = new BrowsePath[names.Count];
            for (int index = 0; index < names.Count; index++)
            {
                paths[index] = new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Aggregates,
                                IncludeSubtypes = true,
                                TargetName = names[index]
                            }
                        ]
                    }
                };
            }
            ArrayOf<BrowsePath> requestedPaths = paths.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse translated = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, requestedPaths, cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(translated.Results, requestedPaths);
            ClientBase.ValidateDiagnosticInfos(translated.DiagnosticInfos, requestedPaths);
            var nodes = new NodeId[names.Count];
            for (int index = 0; index < translated.Results.Count; index++)
            {
                BrowsePathResult result = translated.Results[index];
                if (result.StatusCode == StatusCodes.BadNoMatch || result.StatusCode == StatusCodes.BadNotFound)
                {
                    continue;
                }
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode);
                }
                if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "A successful variable translation must resolve exactly one complete target.");
                }
                NodeId target = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
                RequireNode(target);
                nodes[index] = target;
            }
            return nodes.ToArrayOf();
        }

        /// <summary>
        /// Browses pages through the shared continuation-owning browser.
        /// </summary>
        private async IAsyncEnumerable<ReferenceDescription> BrowseChildrenAsync(
            NodeId parent,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var browser = new Browser(Session);
            int examined = 0;
            await foreach (BrowseResult page in browser.BrowseStreamAsync(null, null,
                [
                    new BrowseDescription
                    {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false))
            {
                if (StatusCode.IsBad(page.StatusCode))
                {
                    throw new ServiceResultException(page.StatusCode);
                }
                for (int index = 0; index < page.References.Count; index++)
                {
                    if (++examined > 100_000)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "Discovery exceeded the bounded browse-reference budget.");
                    }
                    yield return page.References[index];
                }
            }
        }

        /// <summary>
        /// Recognizes actual standard and vendor subtypes independently for each available model.
        /// </summary>
        private async ValueTask<MachineToolDeviceKind?> ClassifyAsync(NodeId type, CancellationToken cancellationToken)
        {
            NodeId machineType = OptionalNode(ObjectTypes.MachineToolType, Namespaces.MachineTool);
            if (!machineType.IsNull &&
                await Session.NodeCache.IsTypeOfAsync(type, machineType, cancellationToken).ConfigureAwait(false))
            {
                return MachineToolDeviceKind.MachineTool;
            }
            NodeId peripheralType = OptionalNode(
                WMTP.ObjectTypes.WirelessMachineToolPeripheralType, WMTP.Namespaces.WMTP);
            if (!peripheralType.IsNull &&
                await Session.NodeCache.IsTypeOfAsync(type, peripheralType, cancellationToken).ConfigureAwait(false))
            {
                return MachineToolDeviceKind.WirelessPeripheral;
            }
            return null;
        }

        /// <summary>
        /// Maps a model identifier only when its namespace is present.
        /// </summary>
        private NodeId OptionalNode(uint identifier, string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            return index < 0 ? NodeId.Null : new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Qualifies a declared browse name without substituting namespace zero.
        /// </summary>
        private QualifiedName Name(string name, string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "A required model namespace is absent.");
            }
            return new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Rejects null identifiers before making a request.
        /// </summary>
        private static void RequireNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A non-null instance identifier is required.", nameof(nodeId));
            }
        }

        /// <summary>
        /// Reports a missing mandatory declaration as a model error, not optional absence.
        /// </summary>
        private static ServiceResultException MissingMandatory(string name)
        {
            return new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                $"The instance does not provide mandatory MachineTool member '{name}'.");
        }

        /// <summary>
        /// Refuses new workflows after this facade is disposed.
        /// </summary>
        private void EnsureActive()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MachineToolClient));
            }
        }

        /// <summary>Whether the client factory opened the session for this facade.</summary>
        private readonly bool m_ownsSession;

        /// <summary>The observation clock, injectable for deterministic bounded workflows.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>Serializes observation admission and disposal without covering application callbacks.</summary>
        private readonly Lock m_observationGate = new();

        /// <summary>Cancels only observations created by this facade.</summary>
        private readonly CancellationTokenSource m_observationShutdown = new();

        /// <summary>Signals when every admitted observation has released its subscription.</summary>
        private readonly TaskCompletionSource<bool> m_observationsDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The shared completion returned to concurrent asynchronous disposers.</summary>
        private Task? m_disposal;

        /// <summary>The number of observations which still own resources.</summary>
        private int m_activeObservations;

        /// <summary>Whether the facade has been disposed.</summary>
        private int m_disposed;
    }
}
