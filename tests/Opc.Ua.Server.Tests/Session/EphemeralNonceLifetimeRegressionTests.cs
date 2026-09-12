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

#nullable enable

using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    public sealed class EphemeralNonceLifetimeRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingOrResettingPolicyDisposesThePreviousEphemeralKey(bool reset)
        {
            using var harness = new NonceHarness();
            harness.Session.GetNewEphemeralKey();
            using Nonce original = GetCurrentNonce(harness.Session);
            Assert.That(GetKey(original), Is.Not.Null);
            if (reset)
            {
                harness.Session.SetUserTokenSecurityPolicy(kPolicy);
            }
            else
            {
                harness.Session.GetNewEphemeralKey();
            }
            Assert.That(GetKey(original), Is.Null);
            harness.Session.GetNewEphemeralKey();
            Nonce current = GetCurrentNonce(harness.Session);
            Assert.That(current, Is.Not.SameAs(original));
            Assert.That(GetKey(current), Is.Not.Null);
            harness.Session.Dispose();
            Assert.That(GetKey(current), Is.Null);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        public async Task RetiredNonceRemainsUsableUntilEveryDecryptBorrowCompletesAsync(int borrowers, bool fail)
        {
            using var harness = new NonceHarness();
            EphemeralKeyType ephemeral = harness.Session.GetNewEphemeralKey()!;
            using Nonce original = GetCurrentNonce(harness.Session);
            using Nonce publicNonce = Nonce.CreateNonce(kPolicy, ephemeral.PublicKey.ToArray());
            using Nonce senderNonce = Nonce.CreateNonce(kPolicy);
            using var issuerCertificates = new CertificateCollection();
            EncryptedSecret encryptor = EncryptedSecret.CreateForEcc(
                harness.MessageContext, kPolicy, issuerCertificates, harness.ServerCertificate,
                publicNonce, harness.ClientCertificate, senderNonce, doNotEncodeSenderCertificate: true);
            byte[] encrypted = encryptor.Encrypt(s_testSecret, harness.ServerNonce.Data!);
            using var releaseFirst = new ManualResetEventSlim();
            using var releaseRest = new ManualResetEventSlim();
            using var key = new BlockingKey(GetKey(original)!, borrowers, fail, releaseFirst, releaseRest);
            s_keyField.SetValue(original, key);
            var operations = new Task<IUserIdentityTokenHandler>[borrowers];
            for (int i = 0; i < operations.Length; i++)
            {
                operations[i] = Task.Run(async () =>
                {
                    var token = new UserNameIdentityToken
                    {
                        PolicyId = "ecc",
                        UserName = "test-user",
                        Password = ByteString.From(encrypted)
                    };
                    (IUserIdentityTokenHandler identity, _) = await harness.Session.ValidateBeforeActivateAsync(
                        harness.Context, harness.ClientSignature, new ExtensionObject(token), new SignatureData(),
                        CancellationToken.None).ConfigureAwait(false);
                    return identity;
                });
            }
            try
            {
                Task completed = await Task.WhenAny(key.Entered.Task, Task.WhenAll(operations))
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (completed != key.Entered.Task)
                {
                    await completed.ConfigureAwait(false);
                    Assert.Fail("Credential validation did not reach ECDH key agreement.");
                }
                harness.Session.GetNewEphemeralKey();
                Assert.That(key.Disposed, Is.False);
                releaseFirst.Set();
                if (borrowers > 1)
                {
                    Task<IUserIdentityTokenHandler> first = await Task.WhenAny(operations).ConfigureAwait(false);
                    IUserIdentityTokenHandler identity = await first.ConfigureAwait(false);
                    Assert.That(((UserNameIdentityTokenHandler)identity).DecryptedPassword, Is.EqualTo(s_testSecret));
                    Assert.That(key.Disposed, Is.False);
                }
            }
            finally
            {
                releaseFirst.Set();
                releaseRest.Set();
            }
            if (fail)
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await Task.WhenAll(operations).ConfigureAwait(false));
            }
            else
            {
                IUserIdentityTokenHandler[] identities = await Task.WhenAll(operations).ConfigureAwait(false);
                foreach (IUserIdentityTokenHandler identity in identities)
                {
                    var userName = (UserNameIdentityTokenHandler)identity;
                    try
                    {
                        Assert.That(userName.DecryptedPassword, Is.EqualTo(s_testSecret));
                    }
                    finally
                    {
                        CryptoUtils.ZeroMemory(userName.DecryptedPassword!);
                    }
                }
            }
            Assert.That(key.Disposed, Is.True);
            Assert.That(GetKey(original), Is.Null);
        }

        private static Nonce GetCurrentNonce(ServerSession session)
        {
            return (Nonce)s_nonceField.GetValue(session)!;
        }

        private static ECDiffieHellman? GetKey(Nonce nonce)
        {
            return (ECDiffieHellman?)s_keyField.GetValue(nonce);
        }

        private sealed class NonceHarness : IDisposable
        {
            public NonceHarness()
            {
                ServerCertificate = CertificateBuilder.Create("CN=Nonce Lifetime Server")
                    .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
                ClientCertificate = CertificateBuilder.Create("CN=Nonce Lifetime Client")
                    .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                MessageContext = ServiceMessageContext.CreateEmpty(telemetry);
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840/nonce-lifetime",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = kPolicy,
                    ServerCertificate = ServerCertificate.RawData.ToByteString(),
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "ecc",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = kPolicy
                        }
                    ]
                };
                var channel = new SecureChannelContext(
                    "nonce-channel", endpoint, RequestEncoding.Binary,
                    ClientCertificate.RawData, ServerCertificate.RawData, new byte[32]);
                Context = new OperationContext(
                    new RequestHeader(), channel, RequestType.ActivateSession, RequestLifetime.None);
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
                server.SetupGet(value => value.MessageContext).Returns(MessageContext);
                ServerNonce = Nonce.CreateNonce(32);
                using Nonce clientNonce = Nonce.CreateNonce(32);
                Session = new ServerSession(
                    Context, server.Object, ServerCertificate, new NodeId(100), clientNonce.Data.ToByteString(),
                    ServerNonce, "NonceLifetime", new ApplicationDescription { ApplicationUri = "urn:nonce-lifetime" },
                    endpoint.EndpointUrl, ClientCertificate.AddRef(), [], 60_000, 10, 10);
                Session.SetUserTokenSecurityPolicy(kPolicy);
                SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(kPolicy)!;
                ClientSignature = SecurityPolicies.Default.CreateSignatureData(
                    policy, ClientCertificate, policy.GetClientSignatureData(
                        channel.ChannelThumbprint, ServerNonce.Data, ServerCertificate.RawData,
                        channel.ServerChannelCertificate, channel.ClientChannelCertificate, clientNonce.Data!));
            }

            public Certificate ServerCertificate { get; }
            public Certificate ClientCertificate { get; }
            public IServiceMessageContext MessageContext { get; }
            public Nonce ServerNonce { get; }
            public SignatureData ClientSignature { get; }
            public OperationContext Context { get; }
            public ServerSession Session { get; }

            public void Dispose()
            {
                Session.Dispose();
                Context.Dispose();
                ServerNonce.Dispose();
                ClientCertificate.Dispose();
                ServerCertificate.Dispose();
            }
        }

        private sealed class BlockingKey(
            ECDiffieHellman inner,
            int borrowers,
            bool fail,
            ManualResetEventSlim releaseFirst,
            ManualResetEventSlim releaseRest) : ECDiffieHellman
        {
            public override ECDiffieHellmanPublicKey PublicKey => inner.PublicKey;
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Disposed { get; private set; }

            public override byte[] DeriveKeyMaterial(ECDiffieHellmanPublicKey otherPartyPublicKey)
            {
                WaitForRelease();
                return inner.DeriveKeyMaterial(otherPartyPublicKey);
            }

#if NET8_0_OR_GREATER
            public override byte[] DeriveRawSecretAgreement(ECDiffieHellmanPublicKey otherPartyPublicKey)
            {
                WaitForRelease();
                return inner.DeriveRawSecretAgreement(otherPartyPublicKey);
            }
#endif

            protected override void Dispose(bool disposing)
            {
                if (disposing && !Disposed)
                {
                    Disposed = true;
                    inner.Dispose();
                }
                base.Dispose(disposing);
            }

            private void WaitForRelease()
            {
                int position = Interlocked.Increment(ref m_entered);
                if (position == borrowers)
                {
                    Entered.TrySetResult(true);
                }
                ManualResetEventSlim release = position == 1 ? releaseFirst : releaseRest;
                if (!release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The key agreement barrier was not released.");
                }
                if (fail)
                {
                    throw new CryptographicException("Controlled key agreement failure.");
                }
            }

            private int m_entered;
        }

        private const string kPolicy = SecurityPolicies.ECC_nistP256;
        private static readonly byte[] s_testSecret = [1, 2, 3, 4];
        private static readonly FieldInfo s_nonceField = typeof(ServerSession)
            .GetField("m_userTokenNonce", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo s_keyField = typeof(Nonce)
            .GetField("m_ecdh", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
}
