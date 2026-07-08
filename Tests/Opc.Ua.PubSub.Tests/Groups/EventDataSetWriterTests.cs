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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Validates the event-mode publisher: <see cref="EventDataSetWriter"/>
    /// drains pending events from the
    /// <see cref="EventPublishedDataSet"/> sampler, projects them via
    /// <see cref="SimpleAttributeOperand"/>s and emits one
    /// <see cref="PubSubDataSetMessage"/> per event with
    /// <see cref="PubSubDataSetMessageType.Event"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("5.3.3", Summary = "EventDataSetWriter event-message build")]
    [TestSpec("6.2.4")]
    public class EventDataSetWriterTests
    {
        [Test]
        [TestSpec("5.3.3")]
        public async Task BuildEventMessagesAsync_EmitsOneMessagePerEventAsync()
        {
            var clock = new FakeTimeProvider();
            var sampler = new StubSampler();
            sampler.Enqueue([new Variant("A1"), new Variant(1.0)]);
            sampler.Enqueue([new Variant("A2"), new Variant(2.0)]);
            EventDataSetWriter writer = BuildWriter(sampler, clock);

            ArrayOf<PubSubDataSetMessage> messages =
                await writer.BuildEventMessagesAsync().ConfigureAwait(false);

            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages[0].MessageType,
                Is.EqualTo(PubSubDataSetMessageType.Event));
            Assert.That(((DataSetField[]?)messages[0].Fields) ?? [], Has.Length.EqualTo(2));
            Assert.That(messages[0].Fields[0].Value, Is.EqualTo(new Variant("A1")));
            Assert.That(messages[1].Fields[1].Value, Is.EqualTo(new Variant(2.0)));
            Assert.That(messages[0].SequenceNumber, Is.LessThan(messages[1].SequenceNumber));
        }

        [Test]
        [TestSpec("5.3.3")]
        public async Task BuildEventMessagesAsync_NoEvents_ReturnsEmptyAsync()
        {
            var sampler = new StubSampler();
            EventDataSetWriter writer = BuildWriter(sampler, new FakeTimeProvider());
            ArrayOf<PubSubDataSetMessage> messages =
                await writer.BuildEventMessagesAsync().ConfigureAwait(false);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        [TestSpec("6.2.4")]
        public async Task BuildEventMessagesAsync_HonoursFieldContentMaskAsync()
        {
            var sampler = new StubSampler();
            sampler.Enqueue([new Variant(1.0), new Variant(2.0)]);
            EventDataSetWriter writer = BuildWriter(
                sampler,
                new FakeTimeProvider(),
                contentMask: (uint)DataSetFieldContentMask.StatusCode);

            ArrayOf<PubSubDataSetMessage> messages =
                await writer.BuildEventMessagesAsync().ConfigureAwait(false);

            Assert.That(messages, Has.Count.EqualTo(1));
            var dsm = (UadpDataSetMessageV2)messages[0];
            Assert.That(dsm.FieldContentMask & DataSetFieldContentMask.StatusCode,
                Is.EqualTo(DataSetFieldContentMask.StatusCode));
        }

        [Test]
        [TestSpec("6.2.4")]
        public async Task EventPublishedDataSet_AlignsFieldsToMetaDataAsync()
        {
            var sampler = new StubSampler();
            sampler.Enqueue([new Variant("event"), new Variant(99)]);
            EventPublishedDataSet pds = BuildPublishedDataSet(sampler);
            ArrayOf<ArrayOf<DataSetField>> rows =
                await pds.SampleAsync().ConfigureAwait(false);

            Assert.That(((ArrayOf<DataSetField>[]?)rows) ?? [], Has.Length.EqualTo(1));
            Assert.That(rows[0][0].Name, Is.EqualTo("Message"));
            Assert.That(rows[0][1].Name, Is.EqualTo("Severity"));
            Assert.That(rows[0][0].Value, Is.EqualTo(new Variant("event")));
            Assert.That(rows[0][1].Value, Is.EqualTo(new Variant(99)));
        }

        private static EventDataSetWriter BuildWriter(
            IEventSampler sampler,
            TimeProvider clock,
            uint contentMask = 0)
        {
            EventPublishedDataSet pds = BuildPublishedDataSet(sampler);
            var writerCfg = new DataSetWriterDataType
            {
                Name = "evt-writer",
                DataSetWriterId = 7,
                DataSetFieldContentMask = contentMask
            };
            return new EventDataSetWriter(writerCfg, pds, clock);
        }

        private static EventPublishedDataSet BuildPublishedDataSet(IEventSampler sampler)
        {
            var pubEvents = new PublishedEventsDataType
            {
                EventNotifier = new NodeId("notifier", 1),
                SelectedFields =
                [
                    new SimpleAttributeOperand { TypeDefinitionId = new NodeId("Base", 1) },
                    new SimpleAttributeOperand { TypeDefinitionId = new NodeId("Base", 1) }
                ]
            };
            var pdsCfg = new PublishedDataSetDataType
            {
                Name = "events-pds",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Fields =
                    [
                        new FieldMetaData { Name = "Message" },
                        new FieldMetaData { Name = "Severity" }
                    ]
                },
                DataSetSource = new ExtensionObject(pubEvents)
            };
            return new EventPublishedDataSet(pdsCfg, sampler);
        }

        private sealed class StubSampler : IEventSampler
        {
            private readonly List<IReadOnlyList<Variant>> m_pending = [];
            public string Name => "stub";

            public void Enqueue(IReadOnlyList<Variant> row)
            {
                m_pending.Add(row);
            }

            public ValueTask<IReadOnlyList<IReadOnlyList<Variant>>> SampleEventsAsync(
                ArrayOf<SimpleAttributeOperand> selectedFields,
                ContentFilter? filter,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<IReadOnlyList<Variant>> copy = m_pending.ToArray();
                m_pending.Clear();
                return new ValueTask<IReadOnlyList<IReadOnlyList<Variant>>>(copy);
            }
        }
    }
}
