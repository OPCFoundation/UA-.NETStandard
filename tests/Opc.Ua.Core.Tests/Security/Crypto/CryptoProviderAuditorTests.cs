/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers reporting of which crypto providers are in use and whether any of
    /// them carry no validation, which is what makes the use of uncertified
    /// cryptography auditable.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoProviderAuditorTests
    {
        [Test]
        public void PlatformOnlyDeploymentHasNothingUncertified()
        {
            var registry = new CryptoProviderRegistry();
            using var auditor = new CryptoProviderAuditor(registry, m_telemetry);

            Assert.That(auditor.UncertifiedProviders.Count, Is.Zero);
        }

        [Test]
        public void UncertifiedProviderIsReported()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.ApplicationInstanceKey,
                new StubProvider("Bespoke", CryptoValidationLevel.Uncertified));

            using var auditor = new CryptoProviderAuditor(
                registry, m_telemetry, CryptoCompliancePolicy.WarnOnUncertified);

            ArrayOf<ICryptoProvider> uncertified = auditor.Report();

            Assert.That(uncertified.Count, Is.EqualTo(1));
            Assert.That(uncertified[0].Name, Is.EqualTo("Bespoke"));
        }

        /// <summary>
        /// A provider that declines to state its validation must be treated as
        /// uncertified, so silence is not a way to pass an audit.
        /// </summary>
        [Test]
        public void UndeclaredProviderCountsAsUncertified()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.KeyAgreement,
                new StubProvider("Silent", CryptoValidationLevel.Unknown));

            using var auditor = new CryptoProviderAuditor(registry, m_telemetry);

            Assert.That(auditor.UncertifiedProviders.Count, Is.EqualTo(1));
        }

        [Test]
        public void ValidatedProviderIsNotReportedAsUncertified()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.ApplicationInstanceKey,
                new StubProvider("Hsm", CryptoValidationLevel.FipsValidated));

            using var auditor = new CryptoProviderAuditor(registry, m_telemetry);

            Assert.That(auditor.UncertifiedProviders.Count, Is.Zero);
        }

        /// <summary>
        /// The default policy must leave an existing deployment exactly as it
        /// was, so nothing is warned about even when something is uncertified.
        /// </summary>
        [Test]
        public void PermissivePolicyStaysQuiet()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.UserIdentityKey,
                new StubProvider("Bespoke", CryptoValidationLevel.Uncertified));

            using var auditor = new CryptoProviderAuditor(
                registry, m_telemetry, CryptoCompliancePolicy.Permissive);

            Assert.Multiple(() =>
            {
                Assert.That(
                    auditor.Report().Count,
                    Is.EqualTo(1),
                    "The facts are still available to a caller that asks.");
                Assert.That(
                    () => auditor.ThrowIfNotCompliant(),
                    Throws.Nothing,
                    "Permissive must never refuse to start.");
            });
        }

        [Test]
        public void FipsOnlyRefusesAnUncertifiedProvider()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.ApplicationInstanceKey,
                new StubProvider("Bespoke", CryptoValidationLevel.Uncertified));

            using var auditor = new CryptoProviderAuditor(
                registry, m_telemetry, CryptoCompliancePolicy.FipsOnly);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => auditor.ThrowIfNotCompliant())!;

            Assert.That(exception.Message, Does.Contain("Bespoke"));
        }

        [Test]
        public void FipsOnlyAcceptsPlatformCryptography()
        {
            var registry = new CryptoProviderRegistry();
            using var auditor = new CryptoProviderAuditor(
                registry, m_telemetry, CryptoCompliancePolicy.FipsOnly);

            Assert.That(
                () => auditor.ThrowIfNotCompliant(),
                Throws.Nothing,
                "Platform cryptography is validated when the OS is configured for it.");
        }

        private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();

        private sealed class StubProvider : ICryptoProvider
        {
            public StubProvider(string name, CryptoValidationLevel level)
            {
                Name = name;
                Validation = new CryptoValidationStatus(level, "test double");
                Capabilities = new ArrayOf<CryptoCapability>(
                    new CryptoCapability[]
                    {
                        new(CryptoPurpose.ApplicationInstanceKey),
                        new(CryptoPurpose.UserIdentityKey),
                        new(CryptoPurpose.KeyAgreement)
                    });
            }

            public string Name { get; }

            public CryptoValidationStatus Validation { get; }

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }
}
