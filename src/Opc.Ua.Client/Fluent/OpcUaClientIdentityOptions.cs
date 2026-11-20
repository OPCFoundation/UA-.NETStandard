/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client identity provider options bound from <c>OpcUa:Client:Identity</c>.
    /// </summary>
    public sealed class OpcUaClientIdentityOptions
    {
        /// <summary>
        /// Enables anonymous user-token support.
        /// </summary>
        public bool EnableAnonymous { get; set; } = true;

        /// <summary>
        /// Username/password identity configuration.
        /// </summary>
        public UserNameClientIdentityOptions? UserName { get; set; }

        /// <summary>
        /// X.509 user-certificate identity configuration.
        /// </summary>
        public X509ClientIdentityOptions? X509 { get; set; }

        /// <summary>
        /// Issued-token identity configuration.
        /// </summary>
        public IssuedTokenClientIdentityOptions? IssuedToken { get; set; }

        /// <summary>
        /// Preferred provider order. Empty preserves registration order.
        /// </summary>
        public IList<string> Order { get; } = [];
    }
}
