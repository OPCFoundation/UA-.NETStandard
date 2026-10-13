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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Gets current per-instance supported CUs, profile dependencies and actual monitoring-capacity evidence.
        /// The values describe behavior, not certification, namespace presence or available NuGet packages.
        /// </summary>
        public MachineToolCapabilities Capabilities => BuildCapabilities();

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => Capabilities.ConformanceUnits;

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => Capabilities.ServerProfiles;

        /// <summary>
        /// Adds a clearly application-owned read-only declaration before the existing machine registration commits.
        /// </summary>
        internal void ConfigureCapabilities(MachineToolMonitoringRequirements requirements, bool jobManagement)
        {
            m_monitoringRequirements = requirements;
            m_hasJobManagementBinding = jobManagement;
            ushort ns = m_context.Context.NamespaceUris.GetIndexOrAppend(MachineToolCapabilityNames.NamespaceUri);
            m_capabilityDeclaration = m_context.Context.CreateInstanceOfBaseObjectType(
                State, new QualifiedName(MachineToolCapabilityNames.Capabilities, ns));
            m_capabilityDeclaration.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            State.AddChild(m_capabilityDeclaration);
            ArrayOf<string> fields = MachineToolCapabilityNames.Fields;
            for (int index = 0; index < fields.Count; index++)
            {
                var variable = new BaseDataVariableState(m_capabilityDeclaration)
                {
                    BrowseName = new QualifiedName(fields[index], ns),
                    DisplayName = new LocalizedText(fields[index]),
                    SymbolicName = fields[index],
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = Ua.VariableTypeIds.PropertyType,
                    DataType = index switch
                    {
                        0 => Ua.DataTypeIds.QualifiedName,
                        1 => Ua.DataTypeIds.String,
                        >= 10 => Ua.DataTypeIds.Boolean,
                        _ => Ua.DataTypeIds.UInt32
                    },
                    ValueRank = index < 2 ? ValueRanks.OneDimension : ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    StatusCode = StatusCodes.Good
                };
                variable.NodeId = m_context.Context.NodeIdFactory!.New(m_context.Context, variable);
                variable.OnSimpleReadValueAsync = ReadCapabilityAsync;
                m_capabilityDeclaration.AddChild(variable);
            }
        }

        /// <summary>
        /// Registers a weak contribution only after the actual Machinery registration has succeeded.
        /// </summary>
        internal void RegisterCapabilities()
        {
            MachineToolConformanceRegistration.Register(m_context.Manager, this);
        }

        /// <summary>
        /// Reads live capability values through the standard asynchronous variable-read pipeline.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ValueTask<AttributeSimpleReadResult> ReadCapabilityAsync(
            ISystemContext context, NodeState node, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MachineToolCapabilities capabilities = Capabilities;
            MachineToolMonitoringCapacity capacity = capabilities.Monitoring;
            Variant value = node.SymbolicName switch
            {
                "ConformanceUnits" => Variant.From(capabilities.ConformanceUnits),
                "ServerProfiles" => Variant.From(capabilities.ServerProfiles),
                "PotentialMonitoredItems" => Variant.From(capacity.PotentialMonitoredItems),
                "MaximumSessions" => Variant.From(capacity.MaximumSessions),
                "MaximumSubscriptions" => Variant.From(capacity.MaximumSubscriptions),
                "MaximumSubscriptionsPerSession" => Variant.From(capacity.MaximumSubscriptionsPerSession),
                "MaximumMonitoredItems" => Variant.From(capacity.MaximumMonitoredItems),
                "MaximumMonitoredItemsPerSubscription" => Variant.From(capacity.MaximumMonitoredItemsPerSubscription),
                "RequiredSessions" => Variant.From(capacity.RequiredSessions),
                "RequiredSubscriptionsPerSession" => Variant.From(capacity.RequiredSubscriptionsPerSession),
                "SupportsMinimum" => Variant.From(capacity.SupportsMinimum),
                "SupportsAll" => Variant.From(capacity.SupportsAll),
                _ => throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "Unknown capability declaration field.")
            };
            ((BaseVariableState)node).Timestamp = DateTimeUtc.Now;
            return new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(ServiceResult.Good, value));
        }

        /// <summary>
        /// Maps all 37 released section 14.1 CUs to this instance's selected, validated provider bindings.
        /// </summary>
        private MachineToolCapabilities BuildCapabilities()
        {
            List<NodeState> nodes = CapabilityNodes();
            MachineToolMonitoringCapacity monitoring = ReadMonitoringCapacity(nodes);
            if (m_disposed || !ReferenceEquals(m_context.Manager.FindPredefinedNode(NodeId), State))
            {
                return new MachineToolCapabilities(NodeId, [], [], monitoring);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool production = Selected(MachineToolFeatures.Production);
            bool parts = Selected(MachineToolFeatures.ProductionParts);
            bool jobs = production && Production.Observation?.Production.Jobs.Count > 0;
            bool dynamicJobs = production && Selected(MachineToolFeatures.DynamicProductionPlan);
            bool rootEvents = Notifier(State);
            bool tools = Selected(MachineToolFeatures.Tools) && State.Equipment?.Tools != null;
            bool prognoses = Selected(MachineToolFeatures.Prognoses) && State.Notification?.Prognoses != null;
            bool notifications = Notifications is { IsDisposed: false };
            Add("MachineTool MachineToolType Mandatory Nodes", true);
            Add("MachineTool Monitoring Basic - Stacklight", Selected(MachineToolFeatures.Stacklights));
            Add("MachineTool Monitoring Basic - PowerOnDuration", Selected(MachineToolFeatures.LegacyPowerOnDuration));
            Add("MachineTool Monitoring Machinery PowerOnDuration", Selected(MachineToolFeatures.OperationCounters));
            Add("MachineTool Monitoring Basic - Channels", Selected(MachineToolFeatures.Channels));
            Add("MachineTool Production Basic", State.Production?.ActiveProgram?.State != null);
            Add("MachineTool Identification SoftwareInformation", Selected(MachineToolFeatures.SoftwareIdentification));
            Add("MachineTool Identification Machinery additional",
                Selected(MachineToolFeatures.AdditionalIdentification));
            Add("MachineTool Monitoring WorkingUnit", Selected(MachineToolFeatures.WorkingUnits));
            Add("MachineTool Equipment ToolIdentification", tools);
            Add("MachineTool Equipment Dynamic Tool List", tools && Selected(MachineToolFeatures.DynamicTools));
            Add("MachineTool Notification - Errors and Alerts", notifications);
            Add("MachineTool Production Job", jobs);
            Add("MachineTool Production LastTransition", production);
            Add("MachineTool Production ProductionJobStateMachineType", production);
            Add("MachineTool Production ProductionProgramStateMachineType", production);
            Add("MachineTool Production ProductionPartStateMachineType", production && parts);
            Add("MachineTool Production InterruptionConditionType",
                production && Selected(MachineToolFeatures.ProductionInterruptions));
            Add("MachineTool Equipment ToolLife", tools && Selected(MachineToolFeatures.ToolLife));
            Add("MachineTool PrognosisType", prognoses);
            Add("MachineTool Prognoses Dynamic List", prognoses && Selected(MachineToolFeatures.DynamicPrognoses));
            Add("MachineTool Production Dynamic Job List", dynamicJobs);
            Add("MachineTool Production Job Available", jobs || dynamicJobs);
            Add("MachineTool Monitor Items Min", monitoring.SupportsMinimum);
            Add("MachineTool Monitor Items", monitoring.SupportsAll);
            Add("MachineTool Event Propagation", rootEvents);
            Add("MachineTool Event Tools", tools && Notifier(State.Equipment!.Tools));
            Add("MachineTool Event Production", AllProductionNotifiers(nodes));
            Add("MachineTool Event Messages", notifications && Notifier(Notifications!.State));
            Add("MachineTool Event Prognoses", prognoses && Notifier(State.Notification!.Prognoses));
            Add("MachineTool Monitoring Obligation", Selected(MachineToolFeatures.Obligation));
            Add("MachineTool Production PartsProducedInLifetime",
                Selected(MachineToolFeatures.PartsProducedInLifetime));
            Add("MachineTool Production Simple Parts Monitoring", production && parts);
            Add("MachineTool Monitoring MaintenanceMode", Selected(MachineToolFeatures.MaintenanceMode));
            Add("MachineTool Components", HasBoundComponents());
            Add("MachineTool Production Machinery Job Management", m_hasJobManagementBinding);
            Add("MachineTool FileSystem", m_fileBinding != null);
            ushort ns = (ushort)m_context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool);
            var units = new List<QualifiedName>(names.Count);
            foreach (string name in names)
            {
                units.Add(new QualifiedName(name, ns));
            }
            return new MachineToolCapabilities(
                NodeId, units.ToArrayOf(), BuildProfiles(names), monitoring);

            void Add(string name, bool supported)
            {
                if (supported)
                {
                    names.Add(name);
                }
            }
        }

        /// <summary>
        /// Evaluates the Table 122 through 133 selection dependencies and configured endpoint security capabilities.
        /// </summary>
        private ArrayOf<string> BuildProfiles(HashSet<string> units)
        {
            var profiles = new List<string>();
            const string prefix = "http://opcfoundation.org/UA-Profile/MachineTool/Server/";
            bool basic = Has("MachineTool Monitor Items Min", "MachineTool Monitoring Basic - Stacklight",
                "MachineTool Monitoring Basic - Channels", "MachineTool Production Basic") &&
                HasSecureEndpoint(SecurityPolicies.Basic256Sha256, certificateToken: false);
            bool monitoring = Has("MachineTool Identification SoftwareInformation",
                "MachineTool Identification Machinery additional", "MachineTool Monitoring WorkingUnit");
            bool alerts = Has("MachineTool Notification - Errors and Alerts",
                "MachineTool Event Propagation", "MachineTool Event Messages");
            bool transitionEvents = Has("MachineTool Production LastTransition",
                "MachineTool Production ProductionJobStateMachineType",
                "MachineTool Production ProductionProgramStateMachineType",
                "MachineTool Production ProductionPartStateMachineType",
                "MachineTool Event Propagation", "MachineTool Event Production");
            Add("Basic", basic);
            Add("BasicSecure", basic &&
                HasSecureEndpoint(SecurityPolicies.Aes128_Sha256_RsaOaep, certificateToken: true));
            Add("Monitoring", monitoring);
            Add("Tools", Has("MachineTool Equipment ToolIdentification", "MachineTool Event Tools"));
            Add("ToolLife", Has("MachineTool Equipment ToolIdentification", "MachineTool Equipment ToolLife"));
            Add("Production", alerts && transitionEvents && Has("MachineTool Production Job"));
            Add("ProductionPlan", alerts && transitionEvents && Has("MachineTool Production Dynamic Job List"));
            Add("ErrorsAndAlerts", alerts);
            Add("Prognoses", Has("MachineTool PrognosisType", "MachineTool Prognoses Dynamic List",
                "MachineTool Event Propagation", "MachineTool Event Prognoses"));
            Add("KPIMonitoring", monitoring &&
                alerts &&
                transitionEvents &&
                Selected(MachineToolFeatures.MachineryState) &&
                Has("MachineTool Production Job Available", "MachineTool Monitoring Obligation",
                    "MachineTool Production PartsProducedInLifetime",
                    "MachineTool Production Simple Parts Monitoring"));
            Add("Components", Has("MachineTool Components"));
            Add("JobManagement", Has("MachineTool Production Machinery Job Management"));
            return profiles.ToArrayOf();

            bool Has(params string[] required)
            {
                foreach (string unit in required)
                {
                    if (!units.Contains(unit))
                    {
                        return false;
                    }
                }
                return true;
            }

            void Add(string suffix, bool supported)
            {
                if (supported)
                {
                    profiles.Add(prefix + suffix);
                }
            }
        }

        /// <summary>
        /// Checks active endpoint descriptions, never the selected client channel or namespace presence.
        /// </summary>
        private bool HasSecureEndpoint(string policy, bool certificateToken)
        {
            if (m_context.Manager.Server is not IServerEndpointRegistryProvider registry)
            {
                return false;
            }
            foreach (EndpointDescription endpoint in registry.ServerEndpoints)
            {
                if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    endpoint.SecurityPolicyUri != policy ||
                    endpoint.ServerCertificate.IsEmpty)
                {
                    continue;
                }
                if (!certificateToken)
                {
                    return true;
                }
                foreach (UserTokenPolicy token in endpoint.UserIdentityTokens)
                {
                    if (token.TokenType == UserTokenType.Certificate &&
                        (string.IsNullOrEmpty(token.SecurityPolicyUri) ||
                            token.SecurityPolicyUri != SecurityPolicies.None))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Counts the actual exposed graph, including referenced components, without counting this extension itself.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private List<NodeState> CapabilityNodes()
        {
            var result = new List<NodeState>();
            var visited = new HashSet<NodeId>();
            var pending = new Stack<NodeState>();
            pending.Push(State);
            while (pending.Count != 0)
            {
                NodeState current = pending.Pop();
                if (ReferenceEquals(current, m_capabilityDeclaration) || !visited.Add(current.NodeId))
                {
                    continue;
                }
                if (visited.Count > 100000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The machine graph exceeds the bounded capability inspection limit.");
                }
                result.Add(current);
                var children = new List<BaseInstanceState>();
                current.GetChildren(m_context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
                var references = new List<IReference>();
                current.GetReferences(m_context.Context, references);
                foreach (IReference reference in references)
                {
                    if (!reference.IsInverse &&
                        m_context.Context.TypeTable.IsTypeOf(
                            reference.ReferenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                    {
                        var id = ExpandedNodeId.ToNodeId(reference.TargetId, m_context.Context.NamespaceUris);
                        if (!id.IsNull && m_context.Manager.FindPredefinedNode(id) is BaseInstanceState target)
                        {
                            pending.Push(target);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Evaluates real server-wide and per-subscription ceilings using the published configured quotas.
        /// </summary>
        private MachineToolMonitoringCapacity ReadMonitoringCapacity(List<NodeState> nodes)
        {
            uint count = 0;
            foreach (NodeState node in nodes)
            {
                if (node is BaseVariableState || (node is BaseObjectState obj && Notifier(obj)))
                {
                    count++;
                }
            }
            ServerCapabilitiesState? server = m_context.Manager.Server.DiagnosticsNodeManager
                .FindPredefinedNode<ServerCapabilitiesState>(Ua.ObjectIds.Server_ServerCapabilities);
            uint sessions = server?.MaxSessions?.Value ?? 0;
            uint subscriptions = server?.MaxSubscriptions?.Value ?? 0;
            uint perSession = server?.MaxSubscriptionsPerSession?.Value ?? 0;
            uint allItems = server?.MaxMonitoredItems?.Value ?? 0;
            uint perSubscription = server?.MaxMonitoredItemsPerSubscription?.Value ?? 0;
            uint halfSessions = Math.Max(1, (m_monitoringRequirements.Sessions + 1) / 2);
            uint halfSubscriptions = Math.Max(1, (m_monitoringRequirements.SubscriptionsPerSession + 1) / 2);
            ulong totalSubscriptions = (ulong)halfSessions * halfSubscriptions;
            ulong fullItems = count * totalSubscriptions;
            uint minimumItems = Math.Max(20, count);
            bool known = server?.MaxMonitoredItems != null && server.MaxMonitoredItemsPerSubscription != null;
            bool minimum = known &&
                sessions >= 1 &&
                subscriptions >= 1 &&
                perSession >= 1 &&
                Fits(perSubscription, minimumItems) &&
                Fits(allItems, minimumItems);
            bool all = known &&
                sessions >= halfSessions &&
                subscriptions >= totalSubscriptions &&
                perSession >= halfSubscriptions &&
                Fits(perSubscription, count) &&
                Fits(allItems, fullItems);
            return new MachineToolMonitoringCapacity(
                count, sessions, subscriptions, perSession, allItems, perSubscription,
                m_monitoringRequirements.Sessions, m_monitoringRequirements.SubscriptionsPerSession, minimum, all);

            static bool Fits(uint ceiling, ulong demand) => ceiling == 0 || ceiling >= demand;
        }

        /// <summary>
        /// Verifies every actually exposed retained-production event source, rather than a type name alone.
        /// </summary>
        private bool AllProductionNotifiers(List<NodeState> nodes)
        {
            var stateType = ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionStateMachineType,
                m_context.Context.NamespaceUris);
            var jobType = ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionJobType, m_context.Context.NamespaceUris);
            var listType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ProductionJobListType, m_context.Context.NamespaceUris);
            bool any = false;
            foreach (NodeState node in nodes)
            {
                if (node is BaseObjectState obj &&
                    (m_context.Context.TypeTable.IsTypeOf(obj.TypeDefinitionId, stateType) ||
                        m_context.Context.TypeTable.IsTypeOf(obj.TypeDefinitionId, jobType) ||
                        m_context.Context.TypeTable.IsTypeOf(obj.TypeDefinitionId, listType)))
                {
                    any = true;
                    if (!Notifier(obj))
                    {
                        return false;
                    }
                }
            }
            return any;
        }

        /// <summary>
        /// Detects the actual shared Components binding once selected by this machine builder.
        /// </summary>
        private bool HasBoundComponents()
        {
            return Components != null;
        }

        /// <summary>
        /// Checks explicit feature selection after the corresponding provider inventory was validated.
        /// </summary>
        private bool Selected(MachineToolFeatures features)
        {
            return (Features & features) == features;
        }

        /// <summary>
        /// Checks the actual event-notifier attribute.
        /// </summary>
        private static bool Notifier(BaseObjectState? node)
        {
            return node != null && (node.EventNotifier & EventNotifiers.SubscribeToEvents) != 0;
        }

        /// <summary>The read-only library-owned extension, excluded from standard-model item accounting.</summary>
        private BaseObjectState? m_capabilityDeclaration;

        /// <summary>The declared deployment monitoring requirements, not server quota overrides.</summary>
        private MachineToolMonitoringRequirements m_monitoringRequirements = new();

        /// <summary>Both job providers and a per-instance catalog were selected.</summary>
        private bool m_hasJobManagementBinding;
    }
}
