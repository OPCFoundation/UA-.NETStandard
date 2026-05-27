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

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Result returned by token-exchange operations.
    /// </summary>
    public sealed class AccessTokenResult
    {
        /// <summary>The issued or refreshed access token (JWT or opaque).</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>The raw access-token bytes as carried by issued identity tokens.</summary>
        public byte[] AccessTokenBytes { get; set; } = Array.Empty<byte>();

        /// <summary>The access-token type, such as <c>JWT</c>.</summary>
        public string TokenType { get; set; } = string.Empty;

        /// <summary>The user token policy id associated with the issued token.</summary>
        public string PolicyId { get; set; } = string.Empty;

        /// <summary>When the access token expires (UTC).</summary>
        public DateTime AccessTokenExpiryTime { get; set; }

        /// <summary>The refresh token (if applicable).</summary>
        public string? RefreshToken { get; set; }

        /// <summary>When the refresh token expires (UTC).</summary>
        public DateTime RefreshTokenExpiryTime { get; set; }
    }

    /// <summary>
    /// Abstraction for the token-issuance back-end of an
    /// OPC 10000-12 §9 AuthorizationService. The default implementation
    /// delegates JWT signing to <see cref="Opc.Ua.Identity.ITokenIssuer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GDS that does not implement an authorization service leaves
    /// <see cref="ApplicationsNodeManager.AccessTokenProvider"/> as
    /// <c>null</c>, which causes the built-in handlers to return
    /// <c>Bad_NotSupported</c>.
    /// </para>
    /// <para>
    /// Implementations may delegate to an external OAuth2 / JWT provider,
    /// an on-premise token service, or any other credential-issuing
    /// mechanism. The interface intentionally mirrors the four
    /// AuthorizationService methods so that the node-manager handler
    /// logic is a thin dispatch layer.
    /// </para>
    /// </remarks>
    public interface IAccessTokenProvider
    {
        /// <summary>
        /// Validates the <paramref name="identityToken"/> and issues an
        /// access token for the requested <paramref name="resourceId"/>.
        /// Implements the legacy <c>RequestAccessToken</c> method
        /// (OPC 10000-12 §9.4).
        /// </summary>
        [Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
        ValueTask<string> RequestAccessTokenAsync(
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken ct = default);

        /// <summary>
        /// Begins a two-phase token-exchange flow (RC).
        /// Implements <c>StartRequestToken</c> (OPC 10000-12 §9.5, RC).
        /// </summary>
        ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken ct = default);

        /// <summary>
        /// Completes the two-phase token-exchange flow (RC).
        /// Implements <c>FinishRequestToken</c> (OPC 10000-12 §9.6, RC).
        /// </summary>
        ValueTask<AccessTokenResult> FinishRequestTokenAsync(
            Guid requestId,
            ArrayOf<string> requestedRoles,
            UserIdentityToken userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct = default);

        /// <summary>
        /// Refreshes an existing access token (RC).
        /// Implements <c>RefreshToken</c> (OPC 10000-12 §9.7, RC).
        /// </summary>
        ValueTask<AccessTokenResult> RefreshTokenAsync(
            string resourceId,
            string currentRefreshToken,
            CancellationToken ct = default);
    }
}
