/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
            : this(authorityUri, tokenBytes, expiresAt, displayName, null)
        {
        }

        /// <summary>
        /// Creates a provider for a pre-supplied JWT payload with a
        /// caller-supplied <see cref="TimeProvider"/> for expiry checks.
        /// </summary>
        public JwtBearerAccessTokenProvider(
            string authorityUri,
            byte[] tokenBytes,
            DateTime expiresAt,
            string displayName,
            TimeProvider? timeProvider)
        {
            if (tokenBytes == null)
            {
                throw new ArgumentNullException(nameof(tokenBytes));
            }

            AuthorityUri = authorityUri ?? throw new ArgumentNullException(nameof(authorityUri));
            m_tokenBytes = (byte[])tokenBytes.Clone();
            m_expiresAt = expiresAt;
            m_displayName = displayName ?? string.Empty;
            m_timeProvider = timeProvider ?? TimeProvider.System;
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
            return FromJwtString(authorityUri, jwt, expiresAt, displayName, null);
        }

        /// <summary>
        /// Creates a provider by UTF-8 encoding a compact JWT string,
        /// using the supplied <see cref="TimeProvider"/> for expiry checks.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="jwt"/> is <c>null</c>.</exception>
        public static JwtBearerAccessTokenProvider FromJwtString(
            string authorityUri,
            string jwt,
            DateTime expiresAt,
            string displayName,
            TimeProvider? timeProvider)
        {
            if (jwt == null)
            {
                throw new ArgumentNullException(nameof(jwt));
            }

            return new JwtBearerAccessTokenProvider(
                authorityUri,
                Encoding.UTF8.GetBytes(jwt),
                expiresAt,
                displayName,
                timeProvider);
        }

        /// <inheritdoc/>
        public string AuthorityUri { get; }

        /// <inheritdoc/>
        public ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (m_expiresAt <= m_timeProvider.GetUtcNow().UtcDateTime)
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
        private readonly TimeProvider m_timeProvider;
    }
}
