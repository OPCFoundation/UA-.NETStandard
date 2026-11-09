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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Reads bounded forecasts through generated notification/list proxies and the shared continuation-owning
        /// browser. Original DataValues distinguish absent optional members from unavailable reported observations.
        /// </summary>
        /// <param name="machineId">The actual machine instance.</param>
        /// <param name="maximumPrognoses">Maximum accepted entries, from 1 through 65536.</param>
        /// <param name="cancellationToken">Cancels the workflow and releases continuation points.</param>
        public async ValueTask<MachineToolPrognosisReadSnapshot> ReadPrognosesAsync(
            NodeId machineId, int maximumPrognoses = 128, CancellationToken cancellationToken = default)
        {
            ValidateInventoryLimit(maximumPrognoses, nameof(maximumPrognoses));
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                CancellationToken ct = operation.Token;
                ct.ThrowIfCancellationRequested();
                MachineToolTypeClient machine = GetMachineProxy(machineId);
                NotificationTypeClient notification = await machine.GetNotificationAsync(Telemetry, ct)
                    .ConfigureAwait(false) ?? throw MissingMandatory(BrowseNames.Notification);
                PrognosisListTypeClient? list = await notification.GetPrognosesAsync(Telemetry, ct)
                    .ConfigureAwait(false);
                if (list == null)
                {
                    return new MachineToolPrognosisReadSnapshot { MachineId = machineId };
                }
                DataValue version = await ReadInventoryVersionAsync(list.ObjectId, ct).ConfigureAwait(false);
                var entries = new List<MachineToolPrognosisReadEntry>();
                var seen = new HashSet<NodeId>();
                await foreach (ReferenceDescription reference in BrowseInventoryMembersAsync(
                    list.ObjectId, NodeClass.Object, ct).ConfigureAwait(false))
                {
                    NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    MachineToolPrognosisKind? kind = await ClassifyPrognosisAsync(type, ct).ConfigureAwait(false);
                    if (kind == null)
                    {
                        continue;
                    }
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    RequireNode(node);
                    if (entries.Count == maximumPrognoses || !seen.Add(node))
                    {
                        throw InventoryLimit("The prognosis inventory is repeated or exceeds its requested limit.");
                    }
                    entries.Add(await ReadPrognosisAsync(node, reference.BrowseName, type, kind.Value, ct)
                        .ConfigureAwait(false));
                }
                await CheckInventoryVersionAsync(list.ObjectId, version, ct).ConfigureAwait(false);
                return new MachineToolPrognosisReadSnapshot
                {
                    MachineId = machineId, ListNodeId = list.ObjectId, NodeVersion = version,
                    Entries = entries.ToArrayOf()
                };
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Resolves actual generated subtypes, retaining the concrete type identifier for companion clients.
        /// </summary>
        private async ValueTask<MachineToolPrognosisKind?> ClassifyPrognosisAsync(
            NodeId type, CancellationToken cancellationToken)
        {
            if (type.IsNull || !await Session.NodeCache.IsTypeOfAsync(
                type, OptionalNode(ObjectTypes.PrognosisType, Namespaces.MachineTool), cancellationToken)
                .ConfigureAwait(false))
            {
                return null;
            }
            for (int i = 0; i < s_prognosisTypeIds.Length; i++)
            {
                if (await Session.NodeCache.IsTypeOfAsync(
                    type, OptionalNode(s_prognosisTypeIds[i], Namespaces.MachineTool), cancellationToken)
                    .ConfigureAwait(false))
                {
                    return (MachineToolPrognosisKind)i;
                }
            }
            return MachineToolPrognosisKind.Companion;
        }

        /// <summary>
        /// Reads every standard descriptive and reference component without discarding quality or source time.
        /// </summary>
        private async ValueTask<MachineToolPrognosisReadEntry> ReadPrognosisAsync(
            NodeId node, QualifiedName browseName, NodeId type, MachineToolPrognosisKind kind,
            CancellationToken cancellationToken)
        {
            ArrayOf<DataValue> values = await ReadInventoryValuesAsync(node,
                [
                    ModelName(BrowseNames.PredictedTime), ModelName(BrowseNames.Activity),
                    ModelName(BrowseNames.Location), ModelName(BrowseNames.ToolIdentifier),
                    ModelName(BrowseNames.ToolName), ModelName(BrowseNames.ToolNodeId),
                    ModelName(BrowseNames.PartIdentifier), ModelName(BrowseNames.PartName),
                    ModelName(BrowseNames.PartNodeId), ModelName(BrowseNames.SourceIdentifier),
                    ModelName(BrowseNames.JobNodeId), ModelName(BrowseNames.UtilityName)
                ], cancellationToken).ConfigureAwait(false);
            bool missing = values[0].IsNull || kind switch
            {
                MachineToolPrognosisKind.Maintenance or MachineToolPrognosisKind.ManualActivity => values[1].IsNull,
                MachineToolPrognosisKind.ProcessChangeover => values[1].IsNull || values[2].IsNull,
                MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload =>
                    values[2].IsNull || values[7].IsNull,
                MachineToolPrognosisKind.ToolLoad or MachineToolPrognosisKind.ToolUnload or
                    MachineToolPrognosisKind.ToolChange => values[2].IsNull,
                MachineToolPrognosisKind.ProductionJobEnd => values[9].IsNull,
                MachineToolPrognosisKind.UtilityChange => values[11].IsNull,
                _ => false
            };
            if (missing)
            {
                throw MissingMandatory("a declared prognosis component");
            }
            return new MachineToolPrognosisReadEntry
            {
                NodeId = node, BrowseName = browseName, TypeDefinitionId = type, Kind = kind,
                PredictedTime = values[0], Activity = values[1], Location = values[2],
                ToolIdentifier = values[3], ToolName = values[4], ToolNodeId = values[5],
                PartIdentifier = values[6], PartName = values[7], PartNodeId = values[8],
                SourceIdentifier = values[9], JobNodeId = values[10], UtilityName = values[11]
            };
        }

        /// <summary>Concrete standard type identifiers in MachineToolPrognosisKind order.</summary>
        private static readonly uint[] s_prognosisTypeIds =
        [
            ObjectTypes.MaintenancePrognosisType, ObjectTypes.ManualActivityPrognosisType,
            ObjectTypes.PartLoadPrognosisType, ObjectTypes.PartUnloadPrognosisType,
            ObjectTypes.ProcessChangeoverPrognosisType, ObjectTypes.ProductionJobEndPrognosisType,
            ObjectTypes.ToolChangePrognosisType, ObjectTypes.ToolLoadPrognosisType,
            ObjectTypes.ToolUnloadPrognosisType, ObjectTypes.UtilityChangePrognosisType
        ];
    }
}
