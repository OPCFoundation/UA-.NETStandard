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
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, null);
            EndpointDescription endpoint = CreateEndpoint();
            endpoint.UserIdentityTokens = server.GetUserTokenPolicies(configuration, endpoint);

            ServerBase.SetServerCertificateInEndpointDescription(endpoint, registry);

            Assert.That(endpoint.UserIdentityTokens.Count, Is.EqualTo(3));
            Assert.That(endpoint.UserIdentityTokens.ToArray().Single(policy => policy.TokenType == type)
                .SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            using CertificateCollection advertised = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate.ToArray(), telemetry);
            Assert.That(advertised[0].RawData, Is.EqualTo(rsa.RawData));
            using RSA publicKey = advertised[0].GetRSAPublicKey();
            using RSA privateKey = rsa.GetRSAPrivateKey();
            byte[] plaintext = [1, 2, 3, 4];
            byte[] encrypted = publicKey.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);
            Assert.That(privateKey.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256), Is.EqualTo(plaintext));
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
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, null);

            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, CreateEndpoint());

            Assert.That(policies.ToArray().Select(policy => policy.TokenType),
                Is.EquivalentTo([UserTokenType.Anonymous, UserTokenType.Certificate]));
            Assert.That(configuration.ServerConfiguration.UserTokenPolicies.Count, Is.EqualTo(3));
        }

        [TestCase(UserTokenType.UserName, null, false)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.Basic256Sha256, false)]
        [TestCase(UserTokenType.UserName, SecurityPolicies.ECC_nistP256, false)]
        [TestCase(UserTokenType.UserName, SecurityPolicies.ECC_nistP256, true)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.RSA_DH_AesGcm, false)]
        [TestCase(UserTokenType.IssuedToken, SecurityPolicies.RSA_DH_AesGcm, true)]
        public async Task NoneEndpointOmitsEncryptedTokensWithoutCompatibleRsaPolicyAsync(
            UserTokenType type, string policyUri, bool hasRsa)
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
            ApplicationConfiguration configuration = CreateConfiguration(registry, type, policyUri);
            ArrayOf<UserTokenPolicy> policies = server.GetUserTokenPolicies(configuration, CreateEndpoint());

            Assert.That(policies.ToArray().Select(policy => policy.TokenType),
                Is.EquivalentTo([UserTokenType.Anonymous, UserTokenType.Certificate]));
            Assert.That(configuration.ServerConfiguration.UserTokenPolicies.Count, Is.EqualTo(3));
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
            configuration.ServerConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.IssuedToken)
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

            Assert.That(policies.ToArray().Single(policy => policy.TokenType == UserTokenType.UserName)
                .SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
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
