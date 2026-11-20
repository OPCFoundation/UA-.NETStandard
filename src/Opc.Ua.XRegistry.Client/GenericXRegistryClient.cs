// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Client;

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// The plain xRegistry client. It binds to any registry companion namespace and drives it
    /// purely through the base-model ObjectType proxies, so it also works against a domain registry
    /// whose types are subtypes of the xRegistry base types.
    /// </summary>
    /// <remarks>
    /// Domain registries do not wrap this class — they derive from <see cref="XRegistryClient"/>
    /// and add their own naming and defaults.
    /// </remarks>
    public sealed class GenericXRegistryClient : XRegistryClient
    {
        /// <summary>
        /// Initializes a generic registry client.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        public GenericXRegistryClient(
            ISession session,
            string registryNamespaceUri,
            ITelemetryContext telemetry)
            : base(session, registryNamespaceUri, telemetry)
        {
        }

        /// <summary>
        /// Initializes a generic registry client rooted at an explicit registry Object.
        /// <para>
        /// A domain registry declares its own root rather than reusing the provisional well-known
        /// identifier, and a caller typically discovers it by Browse. This overload drives such a
        /// registry through the base-model proxies without deriving a domain client.
        /// </para>
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="registryNamespaceUri">The registry companion namespace URI.</param>
        /// <param name="registryNodeId">
        /// The registry root Object. Pass a null NodeId to use the well-known root.
        /// </param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        public GenericXRegistryClient(
            ISession session,
            string registryNamespaceUri,
            NodeId registryNodeId,
            ITelemetryContext telemetry)
            : base(session, registryNamespaceUri, registryNodeId, telemetry)
        {
        }

        /// <summary>
        /// Initializes a generic client bound to the abstract xRegistry base namespace.
        /// </summary>
        /// <param name="session">The connected session whose server hosts the registry.</param>
        /// <param name="telemetry">Telemetry context used by the generated proxies.</param>
        public GenericXRegistryClient(ISession session, ITelemetryContext telemetry)
            : base(session, XRegistryWellKnown.XRegistryNamespaceUri, telemetry)
        {
        }
    }
}
