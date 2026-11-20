/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Pcap.Audit;

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
