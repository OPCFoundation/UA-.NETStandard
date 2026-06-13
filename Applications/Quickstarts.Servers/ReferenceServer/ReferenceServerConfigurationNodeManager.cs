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

using System.Collections.Generic;
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

            await RemovePubSubKeyServiceFoldersAsync(cancellationToken).ConfigureAwait(false);

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

        /// <summary>
        /// Issue #3719: the standard NodeSet declares SecurityGroups
        /// (<c>i=15443</c>) and KeyPushTargets (<c>i=25440</c>) under
        /// PublishSubscribe and each carries a set of mandatory methods
        /// (<c>AddSecurityGroup</c>, <c>RemoveSecurityGroup</c>,
        /// <c>AddPushTarget</c>, <c>RemovePushTarget</c>). The reference
        /// server does not implement PubSub key services, so the folders
        /// are removed entirely rather than carrying no-op method stubs
        /// that would only exist to satisfy the mandatory-method check.
        /// Both folders are optional under <c>PublishSubscribe</c> per
        /// Part 14, so removing them is spec-compliant.
        /// </summary>
        private async ValueTask RemovePubSubKeyServiceFoldersAsync(CancellationToken cancellationToken)
        {
            foreach (NodeId folderNodeId in s_optionalPubSubFolders)
            {
                if (FindPredefinedNode<NodeState>(folderNodeId) != null)
                {
                    await DeleteNodeAsync(SystemContext, folderNodeId, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private static readonly NodeId[] s_optionalPubSubFolders =
        [
            // PublishSubscribe.SecurityGroups instance (NodeId i=15443).
            new NodeId(15443u),
            // PublishSubscribe.KeyPushTargets instance (NodeId i=25440).
            new NodeId(25440u)
        ];
    }
}
