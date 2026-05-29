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
 *
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

#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Static JWT bearer <see cref="IAccessTokenProvider"/> for tests,
    /// smoke flows, and callers that obtain the token out-of-band.
    /// </summary>
    public sealed class JwtBearerAccessTokenProvider : IAccessTokenProvider
    {
        /// <summary>
        /// Creates a provider for a pre-supplied JWT payload.
        /// </summary>
        public JwtBearerAccessTokenProvider(
            string authorityUri,
            byte[] tokenBytes,
            DateTime expiresAt,
            string displayName = "")
        {
            if (tokenBytes == null)
            {
                throw new ArgumentNullException(nameof(tokenBytes));
            }

            AuthorityUri = authorityUri ?? throw new ArgumentNullException(nameof(authorityUri));
            m_tokenBytes = (byte[])tokenBytes.Clone();
            m_expiresAt = expiresAt;
            m_displayName = displayName ?? string.Empty;
        }

        /// <summary>
        /// Creates a provider by UTF-8 encoding a compact JWT string.
        /// </summary>
        public static JwtBearerAccessTokenProvider FromJwtString(
            string authorityUri,
            string jwt,
            DateTime expiresAt,
            string displayName = "")
        {
            if (jwt == null)
            {
                throw new ArgumentNullException(nameof(jwt));
            }

            return new JwtBearerAccessTokenProvider(
                authorityUri,
                Encoding.UTF8.GetBytes(jwt),
                expiresAt,
                displayName);
        }

        /// <inheritdoc/>
        public string AuthorityUri { get; }

        /// <inheritdoc/>
        public ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (m_expiresAt <= TimeProvider.System.GetUtcNow().UtcDateTime)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Token has expired.");
            }

#pragma warning disable CA2000 // Ownership transfers to caller; TODO: remove when analyzer models ValueTask ownership.
            return new ValueTask<AccessToken>(CreateAccessToken());
#pragma warning restore CA2000
        }

        private AccessToken CreateAccessToken()
        {
            return new AccessToken(
                Profiles.JwtUserToken,
                (byte[])m_tokenBytes.Clone(),
                m_expiresAt,
                m_displayName);
        }

        private readonly byte[] m_tokenBytes;
        private readonly DateTime m_expiresAt;
        private readonly string m_displayName;
    }
}
