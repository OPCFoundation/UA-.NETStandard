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

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Immutable snapshot of a role as exposed by the server's
    /// <c>RoleSet</c> per OPC UA Part 18 §4.4. Returned from
    /// <see cref="IRoleManagementClient.ListRolesAsync"/> and
    /// <see cref="IRoleManagementClient.ReadRoleAsync"/>.
    /// </summary>
    public sealed class RoleInfo
    {
        /// <summary>
        /// Initializes the snapshot.
        /// </summary>
        public RoleInfo(
            NodeId roleId,
            QualifiedName browseName,
            IReadOnlyList<IdentityMappingRuleType> identities,
            IReadOnlyList<string> applications,
            bool applicationsExclude,
            IReadOnlyList<EndpointType> endpoints,
            bool endpointsExclude,
            bool customConfiguration)
        {
            RoleId = roleId;
            BrowseName = browseName;
            Identities = identities;
            Applications = applications;
            ApplicationsExclude = applicationsExclude;
            Endpoints = endpoints;
            EndpointsExclude = endpointsExclude;
            CustomConfiguration = customConfiguration;
        }

        /// <summary>The role's NodeId.</summary>
        public NodeId RoleId { get; }

        /// <summary>The role's browse name (e.g. <c>"Observer"</c>).</summary>
        public QualifiedName BrowseName { get; }

        /// <summary>Identity-mapping rules currently configured on the role.</summary>
        public IReadOnlyList<IdentityMappingRuleType> Identities { get; }

        /// <summary>ApplicationUris currently configured on the role.</summary>
        public IReadOnlyList<string> Applications { get; }

        /// <summary>True if <see cref="Applications"/> is an exclude list.</summary>
        public bool ApplicationsExclude { get; }

        /// <summary>Endpoints currently configured on the role.</summary>
        public IReadOnlyList<EndpointType> Endpoints { get; }

        /// <summary>True if <see cref="Endpoints"/> is an exclude list.</summary>
        public bool EndpointsExclude { get; }

        /// <summary>True if the role uses vendor-specific configuration.</summary>
        public bool CustomConfiguration { get; }
    }
}
