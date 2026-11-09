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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsClient
    {
        /// <summary>
        /// Reads native sensors, qualifications, accessories and analog additional sensors with aggregate bounds.
        /// Shared MachineTool code owns tool traversal, life semantics, continuation points and list-version checking.
        /// </summary>
        public async ValueTask<GmsEquipmentReadSnapshot> ReadEquipmentAsync(
            NodeId nodeId, CancellationToken cancellationToken = default)
        {
            using Operation operation = Begin(cancellationToken);
            CancellationToken ct = operation.Token;
            GMSTypeClient root = await RequireGmsAsync(nodeId, ct).ConfigureAwait(false);
            var budget = new ReadBudget(m_options, Session.MessageContext);
            MachineToolEquipmentReadSnapshot inherited = await m_inherited.ReadEquipmentAsync(
                nodeId, m_options.MaximumElements, m_options.MaximumElements * 16, ct).ConfigureAwait(false);
            var sensors = new List<GmsSensorReadSnapshot>();
            for (int i = 0; i < inherited.Tools.Count; i++)
            {
                sensors.Add(await ReadSensorAsync(inherited.Tools[i], budget, ct).ConfigureAwait(false));
            }
            GMSEquipmentTypeClient equipment = await root.GetEquipmentAsync(Telemetry, ct).ConfigureAwait(false) ??
                throw Missing(MachineTool.BrowseNames.Equipment);
            FolderTypeClient? accessoryFolder = await equipment.GetAccessoriesAsync(Telemetry, ct)
                .ConfigureAwait(false);
            var accessories = new List<GmsAccessoryReadSnapshot>();
            if (accessoryFolder != null)
            {
                await foreach (ReferenceDescription reference in BrowseAsync(
                    accessoryFolder.ObjectId, NodeClass.Object, budget, ct).ConfigureAwait(false))
                {
                    GmsAccessoryReadSnapshot? value = await ReadAccessoryAsync(reference, budget, ct)
                        .ConfigureAwait(false);
                    if (value != null)
                    {
                        accessories.Add(value);
                    }
                }
            }
            FolderTypeClient? analogFolder = await equipment.GetAdditionalSensorAsync(Telemetry, ct)
                .ConfigureAwait(false);
            var additional = new List<GmsAdditionalSensorReadSnapshot>();
            if (analogFolder != null)
            {
                await foreach (ReferenceDescription reference in BrowseAsync(
                    analogFolder.ObjectId, NodeClass.Variable, budget, ct).ConfigureAwait(false))
                {
                    NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (!await IsTypeAsync(type, VariableTypeIds.AdditionalSensorType, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    budget.Element(node);
                    ArrayOf<DataValue> sample = await ReadValuesAsync([node], budget, ct).ConfigureAwait(false);
                    ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(node,
                        [Name(BrowseNames.Identifier), Name(BrowseNames.Name), Name(BrowseNames.Class),
                            new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits)], budget, ct).ConfigureAwait(false);
                    Require(fields[0], BrowseNames.Identifier);
                    Require(fields[3], Opc.Ua.BrowseNames.EngineeringUnits);
                    additional.Add(new GmsAdditionalSensorReadSnapshot(node, type, reference.BrowseName,
                        sample[0], fields[0], fields[1], fields[2], fields[3]));
                }
            }
            await CheckVersionAsync(inherited.ToolsNodeId, inherited.NodeVersion, budget, ct).ConfigureAwait(false);
            return new GmsEquipmentReadSnapshot(
                nodeId, inherited, sensors.ToArrayOf(), accessories.ToArrayOf(), additional.ToArrayOf());
        }

        /// <summary>
        /// Reads native scalar fields on the actual inherited tool node without reimplementing its inventory protocol.
        /// </summary>
        private async ValueTask<GmsSensorReadSnapshot> ReadSensorAsync(
            MachineToolToolReadSnapshot tool, ReadBudget budget, CancellationToken ct)
        {
            budget.Element(tool.NodeId);
            bool multi = tool.Kind == MachineToolToolKind.MultiTool;
            ExpandedNodeId expected = multi ? ObjectTypeIds.MultiSensorType : ObjectTypeIds.SensorType;
            if (!await IsTypeAsync(tool.TypeDefinitionId, expected, ct).ConfigureAwait(false))
            {
                throw Missing("native SensorType or MultiSensorType tool");
            }
            budget.Capture([tool.Identifier, tool.Name, tool.LocationName, tool.PlaceNumber,
                tool.ControlIdentifier1, tool.ControlIdentifier2, tool.ControlIdentifierInterpretation, tool.Locked,
                tool.ReasonForLocking, tool.LastUsage, tool.PlannedForOperating]);
            foreach (MachineToolToolLifeReadSnapshot life in tool.Life)
            {
                budget.Capture([life.Value, life.Indication, life.IsCountingUp, life.EngineeringUnits,
                    life.StartValue, life.WarningValue, life.LimitValue]);
            }
            ArrayOf<QualifiedName> names = multi
                ? [Name(BrowseNames.Alignment), Name(BrowseNames.Axes)]
                : [Name(BrowseNames.Class), Name(BrowseNames.Capabilities), Name(BrowseNames.Alignment),
                    Name(BrowseNames.Axes), Name(BrowseNames.IsQualifiedStatus), Name(BrowseNames.AbsoluteProbe),
                    Name(BrowseNames.EngineeringUnit), Name(BrowseNames.MeasuringRange),
                    Name(BrowseNames.WorkingRange), Name(BrowseNames.Resolution)];
            NodeId nativeId = multi ? new MultiSensorTypeClient(Session, tool.NodeId, Telemetry).ObjectId :
                new SensorTypeClient(Session, tool.NodeId, Telemetry).ObjectId;
            ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(nativeId, names, budget, ct)
                .ConfigureAwait(false);
            var children = new List<GmsSensorReadSnapshot>();
            for (int i = 0; i < tool.Tools.Count; i++)
            {
                children.Add(await ReadSensorAsync(tool.Tools[i], budget, ct).ConfigureAwait(false));
            }
            if (multi)
            {
                return new GmsSensorReadSnapshot(tool, default, default, fields[0], fields[1],
                    default, default, default, default, default, default, children.ToArrayOf());
            }
            Require(fields[0], BrowseNames.Class);
            if (tool.ToolLifeNodeId.IsNull)
            {
                throw Missing(MachineTool.BrowseNames.ToolLife);
            }
            return new GmsSensorReadSnapshot(tool, fields[0], fields[1], fields[2], fields[3], fields[4], fields[5],
                fields[6], fields[7], fields[8], fields[9], children.ToArrayOf());
        }

        /// <summary>
        /// Navigates each recognized accessory through its generated native proxy and exact inherited nameplate.
        /// </summary>
        private async ValueTask<GmsAccessoryReadSnapshot?> ReadAccessoryAsync(
            ReferenceDescription reference, ReadBudget budget, CancellationToken ct)
        {
            NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
            NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
            GmsAccessoryKind kind;
            MachineryItemIdentificationTypeClient? identity;
            if (await IsTypeAsync(type, ObjectTypeIds.RotaryTableType, ct).ConfigureAwait(false))
            {
                kind = GmsAccessoryKind.RotaryTable;
                identity = await new RotaryTableTypeClient(Session, node, Telemetry)
                    .GetIdentificationAsync(Telemetry, ct).ConfigureAwait(false);
            }
            else if (await IsTypeAsync(type, ObjectTypeIds.SensorExchangeRackType, ct).ConfigureAwait(false))
            {
                kind = GmsAccessoryKind.SensorExchangeRack;
                identity = await new SensorExchangeRackTypeClient(Session, node, Telemetry)
                    .GetIdentificationAsync(Telemetry, ct).ConfigureAwait(false);
            }
            else if (await IsTypeAsync(type, ObjectTypeIds.TipExchangeRackType, ct).ConfigureAwait(false))
            {
                kind = GmsAccessoryKind.TipExchangeRack;
                identity = await new TipExchangeRackTypeClient(Session, node, Telemetry)
                    .GetIdentificationAsync(Telemetry, ct).ConfigureAwait(false);
            }
            else
            {
                return null;
            }
            budget.Element(node);
            if (identity == null)
            {
                throw Missing(Opc.Ua.Di.BrowseNames.Identification);
            }
            ArrayOf<GmsVariableReading> nameplate = await ReadFieldsAsync(identity.ObjectId,
                [DiName(Opc.Ua.Di.BrowseNames.Manufacturer), DiName(Opc.Ua.Di.BrowseNames.SerialNumber)], budget, ct)
                .ConfigureAwait(false);
            Require(nameplate[0], Opc.Ua.Di.BrowseNames.Manufacturer);
            Require(nameplate[1], Opc.Ua.Di.BrowseNames.SerialNumber);
            ArrayOf<GmsVariableReading> native = await ReadFieldsAsync(node,
                kind == GmsAccessoryKind.RotaryTable
                    ? [Name(BrowseNames.IsIntegrated), Name(BrowseNames.NumberOfAxes)]
                    : [Name(BrowseNames.IsAvailable)], budget, ct).ConfigureAwait(false);
            Require(native[0],
                kind == GmsAccessoryKind.RotaryTable ? BrowseNames.IsIntegrated : BrowseNames.IsAvailable);
            if (kind == GmsAccessoryKind.RotaryTable)
            {
                Require(native[1], BrowseNames.NumberOfAxes);
            }
            return new GmsAccessoryReadSnapshot(node, type, reference.BrowseName, kind,
                new GmsItemIdentificationReadSnapshot(identity.ObjectId, nameplate[0], nameplate[1]),
                kind == GmsAccessoryKind.RotaryTable ? native[0] : default,
                kind == GmsAccessoryKind.RotaryTable ? native[1] : default,
                kind == GmsAccessoryKind.RotaryTable ? default : native[0]);
        }

        /// <summary>
        /// Rejects racing topology reads when the standard optional list version is available.
        /// </summary>
        private async ValueTask CheckVersionAsync(
            NodeId node, DataValue before, ReadBudget budget, CancellationToken ct)
        {
            if (node.IsNull)
            {
                return;
            }
            ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(node,
                [new QualifiedName(Opc.Ua.BrowseNames.NodeVersion)], budget, ct).ConfigureAwait(false);
            DataValue after = fields[0].Value;
            if (before.IsNull != after.IsNull || before.WrappedValue != after.WrappedValue)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The GMS inventory changed while its native fields were being read.");
            }
        }
    }
}
