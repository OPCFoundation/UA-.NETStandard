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

#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Unit tests for the snapshot-trust guard in
    /// <see cref="ManagedSessionExtensions.ValidateAndSortGroup"/> —
    /// the security fix that ensures all snapshots sharing a
    /// <see cref="SubscriptionStateSnapshot.LogicalGroupId"/> agree
    /// on every subscription-wide option before the manager loads
    /// them as a single multi-partition logical subscription.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SnapshotGroupValidationTests
    {
        [Test]
        public void ValidateAndSortGroupSortsByPartitionIndex()
        {
            // Out-of-order input: index 2, then 0, then 1.
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 2),
                NewSnap(partitionIndex: 0),
                NewSnap(partitionIndex: 1)
            };

            List<SubscriptionStateSnapshot> sorted =
                ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket);

            Assert.That(sorted, Has.Count.EqualTo(3));
            Assert.That(sorted[0].PartitionIndex, Is.Zero);
            Assert.That(sorted[1].PartitionIndex, Is.EqualTo(1));
            Assert.That(sorted[2].PartitionIndex, Is.EqualTo(2));
        }

        [Test]
        public void ValidateAndSortGroupAcceptsSinglePartition()
        {
            var bucket = new List<SubscriptionStateSnapshot> { NewSnap() };

            List<SubscriptionStateSnapshot> sorted =
                ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket);

            Assert.That(sorted, Has.Count.EqualTo(1));
        }

        [Test]
        public void ValidateAndSortGroupRejectsDuplicatePartitionIndex()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0),
                NewSnap(partitionIndex: 0)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ValidateAndSortGroupRejectsPartitionIndexGap()
        {
            // 0, 2 — missing 1.
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0),
                NewSnap(partitionIndex: 2)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ValidateAndSortGroupRejectsPublishingIntervalMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, publishingIntervalMs: 500),
                NewSnap(partitionIndex: 1, publishingIntervalMs: 250)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("PublishingIntervalMs"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsKeepAliveCountMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, keepAliveCount: 10),
                NewSnap(partitionIndex: 1, keepAliveCount: 20)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("KeepAliveCount"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsLifetimeCountMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, lifetimeCount: 100),
                NewSnap(partitionIndex: 1, lifetimeCount: 200)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("LifetimeCount"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsMaxNotificationsPerPublishMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, maxNotificationsPerPublish: 1000),
                NewSnap(partitionIndex: 1, maxNotificationsPerPublish: 2000)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("MaxNotificationsPerPublish"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsMinLifetimeIntervalMsMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, minLifetimeIntervalMs: 1000),
                NewSnap(partitionIndex: 1, minLifetimeIntervalMs: 2000)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("MinLifetimeIntervalMs"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsPriorityMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, priority: 0),
                NewSnap(partitionIndex: 1, priority: 5)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("Priority"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsPublishingEnabledMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, publishingEnabled: true),
                NewSnap(partitionIndex: 1, publishingEnabled: false)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("PublishingEnabled"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsDisabledMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, disabled: false),
                NewSnap(partitionIndex: 1, disabled: true)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("Disabled"));
        }

        [Test]
        public void ValidateAndSortGroupRejectsSendInitialValuesOnTransferMismatch()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0, sendInitialValuesOnTransfer: true),
                NewSnap(partitionIndex: 1, sendInitialValuesOnTransfer: false)
            };

            var ex = Assert.Throws<ServiceResultException>(
                () => ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("SendInitialValuesOnTransfer"));
        }

        [Test]
        public void ValidateAndSortGroupAcceptsAllOptionsMatching()
        {
            var bucket = new List<SubscriptionStateSnapshot>
            {
                NewSnap(partitionIndex: 0),
                NewSnap(partitionIndex: 1),
                NewSnap(partitionIndex: 2)
            };

            List<SubscriptionStateSnapshot> sorted =
                ManagedSessionExtensions.ValidateAndSortGroup("g1", bucket);

            Assert.That(sorted, Has.Count.EqualTo(3));
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0)
                {
                    Assert.That(sorted[i].PartitionIndex, Is.Zero);
                }
                else
                {
                    Assert.That(sorted[i].PartitionIndex, Is.EqualTo(i));
                }
            }
        }

        private static SubscriptionStateSnapshot NewSnap(
            int partitionIndex = 0,
            int publishingIntervalMs = 500,
            uint keepAliveCount = 10,
            uint lifetimeCount = 100,
            uint maxNotificationsPerPublish = 1000,
            int minLifetimeIntervalMs = 1000,
            byte priority = 0,
            bool publishingEnabled = true,
            bool disabled = false,
            bool sendInitialValuesOnTransfer = false)
        {
            return new SubscriptionStateSnapshot
            {
                LogicalGroupId = "g1",
                PartitionIndex = partitionIndex,
                PublishingIntervalMs = publishingIntervalMs,
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                MaxNotificationsPerPublish = maxNotificationsPerPublish,
                MinLifetimeIntervalMs = minLifetimeIntervalMs,
                Priority = priority,
                PublishingEnabled = publishingEnabled,
                Disabled = disabled,
                SendInitialValuesOnTransfer = sendInitialValuesOnTransfer
            };
        }
    }
}
