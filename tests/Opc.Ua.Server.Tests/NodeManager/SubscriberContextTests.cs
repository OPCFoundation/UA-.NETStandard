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
                    subscriber.SecureChannelId = "channel-2";
                    break;
                default:
                    subscriber.Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
                    break;
            }
            OperationContext after = MonitoredReadAuthorization.GetSubscriberContext(item);

            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.UserIdentity, Is.SameAs(subscriber.Identity));
            Assert.That(after.ChannelContext?.EndpointDescription, Is.SameAs(subscriber.Endpoint));
            Assert.That(after.ChannelContext?.SecureChannelId, Is.EqualTo(subscriber.SecureChannelId));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(item), Is.SameAs(after));
        }

        /// <summary>
        /// The system context copy of every node manager stays the same object across calls, also
        /// when a session's items span more node managers than a request's copy cache keeps.
        /// </summary>
        [Test]
        public void SystemContextCopyIsStableForManyNodeManagers()
        {
            var subscriber = new Subscriber();
            IMonitoredItem item = subscriber.CreateItem();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var templates = new ServerSystemContext[20];
                var copies = new ServerSystemContext[templates.Length];
                for (int ii = 0; ii < templates.Length; ii++)
                {
                    templates[ii] = new ServerSystemContext(server.Object);
                    copies[ii] = MonitoredReadAuthorization.GetSubscriberSystemContext(item, templates[ii]);
                }

                for (int ii = 0; ii < templates.Length; ii++)
                {
                    ServerSystemContext again = MonitoredReadAuthorization.GetSubscriberSystemContext(item, templates[ii]);
                    Assert.That(again, Is.SameAs(copies[ii]));
                    Assert.That(again.OperationContext, Is.SameAs(MonitoredReadAuthorization.GetSubscriberContext(item)));
                }
            }
        }

        /// <summary>
        /// A sessionless item never reuses a context that was created while it had a session.
        /// </summary>
        [Test]
        public void SessionlessItemDoesNotReuseSessionBoundContext()
        {
            var subscriber = new Subscriber();
            ISession? owner = subscriber.Session.Object;
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.EffectiveIdentity).Returns(subscriber.Identity);

            // the session detaches between the sessionless check and the context's creation
            bool first = true;
            item.SetupGet(value => value.Session).Returns(() =>
            {
                if (first)
                {
                    first = false;
                    return null!;
                }
                return owner!;
            });
            OperationContext bound = MonitoredReadAuthorization.GetSubscriberContext(item.Object);
            Assert.That(bound.Session, Is.SameAs(subscriber.Session.Object));

            owner = null;
            OperationContext sessionless = MonitoredReadAuthorization.GetSubscriberContext(item.Object);

            Assert.That(sessionless, Is.Not.SameAs(bound));
            Assert.That(sessionless.Session, Is.Null);
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(item.Object), Is.SameAs(sessionless));
        }

        /// <summary>
        /// A sampling group's diagnostics mask gets a shared context of its own, reused like the
        /// default one and replaced by the same input changes, without replacing the default one.
        /// </summary>
        [Test]
        public void DiagnosticsMaskGetsItsOwnSharedContext()
        {
            var subscriber = new Subscriber();
            IMonitoredItem first = subscriber.CreateItem();
            IMonitoredItem second = subscriber.CreateItem();
            OperationContext standard = MonitoredReadAuthorization.GetSubscriberContext(first);

            OperationContext masked = MonitoredReadAuthorization.GetSubscriberContext(first, DiagnosticsMasks.None);

            Assert.That(masked, Is.Not.SameAs(standard));
            Assert.That(masked.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.None));
            Assert.That(standard.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.SymbolicId));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(second, DiagnosticsMasks.None), Is.SameAs(masked));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(second), Is.SameAs(standard));

            // groups with different masks alternate without replacing each other's context.
            OperationContext all = MonitoredReadAuthorization.GetSubscriberContext(first, DiagnosticsMasks.OperationAll);
            Assert.That(all.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.OperationAll));
            Assert.That(MonitoredReadAuthorization.GetSubscriberContext(second, DiagnosticsMasks.None), Is.SameAs(masked));
            Assert.That(
                MonitoredReadAuthorization.GetSubscriberContext(second, DiagnosticsMasks.OperationAll), Is.SameAs(all));

            subscriber.Generation++;
            OperationContext refreshed = MonitoredReadAuthorization.GetSubscriberContext(first, DiagnosticsMasks.None);
            Assert.That(refreshed, Is.Not.SameAs(masked));
            Assert.That(refreshed.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.None));
        }

        /// <summary>
        /// A modeled custom session whose identity, identity refresh generation and endpoint can change.
        /// </summary>
        private sealed class Subscriber
        {
            public Subscriber()
            {
                Session.SetupGet(value => value.Id).Returns(new NodeId(1, 1));
                Session.SetupGet(value => value.EffectiveIdentity).Returns(() => Identity);
                Session.SetupGet(value => value.EndpointDescription).Returns(() => Endpoint);
                Session.Setup(value => value.CaptureIdentityRefreshSnapshot())
                    .Returns(() => new IdentityRefreshSnapshot(Identity, Generation));
                Session.SetupGet(value => value.SecureChannelId).Returns(() => SecureChannelId);
            }

            public Mock<ISession> Session { get; } = new();

            public IUserIdentity Identity { get; set; } = new UserIdentity();

            public long Generation { get; set; }

            public string SecureChannelId { get; set; } = "channel-1";

            public EndpointDescription Endpoint { get; set; } =
                new() { SecurityMode = MessageSecurityMode.SignAndEncrypt };

            public IMonitoredItem CreateItem()
            {
                var item = new Mock<IMonitoredItem>();
                item.SetupGet(value => value.Session).Returns(Session.Object);
                item.SetupGet(value => value.EffectiveIdentity).Returns(() => Identity);
                return item.Object;
            }
        }
    }
}
