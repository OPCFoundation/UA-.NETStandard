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
            harness.GetNewEphemeralKey();
            using Nonce original = GetCurrentNonce(harness.Session);
            Assert.That(GetKey(original), Is.Not.Null);
            if (reset)
            {
                harness.Session.SetUserTokenSecurityPolicy(harness.PolicyUri);
            }
            else
            {
                harness.GetNewEphemeralKey();
            }
            Assert.That(GetKey(original), Is.Null);
            harness.GetNewEphemeralKey();
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
            EphemeralKeyType ephemeral = harness.GetNewEphemeralKey();
            using Nonce original = GetCurrentNonce(harness.Session);
            UserNameIdentityToken encrypted = await harness.EncryptPasswordAsync(ephemeral).ConfigureAwait(false);
            using var releaseFirst = new ManualResetEventSlim();
            using var releaseRest = new ManualResetEventSlim();
            using var key = new BlockingKey(GetKey(original)!, borrowers, fail, releaseFirst, releaseRest);
            s_keyField.SetValue(original, key);
            harness.BeforeDecrypt = key.WaitForRelease;
            var operations = new Task<IUserIdentityTokenHandler>[borrowers];
            for (int i = 0; i < operations.Length; i++)
            {
                operations[i] = Task.Run(async () =>
                {
                    var token = new UserNameIdentityToken
                    {
                        PolicyId = "ecc",
                        UserName = "test-user",
                        Password = encrypted.Password,
                        EncryptionAlgorithm = encrypted.EncryptionAlgorithm
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
                    Assert.Fail("Credential validation did not reach the decryption barrier.");
                }
                harness.GetNewEphemeralKey();
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
        /// AS-1: once ActivateSession succeeds the EphemeralKey that decrypted the
        /// identity token shall not be accepted again (OPC 10000-6 6.8.2). The
        /// activation retires it itself and installs the replacement prepared during
        /// validation, so the session is never left without a key (SWEEP-1); the
        /// replacement is what the response hands out.
        /// </summary>
        [Test]
        public async Task SuccessfulActivationRetiresTheEphemeralKeyThatDecryptedTheTokenAsync()
        {
            using var harness = new NonceHarness();
            if (!harness.UsesEcdh)
            {
                Assert.Ignore("Only an ephemeral-key policy encrypts the token with the EphemeralKey.");
            }
            EphemeralKeyType ephemeral = harness.GetNewEphemeralKey();
            using Nonce original = GetCurrentNonce(harness.Session);
            UserNameIdentityToken encrypted = await harness.EncryptPasswordAsync(ephemeral).ConfigureAwait(false);
            var token = new UserNameIdentityToken
            {
                PolicyId = "ecc",
                UserName = "test-user",
                Password = encrypted.Password,
                EncryptionAlgorithm = encrypted.EncryptionAlgorithm
            };

            (IUserIdentityTokenHandler identity, _) = await harness.Session.ValidateBeforeActivateAsync(
                harness.Context, harness.ClientSignature, new ExtensionObject(token), new SignatureData(),
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(s_nonceField.GetValue(harness.Session), Is.SameAs(original),
                    "Validation alone must not retire the key; the activation may still fail.");

                var user = new UserIdentity(identity);
                harness.Session.Activate(
                    harness.Context, identity, user, user, default, Nonce.CreateNonce(32));

                object? current = s_nonceField.GetValue(harness.Session);
                Assert.That(current, Is.Not.Null, "The used key must be replaced, not dropped.");
                Assert.That(current, Is.Not.SameAs(original));
                Assert.That(GetKey(original), Is.Null, "The consumed key must be released.");

                AdditionalParametersType response =
                    SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                        harness.Session, null);
                Assert.That(response.Parameters, Has.Count.EqualTo(1));
                Assert.That(
                    response.Parameters[0].Value.TryGetStructure<EphemeralKeyType>(out EphemeralKeyType? returned),
                    Is.True);
                Assert.That(
                    returned!.PublicKey,
                    Is.EqualTo(((Nonce)current!).Data.ToByteString()),
                    "The response returns the key the activation installed.");
                Assert.That(
                    SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                        harness.Session, null).Parameters.IsEmpty,
                    Is.True,
                    "The installed key is returned once.");
                Assert.That(s_nonceField.GetValue(harness.Session), Is.SameAs(current));
            }
            finally
            {
                CryptoUtils.ZeroMemory(((UserNameIdentityTokenHandler)identity).DecryptedPassword!);
            }
        }

        /// <summary>
        /// K20: a token whose policy has no ephemeral key does not use the
        /// EphemeralKey. Without an ECDHPolicyUri the Server then returns nothing and
        /// retains the previous key (OPC 10000-6 6.8.2).
        /// </summary>
        [Test]
        public async Task ActivationWithTokenThatDidNotUseTheKeyRetainsItAndReturnsNothingAsync()
        {
            using var harness = new NonceHarness(forceRsa: true);
            EphemeralKeyType ephemeral = harness.GetNewEphemeralKey();
            Nonce original = GetCurrentNonce(harness.Session);
            UserNameIdentityToken encrypted = await harness.EncryptPasswordAsync(ephemeral).ConfigureAwait(false);
            var token = new UserNameIdentityToken
            {
                PolicyId = "ecc",
                UserName = "test-user",
                Password = encrypted.Password,
                EncryptionAlgorithm = encrypted.EncryptionAlgorithm
            };

            (IUserIdentityTokenHandler identity, _) = await harness.Session.ValidateBeforeActivateAsync(
                harness.Context, harness.ClientSignature, new ExtensionObject(token), new SignatureData(),
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                var user = new UserIdentity(identity);
                harness.Session.Activate(
                    harness.Context, identity, user, user, default, Nonce.CreateNonce(32));

                Assert.That(s_nonceField.GetValue(harness.Session), Is.SameAs(original));
                Assert.That(GetKey(original), Is.Not.Null);
                AdditionalParametersType response =
                    SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                        harness.Session, null);
                Assert.That(response.Parameters.IsEmpty, Is.True);
                Assert.That(s_nonceField.GetValue(harness.Session), Is.SameAs(original));
                Assert.That(
                    StandardServer.AppendUnsentEphemeralKey(harness.Session, null),
                    Is.Null);
            }
            finally
            {
                CryptoUtils.ZeroMemory(((UserNameIdentityTokenHandler)identity).DecryptedPassword!);
            }
        }

        /// <summary>
        /// SWEEP-1: the key the activation installed reaches the client even when the
        /// (overridable) parameter processing did not return it, and is never added
        /// next to an ECDHKey entry the processing produced.
        /// </summary>
        [Test]
        public async Task UnsentReplacementKeyIsAppendedToTheResponseAsync()
        {
            using var harness = new NonceHarness();
            if (!harness.UsesEcdh)
            {
                Assert.Ignore("Only an ephemeral-key policy encrypts the token with the EphemeralKey.");
            }
            EphemeralKeyType ephemeral = harness.GetNewEphemeralKey();
            UserNameIdentityToken encrypted = await harness.EncryptPasswordAsync(ephemeral).ConfigureAwait(false);
            var token = new UserNameIdentityToken
            {
                PolicyId = "ecc",
                UserName = "test-user",
                Password = encrypted.Password,
                EncryptionAlgorithm = encrypted.EncryptionAlgorithm
            };

            (IUserIdentityTokenHandler identity, _) = await harness.Session.ValidateBeforeActivateAsync(
                harness.Context, harness.ClientSignature, new ExtensionObject(token), new SignatureData(),
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                var user = new UserIdentity(identity);
                harness.Session.Activate(
                    harness.Context, identity, user, user, default, Nonce.CreateNonce(32));
                Nonce current = GetCurrentNonce(harness.Session);

                var existing = new KeyValuePair
                {
                    Key = new QualifiedName("Other"),
                    Value = Variant.From(1)
                };
                AdditionalParametersType? response = StandardServer.AppendUnsentEphemeralKey(
                    harness.Session,
                    new AdditionalParametersType { Parameters = [existing] });

                Assert.That(response, Is.Not.Null);
                Assert.That(response!.Parameters, Has.Count.EqualTo(2));
                Assert.That(
                    response.Parameters[1].Key,
                    Is.EqualTo(QualifiedName.From(AdditionalParameterNames.ECDHKey)));
                Assert.That(
                    response.Parameters[1].Value.TryGetStructure<EphemeralKeyType>(out EphemeralKeyType? returned),
                    Is.True);
                Assert.That(returned!.PublicKey, Is.EqualTo(current.Data.ToByteString()));
                Assert.That(
                    StandardServer.AppendUnsentEphemeralKey(harness.Session, null),
                    Is.Null,
                    "The installed key is returned once.");
            }
            finally
            {
                CryptoUtils.ZeroMemory(((UserNameIdentityTokenHandler)identity).DecryptedPassword!);
            }
        }

        /// <summary>
        /// AS-1: an activation whose identity token did not use the EphemeralKey
        /// leaves it in place for the client.
        /// </summary>
        [Test]
        public void ActivationWithoutEncryptedTokenKeepsTheEphemeralKey()
        {
            using var harness = new NonceHarness();
            harness.GetNewEphemeralKey();
            Nonce original = GetCurrentNonce(harness.Session);
            var identity = new AnonymousIdentityTokenHandler();
            var user = new UserIdentity(identity);

            harness.Session.Activate(
                harness.Context, identity, user, user, default, Nonce.CreateNonce(32));

            Assert.That(s_nonceField.GetValue(harness.Session), Is.SameAs(original));
            Assert.That(GetKey(original), Is.Not.Null);
        }

        /// <summary>
        /// PR review 4166517781: an ECDHPolicyUri that is registered but cannot be
        /// served with the session's server certificate (an ECC policy on an RSA
        /// certificate session) is answered with Bad_SecurityPolicyRejected, and the
        /// working EphemeralKey is kept, in CreateSession and ActivateSession.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnusableEcdhPolicyIsRejectedAndKeepsTheWorkingKey(bool createSession)
        {
            if (SecurityPolicies.Default.GetInfo(kEcdhPolicy) == null)
            {
                Assert.Ignore("The ECC policy is not supported on this platform.");
            }
            using var harness = new NonceHarness(forceRsa: true);
            harness.GetNewEphemeralKey();
            Nonce original = GetCurrentNonce(harness.Session);
            var request = new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHPolicyUri),
                        Value = Variant.From(kEcdhPolicy)
                    }
                ]
            };

            AdditionalParametersType? response = createSession
                ? SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                    harness.Session, request, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                : SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                    harness.Session, request);

            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Parameters, Has.Count.EqualTo(1));
            Assert.That(
                response.Parameters[0].Key,
                Is.EqualTo(QualifiedName.From(AdditionalParameterNames.ECDHKey)));
            Assert.That(response.Parameters[0].Value.TryGetValue(out StatusCode status), Is.True);
            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            Assert.That(GetCurrentNonce(harness.Session), Is.SameAs(original), "The working key must be kept.");
            Assert.That(GetKey(original), Is.Not.Null);
            Assert.That(harness.Session.GetNewEphemeralKey(), Is.Not.Null, "The previous policy must be kept.");
        }

        /// <summary>
        /// PR review 4166517820: when the identity token used the EphemeralKey and the
        /// request names an ECDHPolicyUri the Server rejects, the response carries only
        /// Bad_SecurityPolicyRejected (OPC 10000-6 6.8.2); the replacement the
        /// activation installed stays pending and is returned by the next activation.
        /// </summary>
        [Test]
        public async Task RejectedEcdhPolicyKeepsTheReplacementKeyDeliverableAsync()
        {
            using var harness = new NonceHarness();
            if (!harness.UsesEcdh)
            {
                Assert.Ignore("Only an ephemeral-key policy encrypts the token with the EphemeralKey.");
            }
            EphemeralKeyType ephemeral = harness.GetNewEphemeralKey();
            UserNameIdentityToken encrypted = await harness.EncryptPasswordAsync(ephemeral).ConfigureAwait(false);
            var token = new UserNameIdentityToken
            {
                PolicyId = "ecc",
                UserName = "test-user",
                Password = encrypted.Password,
                EncryptionAlgorithm = encrypted.EncryptionAlgorithm
            };

            (IUserIdentityTokenHandler identity, _) = await harness.Session.ValidateBeforeActivateAsync(
                harness.Context, harness.ClientSignature, new ExtensionObject(token), new SignatureData(),
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                var user = new UserIdentity(identity);
                harness.Session.Activate(
                    harness.Context, identity, user, user, default, Nonce.CreateNonce(32));
                Nonce replacement = GetCurrentNonce(harness.Session);

                var request = new AdditionalParametersType
                {
                    Parameters =
                    [
                        new KeyValuePair
                        {
                            Key = QualifiedName.From(AdditionalParameterNames.ECDHPolicyUri),
                            Value = Variant.From(SecurityPolicies.None)
                        }
                    ]
                };
                AdditionalParametersType? response = StandardServer.AppendUnsentEphemeralKey(
                    harness.Session,
                    SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                        harness.Session, request));

                Assert.That(response, Is.Not.Null);
                Assert.That(response!.Parameters, Has.Count.EqualTo(1));
                Assert.That(response.Parameters[0].Value.TryGetValue(out StatusCode status), Is.True);
                Assert.That(status.Code, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
                Assert.That(GetCurrentNonce(harness.Session), Is.SameAs(replacement));

                AdditionalParametersType next =
                    SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                        harness.Session, null);
                Assert.That(next.Parameters, Has.Count.EqualTo(1));
                Assert.That(
                    next.Parameters[0].Value.TryGetStructure<EphemeralKeyType>(out EphemeralKeyType? returned),
                    Is.True);
                Assert.That(returned!.PublicKey, Is.EqualTo(replacement.Data.ToByteString()));
            }
            finally
            {
                CryptoUtils.ZeroMemory(((UserNameIdentityTokenHandler)identity).DecryptedPassword!);
            }
        }

        [Test]
        public void HarnessConstructionFailureReleasesCertificates()
        {
            Certificate? server = null;
            Certificate? client = null;
            Assert.That(
                () => new NonceHarness(harness =>
                {
                    server = harness.ServerCertificate;
                    client = harness.ClientCertificate;
                    throw new InvalidOperationException("Controlled signature initialization failure.");
                }),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(server, Is.Not.Null);
            Assert.That(client, Is.Not.Null);
            Assert.That(() => server!.RawData, Throws.TypeOf<CryptographicException>());
            Assert.That(() => client!.RawData, Throws.TypeOf<CryptographicException>());
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
        /// Owns a secured session and matching client proof for exercising nonce ownership during credential decryption.
        /// </summary>
        private sealed class NonceHarness : IDisposable
        {
            /// <summary>
            /// Uses ECDH where available and RSA otherwise, without changing the platform's security policy registry.
            /// </summary>
            public NonceHarness(Action<NonceHarness>? beforeSignature = null, bool forceRsa = false)
            {
                UsesEcdh = !forceRsa && SecurityPolicies.Default.GetInfo(kEcdhPolicy) != null;
                PolicyUri = UsesEcdh ? kEcdhPolicy : SecurityPolicies.Basic256Sha256;
                SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(PolicyUri)
                    ?? throw new InvalidOperationException("The nonce fixture requires a supported security policy.");
                try
                {
                    ServerCertificate = CreateCertificate("CN=Nonce Lifetime Server");
                    ClientCertificate = CreateCertificate("CN=Nonce Lifetime Client");
                    ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                    MessageContext = ServiceMessageContext.CreateEmpty(telemetry);
                    var endpoint = new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840/nonce-lifetime",
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = PolicyUri,
                        ServerCertificate = ServerCertificate.RawData.ToByteString(),
                        UserIdentityTokens =
                        [
                            new UserTokenPolicy
                            {
                                PolicyId = "ecc",
                                TokenType = UserTokenType.UserName,
                                SecurityPolicyUri = PolicyUri
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
                    var policies = new Mock<ISecurityPolicyRegistry>();
                    policies.Setup(value => value.GetInfo(It.IsAny<string>()))
                        .Returns((string uri) => SecurityPolicies.Default.GetInfo(uri));
                    policies.Setup(value => value.VerifySignatureData(
                            It.IsAny<SignatureData>(), It.IsAny<string>(), It.IsAny<Certificate>(), It.IsAny<byte[]>()))
                        .Returns((SignatureData signature, string uri, Certificate certificate, byte[] data) =>
                            SecurityPolicies.Default.VerifySignatureData(signature, uri, certificate, data));
                    policies.Setup(value => value.DecryptAsync(
                            It.IsAny<Certificate>(), It.IsAny<string>(), It.IsAny<EncryptedData>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((Certificate certificate, string uri, EncryptedData data, CancellationToken ct) =>
                        {
                            BeforeDecrypt?.Invoke();
                            return SecurityPolicies.Default.DecryptAsync(certificate, uri, data, ct);
                        });
                    server.As<ISecurityPolicyRegistryProvider>().SetupGet(value => value.SecurityPolicyRegistry)
                        .Returns(policies.Object);
                    ServerNonce = Nonce.CreateNonce(32);
                    using var clientNonce = Nonce.CreateNonce(32);
                    Session = new ServerSession(
                        Context, server.Object, ServerCertificate, new NodeId(100), clientNonce.Data.ToByteString(),
                        ServerNonce, "NonceLifetime", new ApplicationDescription { ApplicationUri = "urn:nonce-lifetime" },
                        endpoint.EndpointUrl, ClientCertificate.AddRef(), [], 60_000, 10, 10);
                    Session.SetUserTokenSecurityPolicy(PolicyUri);
                    beforeSignature?.Invoke(this);
                    ClientSignature = SecurityPolicies.Default.CreateSignatureData(
                        policy, ClientCertificate, policy.GetClientSignatureData(
                            channel.ChannelThumbprint, ServerNonce.Data, ServerCertificate.RawData,
                            channel.ServerChannelCertificate, channel.ClientChannelCertificate, clientNonce.Data));
                }
                catch
                {
                    Dispose();
                    throw;
                }
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
            /// Gets the activation context bound to the secured channel.
            /// </summary>
            public OperationContext Context { get; }

            /// <summary>
            /// Gets the session whose user-token nonce can be replaced during decryption.
            /// </summary>
            public ServerSession Session { get; }

            /// <summary>
            /// Gets whether this platform supports the fixture's raw-ECDH policy.
            /// </summary>
            public bool UsesEcdh { get; }

            /// <summary>
            /// Gets the supported policy used for the channel and credential encryption.
            /// </summary>
            public string PolicyUri { get; }

            /// <summary>
            /// Gets or sets the barrier used before the real RSA credential decryption.
            /// </summary>
            public Action? BeforeDecrypt { get; set; }

            /// <summary>
            /// Creates a session nonce and ensures it owns a disposable key for the lifetime assertions.
            /// </summary>
            /// <exception cref="InvalidOperationException">The session did not create a nonce.</exception>
            public EphemeralKeyType GetNewEphemeralKey()
            {
                EphemeralKeyType key = Session.GetNewEphemeralKey()
                    ?? throw new InvalidOperationException("The fixture did not create a session nonce.");
                if (!UsesEcdh)
                {
                    // RSA credential decryption still borrows the session nonce. Attach a real disposable key
                    // to observe its ownership without enabling unsupported raw-ECDH agreement.
                    s_keyField.SetValue(
                        GetCurrentNonce(Session),
                        ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));
                }
                return key;
            }

            /// <summary>
            /// Encrypts the expected plaintext using the actual supported credential-encryption implementation.
            /// </summary>
            public async Task<UserNameIdentityToken> EncryptPasswordAsync(EphemeralKeyType ephemeral)
            {
                using Nonce? publicNonce = UsesEcdh
                    ? Nonce.CreateNonce(PolicyUri, ephemeral.PublicKey.ToArray())
                    : null;
                using var issuerCertificates = new CertificateCollection();
                var token = new UserNameIdentityTokenHandler("test-user", s_testSecret);
                try
                {
                    await token.EncryptAsync(
                        ServerCertificate,
                        ServerNonce.Data!,
                        PolicyUri,
                        MessageContext,
                        receiverEphemeralKey: publicNonce,
                        senderCertificate: ClientCertificate,
                        senderIssuerCertificates: issuerCertificates,
                        doNotEncodeSenderCertificate: true).ConfigureAwait(false);
                    return (UserNameIdentityToken)token.Token;
                }
                finally
                {
                    CryptoUtils.ZeroMemory(token.DecryptedPassword!);
                }
            }

            /// <summary>
            /// Releases the session, operation context, session nonce, and client and server certificates.
            /// </summary>
            public void Dispose()
            {
                Session?.Dispose();
                Context?.Dispose();
                ServerNonce?.Dispose();
                ClientCertificate?.Dispose();
                ServerCertificate?.Dispose();
            }

            private Certificate CreateCertificate(string subjectName)
            {
                return UsesEcdh
                    ? CertificateBuilder.Create(subjectName)
                        .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa()
                    : CertificateBuilder.Create(subjectName).CreateForRSA();
            }
        }

        /// <summary>
        /// Pauses credential decryption and records when the nonce's underlying key is disposed.
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
            /// Holds each key borrower at its barrier and optionally injects an agreement failure after release.
            /// </summary>
            /// <exception cref="TimeoutException"></exception>
            /// <exception cref="CryptographicException"></exception>
            public void WaitForRelease()
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
            /// Counts borrowers that have reached key agreement before a disposal request.
            /// </summary>
            private int m_entered;
        }

        /// <summary>
        /// Selects the ECC security policy whose user-token nonce carries the ephemeral ECDH key.
        /// </summary>
        private const string kEcdhPolicy = SecurityPolicies.ECC_nistP256;

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
