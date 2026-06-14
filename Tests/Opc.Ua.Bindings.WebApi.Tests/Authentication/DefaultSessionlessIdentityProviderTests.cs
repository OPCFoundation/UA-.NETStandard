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

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebApi.Tests.Authentication
{
    /// <summary>
    /// Unit tests for the default ASP.NET Core principal to OPC UA identity
    /// mapper used by sessionless REST calls.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultSessionlessIdentityProviderTests
    {
        [Test]
        public void UnauthenticatedReturnsNull()
        {
            var provider = new DefaultSessionlessIdentityProvider();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Null);
        }

        [Test]
        public void AuthenticatedWithNameClaimReturnsUserIdentity()
        {
            var provider = new DefaultSessionlessIdentityProvider();
            ClaimsIdentity claimsIdentity = new(
                [new Claim(ClaimTypes.Name, "alice")],
                authenticationType: "Test");
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(claimsIdentity)
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Not.Null);
            Assert.That(identity, Is.InstanceOf<UserIdentity>());
            Assert.That(identity!.TokenHandler.Token, Is.InstanceOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)identity.TokenHandler.Token;
            Assert.That(token.UserName, Is.EqualTo("alice"));
        }

        [Test]
        public void AuthenticatedWithoutNameClaimReturnsAnonymous()
        {
            var provider = new DefaultSessionlessIdentityProvider();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"))
            };

            IUserIdentity? identity = provider.Resolve(context);

            Assert.That(identity, Is.Not.Null);
            Assert.That(identity!.TokenHandler.Token, Is.InstanceOf<AnonymousIdentityToken>());
        }
    }
}
