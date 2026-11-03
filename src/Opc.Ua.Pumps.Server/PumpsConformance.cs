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
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Evaluates OPC 40223 Table 152 against the currently registered pump instances.
    /// </summary>
    internal sealed class PumpsConformance : IConformanceContributor
    {
        /// <summary>
        /// Associates evaluation with one standalone or composed runtime.
        /// </summary>
        public PumpsConformance(PumpsRuntime runtime)
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var pumps = new List<PumpState>();
                foreach (PumpState pump in m_runtime.Pumps)
                {
                    if (IsRegistered(pump) && IsPumpType(pump, ObjectTypes.PumpType))
                    {
                        pumps.Add(pump);
                    }
                }
                if (m_runtime.IsDisposed || pumps.Count == 0)
                {
                    return [];
                }

                var nodes = new List<BaseInstanceState>();
                foreach (PumpState pump in pumps)
                {
                    Collect(pump, nodes);
                }
                var units = new List<QualifiedName>
                {
                    new("Base System", m_runtime.NamespaceIndices.Pumps)
                };
                Add(units, pumps.TrueForAll(IsComplete), "Pump PumpType Mandatory Nodes");
                Add(units, pumps.TrueForAll(pump =>
                    pump.Identification is { } identification &&
                        IsComplete(identification) && HasReadAccess(identification)),
                    "Pump Identification");
                Add(units, pumps.TrueForAll(HasLegalPumpClass), "PumpClass Data");
                Add(units, nodes.Exists(node => IsPumpType(node, ObjectTypes.InletConnectionPortType) ||
                    IsPumpType(node, ObjectTypes.OutletConnectionPortType)), "Pump Connection Port");
                Add(units, nodes.Exists(node => IsPumpType(node, ObjectTypes.DrivePortType)), "Pump Drive Port");
                Add(units, nodes.Exists(node => IsPumpType(node, ObjectTypes.ControlType) ||
                    IsPumpType(node, ObjectTypes.ActuationType)), "Pump Control");
                Add(units, nodes.Exists(HasHistory), "Historizing");
                Add(units, HasMeasurementDeclarations() &&
                    nodes.Exists(node => HasBoundAlarm(node, Opc.Ua.ObjectTypeIds.LimitAlarmType)),
                    "Limit Alarm Status");

                var supervision = new List<BaseInstanceState>();
                foreach (BaseInstanceState node in nodes)
                {
                    if (m_runtime.SystemContext.TypeTable.IsTypeOf(
                        node.TypeDefinitionId, Opc.Ua.VariableTypeIds.TwoStateDiscreteType) &&
                        IsSupervisionInput(node))
                    {
                        supervision.Add(node);
                    }
                }
                NodeId diagnosticType = ExpandedNodeId.ToNodeId(
                    Di.ObjectTypeIds.DeviceHealthDiagnosticAlarmType, m_runtime.SystemContext.NamespaceUris);
                Add(units, supervision.Count > 0 && supervision.TrueForAll(node =>
                    HasAlarm(node, diagnosticType) && HasBoundAlarm(node, diagnosticType)),
                    "Supervision Health Status");
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Native units do not prove the inherited core and Machinery facets. The published Advanced
        /// profile also links its required Base profile to the CAS namespace instead of Pumps.
        /// </remarks>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Walks only live instance children, without creating optional model members.
        /// </summary>
        private void Collect(NodeState parent, List<BaseInstanceState> nodes)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_runtime.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (IsRegistered(child))
                {
                    nodes.Add(child);
                    Collect(child, nodes);
                }
            }
        }

        /// <summary>
        /// Checks mandatory declarations from the concrete type and every supertype.
        /// </summary>
        private bool IsComplete(BaseInstanceState instance)
        {
            if (!IsRegistered(instance))
            {
                return false;
            }
            NodeId typeId = instance.TypeDefinitionId;
            var visited = new HashSet<NodeId>();
            while (!typeId.IsNull && visited.Add(typeId))
            {
                // FolderType adds no mandatory children and is owned by the core manager.
                if (typeId == Opc.Ua.ObjectTypeIds.BaseObjectType || typeId == Opc.Ua.ObjectTypeIds.FolderType)
                {
                    return true;
                }
                if (m_runtime.Manager.FindPredefinedNode(typeId) is not BaseTypeState type ||
                    !HasMandatoryChildren(type, instance))
                {
                    return false;
                }
                typeId = type.SuperTypeId;
            }
            return typeId.IsNull;
        }

        /// <summary>
        /// Requires every mandatory declaration while checking children of optional nodes that are present.
        /// </summary>
        private bool HasMandatoryChildren(NodeState declaration, NodeState instance)
        {
            var children = new List<BaseInstanceState>();
            declaration.GetChildren(m_runtime.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                BaseInstanceState? actual = instance.FindChild(m_runtime.SystemContext, child.BrowseName);
                if (actual is null)
                {
                    if (child.ModellingRuleId == Opc.Ua.ObjectIds.ModellingRule_Mandatory)
                    {
                        return false;
                    }
                    continue;
                }
                if (!IsRegistered(actual) || actual.NodeClass != child.NodeClass ||
                    (!child.TypeDefinitionId.IsNull &&
                        !m_runtime.SystemContext.TypeTable.IsTypeOf(actual.TypeDefinitionId, child.TypeDefinitionId)) ||
                    (child is BaseVariableState expected && actual is BaseVariableState variable &&
                        (!m_runtime.SystemContext.TypeTable.IsTypeOf(variable.DataType, expected.DataType) ||
                            !ValueRanks.IsValid(variable.ValueRank, expected.ValueRank))) ||
                    !HasMandatoryChildren(child, actual))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Requires the declared PumpClass on every current pump to contain a legal value.
        /// </summary>
        private bool HasLegalPumpClass(PumpState pump)
        {
            BaseInstanceState? configuration = pump.FindChild(m_runtime.SystemContext,
                new QualifiedName(PumpsModel.DiGroups.Configuration, m_runtime.NamespaceIndices.Di));
            BaseInstanceState? design = configuration?.FindChild(m_runtime.SystemContext,
                new QualifiedName(BrowseNames.Design, m_runtime.NamespaceIndices.Pumps));
            return design?.FindChild(m_runtime.SystemContext,
                new QualifiedName(BrowseNames.PumpClass, m_runtime.NamespaceIndices.Pumps))
                is BaseVariableState variable && IsRegistered(variable) &&
                StatusCode.IsGood(variable.StatusCode) &&
                variable.WrappedValue.TryGetValue(out DeviceTypeEnum value) && (int)value is >= 0 and <= 6;
        }

        /// <summary>
        /// Requires read access for every instantiated identification field without changing role authorization.
        /// </summary>
        private bool HasReadAccess(NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_runtime.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if ((child is BaseVariableState variable && (variable.AccessLevel & AccessLevels.CurrentRead) == 0) ||
                    !HasReadAccess(child))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks the live type table rather than accepting a matching browse name.
        /// </summary>
        private bool IsPumpType(BaseInstanceState node, uint identifier)
        {
            return m_runtime.SystemContext.TypeTable.IsTypeOf(node.TypeDefinitionId,
                PumpsModel.TypeNodeId(identifier, m_runtime.SystemContext.NamespaceUris));
        }

        /// <summary>
        /// Requires an active shared data-provider route and real history access on a quantity.
        /// </summary>
        private bool HasHistory(BaseInstanceState node)
        {
            return node is BaseVariableState { Historizing: true } variable &&
                (variable.AccessLevel & AccessLevels.HistoryRead) != 0 &&
                m_runtime.SystemContext.TypeTable.IsTypeOf(
                    variable.TypeDefinitionId, Opc.Ua.VariableTypeIds.BaseAnalogType) &&
                m_runtime.Manager.Server is IHistorianRegistryProvider registry &&
                registry.HistorianRegistry.Resolve(variable.NodeId) is IHistorianDataProvider;
        }

        /// <summary>
        /// Requires every specified measurement type to declare a live configured limit alarm subtype.
        /// </summary>
        private bool HasMeasurementDeclarations()
        {
            foreach (uint identifier in new[]
            {
                ObjectTypes.MeasurementsType,
                ObjectTypes.DriveMeasurementsType,
                ObjectTypes.InletConnectionMeasurementsType,
                ObjectTypes.OutletConnectionMeasurementsType
            })
            {
                if (m_runtime.Manager.FindPredefinedNode(PumpsModel.TypeNodeId(
                    identifier, m_runtime.SystemContext.NamespaceUris)) is not NodeState type ||
                    !HasAlarm(type, Opc.Ua.ObjectTypeIds.LimitAlarmType))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Matches an actual event declaration against the required standard alarm hierarchy.
        /// </summary>
        private bool HasAlarm(NodeState source, NodeId requiredType)
        {
            var references = new List<IReference>();
            source.GetReferences(m_runtime.SystemContext, references, Opc.Ua.ReferenceTypeIds.GeneratesEvent, false);
            foreach (IReference reference in references)
            {
                NodeId target = ExpandedNodeId.ToNodeId(reference.TargetId, m_runtime.SystemContext.NamespaceUris);
                if (!reference.TargetId.IsAbsolute &&
                    m_runtime.SystemContext.TypeTable.IsKnown(target) &&
                    m_runtime.SystemContext.TypeTable.IsTypeOf(target, requiredType))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Requires a registered condition bound to the variable, not just model-level event declarations.
        /// </summary>
        private bool HasBoundAlarm(NodeState source, NodeId requiredType)
        {
            var references = new List<IReference>();
            source.GetReferences(m_runtime.SystemContext, references, Opc.Ua.ReferenceTypeIds.HasCondition, false);
            foreach (IReference reference in references)
            {
                if (reference.TargetId.IsAbsolute)
                {
                    continue;
                }
                NodeId target = ExpandedNodeId.ToNodeId(reference.TargetId, m_runtime.SystemContext.NamespaceUris);
                if (m_runtime.Manager.FindPredefinedNode(target) is AlarmConditionState alarm &&
                    alarm.InputNode is { } input && input.Value == source.NodeId &&
                    m_runtime.SystemContext.TypeTable.IsTypeOf(alarm.TypeDefinitionId, requiredType))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Includes all discrete variables below the supervision group and its specialized children.
        /// </summary>
        private bool IsSupervisionInput(BaseInstanceState node)
        {
            for (NodeState? parent = node.Parent; parent is BaseInstanceState instance; parent = instance.Parent)
            {
                if (IsPumpType(instance, ObjectTypes.SupervisionType))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Excludes removed nodes even while the runtime retains their construction handles.
        /// </summary>
        private bool IsRegistered(NodeState node)
        {
            return ReferenceEquals(m_runtime.Manager.FindPredefinedNode(node.NodeId), node);
        }

        /// <summary>
        /// Publishes an exact Table 152 title only when its configured rule is met.
        /// </summary>
        private void Add(List<QualifiedName> units, bool met, string title)
        {
            if (met)
            {
                units.Add(new QualifiedName(title, m_runtime.NamespaceIndices.Pumps));
            }
        }

        /// <summary>The standalone or composed owner being evaluated.</summary>
        private readonly PumpsRuntime m_runtime;
    }
}
