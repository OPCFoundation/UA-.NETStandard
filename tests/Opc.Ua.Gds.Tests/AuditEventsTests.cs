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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using GdsAuditEvents = Opc.Ua.Gds.Server.Diagnostics.AuditEvents;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Regression tests for the GDS-specific audit-event helpers and
    /// the redaction placeholders required to keep sensitive material
    /// (private key passwords, private keys) out of audit payloads.
    /// </summary>
    [TestFixture]
    [Category("Audit")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AuditEventsTests
    {
        [Test]
        public void RedactedPrivateKeyPasswordIsStablePlaceholder()
        {
            // The placeholder is part of the public contract; downstream
            // audit consumers may filter on this exact value to spot
            // redacted entries. Do not change the value.
            Assert.That(GdsAuditEvents.RedactedPrivateKeyPassword, Is.EqualTo("<redacted>"));
        }

        [Test]
        public void RedactedPrivateKeyIsEmptyByteString()
        {
            // Private keys must never appear in audit payloads; the
            // helper exposes an empty ByteString as the standard
            // placeholder.
            Assert.That(GdsAuditEvents.RedactedPrivateKey.IsEmpty, Is.True);
        }

        [Test]
        public void KeyCredentialFailureAuditRedactsExceptionDetails()
        {
            const string secret = "credential-secret-must-not-appear";
            var auditServer = new CapturingAuditEventServer();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<AuditEventsTests>();

            auditServer.ReportKeyCredentialDeliveredAuditEvent(
                auditServer.DefaultAuditContext,
                Ua.ObjectIds.Server,
                new MethodState(null),
                [new NodeId(1), false],
                logger,
                new InvalidOperationException($"Store failed with {secret}."));

            Assert.That(auditServer.Events, Has.Count.EqualTo(1));
            AuditUpdateMethodEventState auditEvent = auditServer.Events[0];
            Assert.That(auditEvent.Status!.Value, Is.False);
            Assert.That(auditEvent.Message!.Value.Text, Does.Not.Contain(secret));
            Assert.That(auditEvent.Message.Value.Text, Is.EqualTo("KeyCredentialDeliveredAuditEvent failed."));
        }

        private sealed class CapturingAuditEventServer : IAuditEventServer
        {
            public CapturingAuditEventServer()
            {
                var context = new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = new NamespaceTable()
                };
                context.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
                DefaultAuditContext = context;
            }

            public bool Auditing => true;

            public ISystemContext DefaultAuditContext { get; }

            public List<AuditUpdateMethodEventState> Events { get; } = [];

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                if (e is AuditUpdateMethodEventState auditEvent)
                {
                    Events.Add(auditEvent);
                }
            }
        }
    }
}
