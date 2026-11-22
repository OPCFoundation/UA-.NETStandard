// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    public sealed class NoneEndpointCertificateTests
    {
        [Test]
        [Combinatorial]
        public async Task NoneEndpointUsesRsaForEncryptedTokensWithEccFirstAsync(
            [Values(UserTokenType.UserName, UserTokenType.IssuedToken)] UserTokenType type,
            [Values] bool sendChain,
            [Values(
                ObjectTypes.ApplicationCertificateType,
                ObjectTypes.RsaMinApplicationCertificateType,
                ObjectTypes.RsaSha256ApplicationCertificateType)] uint certificateType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            await registry.UpdateAsync(new SecurityConfiguration { SendCertificateChain = sendChain })
                .ConfigureAwait(false);
            using Certificate ecc = CertificateBuilder.Create("CN=Ecc First")
                .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
            using Certificate rsa = CertificateBuilder.Create("CN=Rsa Token Encryption").CreateForRSA();
            await registry.UpdateApplicationCertificateAsync(ObjectTypeIds.EccNistP256ApplicationCertificateType, ecc)
                .ConfigureAwait(false);
            await registry.UpdateApplicationCertificateAsync(
                new NodeId(certificateType), rsa)
                .ConfigureAwait(false);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, null!);
            EndpointDescription endpoint = CreateEndpoint();
            endpoint.UserIdentityTokens = server.GetUserTokenPolicies(configuration, endpoint);

            ServerBase.SetServerCertificateInEndpointDescription(endpoint, registry);

            Assert.That(endpoint.UserIdentityTokens.Count, Is.EqualTo(3));
            Assert.That(endpoint.UserIdentityTokens.ToArray()!.Single(policy => policy.TokenType == type)
                .SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            using CertificateCollection advertised = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate.ToArray(), telemetry);
            Assert.That(advertised[0].RawData, Is.EqualTo(rsa.RawData));
            using RSA publicKey = advertised[0].GetRSAPublicKey()!;
            using RSA privateKey = rsa.GetRSAPrivateKey()!;
            byte[] plaintext = [1, 2, 3, 4];
            byte[] encrypted = publicKey!.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);
            Assert.That(privateKey!.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256), Is.EqualTo(plaintext));
        }

        [TestCase(UserTokenType.UserName, ObjectTypes.RsaSha256ApplicationCertificateType, (ushort)1024)]
        [TestCase(UserTokenType.IssuedToken, ObjectTypes.RsaSha256ApplicationCertificateType, (ushort)1024)]
        [TestCase(UserTokenType.UserName, ObjectTypes.EccNistP256ApplicationCertificateType, (ushort)2048)]
        [TestCase(UserTokenType.IssuedToken, ObjectTypes.EccNistP256ApplicationCertificateType, (ushort)2048)]
        public async Task NoneEndpointRejectsIncompatibleRsaTypeOrKeySizeAsync(
            UserTokenType type, uint certificateType, ushort keySize)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using Certificate certificate = CertificateBuilder.Create("CN=Incompatible Token Certificate")
                .SetRSAKeySize(keySize).CreateForRSA();
            await registry.UpdateApplicationCertificateAsync(new NodeId(certificateType), certificate)
                .ConfigureAwait(false);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, null!);

            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, CreateEndpoint());

            Assert.That(policies.ToArray()!.Select(policy => policy.TokenType),
                Is.EquivalentTo([UserTokenType.Anonymous, UserTokenType.Certificate]));
            Assert.That(configuration.ServerConfiguration!.UserTokenPolicies.Count, Is.EqualTo(3));
        }

        [TestCase(UserTokenType.UserName, null, false)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.Basic256Sha256, false)]
        [TestCase(UserTokenType.UserName, SecurityPolicies.ECC_nistP256, false)]
        [TestCase(UserTokenType.UserName, SecurityPolicies.ECC_nistP256, true)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.RSA_DH_AesGcm, false)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.RSA_DH_AesGcm, true)]
        public async Task NoneEndpointOmitsEncryptedTokensWithoutCompatibleRsaPolicyAsync(
            UserTokenType type, string? policyUri, bool hasRsa)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using Certificate ecc = CertificateBuilder.Create("CN=Ecc Only")
                .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
            await registry.UpdateApplicationCertificateAsync(ObjectTypeIds.EccNistP256ApplicationCertificateType, ecc)
                .ConfigureAwait(false);
            if (hasRsa)
            {
                using Certificate rsa = CertificateBuilder.Create("CN=Rsa Available").CreateForRSA();
                await registry.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, rsa)
                    .ConfigureAwait(false);
            }
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, policyUri!);
            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, CreateEndpoint());

            Assert.That(policies.ToArray()!.Select(policy => policy.TokenType),
                Is.EquivalentTo([UserTokenType.Anonymous, UserTokenType.Certificate]));
            Assert.That(configuration.ServerConfiguration!.UserTokenPolicies.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task NoneEndpointCertificateSatisfiesAllAdvertisedEncryptionPoliciesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using Certificate minimum = CertificateBuilder.Create("CN=Minimum RSA First")
                .SetRSAKeySize(1024).CreateForRSA();
            using Certificate sha256 = CertificateBuilder.Create("CN=SHA256 RSA Second").CreateForRSA();
            await registry.UpdateApplicationCertificateAsync(ObjectTypeIds.RsaMinApplicationCertificateType, minimum)
                .ConfigureAwait(false);
            await registry.UpdateApplicationCertificateAsync(ObjectTypeIds.RsaSha256ApplicationCertificateType, sha256)
                .ConfigureAwait(false);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(
                registry, UserTokenType.UserName, SecurityPolicies.Basic128Rsa15);
            configuration.ServerConfiguration!.UserTokenPolicies += new UserTokenPolicy(UserTokenType.IssuedToken)
            {
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            };
            EndpointDescription endpoint = CreateEndpoint();
            endpoint.UserIdentityTokens = server.GetUserTokenPolicies(configuration, endpoint);

            ServerBase.SetServerCertificateInEndpointDescription(endpoint, registry);

            Assert.That(endpoint.UserIdentityTokens.Count, Is.EqualTo(4));
            Assert.That(endpoint.ServerCertificate, Is.EqualTo(sha256.RawData.ToByteString()));
        }

        [Test]
        public void ExplicitUnencryptedTokenPolicyOnNoneIsPreserved()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(
                registry, UserTokenType.UserName, SecurityPolicies.None);

            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, CreateEndpoint());

            Assert.That(policies.ToArray()!.Single(policy => policy.TokenType == UserTokenType.UserName)
                .SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        /// <summary>
        /// Part 4 §7.40.4 Table 189: a UserName or IssuedToken policy with SecurityPolicy
        /// None on a Sign channel would send the secret in clear text; the configuration
        /// is rejected, so the policy is not published there. On SignAndEncrypt the
        /// channel encrypts the secret, so the policy stays.
        /// </summary>
        [TestCase(UserTokenType.UserName, MessageSecurityMode.Sign, false)]
        [TestCase(UserTokenType.IssuedToken, MessageSecurityMode.Sign, false)]
        [TestCase(UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt, true)]
        [TestCase(UserTokenType.IssuedToken, MessageSecurityMode.SignAndEncrypt, true)]
        public void UnencryptedSecretTokenPolicyIsNotPublishedOnSignEndpoint(
            UserTokenType type, MessageSecurityMode mode, bool expectPublished)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(
                registry, type, SecurityPolicies.None);
            var endpoint = new EndpointDescription
            {
                SecurityMode = mode,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                EndpointUrl = "opc.tcp://localhost:4840"
            };

            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, endpoint);

            Assert.That(
                policies.ToArray()!.Any(policy => policy.TokenType == type),
                Is.EqualTo(expectPublished));
            Assert.That(
                ServerBase.IsCleartextTokenPolicyOnSignChannel(
                    mode, new UserTokenPolicy(type) { SecurityPolicyUri = SecurityPolicies.None }),
                Is.EqualTo(!expectPublished));
        }

        /// <summary>
        /// A Sign endpoint whose only configured policy is a clear-text secret has no
        /// usable policy left and is not offered (fail closed), while a server that
        /// configures no policies or filters its own keeps the previous behavior.
        /// </summary>
        [Test]
        public void SignEndpointWithOnlyCleartextPoliciesHasNoUsableUserTokenPolicy()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var registry = new CertificateManager(telemetry);
            using var server = new ServerBase(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(
                registry, UserTokenType.UserName, SecurityPolicies.None);
            configuration.ServerConfiguration!.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.None }
            ];
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                EndpointUrl = "opc.tcp://localhost:4840"
            };

            endpoint.UserIdentityTokens = server.GetUserTokenPolicies(configuration, endpoint);

            Assert.That(endpoint.UserIdentityTokens.IsEmpty, Is.True);
            Assert.That(ServerBase.HasNoUsableUserTokenPolicy(configuration, endpoint), Is.True);

            configuration.ServerConfiguration.UserTokenPolicies = default;
            Assert.That(ServerBase.HasNoUsableUserTokenPolicy(configuration, endpoint), Is.False);

            // a server that removes its own policies on purpose (provisioning mode drops
            // Anonymous) keeps the endpoint.
            configuration.ServerConfiguration.UserTokenPolicies = [new UserTokenPolicy(UserTokenType.Anonymous)];
            Assert.That(ServerBase.HasNoUsableUserTokenPolicy(configuration, endpoint), Is.False);
        }

        [TestCase("None", true)]
        [TestCase("http://opcfoundation.org/UA/SecurityPolicy#None", true)]
        [TestCase("", false)]
        [TestCase("Basic256Sha256", false)]
        public void CleartextTokenPolicyCheckResolvesShortNames(string policyUri, bool expected)
        {
            Assert.That(
                ServerBase.IsCleartextTokenPolicyOnSignChannel(
                    MessageSecurityMode.Sign,
                    new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = policyUri }),
                Is.EqualTo(expected));
        }

        private static EndpointDescription CreateEndpoint()
        {
            return new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840"
            };
        }

        private static ApplicationConfiguration CreateConfiguration(
            CertificateManager registry, UserTokenType type, string policyUri)
        {
            return new ApplicationConfiguration
            {
                CertificateManager = registry,
                ServerConfiguration = new ServerConfiguration
                {
                    UserTokenPolicies =
                    [
                        new UserTokenPolicy(UserTokenType.Anonymous),
                        new UserTokenPolicy(UserTokenType.Certificate),
                        new UserTokenPolicy(type) { SecurityPolicyUri = policyUri }
                    ]
                }
            };
        }
    }
}
