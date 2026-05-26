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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Acquires <see cref="AccessToken"/> instances from an
    /// Authorization Service or local credential store, independent of
    /// the OPC UA transport. Used by an
    /// <see cref="IClientIdentityProvider"/> to materialize the payload
    /// of an <c>IssuedIdentityToken</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is responsible for any out-of-band protocol needed
    /// to obtain the token (OAuth2 token endpoint, OIDC device flow,
    /// MSAL silent + interactive, OS native credential broker, GDS
    /// <c>StartRequestToken</c> / <c>KeyCredentialService</c>).
    /// Caching, refresh-token storage and renewal are implementation
    /// details — callers MUST treat each <see cref="AcquireAsync"/>
    /// call as potentially blocking.
    /// </para>
    /// <para>
    /// Implementations SHOULD return a token whose remaining lifetime is
    /// at least long enough to complete one <c>ActivateSession</c> round
    /// trip plus a safety margin. The
    /// <see cref="IClientIdentityProvider"/> caller decides when to call
    /// <see cref="AcquireAsync"/> again for proactive refresh.
    /// </para>
    /// </remarks>
    public interface IAccessTokenProvider
    {
        /// <summary>
        /// Stable URI that identifies the access-token issuer this
        /// provider is wired to (e.g. an Entra tenant URI, an OIDC
        /// authority, a GDS endpoint). Used by composite providers to
        /// pick the right inner provider for a given
        /// <see cref="AuthorizationServerMetadata.AuthorityUri"/>.
        /// </summary>
        string AuthorityUri { get; }

        /// <summary>
        /// Acquires an access token suitable for the
        /// <see cref="AuthorizationServerMetadata.ResourceUri"/>
        /// carried by <paramref name="metadata"/>.
        /// </summary>
        /// <param name="metadata">
        /// The server-asserted Authorization Service metadata extracted
        /// from the JWT <see cref="UserTokenPolicy.IssuerEndpointUrl"/>
        /// payload per Part 6 §6.5.2.2.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A fresh <see cref="AccessToken"/>. The caller takes ownership
        /// and MUST dispose it when the token has been encrypted into a
        /// <c>UserIdentityToken</c>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// <c>BadIdentityTokenRejected</c> when the issuer refused to
        /// issue a token for the requested resource.
        /// </exception>
        ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            CancellationToken ct = default);
    }
}
