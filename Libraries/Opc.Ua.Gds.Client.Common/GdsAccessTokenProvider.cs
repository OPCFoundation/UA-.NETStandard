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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

#nullable enable

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client-side access-token provider backed by a GDS
    /// AuthorizationService instance.
    /// </summary>
    public sealed class GdsAccessTokenProvider : Opc.Ua.Identity.IAccessTokenProvider
    {
        private readonly AuthorizationServiceClient m_client;
        private readonly string m_policyId;

        /// <summary>
        /// Creates a GDS access-token provider.
        /// </summary>
        public GdsAccessTokenProvider(
            AuthorizationServiceClient client,
            string authorityUri,
            string policyId = "jwt")
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrEmpty(authorityUri))
            {
                throw new ArgumentException("Authority URI must be supplied.", nameof(authorityUri));
            }
            AuthorityUri = authorityUri;
            m_policyId = policyId ?? string.Empty;
        }

        /// <inheritdoc/>
        public string AuthorityUri { get; }

        /// <inheritdoc/>
        public async ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            CancellationToken ct = default)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            string audience = metadata.Audience ?? metadata.ResourceUri ?? string.Empty;
            if (string.IsNullOrEmpty(audience))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "GDS access-token acquisition requires a resource URI or audience.");
            }

            string[] scopes = metadata.Scopes.Count == 0
                ? Array.Empty<string>()
                : [.. metadata.Scopes];
            ByteString requestorData = scopes.Length == 0
                ? ByteString.Empty
                : ByteString.From(Encoding.UTF8.GetBytes(string.Join(" ", scopes)));

            (_, Guid requestId) = await m_client
                .StartRequestTokenAsync(audience, m_policyId, requestorData, ct)
                .ConfigureAwait(false);

            (string accessToken,
                DateTime accessTokenExpiryTime,
                _,
                _) = await m_client
                .FinishRequestTokenAsync(
                    requestId,
                    Array.Empty<string>().ToArrayOf(),
                    new AnonymousIdentityToken(),
                    new SignatureData(),
                    ct)
                .ConfigureAwait(false);

            return new AccessToken(
                Profiles.JwtUserToken,
                Encoding.UTF8.GetBytes(accessToken),
                accessTokenExpiryTime,
                audience,
                scopes);
        }
    }
}
