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
using NUnit.Framework;
using Opc.Ua.Pcap.Audit;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Audit
{
    [TestFixture]
    public sealed class PcapAuditEventTests
    {
        [Test]
        public void ConstructorPopulatesAllFields()
        {
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            var properties = new Dictionary<string, string>
            {
                ["Format"] = "json"
            };

            var auditEvent = new PcapAuditEvent(
                PcapAuditEventKind.DumpKeys,
                timestamp,
                "session-1",
                "keys.uakeys.json",
                "opc.tcp://localhost:4840",
                properties);

            Assert.That(auditEvent.Kind, Is.EqualTo(PcapAuditEventKind.DumpKeys));
            Assert.That(auditEvent.Timestamp, Is.EqualTo(timestamp));
            Assert.That(auditEvent.SessionId, Is.EqualTo("session-1"));
            Assert.That(auditEvent.ResourcePath, Is.EqualTo("keys.uakeys.json"));
            Assert.That(auditEvent.RemoteEndpoint, Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(auditEvent.Properties, Is.SameAs(properties));
        }

        [Test]
        public void TimestampIsRequired()
        {
            Assert.That(
                () => new PcapAuditEvent(
                    PcapAuditEventKind.StartCapture,
                    default,
                    sessionId: null,
                    resourcePath: null,
                    remoteEndpoint: null,
                    properties: null),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("timestamp"));
        }

        [Test]
        public void KindIsRequired()
        {
            Assert.That(
                () => new PcapAuditEvent(
                    (PcapAuditEventKind)int.MaxValue,
                    DateTimeOffset.UtcNow,
                    sessionId: null,
                    resourcePath: null,
                    remoteEndpoint: null,
                    properties: null),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property("ParamName").EqualTo("kind"));
        }
    }
}
