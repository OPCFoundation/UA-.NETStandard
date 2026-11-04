/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.EndpointRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Federation.Server
{
    /// <summary>A server-configured local Group selection and its fresh observation boundary.</summary>
    public sealed class GroupFederationSource
    {
        /// <summary>
        /// Configures one source. The observer must perform fresh provider/session checks on
        /// each invocation, for example by calling a provider's PreloadGroupAsync. It must
        /// not return a previously cached snapshot. The binding does not own the provider.
        /// </summary>
        public GroupFederationSource(
            string localXid,
            string remoteXid,
            FederationTrustBinding trust,
            Func<CancellationToken, ValueTask<FederationGroupSnapshot>> observe)
        {
            LocalXid = localXid ?? throw new ArgumentNullException(nameof(localXid));
            RemoteXid = remoteXid ?? throw new ArgumentNullException(nameof(remoteXid));
            Trust = trust ?? throw new ArgumentNullException(nameof(trust));
            Observe = observe ?? throw new ArgumentNullException(nameof(observe));
        }

        /// <summary>Gets the independently allocated local Group Xid.</summary>
        public string LocalXid { get; }

        /// <summary>Gets the exact collection-qualified remote Group Xid.</summary>
        public string RemoteXid { get; }

        /// <summary>Gets the immutable server-owned origin and route configuration.</summary>
        public FederationTrustBinding Trust { get; }

        /// <summary>Gets the fresh authenticated observation boundary.</summary>
        public Func<CancellationToken, ValueTask<FederationGroupSnapshot>> Observe { get; }
    }

    /// <summary>Explicit opt-in configuration; does not advertise a conformance facet.</summary>
    public sealed class EndpointRegistryGroupFederationOptions
    {
        /// <summary>Gets or sets the existing catalog to bind.</summary>
        public EndpointRegistryRoot Root { get; set; }

        /// <summary>
        /// Gets or sets the read-only identifier prefix reserved for the host lifetime.
        /// Use the same prefix on restart. No ordinary mutation can change these Groups.
        /// </summary>
        public string IdentifierPrefix { get; set; } = "federated-";

        /// <summary>Gets or sets the complete configured source selections.</summary>
        public ArrayOf<GroupFederationSource> Sources { get; set; }
    }
}
