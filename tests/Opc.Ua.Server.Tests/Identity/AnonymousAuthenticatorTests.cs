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

using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class AnonymousAuthenticatorTests
    {
        [Test]
        public async Task AuthenticateAsyncAnonymousTokenAccepted()
        {
            var authenticator = new AnonymousAuthenticator();

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new AnonymousIdentityTokenHandler()))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            Assert.That(result.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public async Task AuthenticateAsyncDifferentTokenReturnsNotHandled()
        {
            var authenticator = new AnonymousAuthenticator();

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", Encoding.UTF8.GetBytes("password"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        private static AuthenticationContext CreateContext(IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = handler.TokenType, PolicyId = "policy" },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }
    }
}
