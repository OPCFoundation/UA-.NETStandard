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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Resolves PubSub security key material for offline UADP decryption.
    /// </summary>
    public interface IPubSubKeyResolver
    {
        /// <summary>
        /// Attempts to resolve a key snapshot for the requested SecurityGroup, token and policy.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroupId, or <see langword="null"/> when not known from capture.</param>
        /// <param name="tokenId">SecurityTokenId from the UADP SecurityHeader.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A caller-owned key-material snapshot, or <see langword="null"/> when no key is available.
        /// </returns>
        ValueTask<PubSubKeyMaterial?> TryResolveAsync(
            string? securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            CancellationToken cancellationToken = default);
    }
}
