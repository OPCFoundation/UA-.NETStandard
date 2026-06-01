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

using System;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// A single access-token (typically a JWT or opaque issued-token
    /// payload) acquired by an <see cref="IAccessTokenProvider"/> and
    /// destined for an OPC UA <c>IssuedIdentityToken</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The token body is held as a raw byte array because Part 6 §6.5
    /// defines <c>IssuedIdentityToken.TokenData</c> as a
    /// <c>ByteString</c>. JWTs are passed as their compact (UTF-8) text
    /// encoding; SAML assertions / Kerberos tokens are passed as their
    /// native binary form.
    /// </para>
    /// <para>
    /// Disposing the token wipes the in-memory byte buffer. Callers
    /// MUST dispose access tokens after they have been encrypted into a
    /// <c>UserIdentityToken</c> for transmission.
    /// </para>
    /// </remarks>
    public sealed class AccessToken : IDisposable
    {
        /// <summary>
        /// Creates a new access token.
        /// </summary>
        /// <param name="profileUri">
        /// The OPC UA issued-token profile URI (e.g.
        /// <see cref="Profiles.JwtUserToken"/>). Used by the server to
        /// dispatch validation.
        /// </param>
        /// <param name="tokenData">
        /// The raw token payload as it will appear on the wire (before
        /// transport-level encryption). The buffer is taken by reference
        /// and zeroed on <see cref="Dispose"/>; do NOT reuse the array
        /// after passing it here.
        /// </param>
        /// <param name="expiresAt">
        /// UTC instant after which the token must no longer be used.
        /// <see cref="DateTime.MaxValue"/> indicates "no expiry known".
        /// </param>
        /// <param name="displayName">
        /// Human-readable name carried by the resulting
        /// <see cref="IUserIdentity"/> for logging and diagnostics.
        /// </param>
        /// <param name="grantedScopes">
        /// Scopes the Authorization Service actually granted (may
        /// differ from those requested). Provided for diagnostics and
        /// for the caller's own bookkeeping.
        /// </param>
        public AccessToken(
            string profileUri,
            byte[] tokenData,
            DateTime expiresAt,
            string displayName,
            string[]? grantedScopes = null)
        {
            ProfileUri = profileUri ?? throw new ArgumentNullException(nameof(profileUri));
            m_tokenData = tokenData ?? throw new ArgumentNullException(nameof(tokenData));
            ExpiresAt = expiresAt;
            DisplayName = displayName ?? string.Empty;
            GrantedScopes = grantedScopes ?? [];
        }

        /// <summary>
        /// The OPC UA issued-token profile URI describing the token
        /// payload format (e.g. <see cref="Profiles.JwtUserToken"/>).
        /// </summary>
        public string ProfileUri { get; }

        /// <summary>
        /// The token body as it will appear in
        /// <c>IssuedIdentityToken.TokenData</c> on the wire. The returned
        /// span is only valid while the <see cref="AccessToken"/> is not
        /// disposed.
        /// </summary>
        public ReadOnlySpan<byte> TokenData
        {
            get
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AccessToken));
                }
                return m_tokenData;
            }
        }

        /// <summary>
        /// UTC instant after which the token must not be used.
        /// <see cref="DateTime.MaxValue"/> means "no expiry known".
        /// </summary>
        public DateTime ExpiresAt { get; }

        /// <summary>
        /// Human-readable display name for the authenticated principal.
        /// Used by <see cref="IUserIdentity.DisplayName"/>.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Scopes the Authorization Service granted with this token.
        /// </summary>
        public string[] GrantedScopes { get; }

        /// <summary>
        /// Zeroes the token bytes and marks the instance unusable.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            Array.Clear(m_tokenData, 0, m_tokenData.Length);
        }

        private readonly byte[] m_tokenData;
        private bool m_disposed;
    }
}
