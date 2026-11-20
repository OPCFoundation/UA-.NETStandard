// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Perception;

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
                types: [typeof(StatusCode)],
                modifiers: null);
            Assert.That(method, Is.Not.Null,
                "VisionMediaClient.ClassifyInlineState must exist and take a StatusCode. If this reflection lookup fails the mapping cannot be enforced.");
            return (VisionInlineClipState)method!.Invoke(null, [statusCode])!;
        }
    }
}
