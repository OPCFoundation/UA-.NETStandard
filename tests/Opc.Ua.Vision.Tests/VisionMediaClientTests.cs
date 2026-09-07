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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="VisionMediaClient"/> — enumerations, clip and
    /// stream endpoint calls, ReadLatestClip §6.4 status classification.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionMediaClientTests
    {
        [Test]
        public async Task EnumerateClipEndpointsYieldsFromClipEndpointsFolder()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.MediaNodeId, BrowseNames.ClipEndpoints,
                harness.ClipEndpointsFolderId);
            harness.AddBrowse(harness.ClipEndpointsFolderId,
                [harness.Ref(harness.ClipEndpointNodeId, "ClipA",
                    ObjectTypes.ClipEndpointType)]);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in media.EnumerateClipEndpointsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.ClipEndpointNodeId));
        }

        [Test]
        public async Task EnumerateStreamEndpointsYieldsFromStreamEndpointsFolder()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.MediaNodeId, BrowseNames.StreamEndpoints,
                harness.StreamEndpointsFolderId);
            harness.AddBrowse(harness.StreamEndpointsFolderId,
                [harness.Ref(harness.StreamEndpointNodeId, "StreamA",
                    ObjectTypes.StreamEndpointType)]);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in media.EnumerateStreamEndpointsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.StreamEndpointNodeId));
        }

        [Test]
        public async Task EnumerateClipEndpointsYieldsNothingWhenFolderAbsent()
        {
            var harness = new VisionSessionHarness();

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in media.EnumerateClipEndpointsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetClipReturnsPopulatedResultOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            var descriptor = new VisionImageReferenceDataType
            {
                Uri = "opc.ua://server/clips/latest"
            };
            harness.ConfigureCall(StatusCodes.Good,
                Variant.FromStructure(descriptor),
                new Variant(harness.ClipEndpointNodeId),
                new Variant(ByteString.Empty));

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionClipResult result = await media.GetClipAsync(
                harness.ClipEndpointNodeId,
                "res-1",
                default,
                VisionClipFormatEnum.Jpeg,
                requestInline: false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.EndpointNodeId, Is.EqualTo(harness.ClipEndpointNodeId));
                Assert.That(result.Image, Is.Not.Null);
                Assert.That(result.Image.Uri, Is.EqualTo("opc.ua://server/clips/latest"));
            });
        }

        [Test]
        public void ConfigureStreamEndpointRejectsNullEndpointNodeId()
        {
            var harness = new VisionSessionHarness();
            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await media.ConfigureStreamEndpointAsync(
                    NodeId.Null,
                    VisionVideoCodecEnum.H264,
                    1920, 1080, 30.0, 8_000_000).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("streamEndpointNodeId"));
        }

        [Test]
        public void GetStreamEndpointRejectsNullProfileName()
        {
            var harness = new VisionSessionHarness();
            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await media.GetStreamEndpointAsync(
                    harness.StreamEndpointNodeId,
                    null!,
                    VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("profileName"));
        }

        [Test]
        public async Task ConfigureStreamEndpointDoesNotThrowOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureCall(StatusCodes.Good);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            Assert.DoesNotThrowAsync(async () =>
                await media.ConfigureStreamEndpointAsync(
                    harness.StreamEndpointNodeId,
                    VisionVideoCodecEnum.H264,
                    1920, 1080, 30.0, 8_000_000).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ReleaseStreamEndpointDoesNotThrowOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureCall(StatusCodes.Good);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            Assert.DoesNotThrowAsync(async () =>
                await media.ReleaseStreamEndpointAsync(
                    ByteString.Empty).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task SelectEndpointDoesNotThrowOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureCall(StatusCodes.Good);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            Assert.DoesNotThrowAsync(async () =>
                await media.SelectEndpointAsync(
                    harness.StreamEndpointNodeId,
                    harness.ClipEndpointNodeId).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public void ReadLatestClipRejectsNullEndpointNodeId()
        {
            var harness = new VisionSessionHarness();
            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await media.ReadLatestClipAsync(NodeId.Null).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("clipEndpointNodeId"));
        }

        [Test]
        public async Task ReadLatestClipReturnsAvailableWhenStatusGood()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ClipEndpointNodeId, BrowseNames.LatestClip,
                new(2400u, 3), new Variant(new ByteString(new byte[] { 1, 2, 3 })));

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionInlineClipReading reading = await media.ReadLatestClipAsync(
                harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.Available));
        }

        [Test]
        public async Task ReadLatestClipReturnsNotYetAvailableForBadNoDataAvailable()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.ClipEndpointNodeId, BrowseNames.LatestClip,
                new NodeId(2400u, 3));
            harness.AddValueStatus(new NodeId(2400u, 3),
                StatusCodes.BadNoDataAvailable);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionInlineClipReading reading = await media.ReadLatestClipAsync(
                harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.NotYetAvailable));
        }

        [Test]
        public async Task ReadLatestClipReturnsInlineDisabledForBadNotSupported()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.ClipEndpointNodeId, BrowseNames.LatestClip,
                new NodeId(2400u, 3));
            harness.AddValueStatus(new NodeId(2400u, 3),
                StatusCodes.BadNotSupported);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionInlineClipReading reading = await media.ReadLatestClipAsync(
                harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.InlineDisabled));
        }

        [Test]
        public async Task ReadLatestClipReturnsOverflowForBadEncodingLimitsExceeded()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.ClipEndpointNodeId, BrowseNames.LatestClip,
                new NodeId(2400u, 3));
            harness.AddValueStatus(new NodeId(2400u, 3),
                StatusCodes.BadEncodingLimitsExceeded);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionInlineClipReading reading = await media.ReadLatestClipAsync(
                harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.Overflow));
        }

        [Test]
        public async Task ReadLatestClipReturnsFaultedForOtherBadStatuses()
        {
            var harness = new VisionSessionHarness();
            harness.AddChild(harness.ClipEndpointNodeId, BrowseNames.LatestClip,
                new NodeId(2400u, 3));
            harness.AddValueStatus(new NodeId(2400u, 3),
                StatusCodes.BadDeviceFailure);

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionInlineClipReading reading = await media.ReadLatestClipAsync(
                harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(reading.State, Is.EqualTo(VisionInlineClipState.Faulted));
        }

        [Test]
        public void ReadLatestClipMetadataRejectsNullEndpointNodeId()
        {
            var harness = new VisionSessionHarness();
            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await media.ReadLatestClipMetadataAsync(NodeId.Null)
                    .ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("clipEndpointNodeId"));
        }

        [Test]
        public void ConstructorRejectsNullMediaNodeId()
        {
            var harness = new VisionSessionHarness();

            Assert.Throws<ArgumentException>(() =>
                harness.Client.Media(NodeId.Null));
        }
    }
}
