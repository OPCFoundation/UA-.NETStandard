// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

    /// <summary>
    /// Acquires an access token whose proof also depends on the selected resource-server endpoint.
    /// </summary>
    public interface IEndpointAccessTokenProvider : IAccessTokenProvider
    {
        /// <summary>
        /// Acquires a token using the endpoint's server identity as well as its authorization metadata.
        /// The caller owns and disposes the returned token.
        /// </summary>
        /// <param name="metadata">The authorization service metadata for the requested resource.</param>
        /// <param name="endpoint">The resource-server endpoint selected for the connection.</param>
        /// <param name="ct">The token used to cancel token acquisition.</param>
        /// <returns>The access token. The caller must dispose it after encrypting the user identity token.</returns>
        ValueTask<AccessToken> AcquireAsync(
            AuthorizationServerMetadata metadata,
            EndpointDescription endpoint,
            CancellationToken ct = default);
    }
}
