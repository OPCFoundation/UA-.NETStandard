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

using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Locks the §6.4 inline-clip status-code classification a media
    /// client applies when reading <c>LatestClip</c>. A server that has
    /// <c>InlineDeliveryEnabled = false</c> writes
    /// <see cref="StatusCodes.BadNotSupported"/>, and the client must
    /// surface this as <see cref="VisionInlineClipState.InlineDisabled"/>
    /// rather than a generic <see cref="VisionInlineClipState.Faulted"/>
    /// — the metadata endpoint remains readable regardless. Any refactor
    /// that reshuffles the four members (LatestClip, LatestClipMetadata,
    /// InlineDeliveryEnabled, MaxInlineClipSize) into different classes
    /// still owes callers this exact status mapping.
    /// </summary>
    [TestFixture]
    public sealed class VisionMediaInlineClassificationTests
    {
        [Test]
        public void BadNotSupportedMapsToInlineDisabled()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.BadNotSupported);

            Assert.That(state, Is.EqualTo(VisionInlineClipState.InlineDisabled));
        }

        [Test]
        public void BadNoDataAvailableMapsToNotYetAvailable()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.BadNoDataAvailable);

            Assert.That(state, Is.EqualTo(VisionInlineClipState.NotYetAvailable));
        }

        [Test]
        public void BadEncodingLimitsExceededMapsToOverflow()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(state, Is.EqualTo(VisionInlineClipState.Overflow));
        }

        [Test]
        public void OtherBadStatusMapsToFaulted()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.BadInternalError);

            Assert.That(state, Is.EqualTo(VisionInlineClipState.Faulted));
        }

        [Test]
        public void UncertainStatusMapsToFaulted()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.UncertainNoCommunicationLastUsableValue);

            Assert.That(state, Is.EqualTo(VisionInlineClipState.Faulted));
        }

        [Test]
        public void ArbitraryBadStatusMapsToFaultedNotToOneOfTheDedicatedStates()
        {
            VisionInlineClipState state = ClassifyInlineState(StatusCodes.BadTimeout);

            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(VisionInlineClipState.Faulted));
                Assert.That(state, Is.Not.EqualTo(VisionInlineClipState.InlineDisabled));
                Assert.That(state, Is.Not.EqualTo(VisionInlineClipState.NotYetAvailable));
                Assert.That(state, Is.Not.EqualTo(VisionInlineClipState.Overflow));
            });
        }

        [Test]
        public void InlineClipReadingCarriesStatusCodeAndByteStringWithoutInterferingWithMetadata()
        {
            var bytes = ByteString.From(new byte[] { 1, 2, 3 });
            var meta = new VisionImageReferenceDataType
            {
                Uri = "urn:test:image"
            };
            StatusCode status = StatusCodes.Good;

            var reading = new VisionInlineClipReading(bytes, meta, status, VisionInlineClipState.Available);

            Assert.Multiple(() =>
            {
                Assert.That(reading.Bytes.Length, Is.EqualTo(3));
                Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.Available));
                Assert.That(reading.StatusCode, Is.EqualTo(status));
                Assert.That(reading.Metadata, Is.Not.Null);
                Assert.That(reading.Metadata!.Uri, Is.EqualTo("urn:test:image"));
            });
        }

        [Test]
        public void InlineClipReadingKeepsMetadataAvailableEvenWhenInlineIsDisabled()
        {
            var meta = new VisionImageReferenceDataType
            {
                Uri = "urn:test:meta-only"
            };
            var reading = new VisionInlineClipReading(
                ByteString.Empty,
                meta,
                StatusCodes.BadNotSupported,
                VisionInlineClipState.InlineDisabled);

            Assert.Multiple(() =>
            {
                Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.InlineDisabled));
                Assert.That(reading.Metadata, Is.Not.Null,
                    "The metadata channel must stay readable even when the inline byte channel reports Bad_NotSupported.");
                Assert.That(reading.Metadata!.Uri, Is.EqualTo("urn:test:meta-only"));
            });
        }

        private static VisionInlineClipState ClassifyInlineState(StatusCode statusCode)
        {
            MethodInfo? method = typeof(VisionMediaClient).GetMethod(
                "ClassifyInlineState",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(StatusCode) },
                modifiers: null);
            Assert.That(method, Is.Not.Null,
                "VisionMediaClient.ClassifyInlineState must exist and take a StatusCode. If this reflection lookup fails the mapping cannot be enforced.");
            return (VisionInlineClipState)method!.Invoke(null, new object[] { statusCode })!;
        }
    }
}
