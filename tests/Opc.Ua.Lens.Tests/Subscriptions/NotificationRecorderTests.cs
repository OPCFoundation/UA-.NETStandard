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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens.Tests.Subscriptions
{
    [TestFixture]
    public sealed class NotificationRecorderTests
    {
        [Test]
        public void LatestSamplePreservesValueQualityAndBothTimestamps()
        {
            var stats = new MonitoredItemLiveStats();
            DateTimeUtc source = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTimeUtc server = source + TimeSpan.FromMilliseconds(25);
            var value = new DataValue(Variant.From(42), StatusCodes.UncertainInitialValue, source);
            value = value.WithServerTimestamp(server);
            stats.RecordValue(in value);

            MonitoredItemSample snapshot = stats.Snapshot();
            Assert.That(snapshot.Samples, Is.EqualTo(1));
            Assert.That(snapshot.HasValue, Is.True);
            Assert.That(snapshot.Value, Is.EqualTo("42"));
            Assert.That(snapshot.Status, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(snapshot.SourceTimestamp, Is.EqualTo(source));
            Assert.That(snapshot.ServerTimestamp, Is.EqualTo(server));
        }

        [Test]
        public async Task HiddenDocumentCapturesBeforeAViewExists()
        {
            var source = Channel.CreateUnbounded<NotificationEvent>();
            var recorder = new NotificationRecorder();
            Task capture = recorder.CaptureAsync(source.Reader, CancellationToken.None);
            await source.Writer.WriteAsync(Notification(1)).ConfigureAwait(false);
            await source.Writer.WriteAsync(Notification(2)).ConfigureAwait(false);
            source.Writer.Complete();
            await capture.ConfigureAwait(false);

            Assert.That(recorder.TotalWritten, Is.EqualTo(2));
            Assert.That(ReadSequences(recorder.CreateReader()), Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public void SwitchingRenderersReplaysHistoryWithoutStealingOrDuplicatingNotifications()
        {
            var recorder = new NotificationRecorder();
            recorder.Record(Notification(1));
            ChannelReader<NotificationEvent> animation = recorder.CreateReader();
            ChannelReader<NotificationEvent> trend = recorder.CreateReader();

            Assert.That(ReadSequences(animation), Is.EqualTo(new uint[] { 1 }));
            Assert.That(ReadSequences(trend), Is.EqualTo(new uint[] { 1 }));
            recorder.Record(Notification(2));
            Assert.That(ReadSequences(animation), Is.EqualTo(new uint[] { 2 }));
            Assert.That(ReadSequences(trend), Is.EqualTo(new uint[] { 2 }));
            Assert.That(ReadSequences(recorder.CreateReader()), Is.EqualTo(new uint[] { 1, 2 }));
            Assert.That(recorder.TotalWritten, Is.EqualTo(2));
        }

        [Test]
        public void SlowViewRetainsNewestHistoryAndReportsRetirement()
        {
            var recorder = new NotificationRecorder(capacity: 2);
            ChannelReader<NotificationEvent> hidden = recorder.CreateReader();
            recorder.Record(Notification(1));
            recorder.Record(Notification(2));
            recorder.Record(Notification(3));

            Assert.That(ReadSequences(hidden), Is.EqualTo(new uint[] { 2, 3 }));
            Assert.That(recorder.HistoryDiscarded, Is.EqualTo(1));
            Assert.That(recorder.TotalWritten, Is.EqualTo(3));
        }

        [Test]
        public void ExplicitClearDoesNotRewindDeliveryTotalsOrBreakExistingCursor()
        {
            var recorder = new NotificationRecorder();
            recorder.Record(Notification(1));
            ChannelReader<NotificationEvent> reader = recorder.CreateReader();
            recorder.Clear();
            recorder.Record(Notification(2));

            Assert.That(ReadSequences(reader), Is.EqualTo(new uint[] { 2 }));
            Assert.That(recorder.TotalWritten, Is.EqualTo(2));
        }

        [Test]
        public async Task WaitingViewsWakeOnNewDataAndAfterCompletion()
        {
            var recorder = new NotificationRecorder();
            ChannelReader<NotificationEvent> first = recorder.CreateReader();
            ChannelReader<NotificationEvent> second = recorder.CreateReader();
            Task<bool> firstWait = first.WaitToReadAsync().AsTask();
            Task<bool> secondWait = second.WaitToReadAsync().AsTask();
            Assert.That(firstWait.IsCompleted, Is.False);
            Assert.That(secondWait.IsCompleted, Is.False);
            recorder.Record(Notification(1));

            Assert.That(await firstWait.ConfigureAwait(false), Is.True);
            Assert.That(await secondWait.ConfigureAwait(false), Is.True);
            Assert.That(ReadSequences(first), Is.EqualTo(new uint[] { 1 }));
            Assert.That(ReadSequences(second), Is.EqualTo(new uint[] { 1 }));
            Task<bool> waiting = first.WaitToReadAsync().AsTask();
            recorder.Complete();

            Assert.That(await waiting.ConfigureAwait(false), Is.False);
            await first.Completion.ConfigureAwait(false);
        }

        [Test]
        public async Task CompletionWaitsForTheReadersRetainedData()
        {
            var recorder = new NotificationRecorder();
            recorder.Record(Notification(1));
            ChannelReader<NotificationEvent> reader = recorder.CreateReader();
            recorder.Complete();

            Assert.That(reader.Completion.IsCompleted, Is.False);
            Assert.That(ReadSequences(reader), Is.EqualTo(new uint[] { 1 }));
            await reader.Completion.ConfigureAwait(false);
            Assert.That(await reader.WaitToReadAsync().ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task CancellingOneViewDoesNotCancelAnotherView()
        {
            var recorder = new NotificationRecorder();
            ChannelReader<NotificationEvent> first = recorder.CreateReader();
            ChannelReader<NotificationEvent> second = recorder.CreateReader();
            using var cancellation = new CancellationTokenSource();
            Task<bool> cancelledWait = first.WaitToReadAsync(cancellation.Token).AsTask();
            Task<bool> unaffected = second.WaitToReadAsync().AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => cancelledWait, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(unaffected.IsCompleted, Is.False);
            recorder.Record(Notification(1));
            Assert.That(await unaffected.ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task ASecondSourceIsRejectedInsteadOfCompetingForTheChannel()
        {
            var source = Channel.CreateUnbounded<NotificationEvent>();
            var recorder = new NotificationRecorder();
            Task capture = recorder.CaptureAsync(source.Reader, CancellationToken.None);

            await Assert.ThatAsync(
                () => recorder.CaptureAsync(source.Reader, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            source.Writer.Complete();
            await capture.ConfigureAwait(false);
        }

        private static NotificationEvent Notification(uint sequence)
        {
            return new(NotificationKind.DataChange, 1, 1, sequence,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), sequence);
        }

        private static List<uint> ReadSequences(ChannelReader<NotificationEvent> reader)
        {
            var result = new List<uint>();
            while (reader.TryRead(out NotificationEvent notification))
            {
                result.Add(notification.SequenceNumber);
            }
            return result;
        }
    }
}
