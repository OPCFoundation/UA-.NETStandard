/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server;
using Opc.Ua.Di;
using Opc.Ua.Server.Historian.InMemory;
using AmbClientOptions = AmbSample.Client.AmbClientOptions;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Verifies opt-in configuration and the typed health sample's original evidence.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class HistoryOptionsTests
    {
        /// <summary>
        /// Gets the sample qualities and whether they expose a good typed health value.
        /// </summary>
        private static IEnumerable<TestCaseData> HealthQualities =>
        [
            new TestCaseData((uint)StatusCodes.Good, true),
            new TestCaseData((uint)StatusCodes.Uncertain, false),
            new TestCaseData((uint)StatusCodes.BadNoData, false)
        ];

        [Test]
        public void SampleClientCertificateRootIsExplicitAndMustBeAbsolute()
        {
            Assert.That(AmbClientOptions.Parse([]).PkiRoot, Is.Null);
            Assert.That(
                AmbClientOptions.Parse(["--pki-root", TestContext.CurrentContext.WorkDirectory]).PkiRoot,
                Is.EqualTo(TestContext.CurrentContext.WorkDirectory));
            Assert.Throws<ArgumentException>(() => AmbClientOptions.Parse(["--pki-root", "relative-pki"]));
        }

        [Test]
        public void HistoryDefaultsToDisabledAndFluentConfigurationKeepsTheProvider()
        {
            using var provider = new InMemoryHistorianProvider();
            var options = new AmbServerOptions();
            Assert.That(options.History, Is.Null);
            Assert.That(options.UseHistory(provider, AssetHistoryKinds.DeviceHealth), Is.SameAs(options));
            Assert.That(options.History!.Provider, Is.SameAs(provider));
            Assert.That(options.History.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
            Assert.That(options.History.Capture, Is.Null);
        }

        [Test]
        public void HistoryOptionsRejectNullProvidersAndUnknownSelectionBits()
        {
            using var provider = new InMemoryHistorianProvider();
            Assert.Throws<ArgumentNullException>(() => new AssetHistoryOptions(null!));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AssetHistoryOptions(provider, (AssetHistoryKinds)8));
            Assert.That(new AssetHistoryOptions(provider).Kinds, Is.EqualTo(AssetHistoryKinds.All));
            Assert.That(
                new AssetHistoryOptions(provider, AssetHistoryKinds.None).Kinds,
                Is.EqualTo(AssetHistoryKinds.None));
        }

        [TestCaseSource(nameof(HealthQualities))]
        public void HealthSamplesRetainQualityAndTimestamps(uint status, bool typed)
        {
            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var value = new DataValue(Variant.From((int)DeviceHealthEnumeration.FAILURE), status, timestamp);
            var sample = new AssetHealthHistoryValue(new NodeId(42), value);
            Assert.That(sample.NodeId, Is.EqualTo(new NodeId(42)));
            Assert.That(sample.Value, Is.EqualTo(value));
            Assert.That(sample.Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
            Assert.That(sample.Value.StatusCode, Is.EqualTo((StatusCode)status));
            Assert.That(sample.DeviceHealth.HasValue, Is.EqualTo(typed));
            if (typed)
            {
                Assert.That(sample.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
            }
        }

        [Test]
        public void IncorrectlyTypedHealthDoesNotBecomeAValidEnumeration()
        {
            var sample = new AssetHealthHistoryValue(new NodeId(42), new DataValue(Variant.From("FAILURE")));
            Assert.That(sample.DeviceHealth, Is.Null);
            Assert.That(sample.Value.WrappedValue.TryGetValue(out string? original), Is.True);
            Assert.That(original, Is.EqualTo("FAILURE"));
        }
    }
}
