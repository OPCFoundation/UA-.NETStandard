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
