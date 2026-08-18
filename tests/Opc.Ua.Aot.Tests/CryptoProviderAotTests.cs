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

using System.Security.Cryptography;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the crypto provider model under Native AOT.
    /// </summary>
    /// <remarks>
    /// Registration and resolution must stay free of reflection. Running them in
    /// a trimmed, ahead of time compiled binary is what catches a lookup that
    /// only works because the metadata happened to survive.
    /// </remarks>
    public class CryptoProviderAotTests
    {
        [Test]
        public async Task EmptyRegistryResolvesToThePlatformAsync()
        {
            var registry = new CryptoProviderRegistry();

            ICryptoProvider provider = registry.Resolve(CryptoPurpose.ApplicationInstanceKey);

            await Assert.That(provider.Name).IsEqualTo("Platform");
            await Assert.That(provider.Validation.Level)
                .IsEqualTo(CryptoValidationLevel.FipsCapablePlatform);
        }

        [Test]
        public async Task ResolutionHonoursPrecedenceAsync()
        {
            var broad = new AotStubProvider(
                "Broad", new CryptoCapability(CryptoPurpose.CertificateIssuance));
            var narrow = new AotStubProvider(
                "Narrow",
                new CryptoCapability(
                    CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384));

            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.CertificateIssuance, broad);
            registry.RegisterFor(
                CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384, narrow);

            await Assert.That(
                registry.Resolve(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384).Name)
                .IsEqualTo("Narrow");
            await Assert.That(
                registry.Resolve(CryptoPurpose.CertificateIssuance, SecurityPolicies.Basic256Sha256).Name)
                .IsEqualTo("Broad");
        }

        [Test]
        public async Task AuditorReportsUncertifiedProvidersAsync()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.UserIdentityKey,
                new AotStubProvider(
                    "Bespoke",
                    CryptoValidationLevel.Uncertified,
                    new CryptoCapability(CryptoPurpose.UserIdentityKey)));

            using var auditor = new CryptoProviderAuditor(
                registry,
                DefaultTelemetry.Create(_ => { }),
                CryptoCompliancePolicy.WarnOnUncertified);

            await Assert.That(auditor.UncertifiedProviders.Count).IsEqualTo(1);
        }

        [Test]
        public async Task ComplianceFilterWithholdsUnapprovedPoliciesAsync()
        {
            await Assert.That(
                CryptoCompliance.IsPolicyPermitted(
                    SecurityPolicies.ECC_brainpoolP256r1, CryptoCompliancePolicy.FipsOnly))
                .IsFalse();

            await Assert.That(
                CryptoCompliance.IsPolicyPermitted(
                    SecurityPolicies.Basic256Sha256, CryptoCompliancePolicy.FipsOnly))
                .IsTrue();
        }

        /// <summary>
        /// The operation facets are found by type test rather than by reflection,
        /// so a trimmed binary must still discover them.
        /// </summary>
        [Test]
        public async Task PlatformCarriesEveryOperationFacetAsync()
        {
            var registry = new CryptoProviderRegistry();

            await Assert.That(CryptoCompliance.GetUnservedOperationPurposes(registry).Count)
                .IsEqualTo(0);
        }

        /// <summary>
        /// A provider bound to an operation purpose it cannot perform must be
        /// caught, not silently replaced by the platform.
        /// </summary>
        [Test]
        public async Task ProviderWithoutTheFacetIsReportedAsUnservedAsync()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.ChannelSymmetric,
                new AotStubProvider(
                    "Facetless", new CryptoCapability(CryptoPurpose.ChannelSymmetric)));

            await Assert.That(CryptoCompliance.GetUnservedOperationPurposes(registry).Count)
                .IsEqualTo(1);
            await Assert.That(CryptoProviderFacets.ResolveSymmetric(registry)).IsNull();
        }

        /// <summary>
        /// The default configuration must not take the seam at all.
        /// </summary>
        [Test]
        public async Task FacetResolutionYieldsNothingForThePlatformAsync()
        {
            var registry = new CryptoProviderRegistry();

            await Assert.That(CryptoProviderFacets.ResolveSymmetric(registry)).IsNull();
            await Assert.That(CryptoProviderFacets.ResolveKeyDerivation(registry)).IsNull();
            await Assert.That(CryptoProviderFacets.ResolveRandom(registry)).IsNull();
        }

        /// <summary>
        /// The platform facets must work in a trimmed binary, since a validated
        /// module is compared against them.
        /// </summary>
        [Test]
        public async Task PlatformSymmetricFacetRoundTripsAsync()
        {
            byte[] key = new byte[32];
            byte[] iv = new byte[16];
            byte[] plaintext = new byte[32];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(iv);
            RandomNumberGenerator.Fill(plaintext);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] recovered = new byte[plaintext.Length];

            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;
            provider.Encrypt(
                SymmetricEncryptionAlgorithm.Aes256Cbc, key, iv, plaintext, ciphertext);
            provider.Decrypt(
                SymmetricEncryptionAlgorithm.Aes256Cbc, key, iv, ciphertext, recovered);

            // Compared elementwise rather than with a collection assertion, which
            // uses structural comparison and is not trim safe.
            await Assert.That(recovered.AsSpan().SequenceEqual(plaintext)).IsTrue();
        }

        /// <summary>
        /// A provider declaring a fixed capability set.
        /// </summary>
        private sealed class AotStubProvider : ICryptoProvider
        {
            public AotStubProvider(string name, params CryptoCapability[] capabilities)
                : this(name, CryptoValidationLevel.Uncertified, capabilities)
            {
            }

            public AotStubProvider(
                string name,
                CryptoValidationLevel level,
                params CryptoCapability[] capabilities)
            {
                Name = name;
                Validation = new CryptoValidationStatus(level, "aot test double");
                Capabilities = new ArrayOf<CryptoCapability>(capabilities);
            }

            public string Name { get; }

            public CryptoValidationStatus Validation { get; }

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }
}
