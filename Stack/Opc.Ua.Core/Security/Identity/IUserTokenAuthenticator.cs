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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Outcome of an authentication attempt by an
    /// <see cref="IUserTokenAuthenticator"/>.
    /// </summary>
    public enum AuthenticationOutcome
    {
        /// <summary>
        /// The authenticator does not handle the supplied token type /
        /// profile and the registry should ask the next authenticator
        /// (or fall back to the legacy
        /// <c>SessionManager.ImpersonateUser</c> event).
        /// </summary>
        NotHandled,

        /// <summary>
        /// The token was accepted; <see cref="AuthenticationResult.Identity"/>
        /// is populated.
        /// </summary>
        Accepted,

        /// <summary>
        /// The token was rejected;
        /// <see cref="AuthenticationResult.Error"/> describes the
        /// rejection reason and is reported back to the client.
        /// </summary>
        Rejected
    }

    /// <summary>
    /// The result of an <see cref="IUserTokenAuthenticator.AuthenticateAsync"/>
    /// call. Carries either an authenticated identity or a rejection
    /// reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The authenticator's job is to validate the token and produce a
    /// raw <see cref="IUserIdentity"/> (optionally implementing
    /// <see cref="IIdentityClaims"/>). Role mapping per OPC 10000-18
    /// §4.4.1 is performed separately by <c>IRoleManager</c> on the
    /// returned identity — authenticators MUST NOT pre-grant role
    /// NodeIds.
    /// </para>
    /// <para>
    /// This separation preserves the existing layering: token validation
    /// (this interface) → claim normalisation
    /// (<see cref="IIdentityClaims"/> on the identity) →
    /// Part 18 role mapping (<c>IRoleManager</c>).
    /// </para>
    /// </remarks>
    public readonly record struct AuthenticationResult
    {
        private AuthenticationResult(
            AuthenticationOutcome outcome,
            IUserIdentity? identity,
            ServiceResult? error)
        {
            Outcome = outcome;
            Identity = identity;
            Error = error;
        }

        /// <summary>
        /// The outcome of the authentication attempt.
        /// </summary>
        public AuthenticationOutcome Outcome { get; }

        /// <summary>
        /// The authenticated identity when
        /// <see cref="Outcome"/> is <see cref="AuthenticationOutcome.Accepted"/>;
        /// otherwise <see langword="null"/>.
        /// </summary>
        public IUserIdentity? Identity { get; }

        /// <summary>
        /// The rejection reason when
        /// <see cref="Outcome"/> is <see cref="AuthenticationOutcome.Rejected"/>;
        /// otherwise <see langword="null"/>.
        /// </summary>
        public ServiceResult? Error { get; }

        /// <summary>
        /// Indicates the authenticator does not handle this token.
        /// </summary>
        public static AuthenticationResult NotHandled { get; }
            = new(AuthenticationOutcome.NotHandled, null, null);

        /// <summary>
        /// Indicates the token was accepted and produced
        /// <paramref name="identity"/>.
        /// </summary>
        public static AuthenticationResult Accept(IUserIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            return new AuthenticationResult(
                AuthenticationOutcome.Accepted,
                identity,
                null);
        }

        /// <summary>
        /// Indicates the token was rejected. The reason is reported back
        /// to the client as the <c>ActivateSession</c> response error.
        /// </summary>
        public static AuthenticationResult Reject(ServiceResult error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }
            return new AuthenticationResult(
                AuthenticationOutcome.Rejected,
                null,
                error);
        }
    }

    /// <summary>
    /// Context passed to an <see cref="IUserTokenAuthenticator"/> while
    /// the server is validating an incoming <c>ActivateSession</c> user
    /// identity token.
    /// </summary>
    /// <param name="TokenHandler">
    /// The decoded, decrypted token (per Part 4 §5.6.4): for
    /// <c>UserName</c> the password is already in the clear, for
    /// <c>IssuedToken</c> the issued payload is already decrypted, for
    /// <c>X509</c> the wire certificate is parsed but not validated.
    /// </param>
    /// <param name="UserTokenPolicy">
    /// The endpoint policy the client selected. Carries the OPC UA
    /// security policy used to encrypt the token and, for JWT,
    /// <see cref="UserTokenPolicy.IssuedTokenType"/> /
    /// <see cref="UserTokenPolicy.IssuerEndpointUrl"/>.
    /// </param>
    /// <param name="EndpointDescription">
    /// The endpoint that received the request. Authenticators MAY refuse
    /// to issue an identity for an unsecured endpoint
    /// (<see cref="MessageSecurityMode.None"/>) for token types that
    /// require channel protection.
    /// </param>
    /// <param name="MessageContext">
    /// Encoder factories + telemetry.
    /// </param>
    /// <param name="ChannelCertificate">
    /// The ApplicationInstance certificate bound to the secure channel, when supplied by the client.
    /// </param>
    /// <param name="ChannelApplicationUri">
    /// The ApplicationUri from the session diagnostics for the connecting application, when known.
    /// </param>
    public readonly record struct AuthenticationContext(
        IUserIdentityTokenHandler TokenHandler,
        UserTokenPolicy UserTokenPolicy,
        EndpointDescription EndpointDescription,
        IServiceMessageContext MessageContext,
        Certificate? ChannelCertificate = null,
        string? ChannelApplicationUri = null);

    /// <summary>
    /// Validates a single OPC UA user identity token type / profile.
    /// Plugged into an <see cref="IServerIdentityRegistry"/> which
    /// dispatches by <see cref="UserTokenType"/> and (for issued tokens)
    /// by the <c>IssuedTokenType</c> profile URI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations are responsible for token validation only:
    /// </para>
    /// <list type="bullet">
    ///   <item>Anonymous → trivially accept.</item>
    ///   <item>UserName → verify the decrypted password against an
    ///         <c>IUserDatabase</c>; honour lockout / MustChangePassword.</item>
    ///   <item>X509 → verify the user certificate against an
    ///         <c>ICertificateValidator</c> bound to the <c>Users</c>
    ///         trust list and extract subject / SAN claims.</item>
    ///   <item>Issued (JWT) → verify the JWT signature against an
    ///         <c>IIssuerKeyResolver</c>, check <c>iss</c> / <c>aud</c>
    ///         / <c>exp</c>, and populate
    ///         <see cref="IIdentityClaims"/>.</item>
    /// </list>
    /// <para>
    /// Authenticators MUST NOT perform Part 18 role mapping — that is
    /// the sole responsibility of <c>IRoleManager.ResolveGrantedRoles</c>
    /// invoked by the <c>SessionManager</c> after the authenticator
    /// returns.
    /// </para>
    /// </remarks>
    public interface IUserTokenAuthenticator
    {
        /// <summary>
        /// The OPC UA user-token type this authenticator handles.
        /// </summary>
        UserTokenType TokenType { get; }

        /// <summary>
        /// For <see cref="UserTokenType.IssuedToken"/> authenticators,
        /// the <c>IssuedTokenType</c> profile URI handled (e.g.
        /// <see cref="Profiles.JwtUserToken"/>). <see langword="null"/>
        /// for non-issued-token authenticators.
        /// </summary>
        string? IssuedTokenProfileUri { get; }

        /// <summary>
        /// Validate the token. Returns one of:
        /// <see cref="AuthenticationOutcome.Accepted"/>,
        /// <see cref="AuthenticationOutcome.Rejected"/>, or
        /// <see cref="AuthenticationOutcome.NotHandled"/> (when the
        /// authenticator declines based on policy / endpoint, e.g.
        /// "this JWT issuer only accepts signed channels").
        /// </summary>
        ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default);
    }
}
