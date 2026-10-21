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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies when a source emission may be admitted without the master node manager's lock.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NotificationDispatchStateTests
    {
        /// <summary>
        /// An open generation admits without the lock and counts the lease until it is released.
        /// </summary>
        [Test]
        public void OpenGenerationAdmitsAndCountsLeaseWithoutLock()
        {
            MasterNodeManager.NotificationDispatchState state = CreateState();

            Assert.That(state.TryAdmitFast(out int slot, out bool releaseRequired), Is.True);
            Assert.That(releaseRequired, Is.False);
            Assert.That(state.FastDispatches, Is.EqualTo(1));

            Assert.That(state.ReleaseFast(slot), Is.False);
            Assert.That(state.FastDispatches, Is.Zero);
        }

        /// <summary>
        /// A reserved cutoff sends admission to the locked path before its routes are published, so
        /// routing publication and the cutoff flag are observed together.
        /// </summary>
        [Test]
        public void ReservedCutoffClosesAdmissionWithoutLock()
        {
            MasterNodeManager.NotificationDispatchState state = CreateState();
            state.EmissionCutoffReservations = 1;

            Assert.That(state.TryAdmitFast(out _, out bool releaseRequired), Is.False);
            Assert.That(releaseRequired, Is.False);
            Assert.That(state.FastDispatches, Is.Zero);

            state.EmissionCutoffReservations = 0;
            Assert.That(state.TryAdmitFast(out int slot, out _), Is.True);
            state.ReleaseFast(slot);
        }

        /// <summary>
        /// A cutoff that reserves, publishes and releases between the two gate checks of an
        /// admission rejects it, although the reservation is released again when it is rechecked.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void CutoffRunningBetweenGateChecksRejectsAdmission(bool publish)
        {
            MasterNodeManager.NotificationDispatchState state = CreateState();

            bool admitted = state.TryAdmitFast(out _, out bool releaseRequired, () =>
            {
                state.EmissionCutoffReservations++;
                if (publish)
                {
                    state.BusinessEmissionsEnabled = false;
                }
                state.EmissionCutoffReservations--;
            });

            Assert.That(admitted, Is.False);
            Assert.That(releaseRequired, Is.False);
            Assert.That(state.FastDispatches, Is.Zero);
            Assert.That(state.TryAdmitFast(out int slot, out _), Is.EqualTo(!publish));
            if (!publish)
            {
                state.ReleaseFast(slot);
            }
        }

        /// <summary>
        /// A disabled generation and a waiting drain also close admission without the lock.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CutoffOrWaitingDrainClosesAdmissionWithoutLock(bool drain)
        {
            MasterNodeManager.NotificationDispatchState state = CreateState();
            if (drain)
            {
                state.DispatchesDrained = new TaskCompletionSource<bool>();
            }
            else
            {
                state.BusinessEmissionsEnabled = false;
            }

            Assert.That(state.TryAdmitFast(out _, out _), Is.False);
            Assert.That(state.FastDispatches, Is.Zero);
        }

        /// <summary>
        /// Releasing a lease admitted before a drain reports the drain so it can complete.
        /// </summary>
        [Test]
        public void ReleaseAfterDrainStartedReportsWaitingDrain()
        {
            MasterNodeManager.NotificationDispatchState state = CreateState();
            Assert.That(state.TryAdmitFast(out int slot, out _), Is.True);
            state.DispatchesDrained = new TaskCompletionSource<bool>();

            Assert.That(state.FastDispatches, Is.EqualTo(1));
            Assert.That(state.ReleaseFast(slot), Is.True);
            Assert.That(state.FastDispatches, Is.Zero);
        }

        private static MasterNodeManager.NotificationDispatchState CreateState()
        {
            return new MasterNodeManager.NotificationDispatchState(new Mock<IAsyncNodeManager>().Object);
        }
    }
}
