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

// CA2000: test helpers intentionally transfer disposable ownership or use short-lived throwaway values.
#pragma warning disable CA2000
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.KeyCredential
{
    /// <summary>
    /// Verifies credential secret decryption, policy admission, cancellation, and certificate-registry consistency.
    /// </summary>
    [TestFixture]
    public sealed class KeyCredentialSecretRegressionTests
    {
        /// <summary>
        /// Verifies that supported encrypted updates store usable plaintext without modifying the caller's envelope.
        /// </summary>
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public async Task EncryptedUpdateStoresUsablePlaintextWithoutChangingTheCallerEnvelopeAsync(string policy)
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            byte[] expected = [11, 22, 33, 44, 55];
            byte[] encrypted = EncryptedSecret.CreateForRsa(harness.MessageContext, policy, harness.Certificate)
                .Encrypt(expected, []);
            byte[] original = encrypted.AsSpan().ToArray();
            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(
                encrypted, harness.Certificate.Thumbprint, policy).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.StoreWrites, Is.EqualTo(1));
            Assert.That(harness.Stored.Secret, Is.EqualTo(expected));
            Assert.That(encrypted, Is.EqualTo(original));
            Assert.That(harness.Node.CredentialId.Value, Is.EqualTo("credential-1"));
        }

        [TestCase(32)]
        [TestCase(64)]
        [TestCase(128)]
        public async Task PeerEncryptedUpdateAcceptsAnUncheckedNonceAsync(int nonceLength)
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            byte[] expected = [11, 22, 33, 44, 55];
            byte[] nonce = Nonce.CreateRandomNonceData(nonceLength);
            byte[] envelope = CreatePeerRsaEnvelope(harness, expected, nonce);
            byte[] original = envelope.AsSpan().ToArray();

            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(
                envelope, harness.Certificate.Thumbprint, SecurityPolicies.Aes256_Sha256_RsaPss)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.StoreWrites, Is.EqualTo(1));
            Assert.That(harness.Stored.Secret, Is.EqualTo(expected));
            Assert.That(envelope, Is.EqualTo(original));
        }

        /// <summary>
        /// Verifies that invalid certificates, policies, or encrypted envelopes cannot write a credential.
        /// </summary>
        [TestCase("wrong-certificate")]
        [TestCase("wrong-key")]
        [TestCase("wrong-policy")]
        [TestCase("corrupt-signature")]
        [TestCase("truncated")]
        [TestCase("unknown-policy")]
        [TestCase("none-policy")]
        [TestCase("ecc-policy")]
        [TestCase("rsa-dh-policy")]
        [TestCase("unexpected-clear-thumbprint")]
        public async Task InvalidEncryptedUpdateNeverWritesTheStoreAsync(string error)
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            const string policy = SecurityPolicies.Basic256Sha256;
            string requestedPolicy = policy;
            string thumbprint = harness.Certificate.Thumbprint;
            byte[] bytes = EncryptedSecret.CreateForRsa(harness.MessageContext, policy, harness.Certificate)
                .Encrypt([1, 2, 3, 4], []);
            StatusCode expected = StatusCodes.BadInvalidArgument;
            switch (error)
            {
                case "wrong-certificate":
                    thumbprint = new string('0', 40);
                    expected = StatusCodes.BadCertificateInvalid;
                    break;
                case "wrong-key":
                    using (Certificate other = DefaultCertificateFactory.Instance.CreateCertificate("CN=Other").CreateForRSA())
                    {
                        bytes = EncryptedSecret.CreateForRsa(harness.MessageContext, policy, other).Encrypt([1, 2, 3], []);
                    }
                    expected = StatusCodes.BadCertificateInvalid;
                    break;
                case "wrong-policy":
                    requestedPolicy = SecurityPolicies.Aes256_Sha256_RsaPss;
                    expected = StatusCodes.BadSecurityPolicyRejected;
                    break;
                case "corrupt-signature":
                    bytes[^1] ^= 1;
                    break;
                case "truncated":
                    bytes = bytes.AsSpan(0, 10).ToArray();
                    break;
                case "unknown-policy":
                    requestedPolicy = "urn:unsupported-policy";
                    expected = StatusCodes.BadSecurityPolicyRejected;
                    break;
                case "none-policy":
                    requestedPolicy = SecurityPolicies.None;
                    expected = StatusCodes.BadSecurityPolicyRejected;
                    break;
                case "ecc-policy":
                    requestedPolicy = SecurityPolicies.ECC_nistP256;
                    expected = StatusCodes.BadSecurityPolicyRejected;
                    break;
                case "rsa-dh-policy":
                    requestedPolicy = "http://opcfoundation.org/UA/SecurityPolicy#RSA_DH";
                    expected = StatusCodes.BadSecurityPolicyRejected;
                    break;
                case "unexpected-clear-thumbprint":
                    requestedPolicy = string.Empty;
                    break;
            }
            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(bytes, thumbprint, requestedPolicy)
                .ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(expected));
            Assert.That(harness.StoreWrites, Is.Zero);
            Assert.That(harness.Node.CredentialId.Value, Is.Null.Or.Empty);
        }

        /// <summary>
        /// Verifies that a plaintext update without encryption metadata stores an independent copy of the secret.
        /// </summary>
        [Test]
        public async Task ClearSecretRequiresNoPolicyAndRetainsTheSameBytesAsync()
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            byte[] secret = [7, 8, 9];
            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(secret, string.Empty, string.Empty)
                .ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Stored.Secret, Is.EqualTo(secret));
            Assert.That(harness.Stored.Secret, Is.Not.SameAs(secret));
        }

        /// <summary>
        /// Verifies that a cancelled credential update propagates cancellation without writing secret data.
        /// </summary>
        [Test]
        public async Task CancelledCredentialUpdateCannotPersistASecretAsync()
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(() => harness.UpdateAsync([7, 8, 9], string.Empty, string.Empty, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(harness.StoreWrites, Is.Zero);
        }

        /// <summary>
        /// Verifies that direct and dependency-injected subjects advertise the same registry certificate used to
        /// decrypt.
        /// </summary>
        [Test]
        public async Task EncryptingKeyUsesTheSameConfiguredRegistryAsDecryptionAsync(
            [Values(false, true)] bool dependencyInjection)
        {
            using var harness = new Harness(dependencyInjection);
            await harness.BindAsync().ConfigureAwait(false);
            GetEncryptingKeyMethodStateResult key = await harness.GetKeyAsync(string.Empty).ConfigureAwait(false);
            Assert.That(key.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(key.RevisedSecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
            using Certificate certificate = Certificate.FromRawData(key.PublicKey);
            Assert.That(certificate.Thumbprint, Is.EqualTo(harness.Certificate.Thumbprint));
            Assert.That(certificate.HasPrivateKey, Is.False);
            byte[] encrypted = EncryptedSecret.CreateForRsa(
                harness.MessageContext, key.RevisedSecurityPolicyUri, certificate).Encrypt([41, 42], []);
            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(
                encrypted, certificate.Thumbprint, key.RevisedSecurityPolicyUri).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Stored.Secret, Is.EqualTo(new byte[] { 41, 42 }));
        }

        /// <summary>
        /// Verifies that policy restrictions apply both to encrypting-key discovery and credential updates.
        /// </summary>
        [Test]
        public async Task DisallowedPolicyIsNeitherAdvertisedNorAcceptedAsync()
        {
            using var harness = new Harness(options: new KeyCredentialPushOptions
            {
                AllowedSecurityPolicyUris = [SecurityPolicies.Aes256_Sha256_RsaPss]
            });
            await harness.BindAsync().ConfigureAwait(false);
            GetEncryptingKeyMethodStateResult key = await harness.GetKeyAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            Assert.That(key.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            byte[] encrypted = EncryptedSecret.CreateForRsa(
                harness.MessageContext, SecurityPolicies.Basic256Sha256, harness.Certificate).Encrypt([7, 8, 9], []);
            KeyCredentialUpdateMethodStateResult result = await harness.UpdateAsync(
                encrypted, harness.Certificate.Thumbprint, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            Assert.That(harness.StoreWrites, Is.Zero);
        }

        /// <summary>
        /// Verifies that unsupported encryption policies are rejected without exposing an encrypting key.
        /// </summary>
        [TestCase(SecurityPolicies.None)]
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase("urn:unknown")]
        public async Task UnsupportedEncryptingKeyPolicyIsRejectedAsync(string policy)
        {
            using var harness = new Harness();
            await harness.BindAsync().ConfigureAwait(false);
            GetEncryptingKeyMethodStateResult key = await harness.GetKeyAsync(policy).ConfigureAwait(false);
            Assert.That(key.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            Assert.That(key.PublicKey.IsEmpty, Is.True);
        }

        private static byte[] CreatePeerRsaEnvelope(Harness harness, byte[] secret, byte[] nonce)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.GenerateKey();
            aes.GenerateIV();
            byte[] signingKey = Nonce.CreateRandomNonceData(32);
            byte[] encryptionKey = aes.Key;
            byte[] iv = aes.IV;
            byte[] keyData = null;
            byte[] payload = null;
            try
            {
                using (var keys = new BinaryEncoder(harness.MessageContext))
                {
                    keys.WriteByteString(null, signingKey);
                    keys.WriteByteString(null, encryptionKey);
                    keys.WriteByteString(null, iv);
                    keyData = keys.CloseAndReturnBuffer();
                }
                using RSA rsa = harness.Certificate.GetRSAPublicKey();
                byte[] encryptedKeys = rsa.Encrypt(keyData, RSAEncryptionPadding.OaepSHA256);

                using (var plaintext = new BinaryEncoder(harness.MessageContext))
                {
                    plaintext.WriteByteString(null, nonce);
                    plaintext.WriteByteString(null, secret);
                    int padding = (16 - ((plaintext.Position + 2) % 16)) % 16;
                    for (int i = 0; i < padding; i++)
                    {
                        plaintext.WriteByte(null, (byte)padding);
                    }
                    plaintext.WriteUInt16(null, (ushort)padding);
                    payload = plaintext.CloseAndReturnBuffer();
                }
                using ICryptoTransform cipher = aes.CreateEncryptor();
                byte[] encryptedPayload = cipher.TransformFinalBlock(payload, 0, payload.Length);

                using var encoder = new BinaryEncoder(harness.MessageContext);
                encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
                int lengthPosition = encoder.Position;
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, SecurityPolicies.Aes256_Sha256_RsaPss);
                encoder.WriteByteString(null, Utils.FromHexString(harness.Certificate.Thumbprint));
                encoder.WriteDateTime(null, DateTime.UtcNow);
                encoder.WriteUInt16(null, checked((ushort)encryptedKeys.Length));
                foreach (byte value in encryptedKeys)
                {
                    encoder.WriteByte(null, value);
                }
                foreach (byte value in encryptedPayload)
                {
                    encoder.WriteByte(null, value);
                }
                int signaturePosition = encoder.Position;
                for (int i = 0; i < 32; i++)
                {
                    encoder.WriteByte(null, 0);
                }
                byte[] envelope = encoder.CloseAndReturnBuffer();
                BinaryPrimitives.WriteUInt32LittleEndian(
                    envelope.AsSpan(lengthPosition, 4), checked((uint)(envelope.Length - lengthPosition - 4)));
                using var hmac = new HMACSHA256(signingKey);
                byte[] signature = hmac.ComputeHash(envelope, 0, signaturePosition);
                signature.CopyTo(envelope, signaturePosition);
                return envelope;
            }
            finally
            {
                CryptoUtils.ZeroMemory(signingKey);
                CryptoUtils.ZeroMemory(encryptionKey);
                CryptoUtils.ZeroMemory(iv);
                if (keyData != null)
                {
                    CryptoUtils.ZeroMemory(keyData);
                }
                if (payload != null)
                {
                    CryptoUtils.ZeroMemory(payload);
                }
            }
        }

        /// <summary>
        /// Supplies an authorized credential node, observable store, and certificate registry for secret-update tests.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            /// <summary>
            /// Creates a credential subject directly or through dependency injection with a shared RSA certificate.
            /// </summary>
            public Harness(bool dependencyInjection = false, KeyCredentialPushOptions options = null)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                Certificate = DefaultCertificateFactory.Instance.CreateCertificate("CN=Credential Server").CreateForRSA();
                MessageContext = ServiceMessageContext.Create(telemetry);
                var registry = new Mock<ICertificateRegistry>();
                registry.Setup(value => value.SnapshotApplicationCertificates()).Returns(() =>
                {
                    using CertificateEntry entry = CreateEntry();
                    return new CertificateEntryCollection([entry]);
                });
                registry.Setup(value => value.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                    .Returns(CreateEntry);
                var store = new Mock<IKeyCredentialStore>();
                store.Setup(value => value.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
                store.Setup(value => value.UpdateAsync(
                        It.IsAny<string>(), It.IsAny<Server.KeyCredential>(), It.IsAny<CancellationToken>()))
                    .Callback<string, Server.KeyCredential, CancellationToken>((_, value, _) =>
                    {
                        Stored = value;
                        StoreWrites++;
                    })
                    .Returns(Task.CompletedTask);
                if (dependencyInjection)
                {
                    var services = new ServiceCollection();
                    services.AddSingleton(registry.Object);
                    services.AddSingleton(store.Object);
                    services.AddOpcUa().AddServer(_ => { }).WithKeyCredentialPush();
                    m_services = services.BuildServiceProvider();
                    m_subject = m_services.GetRequiredService<KeyCredentialPushSubject>();
                }
                else
                {
                    m_subject = new KeyCredentialPushSubject(store.Object, options, registry.Object);
                }
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_SecurityAdmin]);
                var channel = new SecureChannelContext("keycredential-regression",
                    new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt }, RequestEncoding.Binary);
                var operation = new OperationContext(
                    new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
                Context = new SessionSystemContext(operation, telemetry)
                {
                    NamespaceUris = new NamespaceTable(),
                    ServerUris = new StringTable()
                };
            }

            /// <summary>
            /// Gets the registry certificate whose private key decrypts accepted credential envelopes.
            /// </summary>
            public Certificate Certificate { get; }

            /// <summary>
            /// Gets the encoding context used to create encrypted secret envelopes.
            /// </summary>
            public IServiceMessageContext MessageContext { get; }

            /// <summary>
            /// Gets the encrypted session context with the security-administrator role.
            /// </summary>
            public SessionSystemContext Context { get; }

            /// <summary>
            /// Gets the credential node created after the subject is bound.
            /// </summary>
            public KeyCredentialConfigurationState Node { get; private set; }

            /// <summary>
            /// Gets the last credential passed to the backing store.
            /// </summary>
            public Server.KeyCredential Stored { get; private set; }

            /// <summary>
            /// Gets the number of credential updates accepted by the backing store.
            /// </summary>
            public int StoreWrites { get; private set; }

            /// <summary>
            /// Binds the subject to its standard folder and creates the credential node used by the tests.
            /// </summary>
            public async Task BindAsync()
            {
                var folder = new KeyCredentialConfigurationFolderState(null)
                {
                    NodeId = KeyCredentialPushSubject.StandardConfigurationFolderNodeId
                };
                await m_subject.BindAsync(folder, Context).ConfigureAwait(false);
                CreateCredentialMethodStateResult created = await folder.CreateCredential.OnCallAsync(
                    Context, folder.CreateCredential, folder.NodeId, "Service", "urn:service",
                    KeyCredentialBridgeOptions.DefaultProfileUri, [], CancellationToken.None).ConfigureAwait(false);
                var children = new System.Collections.Generic.List<BaseInstanceState>();
                folder.GetChildren(Context, children);
                Node = children.OfType<KeyCredentialConfigurationState>().Single(value => value.NodeId == created.CredentialNodeId);
            }

            /// <summary>
            /// Invokes the credential update method with the supplied secret and encryption metadata.
            /// </summary>
            public async Task<KeyCredentialUpdateMethodStateResult> UpdateAsync(
                byte[] secret, string thumbprint, string policy, CancellationToken ct = default)
            {
                return await Node.UpdateCredential.OnCallAsync(
                    Context, Node.UpdateCredential, Node.NodeId, "credential-1",
                    new ByteString(secret), thumbprint, policy, ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Releases dependency-injected services and the registry certificate.
            /// </summary>
            public void Dispose()
            {
                m_services?.Dispose();
                Certificate.Dispose();
            }

            /// <summary>
            /// Requests the credential's public encrypting certificate for the selected security policy.
            /// </summary>
            public async Task<GetEncryptingKeyMethodStateResult> GetKeyAsync(string policy)
            {
                return await Node.GetEncryptingKey.OnCallAsync(
                    Context, Node.GetEncryptingKey, Node.NodeId, "credential-1", policy, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Acquires an application-certificate entry for the harness certificate and an empty issuer chain.
            /// </summary>
            private CertificateEntry CreateEntry()
            {
                using var chain = new CertificateCollection();
                return new CertificateEntry(Certificate, chain, ObjectTypeIds.RsaSha256ApplicationCertificateType);
            }

            /// <summary>
            /// Coordinates the credential push and its owned secret material.
            /// </summary>
            private readonly KeyCredentialPushSubject m_subject;

            /// <summary>
            /// Retains the injectable credential services until the harness is disposed.
            /// </summary>
            private readonly ServiceProvider m_services;
        }
    }
}
