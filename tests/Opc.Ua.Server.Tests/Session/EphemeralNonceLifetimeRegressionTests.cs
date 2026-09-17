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

// CA2000: the test harness owns or immediately tears down the disposable cryptographic helpers.
#pragma warning disable CA2000
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
    /// <summary>
    /// Verifies retirement and borrowed-use lifetimes of ephemeral keys used for session credential decryption.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class EphemeralNonceLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that replacing a nonce, resetting its policy, or disposing the session releases unborrowed keys.
        /// </summary>
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

        /// <summary>
        /// Verifies that a retired nonce retains its key until every in-flight decryption succeeds or fails.
        /// </summary>
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

        /// <summary>
        /// Reads the session's current user-token nonce for direct key-lifetime assertions.
        /// </summary>
        private static Nonce GetCurrentNonce(ServerSession session)
        {
            return (Nonce)s_nonceField.GetValue(session)!;
        }

        /// <summary>
        /// Reads the nonce's ECDH key to distinguish a retained key from a disposed one.
        /// </summary>
        private static ECDiffieHellman? GetKey(Nonce nonce)
        {
            return (ECDiffieHellman?)s_keyField.GetValue(nonce);
        }

        /// <summary>
        /// Owns a secured session and matching client proof for exercising ephemeral credential decryption.
        /// </summary>
        private sealed class NonceHarness : IDisposable
        {
            /// <summary>
            /// Creates elliptic-curve client and server certificates and a session with an ECC username policy.
            /// </summary>
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

            /// <summary>
            /// Gets the server certificate used to encrypt credentials and validate the client proof.
            /// </summary>
            public Certificate ServerCertificate { get; }

            /// <summary>
            /// Gets the client certificate used for credential-envelope signing and session activation proof.
            /// </summary>
            public Certificate ClientCertificate { get; }

            /// <summary>
            /// Gets the message context used to encode and decrypt credential envelopes.
            /// </summary>
            public IServiceMessageContext MessageContext { get; }

            /// <summary>
            /// Gets the server session nonce included in activation signatures and encrypted credentials.
            /// </summary>
            public Nonce ServerNonce { get; }

            /// <summary>
            /// Gets the valid client application signature supplied to activation validation.
            /// </summary>
            public SignatureData ClientSignature { get; }

            /// <summary>
            /// Gets the activation context bound to the secured ECC channel.
            /// </summary>
            public OperationContext Context { get; }

            /// <summary>
            /// Gets the session whose user-token nonce can be replaced during decryption.
            /// </summary>
            public ServerSession Session { get; }

            /// <summary>
            /// Releases the session, operation context, session nonce, and client and server certificates.
            /// </summary>
            public void Dispose()
            {
                Session.Dispose();
                Context.Dispose();
                ServerNonce.Dispose();
                ClientCertificate.Dispose();
                ServerCertificate.Dispose();
            }
        }

        /// <summary>
        /// Pauses ECDH borrowers at key agreement and records when the underlying key is disposed.
        /// </summary>
        private sealed class BlockingKey(
            ECDiffieHellman inner,
            int borrowers,
            bool fail,
            ManualResetEventSlim releaseFirst,
            ManualResetEventSlim releaseRest) : ECDiffieHellman
        {
            /// <inheritdoc/>
            public override ECDiffieHellmanPublicKey PublicKey => inner.PublicKey;

            /// <summary>
            /// Gets the signal raised once every expected borrower has entered key agreement.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets whether the wrapper has released its underlying key.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <summary>
            /// Waits for the borrower's release gate before deriving key material with the retained key.
            /// </summary>
            public override byte[] DeriveKeyMaterial(ECDiffieHellmanPublicKey otherPartyPublicKey)
            {
                WaitForRelease();
                return inner.DeriveKeyMaterial(otherPartyPublicKey);
            }

#if NET8_0_OR_GREATER
            /// <summary>
            /// Waits for the borrower's release gate before deriving the raw shared secret with the retained key.
            /// </summary>
            public override byte[] DeriveRawSecretAgreement(ECDiffieHellmanPublicKey otherPartyPublicKey)
            {
                WaitForRelease();
                return inner.DeriveRawSecretAgreement(otherPartyPublicKey);
            }
#endif

            /// <summary>
            /// Records disposal and releases the wrapped agreement key exactly once.
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing && !Disposed)
                {
                    Disposed = true;
                    inner.Dispose();
                }
                base.Dispose(disposing);
            }

            /// <summary>
            /// Holds each key borrower at its barrier and optionally injects an agreement failure after release.
            /// </summary>
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

            /// <summary>
            /// Counts borrowers that have reached key agreement before a disposal request.
            /// </summary>
            private int m_entered;
        }

        /// <summary>
        /// Selects the ECC security policy whose user-token nonce carries the ephemeral ECDH key.
        /// </summary>
        private const string kPolicy = SecurityPolicies.ECC_nistP256;

        /// <summary>
        /// Supplies the plaintext expected from successful borrowed-key decryption.
        /// </summary>
        private static readonly byte[] s_testSecret = [1, 2, 3, 4];

        /// <summary>
        /// Locates the session's currently published user-token nonce.
        /// </summary>
        private static readonly FieldInfo s_nonceField = typeof(ServerSession)
            .GetField("m_userTokenNonce", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>
        /// Locates the nonce key for replacement with a blocking wrapper and disposal checks.
        /// </summary>
        private static readonly FieldInfo s_keyField = typeof(Nonce)
            .GetField("m_ecdh", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
}
