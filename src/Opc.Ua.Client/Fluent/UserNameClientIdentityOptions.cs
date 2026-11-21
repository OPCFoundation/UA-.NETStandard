// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Username/password client identity options.
    /// </summary>
    public sealed class UserNameClientIdentityOptions
    {
        /// <summary>
        /// User name to send with the selected username token policy.
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Secret name used to resolve the password.
        /// </summary>
        public string SecretName { get; set; } = string.Empty;

        /// <summary>
        /// Secret store type used to route the password lookup.
        /// </summary>
        public string SecretStoreType { get; set; } = string.Empty;

        /// <summary>
        /// Optional secret store path or sub-scope.
        /// </summary>
        public string? SecretStorePath { get; set; }
    }
}
