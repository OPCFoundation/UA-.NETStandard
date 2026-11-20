// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// The <see cref="IEncodeable"/> overload of <see cref="MonitoredItem.SaveValueInCache(IEncodeable, NotificationMessage?, DiagnosticInfo?)"/>
    /// forwards boxed notifications to the typed caches and keeps other encodeables as is.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class MonitoredItemEncodeableCacheTests
    {
        [Test]
        public void BoxedEventFieldListIsCachedAsEvent()
        {
            var item = new MonitoredItem(NUnitTelemetryContext.Create());
            item.NodeClass = NodeClass.Object;
            var message = new NotificationMessage { SequenceNumber = 7 };
            IEncodeable boxed = new EventFieldList { ClientHandle = 3, EventFields = [Variant.From(1u)] };

            item.SaveValueInCache(boxed, message);

            Assert.That(item.LastValue, Is.InstanceOf<EventFieldList>());
            Assert.That(((EventFieldList)item.LastValue!).ClientHandle, Is.EqualTo(3u));
            Assert.That(item.LastMessage, Is.SameAs(message));
        }

        [Test]
        public void OtherEncodeableReplacesCachedEvent()
        {
            var item = new MonitoredItem(NUnitTelemetryContext.Create());
            item.NodeClass = NodeClass.Object;
            item.SaveValueInCache(new EventFieldList { ClientHandle = 3 }, new NotificationMessage());
            IEncodeable other = new Argument { Name = "other" };
            IEncodeable? notified = null;
            item.Notification += (_, e) => notified = e.NotificationValue;

            item.SaveValueInCache(other, new NotificationMessage { SequenceNumber = 9 });

            Assert.That(item.LastValue, Is.SameAs(other));
            Assert.That(notified, Is.SameAs(other));
            Assert.That(item.LastMessage, Is.Null, "the message belongs to an event, and the cached value is no event");
        }
    }
}
