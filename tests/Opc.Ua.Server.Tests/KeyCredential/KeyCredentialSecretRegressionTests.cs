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
using System.Linq;
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
    [TestFixture]
    public sealed class KeyCredentialSecretRegressionTests
    {
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

        private sealed class Harness : IDisposable
        {
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

            public Certificate Certificate { get; }
            public IServiceMessageContext MessageContext { get; }
            public SessionSystemContext Context { get; }
            public KeyCredentialConfigurationState Node { get; private set; }
            public Server.KeyCredential Stored { get; private set; }
            public int StoreWrites { get; private set; }

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

            public async Task<KeyCredentialUpdateMethodStateResult> UpdateAsync(
                byte[] secret, string thumbprint, string policy, CancellationToken ct = default)
            {
                return await Node.UpdateCredential.OnCallAsync(
                    Context, Node.UpdateCredential, Node.NodeId, "credential-1",
                    new ByteString(secret), thumbprint, policy, ct).ConfigureAwait(false);
            }

            public void Dispose()
            {
                m_services?.Dispose();
                Certificate.Dispose();
            }

            public async Task<GetEncryptingKeyMethodStateResult> GetKeyAsync(string policy)
            {
                return await Node.GetEncryptingKey.OnCallAsync(
                    Context, Node.GetEncryptingKey, Node.NodeId, "credential-1", policy, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            private CertificateEntry CreateEntry()
            {
                using var chain = new CertificateCollection();
                return new CertificateEntry(Certificate, chain, ObjectTypeIds.RsaSha256ApplicationCertificateType);
            }

            private readonly KeyCredentialPushSubject m_subject;
            private readonly ServiceProvider m_services;
        }
    }
}
