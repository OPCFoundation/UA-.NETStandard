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

using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
{
    /// <summary>
    /// Unit tests for <see cref="DeterministicEventIdProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Events")]
    public class DeterministicEventIdProviderTests
    {
        [Test]
        public void SameEventContentProducesSameEventIdAcrossReplicas()
        {
            var providerA = new DeterministicEventIdProvider("replica-set");
            var providerB = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState eventA = CreateEvent("started");
            BaseEventState eventB = CreateEvent("started");

            ByteString idA = providerA.CreateEventId(notifier, context, eventA);
            ByteString idB = providerB.CreateEventId(notifier, context, eventB);

            Assert.That(idA.ToArray(), Is.EqualTo(idB.ToArray()));
            Assert.That(idA.Length, Is.EqualTo(32));
        }

        [Test]
        public void DifferentSeedProducesDifferentEventId()
        {
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState e = CreateEvent("started");

            ByteString idA = new DeterministicEventIdProvider("set-a").CreateEventId(notifier, context, e);
            ByteString idB = new DeterministicEventIdProvider("set-b").CreateEventId(notifier, context, e);

            Assert.That(idA.ToArray(), Is.Not.EqualTo(idB.ToArray()));
        }

        [Test]
        public void ConstructorRejectsEmptySeed()
        {
            Assert.That(
                () => new DeterministicEventIdProvider(" "),
                Throws.ArgumentException);
        }

        [Test]
        public void DifferentEventTimeProducesDifferentEventId()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState event1 = CreateEvent("started");
            event1.Time!.Value = new DateTimeUtc(638000000000000000);
            BaseEventState event2 = CreateEvent("started");
            event2.Time!.Value = new DateTimeUtc(639000000000000000);

            ByteString id1 = provider.CreateEventId(notifier, context, event1);
            ByteString id2 = provider.CreateEventId(notifier, context, event2);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void DifferentEventMessageProducesDifferentEventId()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState event1 = CreateEvent("started");
            BaseEventState event2 = CreateEvent("stopped");

            ByteString id1 = provider.CreateEventId(notifier, context, event1);
            ByteString id2 = provider.CreateEventId(notifier, context, event2);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void DifferentNotifierProducesDifferentEventId()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier1 = CreateNotifier();
            notifier1.NodeId = new NodeId("Notifier1", 2);
            BaseObjectState notifier2 = CreateNotifier();
            notifier2.NodeId = new NodeId("Notifier2", 2);
            BaseEventState e = CreateEvent("started");

            ByteString id1 = provider.CreateEventId(notifier1, context, e);
            ByteString id2 = provider.CreateEventId(notifier2, context, e);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void EventIdLengthIs32Bytes()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState e = CreateEvent("started");

            ByteString id = provider.CreateEventId(notifier, context, e);

            Assert.That(id.Length, Is.EqualTo(32));
        }

        [Test]
        public void ConstructorRejectsNullSeed()
        {
            Assert.That(
                () => new DeterministicEventIdProvider(null!),
                Throws.ArgumentException);
        }

        private static ServerSystemContext CreateContext()
        {
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.ServerUris).Returns(messageContext.ServerUris);
            server.Setup(s => s.Factory).Returns(messageContext.Factory);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(messageContext.NamespaceUris));
            return new ServerSystemContext(server.Object);
        }

        private static BaseObjectState CreateNotifier()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 2),
                BrowseName = new QualifiedName("Notifier", 2)
            };
        }

        private static BaseEventState CreateEvent(string message)
        {
            var e = new BaseEventState(null)
            {
                Message = PropertyState<LocalizedText>.With<VariantBuilder>(null!, new LocalizedText(message)),
                Severity = PropertyState<ushort>.With<VariantBuilder>(null!, 500),
                Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(null!, new DateTimeUtc(638000000000000000)),
                ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(
                    null!,
                    new DateTimeUtc(638000000000000000))
            };
            return e;
        }
    }
}
