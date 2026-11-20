// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Identity configuration bound from <c>OpcUa:Server:Identity</c>.
    /// </summary>
    public sealed class OpcUaServerIdentityOptions
    {
        /// <summary>
        /// Built-in authenticator toggles and shared JWT validation defaults.
        /// </summary>
        public DefaultAuthenticatorOptions Defaults { get; set; } = new();

        /// <summary>
        /// Trusted JWT issuers declared for server-side issued-token validation.
        /// </summary>
        public IList<JwtIssuerOptions> Issuers { get; } = [];
    }
}
