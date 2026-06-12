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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Tests;
using TestMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemManagerTriggeringTests.TestMonitoredItem;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Unit tests for the name-based fluent <c>SetTriggeringAsync</c>
    /// helpers on <see cref="ISubscription"/>. The helpers resolve
    /// triggering / triggered item names against the subscription's
    /// <see cref="IMonitoredItemCollection"/> and forward to the
    /// reference-based core method.
    /// </summary>
    [TestFixture]
    public sealed class SetTriggeringFluentHelperTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_managerContext = new FakeMonitoredItemManagerContext
            {
                Id = 7,
                CreateMonitoredItemFactory = (name, options, context) =>
                    new TestMonitoredItem(context, name,
                        (Opc.Ua.OptionsMonitor<MonitoredItemOptions>)options,
                        m_telemetry.CreateLogger("TestMonitoredItem"))
            };
            m_collection = new MonitoredItemManager(m_managerContext, m_telemetry);
            m_subscription = new FakeManagedSubscription
            {
                Id = 7,
                Created = true,
                MonitoredItems = m_collection
            };
        }

        [TearDown]
        public async Task TearDown()
        {
            await m_subscription.DisposeAsync().ConfigureAwait(false);
            await m_collection.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ParamsOverloadResolvesNamesAndForwardsAsync()
        {
            // Arrange: populate the subscription's collection with
            // three items; the fluent helper resolves their names.
            AddItem("trig");
            AddItem("tgt1");
            AddItem("tgt2");
            IMonitoredItem? capturedTrig = null;
            IReadOnlyCollection<IMonitoredItem>? capturedAdd = null;
            IReadOnlyCollection<IMonitoredItem>? capturedRemove = null;
            m_subscription.OnSetTriggeringAsync =
                (trig, add, remove, ct) =>
                {
                    capturedTrig = trig;
                    capturedAdd = add;
                    capturedRemove = remove;
                    return new ValueTask<SetTriggeringResult>(new SetTriggeringResult(
                        trig,
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        StatusCodes.Good));
                };

            // Act
            await m_subscription.SetTriggeringAsync("trig", "tgt1", "tgt2").ConfigureAwait(false);

            // Assert: helper resolved names, forwarded references.
            Assert.That(capturedTrig?.Name, Is.EqualTo("trig"));
            Assert.That(capturedAdd, Is.Not.Null);
            Assert.That(capturedAdd!, Has.Count.EqualTo(2));
            Assert.That(capturedAdd!.Select(x => x.Name),
                Is.EquivalentTo(new[] { "tgt1", "tgt2" }));
            Assert.That(capturedRemove, Is.Null);
        }

        [Test]
        public async Task AddRemoveOverloadResolvesNamesAndForwardsAsync()
        {
            AddItem("trig");
            AddItem("tgt1");
            AddItem("tgt2");
            IReadOnlyCollection<IMonitoredItem>? capturedAdd = null;
            IReadOnlyCollection<IMonitoredItem>? capturedRemove = null;
            m_subscription.OnSetTriggeringAsync =
                (trig, add, remove, ct) =>
                {
                    capturedAdd = add;
                    capturedRemove = remove;
                    return new ValueTask<SetTriggeringResult>(new SetTriggeringResult(
                        trig,
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        StatusCodes.Good));
                };

            await m_subscription.SetTriggeringAsync("trig",
                add: new[] { "tgt1" },
                remove: new[] { "tgt2" }).ConfigureAwait(false);

            Assert.That(capturedAdd, Has.Count.EqualTo(1));
            Assert.That(capturedRemove, Has.Count.EqualTo(1));
        }

        [Test]
        public void ThrowsOnNullSubscription()
        {
            Assert.That(() => ((ISubscription)null!).SetTriggeringAsync(
                "trig", "tgt"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ThrowsOnEmptyTriggeringName()
        {
            Assert.That(() => m_subscription.SetTriggeringAsync(""),
                Throws.ArgumentException);
            Assert.That(() => m_subscription.SetTriggeringAsync(
                "", add: Array.Empty<string>()),
                Throws.ArgumentException);
        }

        [Test]
        public void ThrowsOnUnknownTriggeringName()
        {
            // No items in subscription — "ghost" doesn't resolve.
            Assert.That(() => m_subscription.SetTriggeringAsync(
                "ghost", "tgt"),
                Throws.ArgumentException);
        }

        [Test]
        public void ThrowsOnUnknownAddName()
        {
            AddItem("trig");
            Assert.That(() => m_subscription.SetTriggeringAsync(
                "trig", "ghost"),
                Throws.ArgumentException);
        }

        [Test]
        public void ThrowsOnUnknownRemoveName()
        {
            AddItem("trig");
            AddItem("tgt");
            Assert.That(() => m_subscription.SetTriggeringAsync(
                "trig",
                add: new[] { "tgt" },
                remove: new[] { "ghost" }),
                Throws.ArgumentException);
        }

        [Test]
        public async Task NullAddAndRemoveAreForwardedAsNullAsync()
        {
            AddItem("trig");
            bool seenNullAdd = false;
            bool seenNullRemove = false;
            m_subscription.OnSetTriggeringAsync =
                (trig, add, remove, ct) =>
                {
                    seenNullAdd = add == null;
                    seenNullRemove = remove == null;
                    return new ValueTask<SetTriggeringResult>(new SetTriggeringResult(
                        trig,
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        Array.Empty<(IMonitoredItem, StatusCode)>(),
                        StatusCodes.Good));
                };

            await m_subscription.SetTriggeringAsync("trig",
                add: null, remove: null).ConfigureAwait(false);

            Assert.That(seenNullAdd, Is.True);
            Assert.That(seenNullRemove, Is.True);
        }

        private IMonitoredItem AddItem(string name)
        {
            m_collection.TryAdd(name, Opc.Ua.OptionsFactory.Create(
                new MonitoredItemOptions
                {
                    StartNodeId = new NodeId(name, 0)
                }),
                out IMonitoredItem? item);
            return item!;
        }

        private ITelemetryContext m_telemetry = null!;
        private FakeMonitoredItemManagerContext m_managerContext = null!;
        private MonitoredItemManager m_collection = null!;
        private FakeManagedSubscription m_subscription = null!;
    }
}
