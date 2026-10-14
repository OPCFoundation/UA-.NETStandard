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
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Applies host authority to generic UA mutation targets using their actual model types and owning hierarchy.
    /// A whole request is checked before any mutation is dispatched.
    /// </summary>
    public sealed class McpUaOperationGuard
    {
        /// <summary>
        /// Creates a guard over the host's injected capability policy.
        /// </summary>
        public McpUaOperationGuard(IMcpOperationPolicy policy)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        /// Checks every generic method target and refuses secret-producing model methods on an untyped path.
        /// </summary>
        public async ValueTask AuthorizeCallsAsync(
            OpcUaSessionManager.SessionInfo session,
            ArrayOf<CallMethodRequest> requests,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ct.ThrowIfCancellationRequested();
            for (int index = 0; index < requests.Count; index++)
            {
                CallMethodRequest request = requests[index];
                ArgumentNullException.ThrowIfNull(request);
                await AuthorizeTargetAsync(session, request.ObjectId, genericCall: true, ct).ConfigureAwait(false);
                await AuthorizeTargetAsync(session, request.MethodId, genericCall: true, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks node ownership and permission attributes before sending a generic Write request.
        /// </summary>
        public async ValueTask AuthorizeWritesAsync(
            OpcUaSessionManager.SessionInfo session,
            ArrayOf<WriteValue> requests,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ct.ThrowIfCancellationRequested();
            for (int index = 0; index < requests.Count; index++)
            {
                WriteValue request = requests[index];
                ct.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(request);
                if (request.AttributeId is Attributes.RolePermissions or Attributes.UserRolePermissions or
                    Attributes.AccessRestrictions or Attributes.UserAccessLevel or Attributes.UserExecutable or
                    Attributes.UserWriteMask or Attributes.WriteMask or Attributes.AccessLevel or
                    Attributes.AccessLevelEx or Attributes.Executable)
                {
                    Require(session, McpCapability.IdentityAdministration);
                }
                await AuthorizeTargetAsync(session, request.NodeId, genericCall: false, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks every affected parent, target and type before a generic node/reference mutation.
        /// </summary>
        public async ValueTask AuthorizeNodesAsync(
            OpcUaSessionManager.SessionInfo session,
            ArrayOf<NodeId> nodes,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ct.ThrowIfCancellationRequested();
            for (int index = 0; index < nodes.Count; index++)
            {
                await AuthorizeTargetAsync(session, nodes[index], genericCall: false, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies the same protection to file or domain operations whose selected node may be a protected subtype.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask AuthorizeTargetAsync(
            OpcUaSessionManager.SessionInfo session,
            NodeId node,
            bool genericCall,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ct.ThrowIfCancellationRequested();
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "A mutation target is required.");
            }
            ArrayOf<Protection> protections = await ClassifyAsync(session.Session, node, ct).ConfigureAwait(false);
            foreach (Protection required in protections)
            {
                Require(session, required.Capability);
                if (required.RequiresTypedMutation || (genericCall && required.RequiresTypedCall))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "This protected model requires a typed, secret-safe MCP tool instead of a generic mutation.");
                }
            }
        }

        /// <summary>
        /// Resolves only real ownership links, with explicit limits and no fallback after failed metadata reads.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<ArrayOf<Protection>> ClassifyAsync(
            ISession session, NodeId node, CancellationToken ct)
        {
            var protections = new HashSet<Protection>();
            var pending = new Queue<(NodeId Node, int Depth)>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue((node, 0));
            int references = 0;
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                (NodeId current, int depth) = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }
                if (visited.Count > 128 || depth > 16)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The mutation target's protected ownership could not be classified within host limits.");
                }
                Protection? direct = NamespaceProtection(session, current);
                if (direct.HasValue)
                {
                    protections.Add(direct.Value);
                    continue;
                }
                bool classified = false;
                foreach ((NodeId type, McpCapability capability, bool secret) in s_protectedTypes)
                {
                    if (current == type)
                    {
                        protections.Add(new Protection(capability, secret));
                        classified = true;
                        break;
                    }
                }
                if (classified)
                {
                    continue;
                }
                await foreach (ReferenceDescription reference in session.EnumerateReferencesAsync(
                    current, ReferenceTypeIds.HasTypeDefinition, ct: ct).ConfigureAwait(false))
                {
                    CountReference(ref references);
                    NodeId actual = McpModelAccess.ModelNode(session, reference.NodeId);
                    direct = NamespaceProtection(session, actual);
                    if (direct.HasValue)
                    {
                        protections.Add(direct.Value);
                        classified = true;
                        break;
                    }
                    foreach ((NodeId type, McpCapability capability, bool secret) in s_protectedTypes)
                    {
                        if (actual == type ||
                            await session.NodeCache.IsTypeOfAsync(actual, type, ct).ConfigureAwait(false))
                        {
                            protections.Add(new Protection(capability, secret));
                            classified = true;
                            break;
                        }
                    }
                    if (classified)
                    {
                        break;
                    }
                    await foreach (ReferenceDescription supertype in session.EnumerateReferencesAsync(
                        actual, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, ct: ct).ConfigureAwait(false))
                    {
                        CountReference(ref references);
                        pending.Enqueue((McpModelAccess.ModelNode(session, supertype.NodeId), depth + 1));
                    }
                }
                if (classified)
                {
                    continue;
                }
                await foreach (ReferenceDescription supertype in session.EnumerateReferencesAsync(
                    current, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, ct: ct).ConfigureAwait(false))
                {
                    CountReference(ref references);
                    pending.Enqueue((McpModelAccess.ModelNode(session, supertype.NodeId), depth + 1));
                }
                await foreach (ReferenceDescription parent in session.EnumerateReferencesAsync(
                    current, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse, ct: ct)
                    .ConfigureAwait(false))
                {
                    CountReference(ref references);
                    if (parent.ReferenceTypeId == ReferenceTypeIds.HasComponent ||
                        parent.ReferenceTypeId == ReferenceTypeIds.HasProperty ||
                        parent.ReferenceTypeId == ReferenceTypeIds.Organizes ||
                        await session.NodeCache.IsTypeOfAsync(
                            parent.ReferenceTypeId, ReferenceTypeIds.Aggregates, ct).ConfigureAwait(false))
                    {
                        pending.Enqueue((McpModelAccess.ModelNode(session, parent.NodeId), depth + 1));
                    }
                }
            }
            return [.. protections];
        }

        /// <summary>
        /// Counts every examined edge, not just the references accepted as ownership.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void CountReference(ref int references)
        {
            if (++references > 512)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The mutation target has too many classification references.");
            }
        }

        /// <summary>
        /// Protects model-defined GDS and onboarding types without depending on optional model assemblies.
        /// </summary>
        private static Protection? NamespaceProtection(ISession session, NodeId node)
        {
            string? uri = session.NamespaceUris.GetString(node.NamespaceIndex);
            return uri switch
            {
                "http://opcfoundation.org/UA/GDS/" =>
                    new Protection(McpCapability.DirectoryAdministration, true, true),
                "http://opcfoundation.org/UA/Onboarding/" =>
                    new Protection(McpCapability.Onboarding, true, true),
                _ => null
            };
        }

        /// <summary>
        /// Requires both host authority for the real registered session and an encrypted UA channel.
        /// </summary>
        private void Require(OpcUaSessionManager.SessionInfo session, McpCapability capability)
        {
            _ = m_policy.RequireSession(capability, session.Name);
            McpModelAccess.RequireSecureMutation(session.Session);
        }

        private readonly IMcpOperationPolicy m_policy;

        private static readonly (NodeId Type, McpCapability Capability, bool Secret)[] s_protectedTypes =
        [
            (ObjectTypeIds.RoleSetType, McpCapability.IdentityAdministration, false),
            (ObjectTypeIds.RoleType, McpCapability.IdentityAdministration, false),
            (ObjectTypeIds.NamespaceMetadataType, McpCapability.IdentityAdministration, false),
            (ObjectTypeIds.UserManagementType, McpCapability.IdentityAdministration, false),
            (ObjectTypeIds.SecurityGroupType, McpCapability.PubSubSecurity, true),
            (ObjectTypeIds.SecurityGroupFolderType, McpCapability.PubSubSecurity, true),
            (ObjectTypeIds.PubSubKeyServiceType, McpCapability.PubSubSecurity, true),
            (ObjectTypeIds.TrustListType, McpCapability.CertificateAdministration, false),
            (ObjectTypeIds.CertificateGroupType, McpCapability.CertificateAdministration, false),
            (ObjectTypeIds.CertificateGroupFolderType, McpCapability.CertificateAdministration, false),
            (ObjectTypeIds.ServerConfigurationType, McpCapability.CertificateAdministration, false),
            (ObjectTypeIds.PublishSubscribeType, McpCapability.PubSubConfiguration, true),
            (ObjectTypeIds.PubSubConnectionType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.WriterGroupType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.ReaderGroupType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.DataSetWriterType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.DataSetReaderType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.PubSubConfigurationType, McpCapability.PubSubConfiguration, false),
            (ObjectTypeIds.DataSetFolderType, McpCapability.PubSubConfiguration, false)
        ];

        /// <summary>
        /// Describes the minimum authority and whether raw response delivery would be unsafe.
        /// </summary>
        private readonly record struct Protection(
            McpCapability Capability, bool RequiresTypedCall, bool RequiresTypedMutation = false);
    }
}
