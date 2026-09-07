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
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers the parts of the crypto provider model that carry no cryptography
    /// but decide what gets used and what gets reported.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public class CryptoProviderModelTests
    {
        [Test]
        public void BuilderUseDefaultBindsAndChains()
        {
            var registry = new CryptoProviderRegistry();
            var builder = new CryptoProviderBuilder(registry);
            var house = new StubProvider("House", new CryptoCapability(CryptoPurpose.KeyAgreement));

            CryptoProviderBuilder returned = builder.UseDefault(house);

            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(builder), "the builder must chain");
                Assert.That(registry.Resolve(CryptoPurpose.KeyAgreement), Is.SameAs(house));
            });
        }

        [Test]
        public void BuilderBindsAPurposeUnderOnePolicy()
        {
            var registry = new CryptoProviderRegistry();
            var builder = new CryptoProviderBuilder(registry);
            var narrow = new StubProvider(
                "Narrow",
                new CryptoCapability(
                    CryptoPurpose.CertificateIssuance, SecurityPolicies.Basic256Sha256));

            builder.For(CryptoPurpose.CertificateIssuance, SecurityPolicies.Basic256Sha256)
                .Use(narrow);

            Assert.Multiple(() =>
            {
                Assert.That(
                    registry.Resolve(
                        CryptoPurpose.CertificateIssuance, SecurityPolicies.Basic256Sha256),
                    Is.SameAs(narrow));
                Assert.That(
                    registry.Resolve(
                        CryptoPurpose.CertificateIssuance, SecurityPolicies.ECC_nistP256).Name,
                    Is.EqualTo("Platform"),
                    "a policy specific binding must not leak to other policies");
            });
        }

        [Test]
        public void BuilderRejectsANullRegistry()
        {
            Assert.Throws<ArgumentNullException>(() => new CryptoProviderBuilder(null!));
        }

        /// <summary>
        /// The binding is a struct, so its equality has to behave.
        /// </summary>
        [Test]
        public void BindingEqualityDistinguishesPurposeAndPolicy()
        {
            var registry = new CryptoProviderRegistry();
            var builder = new CryptoProviderBuilder(registry);

            CryptoPurposeBinding purposeOnly = builder.For(CryptoPurpose.UserIdentityKey);
            CryptoPurposeBinding sameAgain = builder.For(CryptoPurpose.UserIdentityKey);
            CryptoPurposeBinding otherPurpose = builder.For(CryptoPurpose.KeyAgreement);
            CryptoPurposeBinding withPolicy = builder.For(
                CryptoPurpose.UserIdentityKey, SecurityPolicies.Basic256Sha256);

            // Assigned first so the operators are genuinely exercised without
            // the analyzer rewriting them into constraints.
            bool operatorEqual = purposeOnly == sameAgain;
            bool operatorNotEqual = purposeOnly != sameAgain;
            bool equalsSame = purposeOnly.Equals(sameAgain);
            bool equalsOther = purposeOnly.Equals(otherPurpose);
            bool equalsWithPolicy = purposeOnly.Equals(withPolicy);
            bool equalsBoxed = purposeOnly.Equals((object)sameAgain);
            bool equalsForeign = purposeOnly.Equals("not a binding");

            Assert.Multiple(() =>
            {
                Assert.That(equalsSame, Is.True);
                Assert.That(operatorEqual, Is.True);
                Assert.That(operatorNotEqual, Is.False);
                Assert.That(equalsOther, Is.False);
                Assert.That(
                    equalsWithPolicy,
                    Is.False,
                    "the same purpose under a policy is a different binding");
                Assert.That(equalsBoxed, Is.True);
                Assert.That(equalsForeign, Is.False);
                Assert.That(
                    purposeOnly.GetHashCode(), Is.EqualTo(sameAgain.GetHashCode()));
            });
        }

        [Test]
        public void AuditorRejectsNullArguments()
        {
            var registry = new CryptoProviderRegistry();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => new CryptoProviderAuditor(null!, NUnitTelemetryContext.Create()));
                Assert.Throws<ArgumentNullException>(
                    () => new CryptoProviderAuditor(registry, null!));
            });
        }

        /// <summary>
        /// The metrics exist so an operator can see which module is in use
        /// without reading the log, so they have to actually publish.
        /// </summary>
        [Test]
        public void AuditorPublishesAProviderMetric()
        {
            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(
                CryptoPurpose.ApplicationInstanceKey,
                new StubProvider(
                    "Token",
                    new CryptoCapability(CryptoPurpose.ApplicationInstanceKey),
                    new CryptoValidationStatus(CryptoValidationLevel.Uncertified, "token")));

            using var auditor = new CryptoProviderAuditor(registry, NUnitTelemetryContext.Create());

            var observed = new List<KeyValuePair<string, object?>>();
            int uncertified = -1;

            using (var listener = new MeterListener())
            {
                listener.InstrumentPublished = (instrument, l) =>
                {
                    // Match on the instrument rather than the meter: the meter's
                    // name comes from the telemetry context, not from this type.
                    if (instrument.Name.StartsWith("opc.ua.crypto", StringComparison.Ordinal))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name.Contains("uncertified", StringComparison.Ordinal))
                    {
                        uncertified = measurement;
                        return;
                    }

                    foreach (KeyValuePair<string, object?> tag in tags)
                    {
                        observed.Add(tag);
                    }
                });

                listener.Start();
                listener.RecordObservableInstruments();
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    observed.Exists(t =>
                        t.Key == "opc.ua.crypto.provider" && (string?)t.Value == "Token"),
                    Is.True,
                    "the provider in use must be observable");
                Assert.That(
                    observed.Exists(t => t.Key == "opc.ua.crypto.validation"),
                    Is.True,
                    "its validation level must be observable alongside it");
                Assert.That(
                    uncertified,
                    Is.GreaterThan(0),
                    "an uncertified provider must be counted");
            });
        }

        [Test]
        public void CapabilityAndPurposeExposeReadableIdentities()
        {
            var capability = new CryptoCapability(
                CryptoPurpose.KeyAgreement, SecurityPolicies.ECC_nistP256);

            Assert.Multiple(() =>
            {
                Assert.That(capability.Purpose, Is.EqualTo(CryptoPurpose.KeyAgreement));
                Assert.That(capability.ToString(), Is.Not.Empty);
                Assert.That(CryptoPurpose.KeyAgreement.ToString(), Is.Not.Empty);
                Assert.That(
                    CryptoPurpose.KeyAgreement, Is.Not.EqualTo(CryptoPurpose.UserIdentityKey));
            });
        }

        [Test]
        public void ValidationStatusDescribesItself()
        {
            var validated = new CryptoValidationStatus(
                CryptoValidationLevel.FipsValidated, "Vendor HSM", "CMVP #1234");
            var unknown = new CryptoValidationStatus(CryptoValidationLevel.Unknown);

            Assert.Multiple(() =>
            {
                Assert.That(validated.IsAcceptableForFips, Is.True);
                Assert.That(unknown.IsAcceptableForFips, Is.False);
                Assert.That(validated.ToString(), Does.Contain("CMVP #1234"));
                Assert.That(
                    CryptoValidationStatus.Platform.Level,
                    Is.EqualTo(CryptoValidationLevel.FipsCapablePlatform));
            });
        }

        private sealed class StubProvider : ICryptoProvider
        {
            public StubProvider(
                string name,
                CryptoCapability capability,
                CryptoValidationStatus validation = default)
            {
                Name = name;
                Capabilities = new ArrayOf<CryptoCapability>(new[] { capability });
                Validation = validation.Level == CryptoValidationLevel.Unknown
                    ? CryptoValidationStatus.Platform
                    : validation;
            }

            public string Name { get; }

            public CryptoValidationStatus Validation { get; }

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }
}
