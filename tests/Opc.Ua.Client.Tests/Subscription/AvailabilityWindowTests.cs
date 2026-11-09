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
