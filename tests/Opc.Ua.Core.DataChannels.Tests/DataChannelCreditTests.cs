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

using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelCreditTests
    {
        [Test]
        public void GrantThenConsumeDecrementsAvailableByPayloadLength()
        {
            var window = new DataChannelSendWindow();

            Assert.That(window.TryGrant(1024), Is.True);
            Assert.That(window.TryConsume(300), Is.True);

            Assert.That(window.Available, Is.EqualTo(724u));
        }

        [Test]
        public void BlockedPayloadFailsAndCountsOneRealStall()
        {
            var window = new DataChannelSendWindow(100);

            Assert.Multiple(() =>
            {
                Assert.That(window.IsBlockedBy(100), Is.False);
                Assert.That(window.IsBlockedBy(101), Is.True);
                Assert.That(window.TryConsume(101), Is.False);
                Assert.That(window.Available, Is.EqualTo(100u));
                Assert.That(window.Stalls, Is.EqualTo(1u));
            });
        }

        [Test]
        public void StallsCountsOnlyFailedPositiveConsumes()
        {
            var window = new DataChannelSendWindow(10);

            Assert.Multiple(() =>
            {
                Assert.That(window.TryConsume(0), Is.True);
                Assert.That(window.TryConsume(-1), Is.True);
                Assert.That(window.TryConsume(4), Is.True);
                Assert.That(window.Stalls, Is.Zero);
                Assert.That(window.TryConsume(7), Is.False);
                Assert.That(window.TryGrant(10), Is.True);
                Assert.That(window.TryConsume(7), Is.True);
                Assert.That(window.Stalls, Is.EqualTo(1u));
            });
        }

        [Test]
        public void GrantThatWouldOverflowIsRefusedAndDoesNotWrap()
        {
            var window = new DataChannelSendWindow(uint.MaxValue - 1);

            Assert.That(window.TryGrant(1), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(window.Available, Is.EqualTo(uint.MaxValue));
                Assert.That(window.TryGrant(1), Is.False);
                Assert.That(window.Available, Is.EqualTo(uint.MaxValue));
                Assert.That(window.IsBlockedBy(int.MaxValue), Is.False);
            });
        }

        [Test]
        public void ResetReturnsFreshWindowToInitialCreditState()
        {
            var window = new DataChannelSendWindow(256);

            Assert.That(window.TryConsume(128), Is.True);
            window.Reset();

            Assert.Multiple(() =>
            {
                Assert.That(window.Available, Is.Zero);
                Assert.That(window.Stalls, Is.Zero);
                Assert.That(window.IsBlockedBy(1), Is.True);
            });
        }

        [Test]
        public void ReplenishmentIsHeldUntilThresholdThenGrantsReleasedBytes()
        {
            var credit = new DataChannelReceiveCredit(1024);

            Assert.That(credit.TryAccount(400), Is.True);
            credit.Release(400);

            Assert.That(credit.TryTakeReplenishment(100, out uint amount), Is.False);
            Assert.That(amount, Is.Zero);

            Assert.That(credit.TryAccount(200), Is.True);
            credit.Release(200);

            Assert.That(credit.TryTakeReplenishment(100, out amount), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(amount, Is.EqualTo(600u));
                Assert.That(amount % 100, Is.Zero);
                Assert.That(credit.Outstanding, Is.EqualTo(1024u));
                Assert.That(credit.Released, Is.Zero);
                Assert.That(credit.LastGrant, Is.EqualTo(600u));
            });
        }

        [Test]
        public void ReceiveAccountingRejectsOverrunAndReleaseSaturatesSafely()
        {
            var credit = new DataChannelReceiveCredit(100);

            Assert.That(credit.TryAccount(30), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
                Assert.That(credit.TryAccount(80), Is.False);
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
            });

            credit.Release(30);
            credit.Release(int.MaxValue);
            credit.Release(int.MaxValue);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(70u));
                Assert.That(credit.Released, Is.EqualTo(uint.MaxValue));
            });
        }

        [Test]
        public void GrantSaturatesReceiveOutstandingRatherThanWrapping()
        {
            var credit = new DataChannelReceiveCredit(uint.MaxValue - 10);

            credit.Grant(20);

            Assert.Multiple(() =>
            {
                Assert.That(credit.Outstanding, Is.EqualTo(uint.MaxValue));
                Assert.That(credit.LastGrant, Is.EqualTo(20u));
            });
        }
    }
}
