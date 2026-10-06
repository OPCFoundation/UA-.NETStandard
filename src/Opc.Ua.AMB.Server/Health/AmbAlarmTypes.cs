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

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// The server-specific alarm and condition types through which the
    /// OPC 10000-110 interfaces are applied on type level (§9.4.2, §12.1).
    /// </summary>
    /// <remarks>
    /// Each type is a subtype of the OPC 10000-100 alarm type it refines,
    /// lives in the type namespace of the manager, carries a
    /// <c>HasInterface</c> reference to the AMB interface and declares the
    /// interface's members as mandatory instance declarations, so an alarm of
    /// the type has them as OPC 10000-3 §6.9 asks. A client that filters for
    /// the Device Integration type still receives the alarms, because the
    /// type tree knows the subtype.
    /// </remarks>
    internal sealed class AmbAlarmTypes
    {
        /// <summary>
        /// The browse name of the type of the maintenance conditions.
        /// </summary>
        public const string MaintenanceActivityTypeName = "AssetMaintenanceActivityConditionType";

        private AmbAlarmTypes(Dictionary<AssetHealthAlarmKind, NodeId> healthAlarmTypes, NodeId maintenanceActivityType)
        {
            m_healthAlarmTypes = healthAlarmTypes;
            MaintenanceActivityType = maintenanceActivityType;
        }

        /// <summary>
        /// Gets the type of the maintenance conditions: a subtype of
        /// <c>2:MaintenanceRequiredAlarmType</c> (§12.1) that implements
        /// <c>IMaintenanceEventType</c>.
        /// </summary>
        public NodeId MaintenanceActivityType { get; }

        /// <summary>
        /// Gets the type of the health alarms of a kind.
        /// </summary>
        public NodeId GetHealthAlarmType(AssetHealthAlarmKind kind)
        {
            return m_healthAlarmTypes[kind];
        }

        /// <summary>
        /// Gets the browse name of the server-specific type of a kind.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not defined.</exception>
        public static string NameOf(AssetHealthAlarmKind kind)
        {
            return kind switch
            {
                AssetHealthAlarmKind.Failure => "AssetFailureAlarmType",
                AssetHealthAlarmKind.CheckFunction => "AssetCheckFunctionAlarmType",
                AssetHealthAlarmKind.OffSpec => "AssetOffSpecAlarmType",
                AssetHealthAlarmKind.MaintenanceRequired => "AssetMaintenanceRequiredAlarmType",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// Gets the OPC 10000-100 alarm type of a kind.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not defined.</exception>
        public static NodeId DiTypeOf(AssetHealthAlarmKind kind, NamespaceTable namespaceUris)
        {
            ExpandedNodeId typeId = kind switch
            {
                AssetHealthAlarmKind.Failure => Opc.Ua.Di.ObjectTypeIds.FailureAlarmType,
                AssetHealthAlarmKind.CheckFunction => Opc.Ua.Di.ObjectTypeIds.CheckFunctionAlarmType,
                AssetHealthAlarmKind.OffSpec => Opc.Ua.Di.ObjectTypeIds.OffSpecAlarmType,
                AssetHealthAlarmKind.MaintenanceRequired => Opc.Ua.Di.ObjectTypeIds.MaintenanceRequiredAlarmType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            return ExpandedNodeId.ToNodeId(typeId, namespaceUris);
        }

        /// <summary>
        /// Builds the type nodes; the caller registers them.
        /// </summary>
        /// <param name="namespaceUris">The namespace table of the server.</param>
        /// <param name="typeNamespaceIndex">The index of the type namespace.</param>
        /// <param name="types">Receives the type nodes.</param>
        public static AmbAlarmTypes Build(
            NamespaceTable namespaceUris,
            ushort typeNamespaceIndex,
            List<BaseObjectTypeState> types)
        {
            ushort amb = (ushort)namespaceUris.GetIndex(Namespaces.AMB);
            NodeId rootCauseInterface = ExpandedNodeId.ToNodeId(ObjectTypeIds.IRootCauseIndicationType, namespaceUris);
            NodeId rootCauseDataType = ExpandedNodeId.ToNodeId(DataTypeIds.RootCauseDataType, namespaceUris);

            var healthAlarmTypes = new Dictionary<AssetHealthAlarmKind, NodeId>();
            foreach (AssetHealthAlarmKind kind in s_kinds)
            {
                string name = NameOf(kind);
                BaseObjectTypeState type = CreateType(
                    typeNamespaceIndex,
                    name,
                    DiTypeOf(kind, namespaceUris),
                    rootCauseInterface,
                    "An asset health alarm (OPC 10000-110 §9.3) that names its potential root causes (§9.4).");
                PropertyState<ArrayOf<RootCauseDataType>> declaration =
                    PropertyState<ArrayOf<RootCauseDataType>>.With<StructureBuilder<RootCauseDataType>>(type);
                Declare(
                    declaration,
                    typeNamespaceIndex,
                    name,
                    new QualifiedName(BrowseNames.PotentialRootCauses, amb),
                    rootCauseDataType,
                    ValueRanks.OneDimension);
                type.AddChild(declaration);
                types.Add(type);
                healthAlarmTypes.Add(kind, type.NodeId);
            }

            BaseObjectTypeState maintenance = BuildMaintenanceActivityType(namespaceUris, typeNamespaceIndex, amb);
            types.Add(maintenance);
            return new AmbAlarmTypes(healthAlarmTypes, maintenance.NodeId);
        }

        /// <summary>
        /// Builds the type of the maintenance conditions with the instance
        /// declarations of <c>IMaintenanceEventType</c> (§12.2): the
        /// mandatory <c>MaintenanceState</c> with its <c>CurrentState</c> and
        /// <c>Id</c>, and the optional members.
        /// </summary>
        private static BaseObjectTypeState BuildMaintenanceActivityType(
            NamespaceTable namespaceUris,
            ushort typeNamespaceIndex,
            ushort amb)
        {
            const string name = MaintenanceActivityTypeName;
            BaseObjectTypeState type = CreateType(
                typeNamespaceIndex,
                name,
                ExpandedNodeId.ToNodeId(Opc.Ua.Di.ObjectTypeIds.MaintenanceRequiredAlarmType, namespaceUris),
                ExpandedNodeId.ToNodeId(ObjectTypeIds.IMaintenanceEventType, namespaceUris),
                "A current or future maintenance activity of an asset (OPC 10000-110 §12).");

            var stateMachine = new BaseObjectState(type)
            {
                NodeId = new NodeId(name + "_" + BrowseNames.MaintenanceState, typeNamespaceIndex),
                BrowseName = new QualifiedName(BrowseNames.MaintenanceState, amb),
                SymbolicName = BrowseNames.MaintenanceState,
                DisplayName = new LocalizedText(BrowseNames.MaintenanceState),
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.MaintenanceEventStateMachineType,
                    namespaceUris),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                ModellingRuleId = Ua.ObjectIds.ModellingRule_Mandatory,
                IsPartOfTypeHierarchy = true
            };
            var currentState = new BaseDataVariableState(stateMachine);
            Declare(
                currentState,
                typeNamespaceIndex,
                name + "_" + BrowseNames.MaintenanceState,
                new QualifiedName(Ua.BrowseNames.CurrentState),
                Ua.DataTypeIds.LocalizedText,
                ValueRanks.Scalar,
                Ua.ObjectIds.ModellingRule_Mandatory);
            currentState.TypeDefinitionId = Ua.VariableTypeIds.FiniteStateVariableType;
            currentState.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
            var id = new PropertyState(currentState);
            Declare(
                id,
                typeNamespaceIndex,
                name + "_" + BrowseNames.MaintenanceState + "_" + Ua.BrowseNames.CurrentState,
                new QualifiedName(Ua.BrowseNames.Id),
                Ua.DataTypeIds.NodeId,
                ValueRanks.Scalar,
                Ua.ObjectIds.ModellingRule_Mandatory);
            currentState.AddChild(id);
            stateMachine.AddChild(currentState);
            type.AddChild(stateMachine);

            NodeId nameNodeId = ExpandedNodeId.ToNodeId(DataTypeIds.NameNodeIdDataType, namespaceUris);
            (string Name, NodeId DataType, int ValueRank)[] optional =
            [
                (BrowseNames.PlannedDate, Ua.DataTypeIds.UtcTime, ValueRanks.Scalar),
                (BrowseNames.EstimatedDowntime, Ua.DataTypeIds.Duration, ValueRanks.Scalar),
                (BrowseNames.MaintenanceSupplier, nameNodeId, ValueRanks.Scalar),
                (BrowseNames.QualificationOfPersonnel, nameNodeId, ValueRanks.Scalar),
                (BrowseNames.PartsOfAssetReplaced, nameNodeId, ValueRanks.OneDimension),
                (BrowseNames.PartsOfAssetServiced, nameNodeId, ValueRanks.OneDimension),
                (
                    BrowseNames.MaintenanceMethod,
                    ExpandedNodeId.ToNodeId(DataTypeIds.MaintenanceMethodEnum, namespaceUris),
                    ValueRanks.Scalar),
                (BrowseNames.ConfigurationChanged, Ua.DataTypeIds.Boolean, ValueRanks.Scalar)
            ];
            foreach ((string member, NodeId dataType, int valueRank) in optional)
            {
                var declaration = new PropertyState(type);
                Declare(
                    declaration,
                    typeNamespaceIndex,
                    name,
                    new QualifiedName(member, amb),
                    dataType,
                    valueRank,
                    Ua.ObjectIds.ModellingRule_Optional);
                type.AddChild(declaration);
            }
            return type;
        }

        private static BaseObjectTypeState CreateType(
            ushort typeNamespaceIndex,
            string name,
            NodeId superTypeId,
            NodeId interfaceId,
            string description)
        {
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(name, typeNamespaceIndex),
                BrowseName = new QualifiedName(name, typeNamespaceIndex),
                DisplayName = new LocalizedText(name),
                Description = new LocalizedText(description),
                SuperTypeId = superTypeId,
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            type.AddReference(Ua.ReferenceTypeIds.HasInterface, false, interfaceId);
            return type;
        }

        /// <summary>
        /// Shapes a mandatory property declaration of a type.
        /// </summary>
        internal static void Declare(
            BaseVariableState declaration,
            ushort typeNamespaceIndex,
            string typeName,
            QualifiedName browseName,
            NodeId dataType,
            int valueRank)
        {
            Declare(
                declaration,
                typeNamespaceIndex,
                typeName,
                browseName,
                dataType,
                valueRank,
                Ua.ObjectIds.ModellingRule_Mandatory);
        }

        /// <summary>
        /// Shapes a property declaration of a type.
        /// </summary>
        internal static void Declare(
            BaseVariableState declaration,
            ushort typeNamespaceIndex,
            string typeName,
            QualifiedName browseName,
            NodeId dataType,
            int valueRank,
            NodeId modellingRule)
        {
            declaration.NodeId = new NodeId(typeName + "_" + browseName.Name, typeNamespaceIndex);
            declaration.BrowseName = browseName;
            declaration.SymbolicName = browseName.Name ?? string.Empty;
            declaration.DisplayName = new LocalizedText(browseName.Name);
            declaration.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            declaration.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            declaration.ModellingRuleId = modellingRule;
            declaration.DataType = dataType;
            declaration.ValueRank = valueRank;
            declaration.AccessLevel = AccessLevels.CurrentRead;
            declaration.UserAccessLevel = AccessLevels.CurrentRead;
            declaration.IsPartOfTypeHierarchy = true;
        }

        private static readonly AssetHealthAlarmKind[] s_kinds =
        [
            AssetHealthAlarmKind.Failure,
            AssetHealthAlarmKind.CheckFunction,
            AssetHealthAlarmKind.OffSpec,
            AssetHealthAlarmKind.MaintenanceRequired
        ];

        private readonly Dictionary<AssetHealthAlarmKind, NodeId> m_healthAlarmTypes;
    }
}
