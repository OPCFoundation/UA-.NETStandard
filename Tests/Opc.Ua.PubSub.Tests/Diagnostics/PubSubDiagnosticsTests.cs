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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Tests.Diagnostics
{
    /// <summary>
    /// Coverage for <see cref="PubSubDiagnostics"/>: counter increment /
    /// read semantics, level-gated error recording, ring buffer wrap and
    /// reset behaviour.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.11", Summary = "PubSubDiagnosticsType counters and error history")]
    public class PubSubDiagnosticsTests
    {
#if NET5_0_OR_GREATER
        private static readonly PubSubDiagnosticsCounterKind[] s_allCounterKinds =
            Enum.GetValues<PubSubDiagnosticsCounterKind>();
#else
        private static readonly PubSubDiagnosticsCounterKind[] s_allCounterKinds =
            (PubSubDiagnosticsCounterKind[])Enum.GetValues(typeof(PubSubDiagnosticsCounterKind));
#endif

        private static FakeTimeProvider NewClock(DateTime? start = null)
        {
            return new FakeTimeProvider(
                new DateTimeOffset(start ?? new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));
        }

        [Test]
        public void Constructor_DefaultsClockToSystemWhenNull()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            Assert.That(sut.Level, Is.EqualTo(PubSubDiagnosticsLevel.High));
        }

        [Test]
        [TestCase(PubSubDiagnosticsLevel.Low)]
        [TestCase(PubSubDiagnosticsLevel.Medium)]
        [TestCase(PubSubDiagnosticsLevel.High)]
        public void Constructor_StoresLevel(PubSubDiagnosticsLevel level)
        {
            var sut = new PubSubDiagnostics(level, NewClock());
            Assert.That(sut.Level, Is.EqualTo(level));
        }

        [Test]
        public void Read_AllCountersStartAtZero()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Medium, NewClock());
            Assert.Multiple(() =>
            {
                foreach (PubSubDiagnosticsCounterKind kind in s_allCounterKinds)
                {
                    Assert.That(sut.Read(kind), Is.Zero, $"counter {kind}");
                }
            });
        }

        [Test]
        public void Increment_DefaultDeltaIsOne()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            sut.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages);
            Assert.That(sut.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages), Is.EqualTo(1));
        }

        [Test]
        public void Increment_AccumulatesAcrossCalls()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            sut.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages, 3);
            sut.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages, 5);
            sut.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
            Assert.That(
                sut.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages),
                Is.EqualTo(9));
        }

        [Test]
        public void Increment_ZeroDeltaIsNoOp()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            sut.Increment(PubSubDiagnosticsCounterKind.SentDataSetMessages, 0);
            Assert.That(sut.Read(PubSubDiagnosticsCounterKind.SentDataSetMessages), Is.Zero);
        }

        [Test]
        public void Increment_NegativeDeltaThrows()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            Assert.That(
                () => sut.Increment(PubSubDiagnosticsCounterKind.SentDataSetMessages, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Increment_InvalidKindThrows()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            Assert.That(
                () => sut.Increment((PubSubDiagnosticsCounterKind)9999, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Read_InvalidKindThrows()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            Assert.That(
                () => sut.Read((PubSubDiagnosticsCounterKind)9999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Increment_AllCountersIndependentlyTracked()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            int i = 1;
            foreach (PubSubDiagnosticsCounterKind kind in s_allCounterKinds)
            {
                sut.Increment(kind, i);
                i++;
            }
            int j = 1;
            Assert.Multiple(() =>
            {
                foreach (PubSubDiagnosticsCounterKind kind in s_allCounterKinds)
                {
                    Assert.That(sut.Read(kind), Is.EqualTo(j), $"counter {kind}");
                    j++;
                }
            });
        }

        [Test]
        public void RecordError_NullMessageThrows()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Medium, NewClock());
            Assert.That(
                () => sut.RecordError((StatusCode)StatusCodes.BadInvalidArgument, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RecordError_AtLowLevelIsIgnored()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "first error");
            Assert.Multiple(() =>
            {
                Assert.That(sut.LastError, Is.Null);
                Assert.That(sut.RecentErrors, Is.Empty);
            });
        }

        [Test]
        public void RecordError_AtMediumLevelKeepsLastErrorButNoHistory()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Medium, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "comms");
            PubSubErrorEntry? last = sut.LastError;
            Assert.Multiple(() =>
            {
                Assert.That(last, Is.Not.Null);
                Assert.That(last!.Value.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCommunicationError));
                Assert.That(last!.Value.Message, Is.EqualTo("comms"));
                Assert.That(sut.RecentErrors, Is.Empty);
            });
        }

        [Test]
        public void RecordError_AtHighLevelPopulatesHistory()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "first");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            sut.RecordError((StatusCode)StatusCodes.BadTimeout, "second");

            ArrayOf<PubSubErrorEntry> recent = sut.RecentErrors;
            Assert.Multiple(() =>
            {
                Assert.That(recent, Has.Count.EqualTo(2));
                Assert.That(recent[0].Message, Is.EqualTo("second"), "newest first");
                Assert.That(recent[1].Message, Is.EqualTo("first"));
                Assert.That(sut.LastError!.Value.Message, Is.EqualTo("second"));
            });
        }

        [Test]
        public void RecordError_RingBufferWrapsAtCapacity()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);
            const int extra = 5;
            int total = PubSubDiagnostics.ErrorHistoryCapacity + extra;
            for (int i = 0; i < total; i++)
            {
                sut.RecordError((StatusCode)StatusCodes.BadInternalError, $"err-{i}");
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }

            ArrayOf<PubSubErrorEntry> recent = sut.RecentErrors;
            Assert.Multiple(() =>
            {
                Assert.That(recent, Has.Count.EqualTo(PubSubDiagnostics.ErrorHistoryCapacity));
                Assert.That(recent[0].Message, Is.EqualTo($"err-{total - 1}"), "newest first after wrap");
                Assert.That(recent[^1].Message, Is.EqualTo($"err-{extra}"), "oldest retained entry");
            });
        }

        [Test]
        public void RecentErrors_AtMediumLevelReturnsEmpty()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Medium, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "x");
            Assert.That(sut.RecentErrors, Is.Empty);
        }

        [Test]
        public void RecentErrors_AtHighLevelEmptyBeforeAnyRecord()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, NewClock());
            Assert.That(sut.RecentErrors, Is.Empty);
        }

        [Test]
        public void LastError_AtLowLevelAlwaysNull()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low, NewClock());
            sut.RecordError((StatusCode)StatusCodes.BadInternalError, "boom");
            Assert.That(sut.LastError, Is.Null);
        }

        [Test]
        public void LastError_BeforeAnyRecordIsNull()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, NewClock());
            Assert.That(sut.LastError, Is.Null);
        }

        [Test]
        public void RecordError_TimestampsUseSuppliedClock()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);

            DateTime expected = clock.GetUtcNow().UtcDateTime;
            sut.RecordError((StatusCode)StatusCodes.BadInternalError, "boom");

            PubSubErrorEntry? last = sut.LastError;
            Assert.That(last!.Value.Timestamp.ToDateTime(), Is.EqualTo(expected));
        }

        [Test]
        public void Reset_ZeroesAllCounters()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, NewClock());
            foreach (PubSubDiagnosticsCounterKind kind in s_allCounterKinds)
            {
                sut.Increment(kind, 7);
            }
            sut.Reset();
            Assert.Multiple(() =>
            {
                foreach (PubSubDiagnosticsCounterKind kind in s_allCounterKinds)
                {
                    Assert.That(sut.Read(kind), Is.Zero, $"counter {kind}");
                }
            });
        }

        [Test]
        public void Reset_ClearsErrorHistoryAndLastError()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "x");
            sut.RecordError((StatusCode)StatusCodes.BadTimeout, "y");

            sut.Reset();

            Assert.Multiple(() =>
            {
                Assert.That(sut.LastError, Is.Null);
                Assert.That(sut.RecentErrors, Is.Empty);
            });
        }

        [Test]
        public void Reset_AtMediumLevelClearsLastError()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.Medium, clock);
            sut.RecordError((StatusCode)StatusCodes.BadCommunicationError, "x");
            sut.Reset();
            Assert.That(sut.LastError, Is.Null);
        }

        [Test]
        public async Task Increment_ConcurrentCallsProduceCorrectTotalAsync()
        {
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, NewClock());
            const int iterations = 1000;
            const int workers = 8;

            using var start = new ManualResetEventSlim(false);
            var tasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                tasks[w] = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < iterations; i++)
                    {
                        sut.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages);
                    }
                });
            }
            start.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(
                sut.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.EqualTo(workers * iterations));
        }

        [Test]
        public async Task RecordError_ConcurrentCallsProduceBoundedHistoryAsync()
        {
            FakeTimeProvider clock = NewClock();
            var sut = new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock);
            const int iterations = 100;
            const int workers = 4;

            using var start = new ManualResetEventSlim(false);
            var tasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                int local = w;
                tasks[w] = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < iterations; i++)
                    {
                        sut.RecordError((StatusCode)StatusCodes.BadInternalError, $"w{local}-{i}");
                    }
                });
            }
            start.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(
                sut.RecentErrors,
                Has.Count.EqualTo(PubSubDiagnostics.ErrorHistoryCapacity));
        }
    }
}
