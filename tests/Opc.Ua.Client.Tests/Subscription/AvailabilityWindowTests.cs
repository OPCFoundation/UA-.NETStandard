// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class AvailabilityWindowTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(300)]
        public void ContainsShouldFindExactlyTheListedNumbers(int count)
        {
            var window = new SubscriptionManager.AvailabilityWindow();

            window.Update(1, 1000, Numbers(100, count), false);

            for (int i = 0; i < count; i++)
            {
                Assert.That(window.Contains((uint)(100 + i)), Is.True);
            }
            Assert.That(window.Contains(99), Is.False);
            Assert.That(window.Contains((uint)(100 + count)), Is.False);
        }

        [TestCase(300, 3)]
        [TestCase(3, 300)]
        [TestCase(5, 2)]
        [TestCase(300, 20)]
        public void UpdateShouldReplaceThePreviousView(int firstCount, int secondCount)
        {
            var window = new SubscriptionManager.AvailabilityWindow();

            window.Update(1, 1000, Numbers(100, firstCount), false);
            window.Update(2, 2000, Numbers(5000, secondCount), true);

            Assert.That(window.MessageGeneration, Is.EqualTo(2));
            Assert.That(window.Horizon, Is.EqualTo(2000));
            Assert.That(window.RetransmissionQueueUnsupported, Is.True);
            Assert.That(window.Contains(100), Is.False);
            Assert.That(window.Contains((uint)(100 + firstCount - 1)), Is.False);
            Assert.That(window.Contains(5000), Is.True);
            Assert.That(window.Contains((uint)(5000 + secondCount - 1)), Is.True);
            Assert.That(window.Contains((uint)(5000 + secondCount)), Is.False);
        }

        [Test]
        public void UpdateShouldNotKeepTheCallersArray()
        {
            var window = new SubscriptionManager.AvailabilityWindow();
            uint[] numbers = [7, 8];

            window.Update(1, 1000, numbers.ToArrayOf(), false);
            numbers[0] = 9;

            Assert.That(window.Contains(7), Is.True);
            Assert.That(window.Contains(9), Is.False);
        }

        private static ArrayOf<uint> Numbers(int start, int count)
        {
            return Enumerable.Range(start, count).Select(i => (uint)i).ToArray().ToArrayOf();
        }
    }
}
