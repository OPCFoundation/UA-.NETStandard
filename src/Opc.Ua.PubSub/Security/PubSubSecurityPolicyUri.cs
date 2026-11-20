// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Well-known PubSub security policy URIs. Mirrors the URI values
    /// declared in OPC UA Part 14 §7.2.4.4.3.1 and the SKS profile
    /// table in Part 14 §8.4 so they can be referenced from
    /// <see cref="SecurityGroupDataType"/> configuration without
    /// re-defining magic strings.
    /// </summary>
    /// <remarks>
    /// Implements the URI catalogue from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>.
    /// </remarks>
    public static class PubSubSecurityPolicyUri
    {
        /// <summary>
        /// No PubSub message security (SecurityMode=None). Used to
        /// disable signing and encryption while still allowing the
        /// SecurityGroupId / TokenId fields to be set to 0.
        /// </summary>
        public const string None =
            "http://opcfoundation.org/UA/SecurityPolicy#None";

        /// <summary>
        /// AES-128 CTR mode with HMAC-SHA-256 signing. Defined for
        /// PubSub by Part 14 §7.2.4.4.3.1.
        /// </summary>
        public const string PubSubAes128Ctr =
            "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR";

        /// <summary>
        /// AES-256 CTR mode with HMAC-SHA-256 signing. Defined for
        /// PubSub by Part 14 §7.2.4.4.3.1.
        /// </summary>
        public const string PubSubAes256Ctr =
            "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR";
    }
}
