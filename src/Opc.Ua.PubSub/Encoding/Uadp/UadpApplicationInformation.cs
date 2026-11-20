// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Application-information payload of a discovery response per
    /// Part 14 §7.2.4.6.7.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.7">
    /// Part 14 §7.2.4.6.7</see>. Carried in the
    /// <see cref="UadpDiscoveryResponseMessage.ApplicationInformation"/>
    /// slot when
    /// <see cref="UadpDiscoveryResponseMessage.DiscoveryType"/> is
    /// <see cref="UadpDiscoveryType.ApplicationInformation"/>.
    /// </remarks>
    public sealed record UadpApplicationInformation
    {
        /// <summary>
        /// Display name of the publishing application.
        /// </summary>
        public LocalizedText ApplicationName { get; init; } = LocalizedText.Null;

        /// <summary>
        /// Application URI (must match the URI in the publisher's
        /// certificate, if signed).
        /// </summary>
        public string ApplicationUri { get; init; } = string.Empty;

        /// <summary>
        /// Product URI of the publisher's product.
        /// </summary>
        public string ProductUri { get; init; } = string.Empty;

        /// <summary>
        /// ApplicationType of the publisher.
        /// </summary>
        public ApplicationType ApplicationType { get; init; }
            = ApplicationType.Server;

        /// <summary>
        /// Optional capability identifiers (e.g. <c>UAMA</c>, <c>NA</c>).
        /// </summary>
        public ArrayOf<string> Capabilities { get; init; } = [];

        /// <summary>
        /// Supported transport profile URIs.
        /// </summary>
        public ArrayOf<string> SupportedTransportProfiles { get; init; } = [];

        /// <summary>
        /// Supported PubSub security policy URIs.
        /// </summary>
        public ArrayOf<string> SupportedSecurityPolicies { get; init; } = [];
    }
}
