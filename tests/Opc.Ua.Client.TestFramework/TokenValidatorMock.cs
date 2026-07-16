/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Identity;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.TestFramework
{
    public sealed class TokenValidatorMock : ITokenValidator, IDisposable
    {
        public IssuedIdentityTokenHandler LastIssuedToken { get; set; }

        public void Dispose()
        {
            LastIssuedToken = null;
        }

        public IUserIdentity ValidateToken(IssuedIdentityTokenHandler issuedToken)
        {
            LastIssuedToken = issuedToken.Copy();

            return new UserIdentity(issuedToken);
        }
    }

    /// <summary>
    /// Adapts the legacy quickstart token validator to the P5 identity-registry surface.
    /// </summary>
    public sealed class MockJwtAuthenticator : IUserTokenAuthenticator
    {
        private readonly ITokenValidator m_validator;

        public MockJwtAuthenticator(ITokenValidator validator)
        {
            m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public UserTokenType TokenType => UserTokenType.IssuedToken;

        public string? IssuedTokenProfileUri => Profiles.JwtUserToken;

        public async ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (context.TokenHandler is not IssuedIdentityTokenHandler issuedToken ||
                issuedToken.IssuedTokenType != IssuedTokenType.JWT)
            {
                return AuthenticationResult.NotHandled;
            }

            return await new ValueTask<AuthenticationResult>(AuthenticateJwt(issuedToken))
                .ConfigureAwait(false);
        }

        private AuthenticationResult AuthenticateJwt(IssuedIdentityTokenHandler issuedToken)
        {
            try
            {
                return AuthenticationResult.Accept(m_validator.ValidateToken(issuedToken));
            }
            catch (ServiceResultException ex)
            {
                return AuthenticationResult.Reject(ex.Result);
            }
        }
    }
}
