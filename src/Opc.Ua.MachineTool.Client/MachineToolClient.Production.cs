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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Reads the actual retained section 8.4 production model through generated proxies and the shared browser.
        /// Original DataValues preserve bad or uncertain equipment quality; access and service failures remain errors.
        /// This workflow never starts, aborts or modifies a job.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineToolProductionReadSnapshot> ReadProductionAsync(
            NodeId machineId, MachineToolProductionReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            MachineToolProductionReadOptions settings = options ?? new();
            settings.Validate();
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                CancellationToken ct = operation.Token;
                ct.ThrowIfCancellationRequested();
                var budget = new ProductionReadBudget(settings, Session.MessageContext);
                MachineToolTypeClient machine = GetMachineProxy(machineId);
                ProductionTypeClient production = await machine.GetProductionAsync(Telemetry, ct)
                    .ConfigureAwait(false) ??
                    throw MissingMandatory(BrowseNames.Production);
                ProductionActiveProgramTypeClient active = await production.GetActiveProgramAsync(Telemetry, ct)
                    .ConfigureAwait(false) ??
                    throw MissingMandatory(BrowseNames.ActiveProgram);
                MachineToolProgramReadSnapshot program =
                    await ReadProductionProgramAsync(active.ObjectId, true, budget, ct).ConfigureAwait(false);
                ProductionJobListTypeClient? plan = await production.GetProductionPlanAsync(Telemetry, ct)
                    .ConfigureAwait(false);
                DataValue version = plan == null ? DataValue.Null :
                    await ReadInventoryVersionAsync(plan.ObjectId, ct).ConfigureAwait(false);
                budget.Capture([version]);
                var jobs = new List<MachineToolJobReadSnapshot>();
                if (plan != null)
                {
                    await foreach (ReferenceDescription reference in BrowseProductionMembersAsync(
                        plan.ObjectId, ObjectTypeIds.ProductionJobType, budget, ct).ConfigureAwait(false))
                    {
                        jobs.Add(await ReadProductionJobAsync(
                            ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris), budget, ct)
                            .ConfigureAwait(false));
                    }
                    await CheckInventoryVersionAsync(plan.ObjectId, version, ct).ConfigureAwait(false);
                }
                ProductionStatisticsTypeClient? statistics = await production.GetStatisticsAsync(Telemetry, ct)
                    .ConfigureAwait(false);
                DataValue lifetime = statistics == null ? DataValue.Null :
                    (await ReadProductionValuesAsync(statistics.ObjectId,
                        [ModelName(BrowseNames.PartsProducedInLifetime)], budget, 0, ct).ConfigureAwait(false))[0];
                return new MachineToolProductionReadSnapshot(
                    machineId, plan?.ObjectId ?? NodeId.Null, version, program, jobs.ToArrayOf(), lifetime);
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Reads one job and its retained children without mistaking vendor descendants for absent standard nodes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<MachineToolJobReadSnapshot> ReadProductionJobAsync(
            NodeId node, ProductionReadBudget budget, CancellationToken ct)
        {
            INode identity = await ReadProductionIdentityAsync(node, budget, ct).ConfigureAwait(false);
            var proxy = new ProductionJobTypeClient(Session, node, Telemetry);
            ArrayOf<DataValue> values = await ReadProductionValuesAsync(node,
                [
                    ModelName(BrowseNames.Identifier), new QualifiedName(Ua.BrowseNames.NumberInList),
                    ModelName(BrowseNames.RunsCompleted), ModelName(BrowseNames.RunsPlanned),
                    ModelName(BrowseNames.CustomerOrderIdentifier), ModelName(BrowseNames.OrderIdentifier),
                    ModelName(BrowseNames.PartsCompleted), ModelName(BrowseNames.PartsGood)
                ], budget, 4, ct).ConfigureAwait(false);
            ArrayOf<NodeId> planned = await ResolveChildNodesAsync(
                node, [ModelName(BrowseNames.RunsPlanned)], ct).ConfigureAwait(false);
            ArrayOf<DataValue> valid = await ReadProductionValuesAsync(
                planned[0], [ModelName(BrowseNames.IsValid)], budget, 1, ct).ConfigureAwait(false);
            ProductionJobStateMachineTypeClient state = await proxy.GetStateAsync(Telemetry, ct)
                .ConfigureAwait(false) ??
                throw MissingMandatory("ProductionJob/State");
            BaseObjectTypeClient programs = await proxy.GetProductionProgramsAsync(Telemetry, ct)
                .ConfigureAwait(false) ??
                throw MissingMandatory("ProductionJob/ProductionPrograms");
            DataValue version = await ReadInventoryVersionAsync(programs.ObjectId, ct).ConfigureAwait(false);
            budget.Capture([version]);
            var programsRead = new List<MachineToolProgramReadSnapshot>();
            await foreach (ReferenceDescription reference in BrowseProductionMembersAsync(
                programs.ObjectId, ObjectTypeIds.ProductionProgramType, budget, ct).ConfigureAwait(false))
            {
                programsRead.Add(await ReadProductionProgramAsync(
                    ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris), false, budget, ct)
                    .ConfigureAwait(false));
            }
            if (programsRead.Count == 0)
            {
                throw MissingMandatory("ProductionPrograms/<Program>");
            }
            await CheckInventoryVersionAsync(programs.ObjectId, version, ct).ConfigureAwait(false);
            BaseObjectTypeClient? sets = await proxy.GetPartSetsAsync(Telemetry, ct).ConfigureAwait(false);
            var setsRead = new List<MachineToolPartSetReadSnapshot>();
            if (sets != null)
            {
                await foreach (ReferenceDescription reference in BrowseProductionMembersAsync(
                    sets.ObjectId, ObjectTypeIds.ProductionPartSetType, budget, ct).ConfigureAwait(false))
                {
                    setsRead.Add(await ReadProductionSetAsync(
                        ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris), budget, ct)
                        .ConfigureAwait(false));
                }
                if (setsRead.Count == 0)
                {
                    throw MissingMandatory("PartSets/<PartSet>");
                }
            }
            return new MachineToolJobReadSnapshot(
                node, ExpandedNodeId.ToNodeId(identity.TypeDefinitionId, Session.NamespaceUris), identity.BrowseName,
                values[0], values[1], values[2], values[3], valid[0], values[4], values[5], values[6], values[7],
                await ReadProductionStateAsync(state.ObjectId, budget, ct).ConfigureAwait(false),
                programsRead.ToArrayOf(), setsRead.ToArrayOf());
        }

        /// <summary>
        /// Reads both program kinds through their generated navigators while retaining absent optional state.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<MachineToolProgramReadSnapshot> ReadProductionProgramAsync(
            NodeId node, bool active, ProductionReadBudget budget, CancellationToken ct)
        {
            INode identity = await ReadProductionIdentityAsync(node, budget, ct).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadProductionValuesAsync(node,
                [
                    ModelName(BrowseNames.Name), new QualifiedName(Ua.BrowseNames.NumberInList),
                    ModelName(BrowseNames.JobIdentifier), ModelName(BrowseNames.JobNodeId)
                ], budget, 2, ct).ConfigureAwait(false);
            ProductionProgramStateMachineTypeClient? state = active
                ? await new ProductionActiveProgramTypeClient(Session, node, Telemetry)
                    .GetStateAsync(Telemetry, ct).ConfigureAwait(false)
                : await new ProductionProgramTypeClient(Session, node, Telemetry)
                    .GetStateAsync(Telemetry, ct).ConfigureAwait(false);
            if (active && state == null)
            {
                throw MissingMandatory("ActiveProgram/State");
            }
            return new MachineToolProgramReadSnapshot(
                node, ExpandedNodeId.ToNodeId(identity.TypeDefinitionId, Session.NamespaceUris), identity.BrowseName,
                values[0], values[1], values[2], values[3],
                state == null ? null :
                    await ReadProductionStateAsync(state.ObjectId, budget, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Reads the current run's part-set aggregates and any actually exposed part nodes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<MachineToolPartSetReadSnapshot> ReadProductionSetAsync(
            NodeId node, ProductionReadBudget budget, CancellationToken ct)
        {
            INode identity = await ReadProductionIdentityAsync(node, budget, ct).ConfigureAwait(false);
            var proxy = new ProductionPartSetTypeClient(Session, node, Telemetry);
            ArrayOf<DataValue> values = await ReadProductionValuesAsync(node,
                [
                    ModelName(BrowseNames.ContainsMixedParts), ModelName(BrowseNames.PartsPlannedPerRun),
                    ModelName(BrowseNames.PartsCompletedPerRun), ModelName(BrowseNames.Name)
                ], budget, 3, ct).ConfigureAwait(false);
            BaseObjectTypeClient? parts = await proxy.GetPartsPerRunAsync(Telemetry, ct).ConfigureAwait(false);
            var partsRead = new List<MachineToolPartReadSnapshot>();
            if (parts != null)
            {
                await foreach (ReferenceDescription reference in BrowseProductionMembersAsync(
                    parts.ObjectId, ObjectTypeIds.ProductionPartType, budget, ct).ConfigureAwait(false))
                {
                    partsRead.Add(await ReadProductionPartAsync(
                        ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris), budget, ct)
                        .ConfigureAwait(false));
                }
                if (partsRead.Count == 0)
                {
                    throw MissingMandatory("PartsPerRun/<Part>");
                }
            }
            return new MachineToolPartSetReadSnapshot(
                node, ExpandedNodeId.ToNodeId(identity.TypeDefinitionId, Session.NamespaceUris), identity.BrowseName,
                values[3], values[0], values[1], values[2], partsRead.ToArrayOf());
        }

        /// <summary>
        /// Reads original part quality and state, not conclusions computed from counters.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<MachineToolPartReadSnapshot> ReadProductionPartAsync(
            NodeId node, ProductionReadBudget budget, CancellationToken ct)
        {
            INode identity = await ReadProductionIdentityAsync(node, budget, ct).ConfigureAwait(false);
            var proxy = new ProductionPartTypeClient(Session, node, Telemetry);
            ArrayOf<DataValue> values = await ReadProductionValuesAsync(node,
                [
                    ModelName(BrowseNames.Name), ModelName(BrowseNames.PartQuality),
                    ModelName(BrowseNames.ProcessIrregularity), ModelName(BrowseNames.Identifier),
                    ModelName(BrowseNames.CustomerOrderIdentifier)
                ], budget, 3, ct).ConfigureAwait(false);
            ProductionPartStateMachineTypeClient state = await proxy.GetStateAsync(Telemetry, ct)
                .ConfigureAwait(false) ??
                throw MissingMandatory("ProductionPart/State");
            return new MachineToolPartReadSnapshot(
                node, ExpandedNodeId.ToNodeId(identity.TypeDefinitionId, Session.NamespaceUris), identity.BrowseName,
                values[0], values[1], values[2], values[3], values[4],
                await ReadProductionStateAsync(state.ObjectId, budget, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Reads the actual CurrentState and optional LastTransition declarations in namespace zero.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<MachineToolProductionStateReadSnapshot> ReadProductionStateAsync(
            NodeId node, ProductionReadBudget budget, CancellationToken ct)
        {
            ArrayOf<NodeId> variables = await ResolveChildNodesAsync(node,
                [
                    new QualifiedName(Ua.BrowseNames.CurrentState),
                    new QualifiedName(Ua.BrowseNames.LastTransition)
                ],
                ct).ConfigureAwait(false);
            if (variables[0].IsNull)
            {
                throw MissingMandatory("ProductionState/CurrentState");
            }
            ArrayOf<DataValue> state = await ReadInventorySamplesAsync(variables, Attributes.Value, ct)
                .ConfigureAwait(false);
            budget.Capture(state);
            ArrayOf<DataValue> current = await ReadProductionValuesAsync(variables[0],
                [new QualifiedName(Ua.BrowseNames.Id), new QualifiedName(Ua.BrowseNames.Number)],
                budget, 1, ct).ConfigureAwait(false);
            ArrayOf<DataValue> transition = variables[1].IsNull ? [DataValue.Null, DataValue.Null, DataValue.Null] :
                await ReadProductionValuesAsync(variables[1],
                    [
                        new QualifiedName(Ua.BrowseNames.Id), new QualifiedName(Ua.BrowseNames.Number),
                        new QualifiedName(Ua.BrowseNames.TransitionTime)
                    ], budget, 1, ct).ConfigureAwait(false);
            return new MachineToolProductionStateReadSnapshot(node, state[0], current[0], current[1],
                state[1], transition[0], transition[1], transition[2]);
        }

        /// <summary>
        /// Reads a namespace-qualified group while separating absent optional nodes from failed services.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ArrayOf<DataValue>> ReadProductionValuesAsync(
            NodeId parent, ArrayOf<QualifiedName> names, ProductionReadBudget budget,
            int mandatoryCount, CancellationToken ct)
        {
            ArrayOf<NodeId> nodes = await ResolveChildNodesAsync(parent, names, ct).ConfigureAwait(false);
            for (int index = 0; index < mandatoryCount; index++)
            {
                if (nodes[index].IsNull)
                {
                    throw MissingMandatory(names[index].ToString());
                }
            }
            ArrayOf<DataValue> values = await ReadInventorySamplesAsync(nodes, Attributes.Value, ct)
                .ConfigureAwait(false);
            budget.Capture(values);
            return values;
        }

        /// <summary>
        /// Captures actual node metadata and bounds the aggregate number of retained elements.
        /// </summary>
        private async ValueTask<INode> ReadProductionIdentityAsync(
            NodeId node, ProductionReadBudget budget, CancellationToken ct)
        {
            RequireNode(node);
            budget.AddElement(node);
            INode identity = await Session.NodeCache.GetNodeAsync(node, ct).ConfigureAwait(false);
            budget.Capture([new DataValue(Variant.From(identity.BrowseName))]);
            return identity;
        }

        /// <summary>
        /// Uses the existing continuation-owning browser and bounds every examined reference across all pages.
        /// </summary>
        private async IAsyncEnumerable<ReferenceDescription> BrowseProductionMembersAsync(
            NodeId parent, ExpandedNodeId baseType, ProductionReadBudget budget,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var required = ExpandedNodeId.ToNodeId(baseType, Session.NamespaceUris);
            await foreach (ReferenceDescription reference in BrowseInventoryMembersAsync(
                parent, NodeClass.Object, ct).ConfigureAwait(false))
            {
                budget.AddReference();
                var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                if (!type.IsNull && await Session.NodeCache.IsTypeOfAsync(type, required, ct).ConfigureAwait(false))
                {
                    yield return reference;
                }
            }
        }

        /// <summary>
        /// Accounts for the entire workflow, including ignored references and raw quality-preserving sample payload.
        /// </summary>
        private sealed class ProductionReadBudget(
            MachineToolProductionReadOptions options, IServiceMessageContext messageContext)
        {
            /// <summary>
            /// Rejects repeated or cyclic retained identities and aggregate element overflow.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            internal void AddElement(NodeId node)
            {
                if (!m_seen.Add(node) || m_seen.Count > m_options.MaximumElements)
                {
                    throw InventoryLimit("The retained production graph is repeated or exceeds its element bound.");
                }
            }

            /// <summary>
            /// Counts every visited reference, not only matching standard types.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            internal void AddReference()
            {
                if (++m_references > m_options.MaximumReferences)
                {
                    throw InventoryLimit("The retained production graph exceeds its browse-reference bound.");
                }
            }

            /// <summary>
            /// Preserves measurement quality while refusing access errors and oversized decoded payloads.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            internal void Capture(ArrayOf<DataValue> values)
            {
                foreach (DataValue value in values)
                {
                    if (value.IsNull)
                    {
                        continue;
                    }
                    if (value.StatusCode == StatusCodes.BadUserAccessDenied ||
                        value.StatusCode == StatusCodes.BadNodeIdUnknown ||
                        value.StatusCode == StatusCodes.BadAttributeIdInvalid ||
                        value.StatusCode == StatusCodes.BadNotReadable ||
                        value.StatusCode == StatusCodes.BadSecurityChecksFailed)
                    {
                        throw new ServiceResultException(value.StatusCode);
                    }
                    using var encoder = new JsonEncoder(m_messageContext);
                    encoder.WriteDataValue("Value", value);
                    m_bytes = checked(m_bytes + Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText()));
                    if (m_bytes > m_options.MaximumPayloadBytes)
                    {
                        throw InventoryLimit("The retained production read exceeds its decoded-payload bound.");
                    }
                }
            }

            /// <summary>The validated aggregate bounds.</summary>
            private readonly MachineToolProductionReadOptions m_options = options;

            /// <summary>The session's native namespace-aware JSON context.</summary>
            private readonly IServiceMessageContext m_messageContext = messageContext;

            /// <summary>The actual visited production element identifiers.</summary>
            private readonly HashSet<NodeId> m_seen = [];

            /// <summary>The number of examined browse references.</summary>
            private int m_references;

            /// <summary>The accumulated decoded sample payload.</summary>
            private int m_bytes;
        }
    }
}
