/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that surfaces the server's active
    /// <see cref="EndpointDescription"/> set. Implemented by
    /// <see cref="ServerInternalData"/> and populated by
    /// <see cref="StandardServer"/> once the endpoints are created. Consumers
    /// resolve the registry via
    /// <c>(server as IServerEndpointRegistryProvider)?.ServerEndpoints</c> so
    /// external/mocked <see cref="IServerInternal"/> implementations remain
    /// binary-compatible.
    /// </summary>
    /// <remarks>
    /// Primary consumer: <see cref="ConfigurationNodeManager"/> needs the
    /// endpoints to enforce OPC UA Part 12 §7.10.7 — a <c>DeleteCertificate</c>
    /// that would remove a certificate still referenced by an
    /// <c>EndpointDescription</c> must be rejected when <c>ApplyChanges</c>
    /// is called.
    /// </remarks>
    public interface IServerEndpointRegistryProvider
    {
        /// <summary>
        /// A snapshot of the endpoints currently advertised by the server.
        /// Each entry's <see cref="EndpointDescription.SecurityPolicyUri"/>
        /// identifies which certificate/type that endpoint presents; the
        /// certificate itself is resolved live from the active certificate
        /// registry at check time (so a rotated certificate is honored even
        /// though the entry's <see cref="EndpointDescription.ServerCertificate"/>
        /// blob was captured at startup). Returns an empty array before the
        /// endpoints are created.
        /// </summary>
        ArrayOf<EndpointDescription> ServerEndpoints { get; }
    }
}
