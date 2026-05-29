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

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// Compile-time lookup of well-known role child node IDs (methods and
    /// properties on each WellKnownRole) used as a fallback when a Browse
    /// from a non-admin session returns no children — the standard nodeset
    /// declares <c>RolePermission Permissions="61455">SecurityAdmin</c> on
    /// these methods/variables, so anonymous and authenticated-user sessions
    /// cannot Browse them. These tests still need to verify the methods exist
    /// on the server, which they do — Browse just hides them.
    /// </summary>
    public static class WellKnownRoleNodeIds
    {
        private sealed record Key(uint Parent, string ChildName);

        private static readonly Dictionary<Key, uint> s_map = BuildMap();

        public static NodeId TryGetChild(NodeId parentId, string childName)
        {
            if (parentId == null ||
                parentId.IsNull ||
                parentId.IdType != IdType.Numeric ||
                parentId.NamespaceIndex != 0 ||
                string.IsNullOrEmpty(childName))
            {
                return NodeId.Null;
            }

            if (!parentId.TryGetValue(out uint parentNumeric))
            {
                return NodeId.Null;
            }
            if (s_map.TryGetValue(new Key(parentNumeric, childName), out uint childId))
            {
                return new NodeId(childId);
            }
            return NodeId.Null;
        }

        private static Dictionary<Key, uint> BuildMap()
        {
            var m = new Dictionary<Key, uint>();

            // RoleSet itself.
            m[new(Objects.Server_ServerCapabilities_RoleSet, "AddRole")]
                = Methods.Server_ServerCapabilities_RoleSet_AddRole;
            m[new(Objects.Server_ServerCapabilities_RoleSet, "RemoveRole")]
                = Methods.Server_ServerCapabilities_RoleSet_RemoveRole;

            AddRoleChildren(m, Objects.WellKnownRole_Anonymous, false,
                Variables.WellKnownRole_Anonymous_Identities, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0);
            AddRoleChildren(m, Objects.WellKnownRole_AuthenticatedUser, false,
                Variables.WellKnownRole_AuthenticatedUser_Identities, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0);

            AddRoleChildren(m, Objects.WellKnownRole_Observer, true,
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
                Methods.WellKnownRole_Observer_RemoveEndpoint);

            AddRoleChildren(m, Objects.WellKnownRole_Operator, true,
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
                Methods.WellKnownRole_Operator_RemoveEndpoint);

            AddRoleChildren(m, Objects.WellKnownRole_Engineer, true,
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
                Methods.WellKnownRole_Engineer_RemoveEndpoint);

            AddRoleChildren(m, Objects.WellKnownRole_Supervisor, true,
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
                Methods.WellKnownRole_Supervisor_RemoveEndpoint);

            AddRoleChildren(m, Objects.WellKnownRole_ConfigureAdmin, true,
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
                Methods.WellKnownRole_ConfigureAdmin_RemoveEndpoint);

            AddRoleChildren(m, Objects.WellKnownRole_SecurityAdmin, true,
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
                Methods.WellKnownRole_SecurityAdmin_RemoveEndpoint);

            return m;
        }

        private static void AddRoleChildren(
            Dictionary<Key, uint> m,
            uint role,
            bool hasMethods,
            uint identities, uint applications, uint applicationsExclude,
            uint endpoints, uint endpointsExclude,
            uint addIdentity, uint removeIdentity,
            uint addApplication, uint removeApplication,
            uint addEndpoint, uint removeEndpoint)
        {
            if (identities != 0)
            {
                m[new(role, "Identities")] = identities;
            }
            if (applications != 0)
            {
                m[new(role, "Applications")] = applications;
            }
            if (applicationsExclude != 0)
            {
                m[new(role, "ApplicationsExclude")] = applicationsExclude;
            }
            if (endpoints != 0)
            {
                m[new(role, "Endpoints")] = endpoints;
            }
            if (endpointsExclude != 0)
            {
                m[new(role, "EndpointsExclude")] = endpointsExclude;
            }

            if (!hasMethods)
            {
                return;
            }

            m[new(role, "AddIdentity")] = addIdentity;
            m[new(role, "RemoveIdentity")] = removeIdentity;
            m[new(role, "AddApplication")] = addApplication;
            m[new(role, "RemoveApplication")] = removeApplication;
            m[new(role, "AddEndpoint")] = addEndpoint;
            m[new(role, "RemoveEndpoint")] = removeEndpoint;
        }
    }
}
