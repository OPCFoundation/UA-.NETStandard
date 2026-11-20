// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Options for the resource-server KeyCredential push binding.
    /// </summary>
    public sealed class KeyCredentialPushOptions
    {
        /// <summary>
        /// Browse path where the configuration folder is mounted. Empty means
        /// the standard ServerConfiguration/KeyCredentialConfiguration node.
        /// </summary>
        public string ConfigurationFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// RSA policies accepted for encrypted credential updates, in default-selection order.
        /// ECC and RSA-DH are rejected because this binding does not own an ephemeral-key exchange.
        /// </summary>
        public ArrayOf<string> AllowedSecurityPolicyUris { get; set; } =
        [
            SecurityPolicies.Aes256_Sha256_RsaPss,
            SecurityPolicies.Aes128_Sha256_RsaOaep,
            SecurityPolicies.Basic256Sha256
        ];
    }
}
