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

using System;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdWorkflowTaskInputTests
    {
        [Test]
        public void EncodedSamplesContainExactlyTheTwoValuesWithoutCapacityPadding()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            DataValue value = OpenUsdWorkflowTestContext.Value(Variant.From(12d));
            var converted = Variant.From(25d);

            ByteString encoded = OpenUsdWorkflowMetadata.EncodeSample(fixture.Context, value, converted);

            Assert.That(encoded.Length, Is.LessThan(128));
            using var decoder = new BinaryDecoder(encoded.Span.ToArray(), fixture.Context.Session.MessageContext);
            Assert.That(decoder.ReadDataValue(null), Is.EqualTo(value));
            Assert.That(decoder.ReadVariant(null), Is.EqualTo(converted));
            Assert.That(decoder.Position, Is.EqualTo(encoded.Length));
        }

        [TestCase(1)]
        [TestCase(128)]
        [TestCase(65536)]
        [TestCase(1048576)]
        public void BoundedEncodingBufferAcceptsItsExactCapacityAndRejectsTheNextByte(int capacity)
        {
            using var buffer = new IndustrialDocumentBuffer(capacity);
            Assert.That(buffer.Length, Is.Zero);
            buffer.Write(new byte[capacity]);
            Assert.That(buffer.Length, Is.EqualTo(capacity));
            Assert.That(buffer.ToArray(), Has.Length.EqualTo(capacity));
            Assert.That(() => buffer.WriteByte(1), Throws.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(buffer.Length, Is.EqualTo(capacity));
            buffer.Position = 0;
            buffer.WriteByte(9);
            Assert.That(buffer.Length, Is.EqualTo(capacity));
            Assert.That(buffer.ToArray()[0], Is.EqualTo(9));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1048577)]
        public void InvalidEncodingCapacitiesAreRejected(int capacity)
        {
            Assert.That(() => new IndustrialDocumentBuffer(capacity), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0, 32, 0)]
        [TestCase(129, 32, 0)]
        [TestCase(1, 31, 0)]
        [TestCase(1, 33, 0)]
        [TestCase(1, 32, -1)]
        [TestCase(1, 32, 16)]
        public void InvalidPreparedBoundsCannotBeRetained(int samples, int digestSize, int seconds)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            Assert.That(() => new OpenUsdWorkflowTaskInput(
                fixture.Context, fixture.Target, "read-bindings", ByteString.From(new byte[digestSize]),
                samples, TimeSpan.FromSeconds(seconds), default, default, null, "review"), Throws.ArgumentException);
        }

        [Test]
        public void SourceAndOriginDigestStorageIsIndependentOfTheCaller()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            byte[] metadata = new byte[32];
            byte[] certificate = new byte[32];
            var origin = new OpenUsdWorkflowOrigin(
                "peer", "/Peers/peer", new ByteString(metadata), new ByteString(certificate));
            var task = new OpenUsdWorkflowTaskInput(
                fixture.Context, fixture.Target, "read-bindings", new ByteString(metadata),
                1, TimeSpan.Zero, default, default, null, "review", [origin]);

            metadata[0] = 11;
            certificate[0] = 22;

            Assert.That(task.MetadataDigest.Span[0], Is.Zero);
            Assert.That(task.Origins[0].MetadataDigest.Span[0], Is.Zero);
            Assert.That(task.Origins[0].SecurityDigest.Span[0], Is.Zero);
            Assert.That(task.Origins[0], Is.Not.SameAs(origin));
        }

        [Test]
        public void MetadataDigestPinsTheNamespaceTableAsWellAsNumericNodeIds()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            ByteString before = OpenUsdWorkflowMetadata.Digest(fixture.Context, fixture.Representation);
            fixture.Server.NamespaceUris.GetIndexOrAppend("urn:additional-origin");

            ByteString after = OpenUsdWorkflowMetadata.Digest(fixture.Context, fixture.Representation);

            Assert.That(before.Length, Is.EqualTo(32));
            Assert.That(after.Length, Is.EqualTo(32));
            Assert.That(after, Is.Not.EqualTo(before));
        }

        [Test]
        public void OversizedMetadataFailsWithoutGrowingTheEncodingBuffer()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.SourceSemanticId = new string('x', OpenUsdWorkflowMetadata.MaximumBytes + 1);

            Assert.That(() => OpenUsdWorkflowMetadata.Digest(fixture.Context, fixture.Representation),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }
    }
}
