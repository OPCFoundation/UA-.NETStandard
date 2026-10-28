using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Fields of the audit events reported by <see cref="AuditEvents"/>.
    /// </summary>
    [TestFixture]
    [Category("AuditEvents")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AuditEventsTests
    {
        /// <summary>
        /// An OpenSecureChannel has no session context yet, so the ClientAuditEntryId of
        /// its audit event must come from the request header (CTT Auditing Connections 001).
        /// </summary>
        [Test]
        public void OpenSecureChannelEventCarriesRequestAuditEntryId()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
            AuditEventState reported = null;
            var server = new Mock<IAuditEventServer>();
            server.Setup(s => s.Auditing).Returns(true);
            server.Setup(s => s.DefaultAuditContext).Returns(context);
            server.Setup(s => s.ReportAuditEvent(It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()))
                .Callback<ISystemContext, AuditEventState>((_, e) => reported = e);

            var request = new OpenSecureChannelRequest
            {
                RequestHeader = new RequestHeader { AuditEntryId = "ctt-audit-entry-1" },
                RequestType = SecurityTokenRequestType.Issue,
                RequestedLifetime = 60000
            };

            server.Object.ReportAuditOpenSecureChannelEvent(
                "channel-1",
                new EndpointDescription { SecurityPolicyUri = SecurityPolicies.None, SecurityMode = MessageSecurityMode.None },
                request,
                null,
                null,
                telemetry.CreateLogger<AuditEventsTests>());

            Assert.That(reported, Is.InstanceOf<AuditOpenSecureChannelEventState>());
            Assert.That(reported.ClientAuditEntryId.Value, Is.EqualTo("ctt-audit-entry-1"));
            Assert.That(reported.Status.Value, Is.True);
        }
    }
}
