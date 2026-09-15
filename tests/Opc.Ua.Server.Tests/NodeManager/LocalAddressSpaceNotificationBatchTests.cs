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
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    public sealed class LocalAddressSpaceNotificationBatchTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NotificationsRemainStagedAcrossAwaitUntilExplicitPublicationAsync(bool publish)
        {
            PredefinedNodesAddressSpace space = Create();
            var notifications = new List<string>();
            space.NodeAdded += _ => notifications.Add("added");
            space.NodeRemoved += _ => notifications.Add("removed");
            using LocalAddressSpaceNotificationBatch batch = space.BeginNotificationBatch();
            space.NotifyAdded(new BaseObjectState(null) { NodeId = new NodeId("node", 2) });
            await Task.Yield();
            space.NotifyRemoved(new NodeId("node", 2));
            Assert.That(notifications, Is.Empty);
            if (publish)
            {
                batch.Publish();
            }
            else
            {
                batch.Dispose();
            }
            Assert.That(notifications, Is.EqualTo(publish ? s_expected : Array.Empty<string>()));
            Assert.That(batch.Publish, Throws.InvalidOperationException);
            space.NotifyRemoved(new NodeId("outside", 2));
            Assert.That(notifications, Has.Count.EqualTo(publish ? 3 : 1));
        }

        [Test]
        public void NestedBatchesAreRejectedAndDisposalRestoresNormalNotificationDelivery()
        {
            PredefinedNodesAddressSpace space = Create();
            using (LocalAddressSpaceNotificationBatch batch = space.BeginNotificationBatch())
            {
                Assert.That(() => space.BeginNotificationBatch(), Throws.InvalidOperationException);
            }
            using LocalAddressSpaceNotificationBatch next = space.BeginNotificationBatch();
            Assert.That(next.Publish, Throws.Nothing);
        }

        [Test]
        public void ExpectedObserverFailureDoesNotPreventLaterNotifications()
        {
            PredefinedNodesAddressSpace space = Create();
            int removed = 0;
            space.NodeAdded += _ => throw new InvalidOperationException("observer failure");
            space.NodeRemoved += _ => removed++;
            using LocalAddressSpaceNotificationBatch batch = space.BeginNotificationBatch();
            space.NotifyAdded(new BaseObjectState(null) { NodeId = new NodeId("node", 2) });
            space.NotifyRemoved(new NodeId("node", 2));
            AggregateException exception = Assert.Throws<AggregateException>(batch.Publish);
            Assert.Multiple(() =>
            {
                Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
                Assert.That(removed, Is.EqualTo(1));
            });
        }

        private static PredefinedNodesAddressSpace Create()
        {
            return new PredefinedNodesAddressSpace(new SystemContext(NUnitTelemetryContext.Create()), [],
                (_, _) => default, (_, _) => new ValueTask<bool>(false));
        }

        private static readonly string[] s_expected = ["added", "removed"];
    }
}
