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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Client")]
    [Category("Identity")]
    public sealed class GdsKeyCredentialAccessTokenProviderTests
    {
        [Test]
        public async Task AcquireAsyncBuildsProofTokenAndCachesCredentialAsync()
        {
            int acquireCount = 0;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                ct =>
                {
                    acquireCount++;
                    return new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential(
                            "cred\"\\id",
                            [1, 2, 3, 4],
                            DateTime.UtcNow.AddMinutes(10),
                            ["scope-a"]));
                },
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };

            AccessToken first = await provider.AcquireAsync(metadata).ConfigureAwait(false);
            AccessToken second = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(acquireCount, Is.EqualTo(1));
            Assert.That(first.ProfileUri, Is.EqualTo(GdsKeyCredentialAccessTokenProvider.ProfileUri));
            Assert.That(first.DisplayName, Is.EqualTo("cred\"\\id"));
            Assert.That(first.GrantedScopes, Has.Length.EqualTo(1));
            Assert.That(first.GrantedScopes[0], Is.EqualTo("scope-a"));
            Assert.That(Encoding.UTF8.GetString(first.TokenData.ToArray()), Does.Contain("\\\"").And.Contain("\\\\"));
            Assert.That(second.DisplayName, Is.EqualTo(first.DisplayName));
        }

        [Test]
        public async Task AcquireAsyncUsesDefaultExpirationWhenCredentialHasNoneAsync()
        {
            DateTime before = DateTime.UtcNow;
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(
                    new GdsIssuedKeyCredential("cred", [1, 2, 3], DateTime.MinValue)),
                "urn:authority",
                TimeSpan.FromMinutes(1));
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };

            AccessToken token = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(token.ExpiresAt, Is.GreaterThanOrEqualTo(before.AddSeconds(30)));
            Assert.That(token.ExpiresAt, Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(2)));
        }

        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    (Func<CancellationToken, ValueTask<GdsIssuedKeyCredential>>)null!,
                    "urn:authority"),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    _ => new ValueTask<GdsIssuedKeyCredential>(
                        new GdsIssuedKeyCredential("cred", [1], DateTime.UtcNow)),
                    null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new GdsKeyCredentialAccessTokenProvider(
                    new object(),
                    "urn:authority",
                    string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task DisposeClearsCachedCredentialAndPreventsAcquireAsync()
        {
            var provider = new GdsKeyCredentialAccessTokenProvider(
                _ => new ValueTask<GdsIssuedKeyCredential>(
                    new GdsIssuedKeyCredential("cred", [1, 2, 3], DateTime.UtcNow.AddMinutes(10))),
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };

            await provider.AcquireAsync(metadata).ConfigureAwait(false);
            FieldInfo cachedCredentialField = typeof(GdsKeyCredentialAccessTokenProvider).GetField(
                "m_cachedCredential",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cachedCredential = (GdsIssuedKeyCredential)cachedCredentialField.GetValue(provider)!;
            byte[] cachedSecret = cachedCredential.CredentialSecret;

            provider.Dispose();
            provider.Dispose();

            Assert.That(cachedSecret, Is.EqualTo(new byte[] { 0, 0, 0 }));
            Assert.That(
                async () => await provider.AcquireAsync(metadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task ReflectionClientSupportsValueTaskAndTaskShapesAsync()
        {
            var client = new ReflectionShapeClient();
            using var provider = new GdsKeyCredentialAccessTokenProvider(
                client,
                "urn:authority",
                "urn:app",
                (ByteString)"public-key"u8.ToArray(),
                SecurityPolicies.Basic256Sha256,
                [ObjectIds.WellKnownRole_AuthenticatedUser]);
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority" };

            AccessToken token = await provider.AcquireAsync(metadata).ConfigureAwait(false);

            Assert.That(client.StartRequestApplicationUri, Is.EqualTo("urn:app"));
            Assert.That(client.StartRequestSecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(client.StartRequestRoles, Is.EqualTo(new[] { ObjectIds.WellKnownRole_AuthenticatedUser }));
            Assert.That(token.DisplayName, Is.EqualTo("credential-id"));
            Assert.That(token.GrantedScopes, Does.Contain(SecurityPolicies.Basic256Sha256));
            Assert.That(token.GrantedScopes, Does.Contain(ObjectIds.WellKnownRole_AuthenticatedUser.ToString()));
        }

        private sealed class ReflectionShapeClient
        {
            public string? StartRequestApplicationUri { get; private set; }

            public string? StartRequestSecurityPolicyUri { get; private set; }

            public ArrayOf<NodeId> StartRequestRoles { get; private set; } = [];

            public ValueTask<NodeId> StartRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                CancellationToken ct)
            {
                _ = publicKey;
                _ = ct;
                StartRequestApplicationUri = applicationUri;
                StartRequestSecurityPolicyUri = securityPolicyUri;
                StartRequestRoles = requestedRoles;
                return new ValueTask<NodeId>(new NodeId(1234));
            }

            public Task<(string credentialId, ByteString credentialSecret, string certificateThumbprint,
                string securityPolicyUri, ArrayOf<NodeId> grantedRoles)> FinishRequestAsync(
                    NodeId requestId,
                    bool cancel,
                    CancellationToken ct)
            {
                _ = requestId;
                _ = cancel;
                _ = ct;
                ArrayOf<NodeId> grantedRoles = [ObjectIds.WellKnownRole_AuthenticatedUser];
                return Task.FromResult((
                    "credential-id",
                    (ByteString)"secret"u8.ToArray(),
                    "thumbprint",
                    SecurityPolicies.Basic256Sha256,
                    grantedRoles));
            }
        }
    }
}
