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
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Checks the serial number arithmetic and receive window of
    /// Part 6 errata 5.2.1.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelSequencingTests
    {
        private const uint Last = uint.MaxValue;

        [Test]
        public void ModulusExcludesZeroSoTheWrapIsADistanceOfOne()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DataChannelSequence.Distance(Last, 1), Is.EqualTo(1u));
                Assert.That(DataChannelSequence.IsAfter(1, Last), Is.True);
                Assert.That(DataChannelSequence.Next(Last), Is.EqualTo(1u));
                Assert.That(DataChannelSequence.Previous(1), Is.EqualTo(Last));
            });
        }

        [Test]
        public void AValueIsNeverAfterItself()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DataChannelSequence.IsAfter(5, 5), Is.False);
                Assert.That(DataChannelSequence.Distance(5, 5), Is.Zero);
            });
        }

        [Test]
        public void HalfTheSpaceAheadIsAfterAndBeyondItIsBehind()
        {
            const uint half = int.MaxValue;

            Assert.Multiple(() =>
            {
                Assert.That(DataChannelSequence.IsAfter(1 + half, 1), Is.True);
                Assert.That(DataChannelSequence.IsAfter(2 + half, 1), Is.False);
            });
        }

        [Test]
        public void AdvanceWrapsOverTheExcludedZero()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DataChannelSequence.Advance(Last, 1), Is.EqualTo(1u));
                Assert.That(DataChannelSequence.Advance(Last - 1, 3), Is.EqualTo(2u));
                Assert.That(DataChannelSequence.Advance(1, 0), Is.EqualTo(1u));
            });
        }

        // DCF-017: the sequence number wraps without a reported gap.
        [Test]
        public void DcF017WrapDoesNotReportAGap()
        {
            var window = new DataChannelReceiveWindow();

            Assert.That(
                window.Accept(Last, out _, out _),
                Is.EqualTo(DataChannelReceiveOutcome.Deliver));

            Assert.Multiple(() =>
            {
                Assert.That(
                    window.Accept(1, out uint from, out uint to),
                    Is.EqualTo(DataChannelReceiveOutcome.Deliver));
                Assert.That(from, Is.Zero);
                Assert.That(to, Is.Zero);
                Assert.That(window.HighestReceived, Is.EqualTo(1u));
            });
        }

        // DCF-018: a duplicate inside the replay window is discarded
        // silently and is never reported as a gap.
        [Test]
        public void DcF018DuplicateInsideTheReplayWindowIsDiscardedSilently()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(10, out _, out _);
            window.Accept(11, out _, out _);

            Assert.That(
                window.Accept(11, out uint from, out uint to),
                Is.EqualTo(DataChannelReceiveOutcome.DiscardDuplicate));

            Assert.Multiple(() =>
            {
                Assert.That(from, Is.Zero);
                Assert.That(to, Is.Zero);
                Assert.That(window.HighestReceived, Is.EqualTo(11u));
            });
        }

        [Test]
        public void AFrameBehindTheReplayWindowResetsTheChannel()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(1000, out _, out _);

            Assert.That(
                window.Accept(1, out _, out _),
                Is.EqualTo(DataChannelReceiveOutcome.Reset));
        }

        [Test]
        public void AFrameAheadByMoreThanOneReportsTheInterveningRange()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(10, out _, out _);

            Assert.That(
                window.Accept(14, out uint from, out uint to),
                Is.EqualTo(DataChannelReceiveOutcome.DeliverWithGap));

            Assert.Multiple(() =>
            {
                Assert.That(from, Is.EqualTo(11u));
                Assert.That(to, Is.EqualTo(13u));
                Assert.That(window.HighestReceived, Is.EqualTo(14u));
            });
        }

        // DCF-019: a control frame does not advance HighestReceived, so a
        // surviving lower numbered frame is still delivered.
        [Test]
        public void DcF019GapDoesNotAdvanceHighestReceived()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(1, out _, out _);

            // The sender expired frames 3 and 5 while frame 2 survived.
            window.RecordGap(3, 3);
            window.RecordGap(5, 5);

            Assert.Multiple(() =>
            {
                Assert.That(window.HighestReceived, Is.EqualTo(1u));
                Assert.That(
                    window.Accept(2, out _, out _),
                    Is.EqualTo(DataChannelReceiveOutcome.Deliver));
            });
        }

        // DCP-004: a frame whose number was named by a GAP is discarded
        // without delivery.
        [Test]
        public void DcP004FrameInsideANamedRunIsDiscarded()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(1, out _, out _);
            window.RecordGap(2, 4);

            Assert.Multiple(() =>
            {
                Assert.That(
                    window.Accept(3, out _, out _),
                    Is.EqualTo(DataChannelReceiveOutcome.DiscardGapped));
                Assert.That(
                    window.Accept(5, out _, out _),
                    Is.EqualTo(DataChannelReceiveOutcome.DeliverWithGap));
            });
        }

        [Test]
        public void AGapBeforeTheFirstDataInitializesHighestReceivedBelowTheRun()
        {
            var window = new DataChannelReceiveWindow();
            window.RecordGap(5, 7);

            Assert.Multiple(() =>
            {
                Assert.That(window.IsInitialized, Is.True);
                Assert.That(window.HighestReceived, Is.EqualTo(4u));
                Assert.That(
                    window.Accept(8, out _, out _),
                    Is.EqualTo(DataChannelReceiveOutcome.DeliverWithGap));
            });
        }

        // DCF-031: retained GAP runs are absolutely bounded and the oldest
        // is discarded once the bound is reached.
        [Test]
        public void DcF031RetainedGapRunsAreAbsolutelyBounded()
        {
            const int maxRuns = 8;
            var window = new DataChannelReceiveWindow(
                DataChannelConstants.MinReplayWindow,
                maxRuns);

            for (uint ii = 1; ii <= maxRuns + 1; ii++)
            {
                window.RecordGap(ii * 100, ii * 100);
            }

            Assert.Multiple(() =>
            {
                Assert.That(window.RetainedGapRuns, Is.EqualTo(maxRuns));
                Assert.That(window.EvictedGapRuns, Is.EqualTo(1L));
                Assert.That(window.IsGapped(100), Is.False, "the oldest run was discarded");
                Assert.That(window.IsGapped(200), Is.True);
            });
        }

        [Test]
        public void ARunFallsAwayOnceHighestReceivedHasMovedWellPastIt()
        {
            var window = new DataChannelReceiveWindow();
            window.Accept(1, out _, out _);
            window.RecordGap(2, 2);

            Assert.That(window.RetainedGapRuns, Is.EqualTo(1));

            window.Accept(3, out _, out _);
            window.Accept(200, out _, out _);

            Assert.That(window.RetainedGapRuns, Is.Zero);
        }

        // DCF-034: SequenceNumber exhaustion forces renewal, and a sender
        // stalls rather than reusing a value under one TokenId.
        [Test]
        public void DcF034RenewalIsDueBeforeTheSpaceIsExhausted()
        {
            var budget = new SequenceNumberBudget(capacity: 1000);

            Assert.That(budget.ShouldRenew, Is.False, "a fresh token has the whole space");

            for (int ii = 0; ii < 1000; ii++)
            {
                Assert.That(budget.TryConsume(), Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(budget.Remaining, Is.Zero);
                Assert.That(budget.ShouldRenew, Is.True);
                Assert.That(budget.MustStall, Is.True);
            });
        }

        [Test]
        public void DcF034ASenderStallsRatherThanReusingASequenceNumber()
        {
            var budget = new SequenceNumberBudget(capacity: 4);

            for (int ii = 0; ii < 4; ii++)
            {
                Assert.That(budget.TryConsume(), Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    budget.TryConsume(),
                    Is.False,
                    "the chunk that would reuse a value is refused, not renumbered");
                Assert.That(
                    budget.Consumed,
                    Is.EqualTo(4L),
                    "a refused chunk does not consume from the budget");
            });
        }

        [Test]
        public void ANewTokenRestoresTheWholeSpace()
        {
            var budget = new SequenceNumberBudget(capacity: 4);

            for (int ii = 0; ii < 4; ii++)
            {
                budget.TryConsume();
            }

            Assert.That(budget.MustStall, Is.True);

            budget.OnTokenActivated();

            Assert.Multiple(() =>
            {
                Assert.That(budget.MustStall, Is.False);
                Assert.That(budget.Consumed, Is.Zero);
                Assert.That(budget.Remaining, Is.EqualTo(4L));
            });
        }

        [Test]
        public void TheRenewalThresholdIsTheLesserOfTheFixedHeadroomAndOneMinuteOfTraffic()
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var budget = new SequenceNumberBudget(clock);

            // Ten chunks over ten seconds is one per second, so one
            // minute of traffic is sixty values: far below the fixed
            // 2^30 headroom, which is what keeps a slow channel from
            // renewing needlessly.
            for (int ii = 0; ii < 10; ii++)
            {
                budget.TryConsume();
            }

            clock.Advance(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(budget.ChunksPerSecond, Is.EqualTo(1d).Within(0.01));
                Assert.That(
                    budget.RenewalThreshold,
                    Is.EqualTo(60L),
                    "the rate based threshold wins while it is the smaller of the two");
                Assert.That(budget.ShouldRenew, Is.False);
            });
        }

        [Test]
        public void TheFixedHeadroomCapsTheThresholdForAVeryFastChannel()
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var budget = new SequenceNumberBudget(clock);

            // A hundred million chunks in one second is a rate whose
            // minute of traffic exceeds the 32 bit space entirely, so the
            // fixed headroom is what bounds the threshold.
            for (int ii = 0; ii < 100_000_000; ii += 1_000_000)
            {
                for (int jj = 0; jj < 1_000_000; jj += 1_000_000)
                {
                    budget.TryConsume();
                }
            }

            clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.That(
                budget.RenewalThreshold,
                Is.LessThanOrEqualTo(DataChannelConstants.SequenceNumberRenewalHeadroom));
        }
    }
}
