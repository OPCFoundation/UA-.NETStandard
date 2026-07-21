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

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MockUsdSink"/> and <see cref="OpenUsdConnectorOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class MockUsdSinkTests
    {
        [Test]
        public void SetAttributeTracksWritesAndValue()
        {
            var sink = new MockUsdSink();
            sink.SetAttribute("/Pump", "radius", new Variant(2.5));

            Assert.That(sink.TotalWrites, Is.EqualTo(1));
            Assert.That(sink.WasWritten("/Pump", "radius"), Is.True);
            Assert.That(sink.TryGetWritten("/Pump", "radius", out Variant value), Is.True);
            Assert.That(value.TryGetValue(out double d), Is.True);
            Assert.That(d, Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void SetTimeSampleTracksTimeSampleWrites()
        {
            var sink = new MockUsdSink();
            sink.SetTimeSample("/Pump", "radius", new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Variant(1.0));

            Assert.That(sink.TimeSampleWrites, Is.EqualTo(1));
        }

        [Test]
        public void ComposePrimTracksActiveAndCount()
        {
            var sink = new MockUsdSink();
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Reference, "@pump.usda@", active: true);
            sink.ComposePrim("/Tool", OpenUsdCompositionArc.Child, null, active: false);

            Assert.That(sink.WasPrimComposed("/Pump"), Is.True);
            Assert.That(sink.IsPrimActive("/Pump"), Is.True);
            Assert.That(sink.IsPrimActive("/Tool"), Is.False);
            Assert.That(sink.ComposedPrimCount, Is.EqualTo(2));
        }

        [Test]
        public void TryGetWrittenReturnsFalseForUnknownProperty()
        {
            var sink = new MockUsdSink();

            Assert.That(sink.TryGetWritten("/Nope", "x", out Variant value), Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void BeginBatchReturnsNoOpScope()
        {
            var sink = new MockUsdSink();

            using IDisposable batch = sink.BeginBatch();
            Assert.That(batch, Is.Not.Null);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="OpenUsdConnectorOptions"/> defaults.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorOptionsTests
    {
        [Test]
        public void DefaultsAreFailClosedAndBounded()
        {
            var options = new OpenUsdConnectorOptions();

            Assert.That(options.EnableCommands, Is.False);
            Assert.That(options.RemoteSessionFactory, Is.Null);
            Assert.That(options.MaxAssetBytes, Is.EqualTo(64 * 1024 * 1024));
            Assert.That(options.MaxTotalAssetBytes, Is.EqualTo(256L * 1024 * 1024));
        }
    }
}
