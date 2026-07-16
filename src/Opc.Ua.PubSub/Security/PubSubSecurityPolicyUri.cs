/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
