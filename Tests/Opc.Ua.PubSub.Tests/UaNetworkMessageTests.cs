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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaNetworkMessageTests
    {
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DataSetMessagesConstructorSetsProperties()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1", WriterGroupId = 5 };
            var messages = new List<PubSubEncoding.JsonDataSetMessage>();

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, messages);

            Assert.That(msg.DataSetMessages, Is.Not.Null);
            Assert.That(msg.DataSetMessages.Count, Is.Zero);
            Assert.That(msg.IsMetaDataMessage, Is.False);
            Assert.That(msg.DataSetMetaData, Is.Null);
        }

        [Test]
        public void MetaDataConstructorSetsProperties()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var metadata = new DataSetMetaDataType { Name = "Meta1" };

            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata);

            Assert.That(msg.IsMetaDataMessage, Is.True);
            Assert.That(msg.DataSetMetaData, Is.Not.Null);
            Assert.That(msg.DataSetMetaData.Name, Is.EqualTo("Meta1"));
            Assert.That(msg.DataSetMessages, Is.Not.Null);
            Assert.That(msg.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void WriterGroupIdPropertyRoundTrips()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                WriterGroupId = 42
            };
            Assert.That(msg.WriterGroupId, Is.EqualTo(42));
        }

        [Test]
        public void DataSetWriterIdSetGetRoundTrips()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                DataSetWriterId = 123
            };
            Assert.That(msg.DataSetWriterId, Is.EqualTo(123));
        }

        [Test]
        public void DataSetWriterIdReturnsNullWhenUnsetAndNoMessages()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.DataSetWriterId, Is.Null);
        }

        [Test]
        public void DataSetWriterIdReturnsSingleMessageWriterIdWhenUnset()
        {
            var dsMessage = new PubSubEncoding.JsonDataSetMessage { DataSetWriterId = 77 };
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(writerGroup, [dsMessage]);

            Assert.That(msg.DataSetWriterId, Is.EqualTo(77));
        }

        [Test]
        public void DataSetWriterIdReturnsNullWhenUnsetAndMultipleMessages()
        {
            var dsMessage1 = new PubSubEncoding.JsonDataSetMessage { DataSetWriterId = 10 };
            var dsMessage2 = new PubSubEncoding.JsonDataSetMessage { DataSetWriterId = 20 };
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, [dsMessage1, dsMessage2]);

            Assert.That(msg.DataSetWriterId, Is.Null);
        }

        [Test]
        public void DataSetWriterIdSetToNullResetsToZero()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage
            {
                DataSetWriterId = 99
            };
            msg.DataSetWriterId = null;
            Assert.That(msg.DataSetWriterId, Is.Null);
        }

        [Test]
        public void DataSetDecodeErrorOccurredEventCanBeSubscribed()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            DataSetDecodeErrorEventArgs receivedArgs = null;
            msg.DataSetDecodeErrorOccurred += (_, args) => receivedArgs = args;

            Assert.That(receivedArgs, Is.Null);
        }

        [Test]
        public void DataSetDecodeErrorOccurredEventWithNoSubscriberDoesNotThrow()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.DoesNotThrow(() =>
            {
                msg.Decode(
                    m_messageContext,
                    System.Text.Encoding.UTF8.GetBytes("{}"),
                    []);
            });
        }

        [Test]
        public void DataSetMessagesListAcceptsNewItems()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.DataSetMessages, Is.Not.Null);

            var dsMessage = new PubSubEncoding.JsonDataSetMessage { DataSetWriterId = 5 };
            msg.DataSetMessages.Add(dsMessage);

            Assert.That(msg.DataSetMessages.Count, Is.EqualTo(1));
        }

        [Test]
        public void MetaDataConstructorWithNullWriterGroupDoesNotThrow()
        {
            var metadata = new DataSetMetaDataType { Name = "Meta1" };
            Assert.DoesNotThrow(() =>
            {
                var msg = new PubSubEncoding.JsonNetworkMessage(null, metadata);
                Assert.That(msg.IsMetaDataMessage, Is.True);
            });
        }

        [Test]
        public void DataSetMessagesConstructorWithNullListCreatesEmptyMessages()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var msg = new PubSubEncoding.JsonNetworkMessage(
                writerGroup, (List<PubSubEncoding.JsonDataSetMessage>)null);
            Assert.That(msg.DataSetMessages, Is.Not.Null);
            Assert.That(msg.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void WriterGroupIdDefaultIsZero()
        {
            var msg = new PubSubEncoding.JsonNetworkMessage();
            Assert.That(msg.WriterGroupId, Is.Zero);
        }
    }
}