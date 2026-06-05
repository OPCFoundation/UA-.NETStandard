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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Reference-server-specific <see cref="ConfigurationNodeManager"/> that
    /// applies a few address-space tweaks needed by the OPC UA Conformance
    /// Test (CTT) suite. These tweaks intentionally live outside of the SDK
    /// because they are test-suite-specific and modify the standard nodeset:
    /// <list type="bullet">
    ///   <item><description>Populate <c>RolePermissions</c>/<c>UserRolePermissions</c>/<c>AccessRestrictions</c>
    ///     on the Server node so CTT tests that read those optional
    ///     attributes get a defined value instead of BadAttributeIdInvalid.</description></item>
    ///   <item><description>Expose a single <c>HasAddIn</c> instance under the Server
    ///     node so CTT tests that require at least one AddIn complete.</description></item>
    ///   <item><description>Add an optional <c>EngineeringUnits</c> property to
    ///     <c>AnalogItemType</c> (Part 8 §5.3.2.3) so CTT browse tests that
    ///     expect this child don't skip.</description></item>
    /// </list>
    /// </summary>
    public sealed class ReferenceServerConfigurationNodeManager : ConfigurationNodeManager
    {
        /// <summary>
        /// Initializes the configuration node manager.
        /// </summary>
        public ReferenceServerConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration)
        {
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            ushort diagnosticsNamespaceIndex = Server.NamespaceUris
                .GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa + "Diagnostics");

            var addedNodes = new List<NodeState>();

            // CTT: populate RolePermissions / UserRolePermissions /
            // AccessRestrictions on the Server node so clients that read
            // those optional attributes (per Part 5 §6.2) get a defined
            // value instead of BadAttributeIdInvalid. Anonymous gets
            // Browse + Read on the metadata; SecurityAdmin and
            // ConfigureAdmin get the full permission mask. This is purely
            // metadata exposure — it doesn't change runtime access
            // enforcement (which is governed by the per-child node
            // RolePermissions in the standard nodeset). The NodeState
            // reference is shared between the diagnostics and core node
            // managers, so attribute mutations propagate automatically.
            ServerObjectState serverNode = FindPredefinedNode<ServerObjectState>(
                ObjectIds.Server);
            if (serverNode != null && serverNode.RolePermissions.IsNull)
            {
                const PermissionType browseAndRead =
                    PermissionType.Browse | PermissionType.Read | PermissionType.ReceiveEvents;
                const PermissionType fullAdmin =
                    PermissionType.Browse |
                    PermissionType.ReadRolePermissions |
                    PermissionType.WriteAttribute |
                    PermissionType.WriteRolePermissions |
                    PermissionType.WriteHistorizing |
                    PermissionType.Read |
                    PermissionType.Write |
                    PermissionType.ReadHistory |
                    PermissionType.InsertHistory |
                    PermissionType.ModifyHistory |
                    PermissionType.DeleteHistory |
                    PermissionType.ReceiveEvents |
                    PermissionType.Call |
                    PermissionType.AddReference |
                    PermissionType.RemoveReference |
                    PermissionType.DeleteNode;
                ArrayOf<RolePermissionType> permissions = new RolePermissionType[]
                {
                    new()
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)browseAndRead
                    },
                    new()
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)browseAndRead
                    },
                    new()
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)fullAdmin
                    },
                    new()
                    {
                        RoleId = ObjectIds.WellKnownRole_ConfigureAdmin,
                        Permissions = (uint)fullAdmin
                    }
                }.ToArrayOf();
                serverNode.RolePermissions = permissions;
                serverNode.UserRolePermissions = permissions;
                serverNode.AccessRestrictions = AccessRestrictionType.None;
            }

            // CTT: expose a single AddIn instance under the Server node
            // so the conformance tests that expect at least one HasAddIn
            // reference (Address Space Base AddIn instance / inverse / target
            // is Object) can complete successfully.  AddIn instances are an
            // optional extensibility point per Part 5 §6.3.6 — clients may
            // browse Server forward via HasAddIn (i=17604) and inspect the
            // returned objects.  The instance is a plain BaseObjectState.
            if (serverNode != null)
            {
                var addIn = new BaseObjectState(serverNode)
                {
                    SymbolicName = "ConformanceAddIn",
                    ReferenceTypeId = ReferenceTypeIds.HasAddIn,
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                    NodeId = new NodeId("ConformanceAddIn", diagnosticsNamespaceIndex),
                    BrowseName = new QualifiedName("ConformanceAddIn", diagnosticsNamespaceIndex),
                    DisplayName = LocalizedText.From("ConformanceAddIn"),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    EventNotifier = EventNotifiers.None
                };
                serverNode.AddReference(ReferenceTypeIds.HasAddIn, false, addIn.NodeId);
                addIn.AddReference(ReferenceTypeIds.HasAddIn, true, serverNode.NodeId);
                await AddPredefinedNodeAsync(SystemContext, addIn, cancellationToken)
                    .ConfigureAwait(false);
                addedNodes.Add(addIn);
            }

            // CTT: declare the optional EngineeringUnits property on
            // VariableTypeIds.AnalogItemType so conformance tests that
            // browse the type for an "EngineeringUnits" child don't skip.
            // Per Part 8 §5.3.2.3 EngineeringUnits is an optional
            // PropertyType child of AnalogItemType — but the standard
            // NodeSet2.xml as shipped only declares EURange. Adding
            // EngineeringUnits as a HasProperty Optional child here
            // matches the spec and is harmless for other clients (the
            // property carries a default-constructed EUInformation
            // value).
            BaseVariableTypeState analogItemType = FindPredefinedNode<BaseVariableTypeState>(
                VariableTypeIds.AnalogItemType);
            if (analogItemType != null &&
                analogItemType.FindChild(SystemContext, new QualifiedName(BrowseNames.EngineeringUnits, 0)) == null)
            {
                var euProperty = PropertyState<EUInformation>
                    .With<StructureBuilder<EUInformation>>(analogItemType);
                euProperty.SymbolicName = BrowseNames.EngineeringUnits;
                euProperty.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                euProperty.TypeDefinitionId = VariableTypeIds.PropertyType;
                euProperty.ModellingRuleId = ObjectIds.ModellingRule_Optional;
                euProperty.NodeId = new NodeId(BrowseNames.EngineeringUnits, diagnosticsNamespaceIndex);
                euProperty.BrowseName = new QualifiedName(BrowseNames.EngineeringUnits, 0);
                euProperty.DisplayName = LocalizedText.From(BrowseNames.EngineeringUnits);
                euProperty.DataType = DataTypeIds.EUInformation;
                euProperty.ValueRank = ValueRanks.Scalar;
                euProperty.AccessLevel = AccessLevels.CurrentRead;
                euProperty.UserAccessLevel = AccessLevels.CurrentRead;
                euProperty.Value = new EUInformation();
                analogItemType.AddChild(euProperty);
                await AddPredefinedNodeAsync(SystemContext, euProperty, cancellationToken)
                    .ConfigureAwait(false);
                addedNodes.Add(euProperty);
            }

            await EnsurePubSubKeyServiceMethodsAsync(addedNodes, cancellationToken)
                .ConfigureAwait(false);

            // Push any newly added namespace-0 nodes into the CoreNodeManager
            // so they are reachable via Browse from the standard address
            // space (base.CreateAddressSpaceAsync already performed the bulk
            // import; this top-up handles the CTT-only nodes).
            if (addedNodes.Count > 0)
            {
                await Server.CoreNodeManager.ImportNodesAsync(
                    SystemContext,
                    addedNodes,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask EnsurePubSubKeyServiceMethodsAsync(
            List<NodeState> addedNodes,
            CancellationToken cancellationToken)
        {
            BaseObjectState securityGroups = FindPredefinedNode<BaseObjectState>(SecurityGroupsNodeId);
            if (securityGroups != null)
            {
                ConfigurePubSubMetadataNode(securityGroups, CreatePubSubFolderRolePermissions());
                await EnsurePubSubMethodAsync(
                    securityGroups,
                    AddSecurityGroupMethodId,
                    BrowseNames.AddSecurityGroup,
                    new NodeId(15461),
                    new NodeId(15445),
                    CreateAddSecurityGroupInputArguments(),
                    new NodeId(15446),
                    CreateAddSecurityGroupOutputArguments(),
                    AddSecurityGroupAsync,
                    addedNodes,
                    cancellationToken).ConfigureAwait(false);
                await EnsurePubSubMethodAsync(
                    securityGroups,
                    RemoveSecurityGroupMethodId,
                    BrowseNames.RemoveSecurityGroup,
                    new NodeId(15464),
                    new NodeId(15448),
                    [CreateArgument("SecurityGroupNodeId", DataTypeIds.NodeId)],
                    NodeId.Null,
                    [],
                    RemoveSecurityGroupAsync,
                    addedNodes,
                    cancellationToken).ConfigureAwait(false);
            }

            BaseObjectState keyPushTargets = FindPredefinedNode<BaseObjectState>(KeyPushTargetsNodeId);
            if (keyPushTargets != null)
            {
                ConfigurePubSubMetadataNode(keyPushTargets, CreatePubSubFolderRolePermissions());
                await EnsurePubSubMethodAsync(
                    keyPushTargets,
                    AddPushTargetMethodId,
                    BrowseNames.AddPushTarget,
                    new NodeId(25366),
                    new NodeId(25442),
                    CreateAddPushTargetInputArguments(),
                    new NodeId(25443),
                    [CreateArgument("PushTargetId", DataTypeIds.NodeId)],
                    AddPushTargetAsync,
                    addedNodes,
                    cancellationToken).ConfigureAwait(false);
                await EnsurePubSubMethodAsync(
                    keyPushTargets,
                    RemovePushTargetMethodId,
                    BrowseNames.RemovePushTarget,
                    new NodeId(25369),
                    new NodeId(25445),
                    [CreateArgument("PushTargetId", DataTypeIds.NodeId)],
                    NodeId.Null,
                    [],
                    RemovePushTargetAsync,
                    addedNodes,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask EnsurePubSubMethodAsync(
            BaseObjectState parent,
            NodeId methodId,
            string browseName,
            NodeId methodDeclarationId,
            NodeId inputArgumentsId,
            Argument[] inputArguments,
            NodeId outputArgumentsId,
            Argument[] outputArguments,
            GenericMethodCalledEventHandler2Async handler,
            List<NodeState> addedNodes,
            CancellationToken cancellationToken)
        {
            MethodState method = FindPredefinedNode<MethodState>(methodId);
            if (method == null)
            {
                method = new MethodState(parent)
                {
                    SymbolicName = browseName,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    MethodDeclarationId = methodDeclarationId,
                    NodeId = methodId,
                    BrowseName = QualifiedName.From(browseName),
                    DisplayName = LocalizedText.From(browseName),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    Executable = true,
                    UserExecutable = true
                };
                parent.AddChild(method);
                ConfigureArgumentProperty(
                    method.CreateOrReplaceInputArguments(SystemContext, null),
                    inputArgumentsId,
                    BrowseNames.InputArguments,
                    inputArguments);
                if (!outputArgumentsId.IsNull)
                {
                    ConfigureArgumentProperty(
                        method.CreateOrReplaceOutputArguments(SystemContext, null),
                        outputArgumentsId,
                        BrowseNames.OutputArguments,
                        outputArguments);
                }
                await AddPredefinedNodeAsync(SystemContext, method, cancellationToken).ConfigureAwait(false);
                addedNodes.Add(method);
            }
            else
            {
                parent.AddReference(ReferenceTypeIds.HasComponent, false, method.NodeId);
                method.AddReference(ReferenceTypeIds.HasComponent, true, parent.NodeId);
                method.MethodDeclarationId = methodDeclarationId;
                method.Executable = true;
                method.UserExecutable = true;
                ConfigureArgumentProperty(
                    method.InputArguments ?? method.CreateOrReplaceInputArguments(
                        SystemContext,
                        FindPredefinedNode<BaseInstanceState>(inputArgumentsId)),
                    inputArgumentsId,
                    BrowseNames.InputArguments,
                    inputArguments);
                if (!outputArgumentsId.IsNull)
                {
                    ConfigureArgumentProperty(
                        method.OutputArguments ?? method.CreateOrReplaceOutputArguments(
                            SystemContext,
                            FindPredefinedNode<BaseInstanceState>(outputArgumentsId)),
                        outputArgumentsId,
                        BrowseNames.OutputArguments,
                        outputArguments);
                }
            }

            ConfigurePubSubMetadataNode(method, CreatePubSubMethodRolePermissions());
            ConfigurePubSubMetadataNode(method.InputArguments, CreatePubSubMetadataRolePermissions());
            ConfigurePubSubMetadataNode(method.OutputArguments, CreatePubSubMetadataRolePermissions());
            method.OnCallMethod2Async = handler;
        }

        private async ValueTask<ServiceResult> AddSecurityGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (!inputArguments[0].TryGetValue(out string securityGroupName))
            {
                return StatusCodes.BadInvalidArgument;
            }

            int id = Interlocked.Increment(ref m_pubSubInstanceId);
            string securityGroupId = CreatePubSubInstanceName(securityGroupName, "SecurityGroup", id);
            NodeId securityGroupNodeId = CreatePubSubInstanceNodeId("SecurityGroups", securityGroupId);
            if (!m_securityGroups.TryAdd(securityGroupNodeId, securityGroupId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ServiceResult result = await AddDynamicPubSubObjectAsync(
                SecurityGroupsNodeId,
                securityGroupNodeId,
                securityGroupId,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                m_securityGroups.TryRemove(securityGroupNodeId, out _);
                return result;
            }

            outputArguments[0] = Variant.From(securityGroupId);
            outputArguments[1] = Variant.From(securityGroupNodeId);
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> RemoveSecurityGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (!inputArguments[0].TryGetValue(out NodeId securityGroupNodeId) ||
                !m_securityGroups.TryRemove(securityGroupNodeId, out _))
            {
                return StatusCodes.BadInvalidArgument;
            }

            await DeleteNodeAsync(SystemContext, securityGroupNodeId, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> AddPushTargetAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (!inputArguments[0].TryGetValue(out string applicationUri))
            {
                return StatusCodes.BadInvalidArgument;
            }

            int id = Interlocked.Increment(ref m_pubSubInstanceId);
            string pushTargetName = CreatePubSubInstanceName(applicationUri, "PushTarget", id);
            NodeId pushTargetId = CreatePubSubInstanceNodeId("KeyPushTargets", pushTargetName);
            if (!m_pushTargets.TryAdd(pushTargetId, pushTargetName))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ServiceResult result = await AddDynamicPubSubObjectAsync(
                KeyPushTargetsNodeId,
                pushTargetId,
                pushTargetName,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                m_pushTargets.TryRemove(pushTargetId, out _);
                return result;
            }

            outputArguments[0] = Variant.From(pushTargetId);
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> RemovePushTargetAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (!inputArguments[0].TryGetValue(out NodeId pushTargetId) ||
                !m_pushTargets.TryRemove(pushTargetId, out _))
            {
                return StatusCodes.BadInvalidArgument;
            }

            await DeleteNodeAsync(SystemContext, pushTargetId, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> AddDynamicPubSubObjectAsync(
            NodeId parentNodeId,
            NodeId nodeId,
            string browseName,
            CancellationToken cancellationToken)
        {
            BaseObjectState parent = FindPredefinedNode<BaseObjectState>(parentNodeId);
            if (parent == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            ushort namespaceIndex = GetReferenceServerPubSubNamespaceIndex();
            var node = new BaseObjectState(parent)
            {
                SymbolicName = browseName,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = LocalizedText.From(browseName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            ConfigurePubSubMetadataNode(node, CreatePubSubObjectRolePermissions());
            parent.AddChild(node);

            await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
            await Server.CoreNodeManager.ImportNodesAsync(
                SystemContext,
                new[] { node },
                true,
                cancellationToken).ConfigureAwait(false);

            return ServiceResult.Good;
        }

        private NodeId CreatePubSubInstanceNodeId(string folderName, string browseName)
        {
            ushort namespaceIndex = GetReferenceServerPubSubNamespaceIndex();
            return new NodeId(folderName + "/" + browseName, namespaceIndex);
        }

        private ushort GetReferenceServerPubSubNamespaceIndex()
        {
            return (ushort)Server.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Namespaces.OpcUa + "ReferenceServer/PubSub");
        }

        private static string CreatePubSubInstanceName(string requestedName, string prefix, int id)
        {
            string name = string.IsNullOrWhiteSpace(requestedName) ? prefix : requestedName.Trim();
            return name + "-" + id.ToString(CultureInfo.InvariantCulture);
        }

        private static void ConfigureArgumentProperty(
            PropertyState<ArrayOf<Argument>> property,
            NodeId nodeId,
            string browseName,
            Argument[] arguments)
        {
            property.SymbolicName = browseName;
            property.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            property.TypeDefinitionId = VariableTypeIds.PropertyType;
            property.NodeId = nodeId;
            property.BrowseName = QualifiedName.From(browseName);
            property.DisplayName = LocalizedText.From(browseName);
            property.DataType = DataTypeIds.Argument;
            property.ValueRank = ValueRanks.OneDimension;
            property.ArrayDimensions = new[] { (uint)arguments.Length }.ToArrayOf();
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.Value = arguments.ToArrayOf();
        }

        private static void ConfigurePubSubMetadataNode(
            NodeState node,
            ArrayOf<RolePermissionType> rolePermissions)
        {
            if (node == null)
            {
                return;
            }

            node.AccessRestrictions = AccessRestrictionType.None;
            node.RolePermissions = rolePermissions;
            node.UserRolePermissions = rolePermissions;
        }

        private static Argument[] CreateAddSecurityGroupInputArguments()
        {
            return
            [
                CreateArgument("SecurityGroupName", DataTypeIds.String),
                CreateArgument("KeyLifetime", DataTypeIds.Duration),
                CreateArgument("SecurityPolicyUri", DataTypeIds.String),
                CreateArgument("MaxFutureKeyCount", DataTypeIds.UInt32),
                CreateArgument("MaxPastKeyCount", DataTypeIds.UInt32)
            ];
        }

        private static Argument[] CreateAddSecurityGroupOutputArguments()
        {
            return
            [
                CreateArgument("SecurityGroupId", DataTypeIds.String),
                CreateArgument("SecurityGroupNodeId", DataTypeIds.NodeId)
            ];
        }

        private static Argument[] CreateAddPushTargetInputArguments()
        {
            return
            [
                CreateArgument("ApplicationUri", DataTypeIds.String),
                CreateArgument("EndpointUrl", DataTypeIds.String),
                CreateArgument("SecurityPolicyUri", DataTypeIds.String),
                CreateArgument("UserTokenType", new NodeId(304)),
                CreateArgument("RequestedKeyCount", DataTypeIds.UInt16),
                CreateArgument("RetryInterval", DataTypeIds.Duration)
            ];
        }

        private static Argument CreateArgument(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar)
        {
            return new Argument
            {
                Name = name,
                Description = LocalizedText.From(name),
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private static ArrayOf<RolePermissionType> CreatePubSubFolderRolePermissions()
        {
            const PermissionType metadata = PermissionType.Browse | PermissionType.Read;
            const PermissionType admin = metadata | PermissionType.Call | PermissionType.AddNode |
                PermissionType.DeleteNode;

            return new RolePermissionType[]
            {
                CreateRolePermission(ObjectIds.WellKnownRole_Anonymous, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_AuthenticatedUser, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_ConfigureAdmin, admin),
                CreateRolePermission(ObjectIds.WellKnownRole_SecurityAdmin, admin)
            }.ToArrayOf();
        }

        private static ArrayOf<RolePermissionType> CreatePubSubMethodRolePermissions()
        {
            const PermissionType metadata = PermissionType.Browse | PermissionType.Read;
            const PermissionType admin = metadata | PermissionType.Call;

            return new RolePermissionType[]
            {
                CreateRolePermission(ObjectIds.WellKnownRole_Anonymous, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_AuthenticatedUser, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_ConfigureAdmin, admin),
                CreateRolePermission(ObjectIds.WellKnownRole_SecurityAdmin, admin)
            }.ToArrayOf();
        }

        private static ArrayOf<RolePermissionType> CreatePubSubMetadataRolePermissions()
        {
            const PermissionType metadata = PermissionType.Browse | PermissionType.Read;

            return new RolePermissionType[]
            {
                CreateRolePermission(ObjectIds.WellKnownRole_Anonymous, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_AuthenticatedUser, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_ConfigureAdmin, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_SecurityAdmin, metadata)
            }.ToArrayOf();
        }

        private static ArrayOf<RolePermissionType> CreatePubSubObjectRolePermissions()
        {
            const PermissionType metadata = PermissionType.Browse | PermissionType.Read;
            const PermissionType admin = metadata | PermissionType.DeleteNode;

            return new RolePermissionType[]
            {
                CreateRolePermission(ObjectIds.WellKnownRole_Anonymous, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_AuthenticatedUser, metadata),
                CreateRolePermission(ObjectIds.WellKnownRole_ConfigureAdmin, admin),
                CreateRolePermission(ObjectIds.WellKnownRole_SecurityAdmin, admin)
            }.ToArrayOf();
        }

        private static RolePermissionType CreateRolePermission(NodeId roleId, PermissionType permissions)
        {
            return new RolePermissionType
            {
                RoleId = roleId,
                Permissions = (uint)permissions
            };
        }

        private static readonly NodeId SecurityGroupsNodeId = new(15443);
        private static readonly NodeId AddSecurityGroupMethodId = new(15444);
        private static readonly NodeId RemoveSecurityGroupMethodId = new(15447);
        private static readonly NodeId KeyPushTargetsNodeId = new(25440);
        private static readonly NodeId AddPushTargetMethodId = new(25441);
        private static readonly NodeId RemovePushTargetMethodId = new(25444);

        private readonly ConcurrentDictionary<NodeId, string> m_securityGroups = [];
        private readonly ConcurrentDictionary<NodeId, string> m_pushTargets = [];
        private int m_pubSubInstanceId;
    }
}
