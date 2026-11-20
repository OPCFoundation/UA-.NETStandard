/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Identifies a trust list by a well-known or user-defined name.
    /// A trust list is a paired set of stores: a trusted-certificate store
    /// and an issuer-certificate store.
    /// </summary>
    public sealed record TrustListIdentifier(string Name)
    {
        /// <summary>
        /// Peer application certificates (UA-TCP / UA binary).
        /// </summary>
        public static readonly TrustListIdentifier Peers = new("Peers");

        /// <summary>
        /// User X.509 certificates (identity tokens).
        /// </summary>
        public static readonly TrustListIdentifier Users = new("Users");

        /// <summary>
        /// HTTPS transport certificates.
        /// </summary>
        public static readonly TrustListIdentifier Https = new("Https");

        /// <summary>
        /// Rejected certificates (no issuer pair).
        /// </summary>
        public static readonly TrustListIdentifier Rejected = new("Rejected");

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }
    }
}
