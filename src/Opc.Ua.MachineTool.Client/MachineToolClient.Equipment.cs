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
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Reads a bounded, subtype-aware tool inventory through generated proxies and the shared paged browser.
        /// A changed NodeVersion rejects a racing read rather than presenting a mixed topology as a stable inventory.
        /// </summary>
        /// <param name="machineId">The actual machine instance.</param>
        /// <param name="maximumTools">Maximum combined tools and multitools, from 1 through 65536.</param>
        /// <param name="maximumLifeEntries">Maximum combined life measurements, from 1 through 65536.</param>
        /// <param name="cancellationToken">Cancels reads and releases any acquired continuation point.</param>
        public async ValueTask<MachineToolEquipmentReadSnapshot> ReadEquipmentAsync(
            NodeId machineId, int maximumTools = 1024, int maximumLifeEntries = 4096,
            CancellationToken cancellationToken = default)
        {
            ValidateInventoryLimit(maximumTools, nameof(maximumTools));
            ValidateInventoryLimit(maximumLifeEntries, nameof(maximumLifeEntries));
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                CancellationToken ct = operation.Token;
                ct.ThrowIfCancellationRequested();
                MachineToolTypeClient machine = GetMachineProxy(machineId);
                EquipmentTypeClient equipment = await machine.GetEquipmentAsync(Telemetry, ct).ConfigureAwait(false) ??
                    throw MissingMandatory(BrowseNames.Equipment);
                ToolListTypeClient? list = await equipment.GetToolsAsync(Telemetry, ct).ConfigureAwait(false);
                if (list == null)
                {
                    return new MachineToolEquipmentReadSnapshot { MachineId = machineId };
                }
                DataValue version = await ReadInventoryVersionAsync(list.ObjectId, ct).ConfigureAwait(false);
                var budget = new EquipmentReadBudget(maximumTools, maximumLifeEntries);
                ArrayOf<MachineToolToolReadSnapshot> tools = await ReadToolsAsync(list.ObjectId, false, budget, ct)
                    .ConfigureAwait(false);
                await CheckInventoryVersionAsync(list.ObjectId, version, ct).ConfigureAwait(false);
                return new MachineToolEquipmentReadSnapshot
                {
                    MachineId = machineId,
                    ToolsNodeId = list.ObjectId,
                    NodeVersion = version,
                    Tools = tools
                };
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Traverses only actual BaseTool subtypes and bounds the complete inventory, not merely a browse page.
        /// </summary>
        private async ValueTask<ArrayOf<MachineToolToolReadSnapshot>> ReadToolsAsync(
            NodeId parent, bool constituent, EquipmentReadBudget budget, CancellationToken cancellationToken)
        {
            var tools = new List<MachineToolToolReadSnapshot>();
            await foreach (ReferenceDescription reference in BrowseInventoryMembersAsync(
                parent, NodeClass.Object, cancellationToken).ConfigureAwait(false))
            {
                NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                MachineToolToolKind? kind = await ClassifyToolAsync(type, cancellationToken).ConfigureAwait(false);
                if (kind == null)
                {
                    continue;
                }
                if (constituent && kind != MachineToolToolKind.Tool)
                {
                    throw MissingMandatory("MultiTool constituent of ToolType");
                }
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                RequireNode(node);
                if (!budget.Seen.Add(node) || ++budget.Tools > budget.MaximumTools)
                {
                    throw InventoryLimit("The tool inventory is cyclic, repeated, or exceeds its requested limit.");
                }
                tools.Add(await ReadToolAsync(node, type, reference.BrowseName, kind.Value, budget, cancellationToken)
                    .ConfigureAwait(false));
            }
            return tools.ToArrayOf();
        }

        /// <summary>
        /// Recognizes actual companion types through the existing type cache, never by browse-name guesses.
        /// </summary>
        private async ValueTask<MachineToolToolKind?> ClassifyToolAsync(
            NodeId type, CancellationToken cancellationToken)
        {
            if (type.IsNull)
            {
                return null;
            }
            if (await Session.NodeCache.IsTypeOfAsync(type,
                OptionalNode(ObjectTypes.ToolType, Namespaces.MachineTool), cancellationToken).ConfigureAwait(false))
            {
                return MachineToolToolKind.Tool;
            }
            if (await Session.NodeCache.IsTypeOfAsync(type,
                OptionalNode(ObjectTypes.MultiToolType, Namespaces.MachineTool), cancellationToken)
                .ConfigureAwait(false))
            {
                return MachineToolToolKind.MultiTool;
            }
            return await Session.NodeCache.IsTypeOfAsync(type,
                OptionalNode(ObjectTypes.BaseToolType, Namespaces.MachineTool), cancellationToken)
                .ConfigureAwait(false) ?
                MachineToolToolKind.Companion : null;
        }

        /// <summary>
        /// Reads a generated tool's raw fields without converting bad values into successful defaults.
        /// </summary>
        private async ValueTask<MachineToolToolReadSnapshot> ReadToolAsync(
            NodeId node, NodeId type, QualifiedName name, MachineToolToolKind kind,
            EquipmentReadBudget budget, CancellationToken cancellationToken)
        {
            var proxy = new BaseToolTypeClient(Session, node, Telemetry);
            BaseObjectTypeClient? location = await proxy.GetLocationAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<DataValue> basic = await ReadInventoryValuesAsync(node,
                [ModelName(BrowseNames.Identifier), ModelName(BrowseNames.Name)], cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<DataValue> place = location == null ? [DataValue.Null, DataValue.Null] :
                await ReadInventoryValuesAsync(location.ObjectId,
                    [ModelName(BrowseNames.Name), ModelName(BrowseNames.PlaceNumber)], cancellationToken)
                    .ConfigureAwait(false);
            if (location != null && (place[0].IsNull || place[1].IsNull))
            {
                throw MissingMandatory("Location/Name or PlaceNumber");
            }
            var result = new MachineToolToolReadSnapshot
            {
                NodeId = node,
                TypeDefinitionId = type,
                BrowseName = name,
                Kind = kind,
                Identifier = basic[0],
                Name = basic[1],
                LocationNodeId = location?.ObjectId ?? NodeId.Null,
                LocationName = place[0],
                PlaceNumber = place[1]
            };
            if (kind == MachineToolToolKind.MultiTool)
            {
                return result with
                {
                    Tools = await ReadToolsAsync(node, true, budget, cancellationToken).ConfigureAwait(false)
                };
            }
            if (kind != MachineToolToolKind.Tool)
            {
                return result;
            }
            var tool = new ToolTypeClient(Session, node, Telemetry);
            BaseObjectTypeClient? life = await tool.GetToolLifeAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<QualifiedName> names =
            [
                ModelName(BrowseNames.ControlIdentifier1), ModelName(BrowseNames.ControlIdentifier2),
                ModelName(BrowseNames.ControlIdentifierInterpretation), ModelName(BrowseNames.Locked),
                ModelName(BrowseNames.LastUsage), ModelName(BrowseNames.PlannedForOperating)
            ];
            ArrayOf<NodeId> nodes = await ResolveChildNodesAsync(node, names, cancellationToken).ConfigureAwait(false);
            if (nodes[0].IsNull || nodes[2].IsNull || nodes[3].IsNull)
            {
                throw MissingMandatory("Tool controller identifiers or Locked");
            }
            ArrayOf<DataValue> values = await ReadInventorySamplesAsync(nodes, Attributes.Value, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<DataValue> reason = await ReadInventoryValuesAsync(
                nodes[3], [ModelName(BrowseNames.ReasonForLocking)], cancellationToken).ConfigureAwait(false);
            if (reason[0].IsNull)
            {
                throw MissingMandatory("Locked/ReasonForLocking");
            }
            return result with
            {
                ControlIdentifier1 = values[0], ControlIdentifier2 = values[1],
                ControlIdentifierInterpretation = values[2], Locked = values[3], ReasonForLocking = reason[0],
                LastUsage = values[4], PlannedForOperating = values[5],
                ToolLifeNodeId = life?.ObjectId ?? NodeId.Null,
                Life = life == null ? [] :
                    await ReadToolLifeAsync(life.ObjectId, budget, cancellationToken).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads independent Number variables with their raw thresholds and true companion browse names.
        /// </summary>
        private async ValueTask<ArrayOf<MachineToolToolLifeReadSnapshot>> ReadToolLifeAsync(
            NodeId parent, EquipmentReadBudget budget, CancellationToken cancellationToken)
        {
            var entries = new List<MachineToolToolLifeReadSnapshot>();
            await foreach (ReferenceDescription reference in BrowseInventoryMembersAsync(
                parent, NodeClass.Variable, cancellationToken).ConfigureAwait(false))
            {
                NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                if (type.IsNull || !await Session.NodeCache.IsTypeOfAsync(type,
                    OptionalNode(VariableTypes.ToolLifeType, Namespaces.MachineTool), cancellationToken)
                    .ConfigureAwait(false))
                {
                    continue;
                }
                if (++budget.LifeEntries > budget.MaximumLifeEntries)
                {
                    throw InventoryLimit("The complete tool-life inventory exceeds its requested limit.");
                }
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                RequireNode(node);
                ArrayOf<DataValue> values = await ReadInventoryValuesAsync(node,
                    [
                        ModelName(BrowseNames.EngineeringUnits), ModelName(BrowseNames.Indication),
                        ModelName(BrowseNames.IsCountingUp), ModelName(BrowseNames.StartValue),
                        ModelName(BrowseNames.WarningValue), ModelName(BrowseNames.LimitValue)
                    ], cancellationToken).ConfigureAwait(false);
                if (values[0].IsNull || values[1].IsNull || values[2].IsNull)
                {
                    throw MissingMandatory("ToolLife units, indication or counting direction");
                }
                ArrayOf<DataValue> measured = await ReadInventorySamplesAsync(
                    [node], Attributes.Value, cancellationToken).ConfigureAwait(false);
                ArrayOf<DataValue> dataType = await ReadInventorySamplesAsync(
                    [node], Attributes.DataType, cancellationToken).ConfigureAwait(false);
                if (!dataType[0].WrappedValue.TryGetValue(out NodeId declared))
                {
                    throw MissingMandatory("ToolLife DataType");
                }
                entries.Add(new MachineToolToolLifeReadSnapshot
                {
                    NodeId = node, BrowseName = reference.BrowseName, TypeDefinitionId = type, DataType = declared,
                    Value = measured[0], EngineeringUnits = values[0], Indication = values[1],
                    IsCountingUp = values[2], StartValue = values[3], WarningValue = values[4], LimitValue = values[5]
                });
            }
            if (entries.Count == 0)
            {
                throw MissingMandatory("ToolLife/<ToolLifeEntry>");
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Uses the stack browser's continuation ownership, including on cancellation or early bounded rejection.
        /// </summary>
        private async IAsyncEnumerable<ReferenceDescription> BrowseInventoryMembersAsync(
            NodeId parent, NodeClass nodeClass, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var browser = new Browser(Session) { MaxReferencesReturned = 64 };
            int examined = 0;
            await foreach (BrowseResult page in browser.BrowseStreamAsync(null, null,
                [
                    new BrowseDescription
                    {
                        NodeId = parent, BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent, IncludeSubtypes = true,
                        NodeClassMask = (uint)nodeClass, ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false))
            {
                if (StatusCode.IsBad(page.StatusCode))
                {
                    throw new ServiceResultException(page.StatusCode);
                }
                for (int i = 0; i < page.References.Count; i++)
                {
                    if (++examined > 100_000)
                    {
                        throw InventoryLimit("The inventory exceeded its bounded browse-reference budget.");
                    }
                    yield return page.References[i];
                }
            }
        }

        /// <summary>
        /// Resolves declared members through the common translator and preserves absent optional samples.
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> ReadInventoryValuesAsync(
            NodeId parent, ArrayOf<QualifiedName> names, CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildNodesAsync(parent, names, cancellationToken)
                .ConfigureAwait(false);
            return await ReadInventorySamplesAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Batches original DataValues with both timestamps; no typed getter discards quality or optional presence.
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> ReadInventorySamplesAsync(
            ArrayOf<NodeId> nodes, uint attribute, CancellationToken cancellationToken)
        {
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsNull)
                {
                    requests.Add(new ReadValueId { NodeId = nodes[i], AttributeId = attribute });
                    positions.Add(i);
                }
            }
            var values = new DataValue[nodes.Count];
            if (requests.Count != 0)
            {
                ArrayOf<ReadValueId> requested = requests.ToArrayOf();
                ReadResponse response = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, requested, cancellationToken).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requested);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requested);
                for (int i = 0; i < positions.Count; i++)
                {
                    values[positions[i]] = response.Results[i];
                }
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Reads the optional namespace-zero list version using the same raw sample workflow.
        /// </summary>
        private async ValueTask<DataValue> ReadInventoryVersionAsync(NodeId list, CancellationToken cancellationToken)
        {
            ArrayOf<DataValue> values = await ReadInventoryValuesAsync(
                list, [new QualifiedName(Opc.Ua.BrowseNames.NodeVersion)], cancellationToken).ConfigureAwait(false);
            return values[0];
        }

        /// <summary>
        /// Refuses to label a racing multi-request inventory read as a stable topology.
        /// </summary>
        private async ValueTask CheckInventoryVersionAsync(
            NodeId list, DataValue before, CancellationToken cancellationToken)
        {
            DataValue after = await ReadInventoryVersionAsync(list, cancellationToken).ConfigureAwait(false);
            if (before.IsNull != after.IsNull || before.WrappedValue != after.WrappedValue)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The inventory changed during the read; retry the bounded workflow.");
            }
        }

        /// <summary>
        /// Qualifies a canonical MachineTool declaration.
        /// </summary>
        private QualifiedName ModelName(string name) => Name(name, Namespaces.MachineTool);

        /// <summary>
        /// Validates a caller-selected aggregate inventory bound.
        /// </summary>
        private static void ValidateInventoryLimit(int value, string name)
        {
            if (value is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        /// <summary>
        /// Reports exceeded inventories without truncating or silently dropping entries.
        /// </summary>
        private static ServiceResultException InventoryLimit(string message) =>
            new(StatusCodes.BadEncodingLimitsExceeded, message);

        /// <summary>
        /// Accounts for a complete read across nested tools and browse pages.
        /// </summary>
        private sealed class EquipmentReadBudget
        {
            /// <summary>
            /// Captures the caller's aggregate bounds.
            /// </summary>
            internal EquipmentReadBudget(int maximumTools, int maximumLifeEntries)
            {
                MaximumTools = maximumTools;
                MaximumLifeEntries = maximumLifeEntries;
            }

            /// <summary>
            /// Gets the maximum accepted tool count.
            /// </summary>
            internal int MaximumTools { get; }

            /// <summary>
            /// Gets the maximum accepted life count.
            /// </summary>
            internal int MaximumLifeEntries { get; }

            /// <summary>
            /// Gets visited actual tool identifiers for cycle and duplicate detection.
            /// </summary>
            internal HashSet<NodeId> Seen { get; } = [];

            /// <summary>
            /// Gets or sets the current aggregate tool count.
            /// </summary>
            internal int Tools { get; set; }

            /// <summary>
            /// Gets or sets the current aggregate life count.
            /// </summary>
            internal int LifeEntries { get; set; }
        }
    }
}
