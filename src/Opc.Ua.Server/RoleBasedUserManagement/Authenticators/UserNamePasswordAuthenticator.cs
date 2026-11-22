// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates username/password identity tokens against an <see cref="IUserDatabase"/>.
    /// </summary>
    public sealed class UserNamePasswordAuthenticator : IUserTokenAuthenticator
    {
        private readonly IUserDatabase m_userDatabase;
        private readonly IUserManagement m_userManagement;
        private readonly Func<UserNameIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>>? m_verify;

        /// <summary>
        /// Creates a username/password authenticator.
        /// </summary>
        public UserNamePasswordAuthenticator(
            IUserDatabase userDatabase,
            IUserManagement userManagement,
            ITelemetryContext telemetry)
        {
            m_userDatabase = userDatabase ?? throw new ArgumentNullException(nameof(userDatabase));
            m_userManagement = userManagement ?? throw new ArgumentNullException(nameof(userManagement));
            _ = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a delegate-backed username/password authenticator.
        /// </summary>
        public UserNamePasswordAuthenticator(
            Func<UserNameIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>> verify)
        {
            m_userDatabase = null!;
            m_userManagement = null!;
            m_verify = verify ?? throw new ArgumentNullException(nameof(verify));
        }

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.UserName;

        /// <inheritdoc/>
        public string? IssuedTokenProfileUri => null;

        /// <inheritdoc/>
        public ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not UserNameIdentityTokenHandler userTokenHandler)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }

            if (m_verify != null)
            {
                return AuthenticateWithVerifierAsync(userTokenHandler, ct);
            }

            string userName = userTokenHandler.UserName;
            byte[]? password = userTokenHandler.DecryptedPassword;
            if (string.IsNullOrEmpty(userName))
            {
                return Reject(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty username is not accepted.");
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                return Reject(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty password is not accepted.");
            }

            ct.ThrowIfCancellationRequested();
            bool credentialsValid = m_userDatabase.CheckCredentials(userName, password);
            if (!m_userManagement.IsUserActive(userName) || !credentialsValid)
            {
                return Reject(
                    StatusCodes.BadUserAccessDenied,
                    "Invalid username or password.");
            }

            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Accept(new UserIdentity(userTokenHandler)));
        }

        private async ValueTask<AuthenticationResult> AuthenticateWithVerifierAsync(
            UserNameIdentityTokenHandler userTokenHandler,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                IUserIdentity? identity = await m_verify!(userTokenHandler, ct).ConfigureAwait(false);
                return identity == null
                    ? RejectResult(
                        StatusCodes.BadIdentityTokenRejected,
                        "Username token verifier did not return an identity.")
                    : AuthenticationResult.Accept(identity);
            }
            catch (ServiceResultException ex)
            {
                return AuthenticationResult.Reject(ex.Result);
            }
        }

        private static ValueTask<AuthenticationResult> Reject(StatusCode statusCode, string message)
        {
            return new ValueTask<AuthenticationResult>(RejectResult(statusCode, message));
        }

        private static AuthenticationResult RejectResult(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(new ServiceResult(statusCode, new LocalizedText(message)));
        }
    }
}
