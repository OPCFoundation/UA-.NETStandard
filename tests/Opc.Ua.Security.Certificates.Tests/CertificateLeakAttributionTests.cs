/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for per-fixture certificate leak attribution.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [CertificateLeakAttribution]
    public class CertificateLeakAttributionTests
    {
        [SetUp]
        public void SetUp()
        {
            CertificateLeakAttribution.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CertificateLeakAttribution.Reset();
        }

        [Test]
        public void BuildSummaryIncludesUnbalancedFixtureActivity()
        {
            CertificateLeakAttribution.Record(
                "Opc.Ua.Tests.LeakingFixture",
                created: 3,
                disposed: 2);
            CertificateLeakAttribution.Record(
                "Opc.Ua.Tests.BalancedFixture",
                created: 4,
                disposed: 4);

            string summary = CertificateLeakAttribution.BuildSummary();

            Assert.That(summary, Does.Contain("Opc.Ua.Tests.LeakingFixture"));
            Assert.That(summary, Does.Contain("net=1, created=3, disposed=2"));
            Assert.That(summary, Does.Not.Contain("Opc.Ua.Tests.BalancedFixture"));
        }

        [Test]
        public void BuildSummaryAggregatesRepeatedFixtureActivity()
        {
            CertificateLeakAttribution.Record(
                "Opc.Ua.Tests.RepeatedFixture",
                created: 2,
                disposed: 1);
            CertificateLeakAttribution.Record(
                "Opc.Ua.Tests.RepeatedFixture",
                created: 5,
                disposed: 3);

            string summary = CertificateLeakAttribution.BuildSummary();

            Assert.That(summary, Does.Contain("net=3, created=7, disposed=4"));
        }

        [Test]
        public void LeakDumpReflectsTrackingMode()
        {
            using Certificate cert = CertificateBuilder.Create("CN=LeakDumpTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var message = new StringBuilder();

            LeakDetectionHelpers.AppendLeakDumps(message);

            if (Certificate.LeakTrackingEnabled)
            {
                Assert.That(
                    message.ToString(),
                    Does.Contain(nameof(CertificateLeakAttributionTests)));
                Assert.That(
                    message.ToString(),
                    Does.Contain(nameof(LeakDumpReflectsTrackingMode)));
            }
            else
            {
                Assert.That(
                    message.ToString(),
                    Does.Contain("OPCUA_CERTIFICATE_LEAK_TRACKING=1"));
            }
        }
    }
}
