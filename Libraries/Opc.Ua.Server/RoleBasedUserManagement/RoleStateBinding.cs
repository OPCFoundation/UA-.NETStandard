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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds the well-known role nodes in the address space to a
    /// <see cref="RoleManager"/>.
    /// </summary>
    public static class RoleStateBinding
    {
        private sealed record RoleNodeIds(
            uint RoleId,
            uint Identities,
            uint Applications,
            uint ApplicationsExclude,
            uint Endpoints,
            uint EndpointsExclude,
            uint AddIdentity,
            uint RemoveIdentity,
            uint AddApplication,
            uint RemoveApplication,
            uint AddEndpoint,
            uint RemoveEndpoint);

        private static readonly RoleNodeIds[] s_roles =
        [
            new(
                Objects.WellKnownRole_Observer,
                Variables.WellKnownRole_Observer_Identities,
                Variables.WellKnownRole_Observer_Applications,
                Variables.WellKnownRole_Observer_ApplicationsExclude,
                Variables.WellKnownRole_Observer_Endpoints,
                Variables.WellKnownRole_Observer_EndpointsExclude,
                Methods.WellKnownRole_Observer_AddIdentity,
                Methods.WellKnownRole_Observer_RemoveIdentity,
                Methods.WellKnownRole_Observer_AddApplication,
                Methods.WellKnownRole_Observer_RemoveApplication,
                Methods.WellKnownRole_Observer_AddEndpoint,
                Methods.WellKnownRole_Observer_RemoveEndpoint),
            new(
                Objects.WellKnownRole_Operator,
                Variables.WellKnownRole_Operator_Identities,
                Variables.WellKnownRole_Operator_Applications,
                Variables.WellKnownRole_Operator_ApplicationsExclude,
                Variables.WellKnownRole_Operator_Endpoints,
                Variables.WellKnownRole_Operator_EndpointsExclude,
                Methods.WellKnownRole_Operator_AddIdentity,
                Methods.WellKnownRole_Operator_RemoveIdentity,
                Methods.WellKnownRole_Operator_AddApplication,
                Methods.WellKnownRole_Operator_RemoveApplication,
                Methods.WellKnownRole_Operator_AddEndpoint,
                Methods.WellKnownRole_Operator_RemoveEndpoint),
            new(
                Objects.WellKnownRole_Engineer,
                Variables.WellKnownRole_Engineer_Identities,
                Variables.WellKnownRole_Engineer_Applications,
                Variables.WellKnownRole_Engineer_ApplicationsExclude,
                Variables.WellKnownRole_Engineer_Endpoints,
                Variables.WellKnownRole_Engineer_EndpointsExclude,
                Methods.WellKnownRole_Engineer_AddIdentity,
                Methods.WellKnownRole_Engineer_RemoveIdentity,
                Methods.WellKnownRole_Engineer_AddApplication,
                Methods.WellKnownRole_Engineer_RemoveApplication,
                Methods.WellKnownRole_Engineer_AddEndpoint,
                Methods.WellKnownRole_Engineer_RemoveEndpoint),
            new(
                Objects.WellKnownRole_Supervisor,
                Variables.WellKnownRole_Supervisor_Identities,
                Variables.WellKnownRole_Supervisor_Applications,
                Variables.WellKnownRole_Supervisor_ApplicationsExclude,
                Variables.WellKnownRole_Supervisor_Endpoints,
                Variables.WellKnownRole_Supervisor_EndpointsExclude,
                Methods.WellKnownRole_Supervisor_AddIdentity,
                Methods.WellKnownRole_Supervisor_RemoveIdentity,
                Methods.WellKnownRole_Supervisor_AddApplication,
                Methods.WellKnownRole_Supervisor_RemoveApplication,
                Methods.WellKnownRole_Supervisor_AddEndpoint,
                Methods.WellKnownRole_Supervisor_RemoveEndpoint),
            new(
                Objects.WellKnownRole_ConfigureAdmin,
                Variables.WellKnownRole_ConfigureAdmin_Identities,
                Variables.WellKnownRole_ConfigureAdmin_Applications,
                Variables.WellKnownRole_ConfigureAdmin_ApplicationsExclude,
                Variables.WellKnownRole_ConfigureAdmin_Endpoints,
                Variables.WellKnownRole_ConfigureAdmin_EndpointsExclude,
                Methods.WellKnownRole_ConfigureAdmin_AddIdentity,
                Methods.WellKnownRole_ConfigureAdmin_RemoveIdentity,
                Methods.WellKnownRole_ConfigureAdmin_AddApplication,
                Methods.WellKnownRole_ConfigureAdmin_RemoveApplication,
                Methods.WellKnownRole_ConfigureAdmin_AddEndpoint,
                Methods.WellKnownRole_ConfigureAdmin_RemoveEndpoint),
            new(
                Objects.WellKnownRole_SecurityAdmin,
                Variables.WellKnownRole_SecurityAdmin_Identities,
                Variables.WellKnownRole_SecurityAdmin_Applications,
                Variables.WellKnownRole_SecurityAdmin_ApplicationsExclude,
                Variables.WellKnownRole_SecurityAdmin_Endpoints,
                Variables.WellKnownRole_SecurityAdmin_EndpointsExclude,
                Methods.WellKnownRole_SecurityAdmin_AddIdentity,
                Methods.WellKnownRole_SecurityAdmin_RemoveIdentity,
                Methods.WellKnownRole_SecurityAdmin_AddApplication,
                Methods.WellKnownRole_SecurityAdmin_RemoveApplication,
                Methods.WellKnownRole_SecurityAdmin_AddEndpoint,
                Methods.WellKnownRole_SecurityAdmin_RemoveEndpoint),
        ];

        /// <summary>
        /// Walk each well-known role on the given node manager and hook each
        /// role's 6 method nodes + 5 variable nodes to the supplied manager.
        /// </summary>
        public static void Bind(AsyncCustomNodeManager nodeManager, IRoleManager manager)
        {
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            foreach (RoleNodeIds ids in s_roles)
            {
                var roleId = new NodeId(ids.RoleId);
                manager.EnsureRole(roleId);

                BindMethodHandler(nodeManager, ids.AddIdentity, (input, output) =>
                    TryGetRule(input, out IdentityMappingRuleType? rule)
                        ? manager.AddIdentity(roleId, rule)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindMethodHandler(nodeManager, ids.RemoveIdentity, (input, output) =>
                    TryGetRule(input, out IdentityMappingRuleType? rule)
                        ? manager.RemoveIdentity(roleId, rule)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindMethodHandler(nodeManager, ids.AddApplication, (input, output) =>
                    TryGetString(input, out string? uri)
                        ? manager.AddApplication(roleId, uri)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindMethodHandler(nodeManager, ids.RemoveApplication, (input, output) =>
                    TryGetString(input, out string? uri)
                        ? manager.RemoveApplication(roleId, uri)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindMethodHandler(nodeManager, ids.AddEndpoint, (input, output) =>
                    TryGetEndpoint(input, out EndpointType? ep)
                        ? manager.AddEndpoint(roleId, ep)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindMethodHandler(nodeManager, ids.RemoveEndpoint, (input, output) =>
                    TryGetEndpoint(input, out EndpointType? ep)
                        ? manager.RemoveEndpoint(roleId, ep)
                        : new ServiceResult(StatusCodes.BadInvalidArgument));

                BindIdentitiesRead(nodeManager, ids.Identities, manager, roleId);
                BindApplicationsRead(nodeManager, ids.Applications, manager, roleId);
                BindApplicationsExcludeRead(nodeManager, ids.ApplicationsExclude, manager, roleId);
                BindEndpointsRead(nodeManager, ids.Endpoints, manager, roleId);
                BindEndpointsExcludeRead(nodeManager, ids.EndpointsExclude, manager, roleId);
            }

            BindRoleSetMethods(nodeManager, manager);
        }

        /// <summary>
        /// Hook AddRole/RemoveRole on the RoleSet object so dynamic role
        /// creation/deletion routes through the supplied manager. The
        /// <see cref="RoleManager.DynamicRoleNamespaceIndex"/> is set to the
        /// node manager's first owned namespace so synthetic NodeIds don't
        /// collide with the standard namespace 0 IDs.
        /// </summary>
        private static void BindRoleSetMethods(AsyncCustomNodeManager nm, IRoleManager manager)
        {
            ushort nsIndex = 0;
            string? firstOwned = nm.NamespaceUris?.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstOwned))
            {
                nsIndex = (ushort)nm.SystemContext.NamespaceUris.GetIndex(firstOwned!);
                if (nsIndex == ushort.MaxValue)
                {
                    nsIndex = 0;
                }
            }
            manager.DynamicRoleNamespaceIndex = nsIndex;

            BindMethodHandler(nm, Methods.Server_ServerCapabilities_RoleSet_AddRole,
                (input, output) =>
                {
                    if (input.Count < 2
                        || !input[0].TryGetValue(out string roleName)
                        || !input[1].TryGetValue(out string namespaceUri))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument);
                    }
                    ServiceResult result = manager.AddRole(roleName, namespaceUri, out NodeId newRoleId);
                    if (ServiceResult.IsGood(result))
                    {
                        output.Add(new Variant(newRoleId));
                    }
                    return result;
                });

            BindMethodHandler(nm, Methods.Server_ServerCapabilities_RoleSet_RemoveRole,
                (input, output) =>
                {
                    if (input.Count < 1 || !input[0].TryGetValue(out NodeId roleId))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument);
                    }
                    return manager.RemoveRole(roleId);
                });
        }

        private static void BindMethodHandler(
            AsyncCustomNodeManager nm,
            uint nodeId,
            Func<ArrayOf<Variant>, List<Variant>, ServiceResult> handler)
        {
            MethodState method = nm.FindPredefinedNode<MethodState>(new NodeId(nodeId));
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (ISystemContext ctx, MethodState m, NodeId obj,
                ArrayOf<Variant> input, List<Variant> output) => handler(input, output);
        }

        private static void BindIdentitiesRead(
            AsyncCustomNodeManager nm, uint nodeId, IRoleManager manager, NodeId roleId)
        {
            BaseDataVariableState v = nm.FindPredefinedNode<BaseDataVariableState>(new NodeId(nodeId));
            if (v == null)
            {
                return;
            }
            v.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
            {
                ExtensionObject[] arr = manager.SnapshotIdentities(roleId)
                    .Select(r => new ExtensionObject(r))
                    .ToArray();
                value = Variant.From(arr);
                return ServiceResult.Good;
            };
        }

        private static void BindApplicationsRead(
            AsyncCustomNodeManager nm, uint nodeId, IRoleManager manager, NodeId roleId)
        {
            BaseDataVariableState v = nm.FindPredefinedNode<BaseDataVariableState>(new NodeId(nodeId));
            if (v == null)
            {
                return;
            }
            v.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
            {
                string[] arr = manager.SnapshotApplications(roleId, out _).ToArray();
                value = Variant.From(arr);
                return ServiceResult.Good;
            };
        }

        private static void BindApplicationsExcludeRead(
            AsyncCustomNodeManager nm, uint nodeId, IRoleManager manager, NodeId roleId)
        {
            BaseDataVariableState v = nm.FindPredefinedNode<BaseDataVariableState>(new NodeId(nodeId));
            if (v == null)
            {
                return;
            }
            v.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
            {
                _ = manager.SnapshotApplications(roleId, out bool exclude);
                value = Variant.From(exclude);
                return ServiceResult.Good;
            };
        }

        private static void BindEndpointsRead(
            AsyncCustomNodeManager nm, uint nodeId, IRoleManager manager, NodeId roleId)
        {
            BaseDataVariableState v = nm.FindPredefinedNode<BaseDataVariableState>(new NodeId(nodeId));
            if (v == null)
            {
                return;
            }
            v.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
            {
                ExtensionObject[] arr = manager.SnapshotEndpoints(roleId, out _)
                    .Select(e => new ExtensionObject(e))
                    .ToArray();
                value = Variant.From(arr);
                return ServiceResult.Good;
            };
        }

        private static void BindEndpointsExcludeRead(
            AsyncCustomNodeManager nm, uint nodeId, IRoleManager manager, NodeId roleId)
        {
            BaseDataVariableState v = nm.FindPredefinedNode<BaseDataVariableState>(new NodeId(nodeId));
            if (v == null)
            {
                return;
            }
            v.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
            {
                _ = manager.SnapshotEndpoints(roleId, out bool exclude);
                value = Variant.From(exclude);
                return ServiceResult.Good;
            };
        }

        private static bool TryGetRule(ArrayOf<Variant> args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IdentityMappingRuleType? rule)
        {
            rule = null;
            if (args.Count == 0)
            {
                return false;
            }
            if (args[0].TryGetValue(out ExtensionObject ext)
                && ext.TryGetValue(out IdentityMappingRuleType? r))
            {
                rule = r;
                return r != null;
            }
            return false;
        }

        private static bool TryGetString(ArrayOf<Variant> args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = null;
            if (args.Count == 0)
            {
                return false;
            }
            if (args[0].TryGetValue(out string? s))
            {
                value = s;
                return true;
            }
            return false;
        }

        private static bool TryGetEndpoint(ArrayOf<Variant> args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EndpointType? endpoint)
        {
            endpoint = null;
            if (args.Count == 0)
            {
                return false;
            }
            if (args[0].TryGetValue(out ExtensionObject ext)
                && ext.TryGetValue(out EndpointType? e))
            {
                endpoint = e;
                return e != null;
            }
            return false;
        }
    }
}
