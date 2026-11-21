// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Options for the experimental KeyCredential issued-token bridge.
    /// </summary>
    public sealed class KeyCredentialBridgeOptions
    {
        /// <summary>
        /// Default vendor profile URI for KeyCredential issued tokens.
        /// </summary>
        public const string DefaultProfileUri =
            "urn:opcfoundation:netstandard:profile:authentication:keycredential";

        /// <summary>
        /// Vendor profile URI handled by the authenticator.
        /// </summary>
        public string ProfileUri { get; set; } = DefaultProfileUri;

        /// <summary>
        /// Maximum accepted age for the nonce proof carried by the token.
        /// </summary>
        public TimeSpan NonceLifetime { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Time source used to validate token freshness and expire replay entries.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }
}
