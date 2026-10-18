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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Client;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Positioning.Tests
{
    public sealed partial class PositioningStrictReadTests
    {
        [TestCaseSource(nameof(StreamQualityCases))]
        public async Task StrictStreamsNeverTurnNonGoodValuesIntoSuccessfulResultsAsync(string kind, StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupStreamMetadata(session);
            Mock<IStreamingSubscription> streaming = Streaming(StreamValue(kind, status));
            var observed = new List<StatusCode>();
            await AssertStatusAsync(
                () => ObserveAsync(kind, session, streaming.Object, observed),
                status).ConfigureAwait(false);
            Assert.That(observed, Is.Empty);
            streaming.Verify(s => s.DisposeAsync(), Times.Never);
            VerifyBorrowed(session);
        }

        [TestCase("frame")]
        [TestCase("position-frame")]
        [TestCase("position")]
        [TestCase("location")]
        [TestCase("version")]
        public async Task StrictStreamsAcceptGoodValuesAndBorrowSubscriptionAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            SetupStreamMetadata(session);
            Mock<IStreamingSubscription> streaming = Streaming(StreamValue(kind, StatusCodes.Good));
            var observed = new List<StatusCode>();
            await ObserveAsync(kind, session, streaming.Object, observed).ConfigureAwait(false);
            Assert.That(observed, Is.EqualTo(new[] { StatusCodes.Good }));
            streaming.Verify(s => s.DisposeAsync(), Times.Never);
            VerifyBorrowed(session);
        }

        [TestCase("frame")]
        [TestCase("position")]
        [TestCase("location")]
        public async Task StreamsDoNotHideNonGoodBaseOrCrsMetadataAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            SetupStreamMetadata(session);
            SetupRead(
                session,
                node =>
                {
                    DataValue value = node == s_base ? new DataValue(new Variant(NodeId.Null)) : GlobalValue(node);
                    if (node == s_base || node == s_crs)
                    {
                        value = value.WithStatus(StatusCodes.Uncertain);
                    }
                    return value;
                });
            Mock<IStreamingSubscription> streaming = Streaming(StreamValue(kind, StatusCodes.Good));
            var observed = new List<StatusCode>();
            await AssertStatusAsync(
                () => ObserveAsync(kind, session, streaming.Object, observed),
                StatusCodes.Uncertain).ConfigureAwait(false);
            Assert.That(observed, Is.Empty);
            streaming.Verify(s => s.DisposeAsync(), Times.Never);
        }

        [TestCase("frame")]
        [TestCase("position")]
        [TestCase("location")]
        [TestCase("version")]
        public async Task LegacyStreamsRetainBadQualityRatherThanReclassifyingItAsGoodAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            SetupStreamMetadata(session);
            Mock<IStreamingSubscription> streaming = Streaming(StreamValue(kind, StatusCodes.BadUserAccessDenied));
            var observed = new List<StatusCode>();
            await ObserveAsync(kind, session, streaming.Object, observed, legacy: true).ConfigureAwait(false);
            Assert.That(observed, Is.EqualTo(new[] { StatusCodes.BadUserAccessDenied }));
        }

        private static IEnumerable<TestCaseData> StreamQualityCases()
        {
            foreach (string kind in new[] { "frame", "position-frame", "position", "location", "version" })
            {
                yield return new TestCaseData(kind, StatusCodes.BadUserAccessDenied);
                yield return new TestCaseData(kind, StatusCodes.Uncertain);
            }
        }

        private static void SetupStreamMetadata(Mock<ISession> session)
        {
            SetupTranslate(
                session,
                path => Resolved(
                    path.RelativePath.Elements[0].TargetName.Name switch
                    {
                        "Base" => s_base,
                        "PositionFrame" => s_frame,
                        "Position" => s_position,
                        "SourceId" => s_source,
                        "CoordinateReferenceSystem" => s_crs,
                        "NodeVersion" => s_zone,
                        _ => throw new AssertionException("Unexpected stream metadata path.")
                    }));
            SetupRead(session, node => node == s_base ? new DataValue(new Variant(NodeId.Null)) : GlobalValue(node));
        }

        private static DataValue StreamValue(string kind, StatusCode status)
        {
            return kind switch
            {
                "frame" or "position-frame" => Structure(Frame(), status),
                "position" => Structure(Position(), status),
                "location" => Structure(new GlobalLocationDataType { Position = Position() }, status),
                _ => new DataValue(Variant.From("2"), status)
            };
        }

        private static Mock<IStreamingSubscription> Streaming(DataValue value)
        {
            var streaming = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            streaming.Setup(
                s => s.SubscribeDataChangesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<MonitoringOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NodeId _, MonitoringOptions? _, CancellationToken token) => ChangesAsync(value, token));
            return streaming;
        }

        private static async IAsyncEnumerable<DataValueChange> ChangesAsync(
            DataValue value,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new DataValueChange(null, value, null);
        }

        private static Task ObserveAsync(
            string kind,
            Mock<ISession> session,
            IStreamingSubscription streaming,
            List<StatusCode> observed,
            bool legacy = false)
        {
            RelativeSpatialLocationClient relative = legacy
                ? new RelativeSpatialLocationClient(session.Object, Mock.Of<ITelemetryContext>())
                : Relative(session);
            GlobalPositioningClient global = legacy
                ? new GlobalPositioningClient(session.Object, Mock.Of<ITelemetryContext>())
                : Global(session);
            return kind switch
            {
                "frame" => ObserveStatusesAsync(
                    relative.ObserveFrameAsync(s_frame, streaming),
                    value => value.StatusCode,
                    observed),
                "position-frame" => ObserveStatusesAsync(
                    relative.ObservePositionFrameAsync(s_zone, streaming),
                    value => value.StatusCode,
                    observed),
                "position" => ObserveStatusesAsync(
                    global.ObserveGlobalPositionAsync(s_position, streaming),
                    value => value.StatusCode,
                    observed),
                "location" => ObserveStatusesAsync(
                    global.ObserveGlobalLocationAsync(s_location, streaming),
                    value => value.StatusCode,
                    observed),
                _ => ObserveStatusesAsync(
                    relative.ObserveNodeVersionAsync(s_zone, streaming),
                    value => value.StatusCode,
                    observed)
            };
        }

        private static async Task ObserveStatusesAsync<T>(
            IAsyncEnumerable<T> values,
            Func<T, StatusCode> status,
            List<StatusCode> observed)
        {
            await foreach (T value in values.ConfigureAwait(false))
            {
                observed.Add(status(value));
            }
        }
    }
}
