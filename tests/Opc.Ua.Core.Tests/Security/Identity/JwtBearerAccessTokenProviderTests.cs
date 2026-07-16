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

using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class JwtBearerAccessTokenProviderTests
    {
        [Test]
        public async Task AcquireAsyncReturnsFreshTokenPerCall()
        {
            var provider = new JwtBearerAccessTokenProvider(
                "https://issuer.example",
                [1, 2, 3],
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
                "jwt");

            AccessToken first = await provider
                .AcquireAsync(new AuthorizationServerMetadata())
                .ConfigureAwait(false);
            AccessToken second = await provider
                .AcquireAsync(new AuthorizationServerMetadata())
                .ConfigureAwait(false);

            try
            {
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first.TokenData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(second.TokenData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(first.DisplayName, Is.EqualTo("jwt"));
                Assert.That(second.DisplayName, Is.EqualTo("jwt"));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public async Task AcquireAsyncClonesBytesForEachToken()
        {
            byte[] supplied = [1, 2, 3];
            var provider = new JwtBearerAccessTokenProvider(
                "https://issuer.example",
                supplied,
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5));
            supplied[0] = 99;

            AccessToken first = await provider
                .AcquireAsync(new AuthorizationServerMetadata())
                .ConfigureAwait(false);
            try
            {
                byte[] mutableBytes = GetMutableTokenBytes(first);
                mutableBytes[0] = 42;
            }
            finally
            {
                first.Dispose();
            }

            using AccessToken second = await provider
                .AcquireAsync(new AuthorizationServerMetadata())
                .ConfigureAwait(false);

            Assert.That(second.TokenData.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void AcquireAsyncRejectsExpiredToken()
        {
            var provider = new JwtBearerAccessTokenProvider(
                "https://issuer.example",
                [1],
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-5));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await provider
                    .AcquireAsync(new AuthorizationServerMetadata())
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(ex.Message, Does.Contain("Token has expired."));
        }

        [Test]
        public async Task FromJwtStringUsesUtf8Encoding()
        {
            const string jwt = "header.payload-✓.signature";
            var provider = JwtBearerAccessTokenProvider.FromJwtString(
                "https://issuer.example",
                jwt,
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
                "display");

            using AccessToken token = await provider
                .AcquireAsync(new AuthorizationServerMetadata())
                .ConfigureAwait(false);

            Assert.That(token.ProfileUri, Is.EqualTo(Profiles.JwtUserToken));
            Assert.That(Encoding.UTF8.GetString(token.TokenData.ToArray()), Is.EqualTo(jwt));
            Assert.That(token.DisplayName, Is.EqualTo("display"));
        }

        private static byte[] GetMutableTokenBytes(AccessToken token)
        {
            FieldInfo field = typeof(AccessToken).GetField(
                "m_tokenData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (byte[])field.GetValue(token);
        }
    }
}
