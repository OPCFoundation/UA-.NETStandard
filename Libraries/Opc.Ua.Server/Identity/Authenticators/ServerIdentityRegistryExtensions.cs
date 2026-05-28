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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Convenience registrations for the built-in server identity authenticators.
    /// </summary>
    public static class ServerIdentityRegistryExtensions
    {
        /// <summary>
        /// Registers anonymous and username/password authenticators plus optional X.509 and JWT authenticators.
        /// </summary>
        public static IServerIdentityRegistry RegisterDefaultAuthenticators(
            this IServerIdentityRegistry registry,
            IUserDatabase userDatabase,
            ICertificateValidatorEx? userCertificateValidator = null,
            Func<IssuedIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity?>>?
                jwtTokenValidator = null)
        {
            if (userDatabase == null)
            {
                throw new ArgumentNullException(nameof(userDatabase));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(new AnonymousAuthenticator());
            registry.Register(new UserNamePasswordAuthenticator(
                (token, ct) => VerifyUserDatabaseAsync(userDatabase, token, ct)));
            if (userCertificateValidator != null)
            {
                registry.Register(new X509Authenticator(userCertificateValidator));
            }
            if (jwtTokenValidator != null)
            {
                registry.Register(new JwtAuthenticator(jwtTokenValidator));
            }

            return registry;
        }

        /// <summary>
        /// Registers anonymous and delegate-backed username/password, X.509 and JWT authenticators.
        /// </summary>
        public static IServerIdentityRegistry RegisterDefaultAuthenticators(
            this IServerIdentityRegistry registry,
            Func<UserNameIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>> verifyUserName,
            Func<X509IdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>>? verifyX509 = null,
            Func<IssuedIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity?>>? verifyJwt = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (verifyUserName == null)
            {
                throw new ArgumentNullException(nameof(verifyUserName));
            }

            registry.Register(new AnonymousAuthenticator());
            registry.Register(new UserNamePasswordAuthenticator(verifyUserName));
            if (verifyX509 != null)
            {
                registry.Register(new X509Authenticator(verifyX509));
            }
            if (verifyJwt != null)
            {
                registry.Register(new JwtAuthenticator(verifyJwt));
            }

            return registry;
        }

        private static ValueTask<IUserIdentity> VerifyUserDatabaseAsync(
            IUserDatabase userDatabase,
            UserNameIdentityTokenHandler userTokenHandler,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string userName = userTokenHandler.UserName;
            byte[]? password = userTokenHandler.DecryptedPassword;
            if (string.IsNullOrEmpty(userName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty username is not accepted.");
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty password is not accepted.");
            }

            if (!userDatabase.CheckCredentials(userName, password))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Invalid username or password.");
            }

            return new ValueTask<IUserIdentity>(new UserIdentity(userTokenHandler));
        }
    }
}
