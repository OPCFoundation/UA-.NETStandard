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

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Strongly-typed client over the OPC UA Part 18 §4 role-management
    /// methods on the server's <c>RoleSet</c> and <c>RoleType</c> objects.
    /// </summary>
    /// <remarks>
    /// All mutator operations require the calling session to hold the
    /// <c>SecurityAdmin</c> role and to use a <c>SignAndEncrypt</c> secure
    /// channel; otherwise the server returns
    /// <c>Bad_UserAccessDenied</c> / <c>Bad_SecurityModeInsufficient</c>
    /// which surfaces as a <see cref="ServiceResultException"/>.
    /// </remarks>
    public interface IRoleManagementClient
    {
        /// <summary>
        /// Browses the server's <c>RoleSet</c> and returns a snapshot of
        /// every role exposed by it.
        /// </summary>
        ValueTask<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a single role by NodeId.
        /// </summary>
        ValueTask<RoleInfo> ReadRoleAsync(NodeId roleId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleSet.AddRole</c> per Part 18 §4.2.2 and returns the
        /// new role's NodeId.
        /// </summary>
        ValueTask<NodeId> AddRoleAsync(
            string roleName,
            string? namespaceUri = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleSet.RemoveRole</c> per Part 18 §4.2.3.
        /// </summary>
        ValueTask RemoveRoleAsync(NodeId roleId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.AddIdentity</c> per Part 18 §4.4.5.
        /// </summary>
        ValueTask AddIdentityAsync(
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.RemoveIdentity</c> per Part 18 §4.4.6.
        /// </summary>
        ValueTask RemoveIdentityAsync(
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.AddApplication</c> per Part 18 §4.4.7.
        /// </summary>
        ValueTask AddApplicationAsync(
            NodeId roleId,
            string applicationUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.RemoveApplication</c> per Part 18 §4.4.8.
        /// </summary>
        ValueTask RemoveApplicationAsync(
            NodeId roleId,
            string applicationUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.AddEndpoint</c> per Part 18 §4.4.9.
        /// </summary>
        ValueTask AddEndpointAsync(
            NodeId roleId,
            EndpointType endpoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RoleType.RemoveEndpoint</c> per Part 18 §4.4.10.
        /// </summary>
        ValueTask RemoveEndpointAsync(
            NodeId roleId,
            EndpointType endpoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the <c>ApplicationsExclude</c> property on a role.
        /// </summary>
        ValueTask SetApplicationsExcludeAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the <c>EndpointsExclude</c> property on a role.
        /// </summary>
        ValueTask SetEndpointsExcludeAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the <c>CustomConfiguration</c> property on a role.
        /// </summary>
        ValueTask SetCustomConfigurationAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default);
    }
}
