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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers provider selection across purpose, security policy and certificate
    /// type, which is what allows an instance key in a token, user keys in a
    /// remote service and everything else in software at the same time.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoProviderRegistryTests
    {
        [Test]
        public void EmptyRegistryResolvesToThePlatform()
        {
            var registry = new CryptoProviderRegistry();

            Assert.That(
                registry.Resolve(CryptoPurpose.ApplicationInstanceKey),
                Is.SameAs(PlatformCryptoProvider.Instance),
                "A caller that registers nothing must keep today's behaviour.");
        }

        [Test]
        public void PurposeBindingBeatsTheDefault()
        {
            var tpm = new StubProvider("Tpm", CryptoPurpose.ApplicationInstanceKey);
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.ApplicationInstanceKey, tpm);

            Assert.Multiple(() =>
            {
                Assert.That(
                    registry.Resolve(CryptoPurpose.ApplicationInstanceKey),
                    Is.SameAs(tpm));
                Assert.That(
                    registry.Resolve(CryptoPurpose.UserIdentityKey),
                    Is.SameAs(PlatformCryptoProvider.Instance),
                    "An unbound purpose must not be captured by another purpose's binding.");
            });
        }

        [Test]
        public void PolicyBindingBeatsPurposeBinding()
        {
            var broad = new StubProvider("Broad", CryptoPurpose.CertificateIssuance);
            var narrow = new StubProvider(
                "Narrow",
                new CryptoCapability(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384));

            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.CertificateIssuance, broad);
            registry.RegisterFor(
                CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384, narrow);

            Assert.Multiple(() =>
            {
                Assert.That(
                    registry.Resolve(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384),
                    Is.SameAs(narrow),
                    "The most specific registration must win.");
                Assert.That(
                    registry.Resolve(CryptoPurpose.CertificateIssuance, SecurityPolicies.Basic256Sha256),
                    Is.SameAs(broad),
                    "Other policies must still use the purpose binding.");
            });
        }

        [Test]
        public void RegisteredDefaultBeatsThePlatform()
        {
            var house = new StubProvider(
                "House",
                new CryptoCapability(CryptoPurpose.ApplicationInstanceKey),
                new CryptoCapability(CryptoPurpose.UserIdentityKey));

            var registry = new CryptoProviderRegistry();
            registry.RegisterDefault(house);

            Assert.Multiple(() =>
            {
                Assert.That(registry.Resolve(CryptoPurpose.UserIdentityKey), Is.SameAs(house));
                Assert.That(
                    registry.Resolve(CryptoPurpose.KeyAgreement),
                    Is.SameAs(PlatformCryptoProvider.Instance),
                    "A default that cannot serve the purpose must fall through, not fail later.");
            });
        }

        /// <summary>
        /// Binding a provider to something it never claimed is a configuration
        /// mistake. Falling through keeps the failure near its cause instead of
        /// surfacing deep in a handshake.
        /// </summary>
        [Test]
        public void ProviderThatCannotServeThePurposeIsSkipped()
        {
            var signingOnly = new StubProvider("SigningOnly", CryptoPurpose.UserIdentityKey);
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.KeyAgreement, signingOnly);

            Assert.That(
                registry.Resolve(CryptoPurpose.KeyAgreement),
                Is.SameAs(PlatformCryptoProvider.Instance));
        }

        [Test]
        public void CertificateTypeNarrowsACapability()
        {
            var rsaOnly = new StubProvider(
                "RsaOnly",
                new CryptoCapability(
                    CryptoPurpose.ApplicationInstanceKey,
                    null,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType));

            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.ApplicationInstanceKey, rsaOnly);

            Assert.Multiple(() =>
            {
                Assert.That(
                    registry.Resolve(
                        CryptoPurpose.ApplicationInstanceKey,
                        null,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    Is.SameAs(rsaOnly));
                Assert.That(
                    registry.Resolve(
                        CryptoPurpose.ApplicationInstanceKey,
                        null,
                        ObjectTypeIds.EccNistP256ApplicationCertificateType),
                    Is.SameAs(PlatformCryptoProvider.Instance),
                    "A provider that only claims RSA must not be used for an ECC certificate.");
            });
        }

        /// <summary>
        /// The motivating deployment: instance key in a token, user keys in a
        /// remote service, certificate issuance narrowed to one policy, and the
        /// platform for the rest.
        /// </summary>
        [Test]
        public void MixedDeploymentResolvesEachPurposeIndependently()
        {
            var tpm = new StubProvider(
                "Tpm", CryptoPurpose.ApplicationInstanceKey, CryptoPurpose.KeyAgreement);
            var keyVault = new StubProvider("KeyVault", CryptoPurpose.UserIdentityKey);
            var hsm = new StubProvider(
                "Hsm",
                new CryptoCapability(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384));

            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.ApplicationInstanceKey, tpm);
            registry.RegisterFor(CryptoPurpose.KeyAgreement, tpm);
            registry.RegisterFor(CryptoPurpose.UserIdentityKey, keyVault);
            registry.RegisterFor(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384, hsm);

            Assert.Multiple(() =>
            {
                Assert.That(registry.Resolve(CryptoPurpose.ApplicationInstanceKey), Is.SameAs(tpm));
                Assert.That(registry.Resolve(CryptoPurpose.KeyAgreement), Is.SameAs(tpm));
                Assert.That(registry.Resolve(CryptoPurpose.UserIdentityKey), Is.SameAs(keyVault));
                Assert.That(
                    registry.Resolve(CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP384),
                    Is.SameAs(hsm));
                Assert.That(
                    registry.Resolve(CryptoPurpose.ChannelSymmetric),
                    Is.SameAs(PlatformCryptoProvider.Instance));
            });

            Assert.That(registry.Providers.Count, Is.EqualTo(4), "Platform, TPM, Key Vault and HSM.");
        }

        [Test]
        public void RegistrationRejectsNulls()
        {
            var registry = new CryptoProviderRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => registry.RegisterDefault(null),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => registry.RegisterFor(CryptoPurpose.KeyAgreement, null),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => registry.RegisterFor(
                        CryptoPurpose.KeyAgreement, string.Empty, PlatformCryptoProvider.Instance),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void PlatformProviderReportsPlatformValidation()
        {
            CryptoValidationStatus status = PlatformCryptoProvider.Instance.Validation;

            Assert.Multiple(() =>
            {
                Assert.That(status.Level, Is.EqualTo(CryptoValidationLevel.FipsCapablePlatform));
                Assert.That(
                    status.IsAcceptableForFips,
                    Is.True,
                    "Platform cryptography is acceptable when the OS is configured for it.");
                Assert.That(
                    new CryptoValidationStatus(CryptoValidationLevel.Uncertified).IsAcceptableForFips,
                    Is.False);
                Assert.That(
                    new CryptoValidationStatus(CryptoValidationLevel.Unknown).IsAcceptableForFips,
                    Is.False,
                    "An undeclared provider must be treated as uncertified.");
            });
        }

        [Test]
        public void PurposeRequiresAName()
        {
            Assert.That(() => new CryptoPurpose(" "), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// A provider that declares exactly the capabilities it is given.
        /// </summary>
        private sealed class StubProvider : ICryptoProvider
        {
            public StubProvider(string name, params CryptoPurpose[] purposes)
            {
                Name = name;
                var capabilities = new CryptoCapability[purposes.Length];
                for (int ii = 0; ii < purposes.Length; ii++)
                {
                    capabilities[ii] = new CryptoCapability(purposes[ii]);
                }
                Capabilities = new ArrayOf<CryptoCapability>(capabilities);
            }

            public StubProvider(string name, params CryptoCapability[] capabilities)
            {
                Name = name;
                Capabilities = new ArrayOf<CryptoCapability>(capabilities);
            }

            public string Name { get; }

            public CryptoValidationStatus Validation { get; }
                = new(CryptoValidationLevel.Uncertified, "test double");

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }
}
