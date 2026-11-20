// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        public async Task AcquireAsyncRejectsExpiredToken()
        {
            var provider = new JwtBearerAccessTokenProvider(
                "https://issuer.example",
                [1],
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-5));

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
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
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (byte[])field!.GetValue(token)!;
        }
    }
}
