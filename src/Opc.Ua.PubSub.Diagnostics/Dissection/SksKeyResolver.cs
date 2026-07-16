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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// PubSub key resolver backed by an SKS or static <see cref="IPubSubSecurityKeyProvider"/>.
    /// </summary>
    public sealed class SksKeyResolver : IPubSubKeyResolver
    {
        /// <summary>
        /// Initializes a resolver over an existing PubSub security key provider.
        /// </summary>
        /// <param name="keyProvider">SKS-backed or static security key provider.</param>
        public SksKeyResolver(IPubSubSecurityKeyProvider keyProvider)
        {
            ArgumentNullException.ThrowIfNull(keyProvider);
            m_keyProvider = keyProvider;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubKeyMaterial?> TryResolveAsync(
            string? securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(securityPolicyUri);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(securityGroupId) &&
                !string.Equals(securityGroupId, m_keyProvider.SecurityGroupId, StringComparison.Ordinal))
            {
                return null;
            }

            PubSubSecurityKey? key = await m_keyProvider
                .TryGetKeyAsync(tokenId, cancellationToken)
                .ConfigureAwait(false);
            if (key is null)
            {
                return null;
            }

            return new PubSubKeyMaterial(
                m_keyProvider.SecurityGroupId,
                key.TokenId,
                securityPolicyUri,
                key.SigningKey.Span.ToArray(),
                key.EncryptingKey.Span.ToArray(),
                key.KeyNonce.Span.ToArray());
        }

        private readonly IPubSubSecurityKeyProvider m_keyProvider;
    }
}
