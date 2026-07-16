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
using System.Diagnostics.Tracing;
using System.Threading;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="OpcUaServerEventSource"/>.
    /// </summary>
    [TestFixture]
    [Category("OpcUaServerEventSource")]
    [NonParallelizable]
    public class OpcUaServerEventSourceTests
    {
        [Test]
        public void EventLogIsAvailableWithExpectedName()
        {
            OpcUaServerEventSource eventLog = ServerUtils.EventLog;

            Assert.That(eventLog, Is.Not.Null);
            Assert.That(eventLog.Name, Is.EqualTo("OPC-UA-Server"));
        }

        [Test]
        public void EventMethodsDoNotThrowWhenNoListenerAttached()
        {
            OpcUaServerEventSource eventLog = ServerUtils.EventLog;

            Assert.DoesNotThrow(() => eventLog.ServerCall("Read", 42));
            Assert.DoesNotThrow(
                () => eventLog.SessionState("Created", "s1", "name", "ch1", "user"));
            Assert.DoesNotThrow(() => eventLog.MonitoredItemReady(7, "ready"));
        }

        [Test]
        public void ServerCallWritesEventWhenEnabled()
        {
            using var listener = new CollectingEventListener(ServerUtils.EventLog);

            ServerUtils.EventLog.ServerCall("Browse", 99);

            EventWrittenEventArgs written = listener.FindEvent("ServerCall");
            Assert.That(written, Is.Not.Null);
            Assert.That(written.Payload[0], Is.EqualTo("Browse"));
            Assert.That(written.Payload, Has.Count.EqualTo(2));
        }

        [Test]
        public void SessionStateWritesEventWhenEnabled()
        {
            using var listener = new CollectingEventListener(ServerUtils.EventLog);

            ServerUtils.EventLog.SessionState("Activated", "sid", "sname", "chan", "ident");

            EventWrittenEventArgs written = listener.FindEvent("SessionState");
            Assert.That(written, Is.Not.Null);
            Assert.That(written.Payload[0], Is.EqualTo("Activated"));
            Assert.That(written.Payload[4], Is.EqualTo("ident"));
        }

        [Test]
        public void MonitoredItemReadyWritesEventWhenEnabled()
        {
            using var listener = new CollectingEventListener(ServerUtils.EventLog);

            ServerUtils.EventLog.MonitoredItemReady(123, "publishing");

            EventWrittenEventArgs written = listener.FindEvent("MonitoredItemReady");
            Assert.That(written, Is.Not.Null);
            Assert.That(written.Payload, Has.Count.EqualTo(2));
            Assert.That(written.Payload[1], Is.EqualTo("publishing"));
        }

        private sealed class CollectingEventListener : EventListener
        {
            private readonly EventSource m_source;
            private readonly List<EventWrittenEventArgs> m_events = [];
            private readonly Lock m_lock = new();

            public CollectingEventListener(EventSource source)
            {
                m_source = source;
                EnableEvents(source, EventLevel.Verbose);
            }

            public EventWrittenEventArgs FindEvent(string eventName)
            {
                lock (m_lock)
                {
                    return m_events.Find(e => e.EventName == eventName);
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                lock (m_lock)
                {
                    m_events.Add(eventData);
                }
            }

            public override void Dispose()
            {
                DisableEvents(m_source);
                base.Dispose();
            }
        }
    }
}
