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

using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies the subscriber context shared by the monitored items of a session.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SubscriberContextTests
    {
        /// <summary>
        /// Items of one session share one context, and its system context copy per template.
        /// </summary>
        [Test]
        public void ItemsOfOneSessionShareOneContext()
        {
            var subscriber = new Subscriber();
            IMonitoredItem first = subscriber.CreateItem();
            IMonitoredItem second = subscriber.CreateItem();
            var template = new ServerSystemContext(DeterministicServerMock.Create(out MonitoredItemQueueFactory queues).Object);
            using (queues)
            {
                OperationContext context = MonitoredReadAuthorization.GetSubscriberContext(first);

                Assert.That(MonitoredReadAuthorization.GetSubscriberContext(second), Is.SameAs(context));
                Assert.That(context.Session, Is.SameAs(subscriber.Session.Object));
                Assert.That(context.UserIdentity, Is.SameAs(subscriber.Identity));
                Assert.That(context.ChannelContext?.EndpointDescription, Is.SameAs(subscriber.Endpoint));
                Assert.That(context.GetSystemContext(template), Is.SameAs(
                    MonitoredReadAuthorization.GetSubscriberContext(second).GetSystemContext(template)));
            }
        }

        /// <summary>
        /// A new identity, identity refresh or endpoint replaces the shared context.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ChangedSubscriberInputReplacesContext(int change)
        {
            var subscriber = new Subscriber();
            IMonitoredItem item = subscriber.CreateItem();
            OperationContext before = MonitoredReadAuthorization.GetSubscriberContext(item);

            switch (change)
            {
                case 0:
                    subscriber.Identity = new UserIdentity();
                    break;
                case 1:
                    subscriber.Generation++;
                    break;
                case 2:
                    subscriber.Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
                    break;
                default:
                    subscriber.SecureChannelId = "replacement-channel";
                    break;
            }
            OperationContext after = MonitoredReadAuthorization.GetSubscriberContext(item);

            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.UserIdentity, Is.SameAs(subscriber.Identity));
            Assert.That(after.ChannelContext?.EndpointDescription, Is.SameAs(subscriber.Endpoint));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(item), Is.SameAs(after));
        }

        /// <summary>
        /// Switching an item to sessionless delivery does not reuse its prior session context.
        /// </summary>
        [Test]
        public void SessionlessItemDoesNotReusePriorSessionContext()
        {
            var subscriber = new Subscriber();
            IMonitoredItem item = subscriber.CreateItem();
            OperationContext sessionContext = MonitoredReadAuthorization.GetSubscriberContext(item);
            subscriber.ActiveSession = null;

            OperationContext sessionlessContext = MonitoredReadAuthorization.GetSubscriberContext(item);

            Assert.That(sessionlessContext, Is.Not.SameAs(sessionContext));
            Assert.That(sessionlessContext.Session, Is.Null);
            Assert.That(sessionlessContext.UserIdentity, Is.SameAs(subscriber.Identity));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(item), Is.SameAs(sessionlessContext));
        }

        /// <summary>
        /// A modeled custom session whose identity, identity refresh generation and endpoint can change.
        /// </summary>
        private sealed class Subscriber
        {
            public Subscriber()
            {
                Session.SetupGet(value => value.Id).Returns(new NodeId(1, 1));
                Session.SetupGet(value => value.SecureChannelId).Returns(() => SecureChannelId);
                Session.SetupGet(value => value.EffectiveIdentity).Returns(() => Identity);
                Session.SetupGet(value => value.EndpointDescription).Returns(() => Endpoint);
                Session.Setup(value => value.CaptureIdentityRefreshSnapshot())
                    .Returns(() => new IdentityRefreshSnapshot(Identity, Generation));
            }

            public Mock<ISession> Session { get; } = new();

            public IUserIdentity Identity { get; set; } = new UserIdentity();

            public long Generation { get; set; }

            public string SecureChannelId { get; set; } = "initial-channel";

            public ISession? ActiveSession { get; set; }

            public EndpointDescription Endpoint { get; set; } =
                new() { SecurityMode = MessageSecurityMode.SignAndEncrypt };

            public IMonitoredItem CreateItem()
            {
                var item = new Mock<IMonitoredItem>();
                item.SetupGet(value => value.Session).Returns(() => ActiveSession!);
                item.SetupGet(value => value.EffectiveIdentity).Returns(() => Identity);
                ActiveSession = Session.Object;
                return item.Object;
            }
        }
    }
}
