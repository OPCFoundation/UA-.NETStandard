// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Issued-token client identity options.
    /// </summary>
    public sealed class IssuedTokenClientIdentityOptions
    {
        /// <summary>
        /// Issued-token profile URI.
        /// </summary>
        public string ProfileUri { get; set; } = Profiles.JwtUserToken;

        /// <summary>
        /// Authority URI used to select a registered access-token provider.
        /// </summary>
        public string? AuthorityUri { get; set; }
    }
}
