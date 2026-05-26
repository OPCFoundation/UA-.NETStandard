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

            if (!m_userManagement.IsUserActive(userName) ||
                !m_userDatabase.CheckCredentials(userName, password))
            {
                return Reject(
                    StatusCodes.BadUserAccessDenied,
                    "Invalid username or password.");
            }

            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Accept(new UserIdentity(userTokenHandler)));
        }

        private static ValueTask<AuthenticationResult> Reject(StatusCode statusCode, string message)
        {
            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Reject(new ServiceResult(statusCode, new LocalizedText(message))));
        }
    }
}
