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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Immutable snapshot of a role's configuration per OPC UA Part 18 §4.4.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="IRoleManager"/> methods that expose role state to
    /// the address-space binding (<see cref="RoleStateBinding"/>) and to test
    /// code. The lists are caller-owned copies; mutating them does not affect
    /// the manager.
    /// </remarks>
    public sealed class RoleEntry
    {
        internal RoleEntry(
            NodeId roleId,
            string? browseName,
            ushort namespaceIndex,
            bool isReserved,
            bool isWellKnown,
            IReadOnlyList<IdentityMappingRuleType> identities,
            IReadOnlyList<string> applications,
            bool applicationsExclude,
            IReadOnlyList<EndpointType> endpoints,
            bool endpointsExclude,
            bool customConfiguration)
        {
            RoleId = roleId;
            BrowseName = browseName;
            NamespaceIndex = namespaceIndex;
            IsReserved = isReserved;
            IsWellKnown = isWellKnown;
            Identities = identities;
            Applications = applications;
            ApplicationsExclude = applicationsExclude;
            Endpoints = endpoints;
            EndpointsExclude = endpointsExclude;
            CustomConfiguration = customConfiguration;
        }

        /// <summary>
        /// The role's NodeId (e.g. <c>Opc.Ua.ObjectIds.WellKnownRole_Observer</c>).
        /// </summary>
        public NodeId RoleId { get; }

        /// <summary>
        /// Browse name of the role; <c>null</c> for unnamed dynamic roles where
        /// the manager only tracks the NodeId.
        /// </summary>
        public string? BrowseName { get; }

        /// <summary>
        /// Namespace index for the role's NodeId.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// True if the role is one of the three reserved well-known roles
        /// (<c>Anonymous</c>, <c>AuthenticatedUser</c>, <c>TrustedApplication</c>)
        /// that the spec forbids from being modified or removed (Part 18 §4.3).
        /// </summary>
        public bool IsReserved { get; }

        /// <summary>
        /// True if the role is one of the nine well-known roles defined in
        /// OPC UA Part 3 §4.9.2 (Anonymous, AuthenticatedUser, TrustedApplication,
        /// Observer, Operator, Engineer, Supervisor, ConfigureAdmin, SecurityAdmin).
        /// </summary>
        public bool IsWellKnown { get; }

        /// <summary>
        /// Snapshot of identity-mapping rules per Part 18 §4.4.3. Treat as
        /// read-only — modifications are not reflected back into the manager.
        /// </summary>
        public IReadOnlyList<IdentityMappingRuleType> Identities { get; }

        /// <summary>
        /// Snapshot of ApplicationUris per Part 18 §4.4.1.
        /// </summary>
        public IReadOnlyList<string> Applications { get; }

        /// <summary>
        /// True if <see cref="Applications"/> is an exclude list.
        /// </summary>
        public bool ApplicationsExclude { get; }

        /// <summary>
        /// Snapshot of endpoints per Part 18 §4.4.1.
        /// </summary>
        public IReadOnlyList<EndpointType> Endpoints { get; }

        /// <summary>
        /// True if <see cref="Endpoints"/> is an exclude list.
        /// </summary>
        public bool EndpointsExclude { get; }

        /// <summary>
        /// True if the role uses vendor-specific configuration (Part 18 §4.4.1).
        /// When <c>true</c>, the role may be granted even when
        /// <see cref="Identities"/> is empty.
        /// </summary>
        public bool CustomConfiguration { get; }
    }
}
