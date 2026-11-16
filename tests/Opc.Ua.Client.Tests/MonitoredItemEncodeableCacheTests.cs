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
