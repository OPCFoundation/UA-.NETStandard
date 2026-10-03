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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    public sealed class XRegistryInvalidationTests
    {
        [Test]
        public async Task ANotificationBurstUsesOneCoalescedFullRepairHintAsync()
        {
            var filter = new EventFilter
            {
                SelectClauses =
                    [new SimpleAttributeOperand { BrowsePath = [new QualifiedName(BrowseNames.Subject, 1)] }]
            };
            var notifier = new XRegistryOpcUaEndpoint.InvalidationNotifier(filter);
            ISubscription subscription = Mock.Of<ISubscription>();
            for (int index = 0; index < 1000; index++)
            {
                await notifier.OnEventDataNotificationAsync(subscription, 1, DateTime.UtcNow,
                    new[] { new EventNotification(null, [Variant.From("/groups/" + index)]) },
                    PublishState.None, []).ConfigureAwait(false);
            }
            await using IAsyncEnumerator<XRegistryChangeHint> reader = notifier.ReadAllAsync(CancellationToken.None)
                .GetAsyncEnumerator();
            Assert.That(await reader.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.Current.Path, Is.EqualTo("/groups/999"));
                Assert.That(reader.Current.RequiresFullInventory, Is.True);
            });
            Task<bool> next = reader.MoveNextAsync().AsTask();
            Assert.That(next.IsCompleted, Is.False, "A burst must not retain an unbounded event backlog.");
            notifier.Complete();
            Assert.That(await next.ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task AConnectionChangeEndsTheOldStreamAfterRequiringFullRepairAsync()
        {
            var notifier = new XRegistryOpcUaEndpoint.InvalidationNotifier(new EventFilter());
            notifier.ConnectionLost();
            await using IAsyncEnumerator<XRegistryChangeHint> reader = notifier.ReadAllAsync(CancellationToken.None)
                .GetAsyncEnumerator();
            Assert.That(await reader.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(reader.Current.Reason, Is.EqualTo("connection-or-namespace-changed"));
            Assert.That(reader.Current.RequiresFullInventory, Is.True);
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reader.MoveNextAsync().ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public async Task ModelChangesInvalidateTheOldFilterAndNamespaceMappingAsync()
        {
            var type = new NodeId(100, 2);
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand { BrowsePath = [new QualifiedName(Ua.BrowseNames.EventType)] },
                    new SimpleAttributeOperand { BrowsePath = [new QualifiedName(BrowseNames.Subject, 2)] }
                ]
            };
            var notifier = new XRegistryOpcUaEndpoint.InvalidationNotifier(filter, [type]);
            await notifier.OnEventDataNotificationAsync(Mock.Of<ISubscription>(), 1, DateTime.UtcNow,
                new[] { new EventNotification(null, [Variant.From(type), Variant.From("/")]) },
                PublishState.None, []).ConfigureAwait(false);
            await using IAsyncEnumerator<XRegistryChangeHint> reader = notifier.ReadAllAsync(CancellationToken.None)
                .GetAsyncEnumerator();
            Assert.That(await reader.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(reader.Current.Reason, Is.EqualTo("model-changed"));
            Assert.That(reader.Current.RequiresFullInventory, Is.True);
            Assert.ThrowsAsync<ServiceResultException>(async () => await reader.MoveNextAsync().ConfigureAwait(false));
        }
    }
}
