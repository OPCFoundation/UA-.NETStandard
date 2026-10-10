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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Opc.Ua.Identity;

namespace Opc.Ua.XRegistry.Connector
{
    internal sealed class XRegistryConnectorNativeIdentity(
        ISecretRegistry secrets, IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        public async ValueTask<IUserIdentity> VerifyUserAsync(
            UserNameIdentityTokenHandler token, CancellationToken ct)
        {
            if (!await CurrentUserAsync(token, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "The native credential is not authorized.");
            }
            return new UserIdentity(token);
        }

        public async ValueTask<bool> IsCurrentAsync(IUserIdentity identity, CancellationToken ct)
        {
            if (identity.TokenType == UserTokenType.UserName)
            {
                return identity.TokenHandler is UserNameIdentityTokenHandler token &&
                    await CurrentUserAsync(token, ct).ConfigureAwait(false);
            }
            if (identity.TokenType == UserTokenType.IssuedToken)
            {
                return identity is IIdentityClaims claims &&
                    claims.Claims.TryGetValue("exp", out object? value) &&
                    value is long expires &&
                    m_time.GetUtcNow().ToUnixTimeSeconds() < expires;
            }
            return identity.TokenType == UserTokenType.Certificate;
        }

        private async ValueTask<bool> CurrentUserAsync(UserNameIdentityTokenHandler token, CancellationToken ct)
        {
            IConfigurationSection? user = configuration.GetSection("NativeGateway:Users").GetChildren()
                .SingleOrDefault(section => section["UserName"] == token.UserName);
            if (user is null ||
                (user["Enabled"] is { } enabled && !bool.Parse(enabled)) ||
                token.DecryptedPassword is not { Length: > 0 } supplied ||
                user["PasswordSecret"] is not { Length: > 0 } name)
            {
                return false;
            }
            using ISecret? expected = await secrets.GetAsync(
                new SecretIdentifier(name, user["SecretStoreType"] ?? "Environment"), ct).ConfigureAwait(false);
            return expected is not null && CryptographicOperations.FixedTimeEquals(supplied, expected.Bytes);
        }

        private readonly TimeProvider m_time = timeProvider ?? TimeProvider.System;
    }
}
