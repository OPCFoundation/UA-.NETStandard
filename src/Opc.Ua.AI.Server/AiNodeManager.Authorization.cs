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
using Opc.Ua.Server;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// Captures the creator without retaining credentials or a mutable identity.
        /// </summary>
        private InferenceOwner? CaptureOwner(ISystemContext context, bool sessionBound)
        {
            if (ReferenceEquals(context, SystemContext))
            {
                return new InferenceOwner(NodeId.Null, null, sessionBound);
            }

            return TryGetCaller(context, out NodeId sessionId, out string? continuityKey)
                ? new InferenceOwner(sessionId, continuityKey, sessionBound)
                : null;
        }

        /// <summary>
        /// Checks session-bound transfers and identity-bound durable jobs.
        /// Anonymous jobs remain session-bound because anonymity establishes no durable identity.
        /// </summary>
        private bool IsOwner(ISystemContext context, InferenceOwner owner)
        {
            if (ReferenceEquals(context, SystemContext))
            {
                return true;
            }

            if (!TryGetCaller(context, out NodeId sessionId, out string? continuityKey) ||
                !string.Equals(owner.ContinuityKey, continuityKey, StringComparison.Ordinal))
            {
                return false;
            }

            return !(owner.SessionBound || owner.ContinuityKey is null) || owner.SessionId == sessionId;
        }

        /// <summary>
        /// Resolves the same token-specific continuity key used by server session and subscription transfer.
        /// </summary>
        private static bool TryGetCaller(
            ISystemContext context,
            out NodeId sessionId,
            out string? continuityKey)
        {
            sessionId = NodeId.Null;
            continuityKey = null;
            if (context is not ISessionSystemContext
                {
                    SessionId: NodeId id,
                    UserIdentity: { } identity
                } ||
                id.IsNull)
            {
                return false;
            }

            IUserIdentityTokenHandler token;
            if (context is ServerSystemContext { OperationContext.Session: { } session })
            {
                identity = session.Identity;
                token = session.IdentityToken;
            }
            else
            {
                token = identity.TokenHandler;
            }

            sessionId = id;
            return ClientUserIdResolver.TryResolveContinuityKey(token, identity, out continuityKey) &&
                (token.TokenType == UserTokenType.Anonymous || continuityKey is not null);
        }

        /// <summary>
        /// Applies creator authorization to every node, including variable notifications and file methods.
        /// </summary>
        private void ProtectOwnedTree(NodeState node, InferenceOwner owner)
        {
            if (m_nodeOwners.TryGetValue(node, out _))
            {
                return;
            }

            m_nodeOwners.Add(node, owner);
            node.IsPartOfTypeHierarchy = false;
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>>? readRoles = node.OnReadRolePermissions;
            node.OnReadRolePermissions = (
                context, target, ref value) =>
            {
                if (!IsOwner(context, owner))
                {
                    // An empty permission array means unrestricted, not denied.
                    value = s_deniedPermissions;
                    return ServiceResult.Good;
                }

                return readRoles?.Invoke(context, target, ref value) ?? ServiceResult.Good;
            };

            if (node is BaseVariableState variable)
            {
                variable.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                variable.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                NodeAttributeEventHandler<byte>? readAccess = variable.OnReadUserAccessLevel;
                variable.OnReadUserAccessLevel = (context, target, ref value) =>
                {
                    if (!IsOwner(context, owner))
                    {
                        value = AccessLevels.None;
                        return ServiceResult.Good;
                    }

                    return readAccess?.Invoke(context, target, ref value) ?? ServiceResult.Good;
                };
            }

            if (node is MethodState method)
            {
                NodeAttributeEventHandler<bool>? readExecutable = method.OnReadUserExecutable;
                method.OnReadUserExecutable = (context, target, ref value) =>
                {
                    if (!IsOwner(context, owner))
                    {
                        value = false;
                        return ServiceResult.Good;
                    }

                    return readExecutable?.Invoke(context, target, ref value) ?? ServiceResult.Good;
                };
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                ProtectOwnedTree(child, owner);
            }
        }

        /// <summary>
        /// Reauthorizes the deployment and original entry method before a transfer dispatches inference.
        /// </summary>
        private async ValueTask<ServiceResult> AuthorizeDeploymentAsync(
            ISystemContext context,
            DeploymentState deployment,
            NodeId invocationMethodId,
            CancellationToken ct)
        {
            if (ReferenceEquals(context, SystemContext))
            {
                return ServiceResult.Good;
            }

            if (context is not ServerSystemContext { OperationContext: { } operation })
            {
                return StatusCodes.BadUserAccessDenied;
            }

            MethodState? invocationMethod = FindPredefinedNode<MethodState>(invocationMethodId);
            if (invocationMethod is null)
            {
                return StatusCodes.BadMethodInvalid;
            }

            foreach (NodeState target in new NodeState[] { deployment, invocationMethod })
            {
                object? handle = await GetManagerHandleAsync(target.NodeId, ct).ConfigureAwait(false);
                if (handle is null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                (bool handled, ServiceResult? result) = await TryValidatePermissionsAsync(
                    operation,
                    handle,
                    PermissionType.Call,
                    null,
                    permissionsOnly: true,
                    logger: m_logger,
                    validateAccessRestrictions: true,
                    cancellationToken: ct).ConfigureAwait(false);
                if (!handled)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }
                if (ServiceResult.IsBad(result))
                {
                    return result!;
                }
            }

            foreach (uint attributeId in new[] { Attributes.Executable, Attributes.UserExecutable })
            {
                (ServiceResult result, DataValue value) = await invocationMethod.ReadAttributeAsync(
                    context, attributeId, default, QualifiedName.Null, new DataValue(), ct).ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                if (!value.WrappedValue.TryGetValue(out bool executable) || !executable)
                {
                    return attributeId == Attributes.Executable
                        ? StatusCodes.BadNotExecutable
                        : StatusCodes.BadUserAccessDenied;
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Immutable authorization boundary for one inference object.
        /// </summary>
        private sealed class InferenceOwner
        {
            /// <summary>
            /// Records the creating session and its validated identity continuity key.
            /// </summary>
            public InferenceOwner(NodeId sessionId, string? continuityKey, bool sessionBound)
            {
                SessionId = sessionId;
                ContinuityKey = continuityKey;
                SessionBound = sessionBound;
            }

            /// <summary>
            /// The session that created the object.
            /// </summary>
            public NodeId SessionId { get; }

            /// <summary>
            /// The credential-free server continuity key, or null for an anonymous creator.
            /// </summary>
            public string? ContinuityKey { get; }

            /// <summary>
            /// Whether even an authenticated owner must use the original session.
            /// </summary>
            public bool SessionBound { get; }
        }

        /// <summary>Nonempty permissions that grant nothing, including to administrator roles.</summary>
        private static readonly ArrayOf<RolePermissionType> s_deniedPermissions =
        [
            new RolePermissionType { RoleId = Ua.ObjectIds.WellKnownRole_Anonymous, Permissions = 0 }
        ];

        /// <summary>Owner associations that do not retain reclaimed jobs or transfers.</summary>
        private readonly ConditionalWeakTable<NodeState, InferenceOwner> m_nodeOwners = [];
    }
}
