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

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Diagnostics
{
    [TestFixture]
    [Category("AuditEvents")]
    [Parallelizable]
    public class AuditEventsTests
    {
        private const string AuditEntryId = "audit-entry";
        private const string UserDisplayName = "audit-user";
        private const string SessionUserDisplayName = "session-user";
        private static readonly ILogger s_logger = NullLogger.Instance;

        [Test]
        public void RedactedPrivateKeyUsesEmptyByteString()
        {
            Assert.That(AuditEvents.RedactedPrivateKey, Is.EqualTo(ByteString.Empty));
        }

        [Test]
        public void GuardedReportMethodsDoNotEmitWhenAuditingDisabled()
        {
            CapturingAuditEventServer server = CreateAuditServer(auditing: false);

            foreach (Action<IAuditEventServer> report in CreateGuardedReportActions())
            {
                report(server);
            }

            Assert.That(server.Events, Is.Empty);
        }

        [Test]
        public void ReportAuditCertificateEventWithoutExceptionDoesNotEmit()
        {
            using Certificate certificate = CreateCertificate();
            CapturingAuditEventServer server = CreateAuditServer();

            server.ReportAuditCertificateEvent(certificate, null, s_logger);

            Assert.That(server.Events, Is.Empty);
        }

        [Test]
        public void ReportAuditWriteUpdateEventWithoutSessionUserDoesNotEmit()
        {
            CapturingAuditEventServer server = CreateAuditServer();
            ISystemContext systemContext = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(new NamespaceTable()),
                EncodeableFactory = EncodeableFactory.Create()
            };

            server.ReportAuditWriteUpdateEvent(
                systemContext,
                CreateWriteValue(),
                new Variant("old"),
                StatusCodes.Good,
                s_logger);

            Assert.That(server.Events, Is.Empty);
        }

        [Test]
        public void HappyReportMethodsEmitExpectedAuditEvents()
        {
            using Certificate certificate = CreateCertificate();
            CapturingAuditEventServer server = CreateAuditServer();
            AuditEventExpectation[] expectations = CreateHappyReportExpectations(certificate).ToArray();

            foreach (AuditEventExpectation expectation in expectations)
            {
                int beforeCount = server.Events.Count;

                expectation.Report(server);

                Assert.That(server.Events, Has.Count.EqualTo(beforeCount + 1), expectation.Name);
                AuditEventState auditEvent = server.Events[^1];
                Assert.That(auditEvent, Is.TypeOf(expectation.EventType), expectation.Name);
                Assert.That(auditEvent.SourceName?.Value, Is.EqualTo(expectation.SourceName), expectation.Name);
                Assert.That(auditEvent.Status?.Value, Is.EqualTo(expectation.Status), expectation.Name);
            }
        }

        [Test]
        public void ReportAuditWriteUpdateEventWithIndexRangeEmitsAuditEvent()
        {
            CapturingAuditEventServer server = CreateAuditServer();
            WriteValue writeValue = CreateWriteValue();
            writeValue.IndexRange = "1";
            ServiceResult validationResult = WriteValue.Validate(writeValue);
            int[] oldValues = [1, 2, 3];
            Variant oldValue = new(oldValues);

            server.ReportAuditWriteUpdateEvent(
                CreateSystemContext(RequestType.Write),
                writeValue,
                oldValue,
                StatusCodes.Good,
                s_logger);

            Assert.That(ServiceResult.IsGood(validationResult), Is.True);
            AuditWriteUpdateEventState auditEvent = (AuditWriteUpdateEventState)server.Events.Single();
            Assert.That(auditEvent.SourceName.Value, Is.EqualTo("Attribute/Write"));
            Assert.That(auditEvent.Status.Value, Is.True);
        }

        [Test]
        public void ReportAuditOpenSecureChannelEventWithServiceResultExceptionUsesInnerStatus()
        {
            CapturingAuditEventServer server = CreateAuditServer();
            ServiceResultException exception = CreateServiceResultException(
                StatusCodes.BadSecurityChecksFailed);

            server.ReportAuditOpenSecureChannelEvent(
                "channel-2",
                CreateEndpointDescription(),
                CreateOpenSecureChannelRequest(),
                null,
                new InvalidOperationException("outer", exception),
                s_logger);

            AuditOpenSecureChannelEventState auditEvent =
                (AuditOpenSecureChannelEventState)server.Events.Single();
            Assert.That(auditEvent.Status.Value, Is.False);
            Assert.That(auditEvent.StatusCodeId.Value, Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ReportAuditCloseSecureChannelEventWithServiceResultExceptionWithoutInnerResultUsesUncertainStatus()
        {
            CapturingAuditEventServer server = CreateAuditServer();

            server.ReportAuditCloseSecureChannelEvent(
                "channel-3",
                new InvalidOperationException(
                    "outer",
                    new ServiceResultException(StatusCodes.BadSecureChannelClosed)),
                s_logger);

            AuditChannelEventState auditEvent = (AuditChannelEventState)server.Events.Single();
            Assert.That(auditEvent.Status.Value, Is.False);
            Assert.That(auditEvent.StatusCodeId.Value, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
        }

        [Test]
        public void ReportSessionMethodsWithExceptionsEmitFailedAuditEvents()
        {
            CapturingAuditEventServer server = CreateAuditServer();
            ISession session = CreateSession();

            server.ReportAuditCreateSessionEvent(
                AuditEntryId,
                session,
                1_000,
                s_logger,
                new ServiceResultException(StatusCodes.BadSessionIdInvalid));
            server.ReportAuditActivateSessionEvent(
                s_logger,
                AuditEntryId,
                session,
                new ServiceResultException(StatusCodes.BadUserAccessDenied));

            Assert.That(server.Events, Has.Count.EqualTo(2));
            Assert.That(server.Events[0], Is.TypeOf<AuditCreateSessionEventState>());
            Assert.That(server.Events[0].Status.Value, Is.False);
            Assert.That(server.Events[1], Is.TypeOf<AuditActivateSessionEventState>());
            Assert.That(server.Events[1].Status.Value, Is.False);
        }

        [Test]
        public void ReportCertificateUpdateMethodsWithExceptionsEmitFailedAuditEvents()
        {
            CapturingAuditEventServer server = CreateAuditServer();
            ISystemContext systemContext = CreateSystemContext(RequestType.Call);
            MethodState method = CreateMethodState();
            Exception exception = new ServiceResultException(StatusCodes.BadInvalidArgument);

            server.ReportCertificateUpdatedAuditEvent(
                systemContext,
                ObjectIds.Server,
                method,
                CreateInputArguments(),
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.ApplicationCertificateType,
                s_logger,
                exception);
            server.ReportCertificateUpdateRequestedAuditEvent(
                systemContext,
                ObjectIds.Server,
                method,
                CreateInputArguments(),
                s_logger,
                exception);

            Assert.That(server.Events, Has.Count.EqualTo(2));
            Assert.That(server.Events[0], Is.TypeOf<CertificateUpdatedAuditEventState>());
            Assert.That(server.Events[0].Status.Value, Is.False);
            Assert.That(server.Events[1], Is.TypeOf<CertificateUpdateRequestedAuditEventState>());
            Assert.That(server.Events[1].Status.Value, Is.False);
        }

        [Test]
        public void ReportTrustListMethodsUseValidSystemContext()
        {
            TrustListState trustList = new(null);
            ISystemContext systemContext = CreateSystemContext(RequestType.Call);

            trustList.ReportTrustListUpdatedAuditEvent(
                systemContext,
                ObjectIds.ServerConfiguration,
                "TrustList/Update",
                MethodIds.ServerConfiguration_UpdateCertificate,
                CreateInputArguments(),
                StatusCodes.Good,
                s_logger);
            trustList.ReportTrustListUpdateRequestedAuditEvent(
                systemContext,
                ObjectIds.ServerConfiguration,
                "TrustList/UpdateRequested",
                MethodIds.ServerConfiguration_UpdateCertificate,
                CreateInputArguments(),
                s_logger);

            Assert.That(trustList, Is.Not.Null);
        }

        private static IEnumerable<Action<IAuditEventServer>> CreateGuardedReportActions()
        {
            yield return server => server.ReportAuditEvent(
                CreateOperationContext(RequestType.Read),
                "Read",
                new ServiceResultException(StatusCodes.BadUnexpectedError),
                s_logger);
            yield return server => server.ReportAuditWriteUpdateEvent(
                CreateSystemContext(RequestType.Write),
                CreateWriteValue(),
                new Variant("old"),
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryValueUpdateEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateUpdateDataDetails(),
                [new DataValue(new Variant("old"))],
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryAnnotationUpdateEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateUpdateStructureDataDetails(),
                new[] { new DataValue(new Variant("old-annotation")) }.ToArrayOf(),
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryEventUpdateEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateUpdateEventDetails(),
                new[] { CreateHistoryEventFieldList("old-event") }.ToArrayOf(),
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryRawModifyDeleteEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateDeleteRawModifiedDetails(),
                new[] { new DataValue(new Variant("old-raw")) }.ToArrayOf(),
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryAtTimeDeleteEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateDeleteAtTimeDetails(),
                [new DataValue(new Variant("old-at-time"))],
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditHistoryEventDeleteEvent(
                CreateSystemContext(RequestType.HistoryUpdate),
                CreateDeleteEventDetails(),
                [new DataValue(new Variant("old-event-delete"))],
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditCertificateEvent(
                null,
                CreateServiceResultException(StatusCodes.BadCertificateUntrusted),
                s_logger);
            yield return server => server.ReportAuditCertificateDataMismatchEvent(
                null,
                "host",
                "urn:invalid",
                StatusCodes.BadCertificateUriInvalid,
                s_logger);
            yield return server => server.ReportAuditCancelEvent(
                new NodeId("session", 2),
                10,
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditRoleMappingRuleChangedEvent(
                CreateSystemContext(RequestType.Call),
                ObjectIds.WellKnownRole_Observer,
                CreateMethodState(),
                CreateInputArguments(),
                true,
                s_logger);
            yield return server => server.ReportAuditCreateSessionEvent(
                AuditEntryId,
                CreateSession(),
                1_000,
                s_logger);
            yield return server => server.ReportAuditActivateSessionEvent(
                s_logger,
                AuditEntryId,
                CreateSession());
            yield return server => server.ReportAuditUrlMismatchEvent(
                AuditEntryId,
                CreateSession(),
                1_000,
                "opc.tcp://wrong-host:4840",
                s_logger);
            yield return server => server.ReportAuditCloseSessionEvent(
                AuditEntryId,
                CreateSession(),
                s_logger);
            yield return server => server.ReportAuditTransferSubscriptionEvent(
                AuditEntryId,
                CreateSession(),
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditAddNodesEvent(
                CreateSystemContext(RequestType.AddNodes),
                CreateAddNodesItems(),
                "Add nodes",
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditDeleteNodesEvent(
                CreateSystemContext(RequestType.DeleteNodes),
                CreateDeleteNodesItems(),
                "Delete nodes",
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditAddReferencesEvent(
                CreateSystemContext(RequestType.AddReferences),
                CreateAddReferencesItems(),
                "Add references",
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditDeleteReferencesEvent(
                CreateSystemContext(RequestType.DeleteReferences),
                CreateDeleteReferencesItems(),
                "Delete references",
                StatusCodes.Good,
                s_logger);
            yield return server => server.ReportAuditOpenSecureChannelEvent(
                "channel-1",
                CreateEndpointDescription(),
                CreateOpenSecureChannelRequest(),
                null,
                null,
                s_logger);
            yield return server => server.ReportAuditCloseSecureChannelEvent(
                "channel-1",
                null,
                s_logger);
            yield return server => server.ReportAuditUpdateMethodEvent(
                CreateSystemContext(RequestType.Call),
                ObjectIds.Server,
                MethodIds.Server_GetMonitoredItems,
                CreateInputArguments(),
                "Call method",
                StatusCodes.Good,
                s_logger);
        }

        private static IEnumerable<AuditEventExpectation> CreateHappyReportExpectations(
            Certificate certificate)
        {
            yield return new AuditEventExpectation(
                "ReportAuditEvent",
                server => server.ReportAuditEvent(
                    CreateOperationContext(RequestType.Read),
                    "Read",
                    new ServiceResultException(StatusCodes.BadUnexpectedError),
                    s_logger),
                typeof(AuditEventState),
                "Attribute/Read",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditWriteUpdateEvent",
                server => server.ReportAuditWriteUpdateEvent(
                    CreateSystemContext(RequestType.Write),
                    CreateWriteValue(),
                    new Variant("old"),
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditWriteUpdateEventState),
                "Attribute/Write",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryValueUpdateEvent",
                server => server.ReportAuditHistoryValueUpdateEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateUpdateDataDetails(),
                    [new DataValue(new Variant("old"))],
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditHistoryValueUpdateEventState),
                "Attribute/HistoryValueUpdate",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryAnnotationUpdateEvent",
                server => server.ReportAuditHistoryAnnotationUpdateEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateUpdateStructureDataDetails(),
                    new[] { new DataValue(new Variant("old-annotation")) }.ToArrayOf(),
                    StatusCodes.BadHistoryOperationInvalid,
                    s_logger),
                typeof(AuditHistoryAnnotationUpdateEventState),
                "Attribute/HistoryAnnotationUpdate",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryEventUpdateEvent",
                server => server.ReportAuditHistoryEventUpdateEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateUpdateEventDetails(),
                    new[] { CreateHistoryEventFieldList("old-event") }.ToArrayOf(),
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditHistoryEventUpdateEventState),
                "Attribute/HistoryEventUpdate",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryRawModifyDeleteEvent",
                server => server.ReportAuditHistoryRawModifyDeleteEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateDeleteRawModifiedDetails(),
                    new[] { new DataValue(new Variant("old-raw")) }.ToArrayOf(),
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditHistoryRawModifyDeleteEventState),
                "Attribute/HistoryRawModifyDelete",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryAtTimeDeleteEvent",
                server => server.ReportAuditHistoryAtTimeDeleteEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateDeleteAtTimeDetails(),
                    [new DataValue(new Variant("old-at-time"))],
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditHistoryAtTimeDeleteEventState),
                "Attribute/HistoryAtTimeDelete",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditHistoryEventDeleteEvent",
                server => server.ReportAuditHistoryEventDeleteEvent(
                    CreateSystemContext(RequestType.HistoryUpdate),
                    CreateDeleteEventDetails(),
                    [new DataValue(new Variant("old-event-delete"))],
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditHistoryEventDeleteEventState),
                "Attribute/HistoryEventDelete",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditCertificateEvent",
                server => server.ReportAuditCertificateEvent(
                    certificate,
                    CreateServiceResultException(StatusCodes.BadCertificateUntrusted),
                    s_logger),
                typeof(AuditCertificateUntrustedEventState),
                "Security/Certificate",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditCertificateDataMismatchEvent",
                server => server.ReportAuditCertificateDataMismatchEvent(
                    certificate,
                    "wrong-host",
                    "urn:wrong",
                    StatusCodes.BadCertificateUriInvalid,
                    s_logger),
                typeof(AuditCertificateDataMismatchEventState),
                "Security/Certificate",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditCancelEvent",
                server => server.ReportAuditCancelEvent(
                    new NodeId("session", 2),
                    10,
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditCancelEventState),
                "Session/Cancel",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditRoleMappingRuleChangedEvent",
                server => server.ReportAuditRoleMappingRuleChangedEvent(
                    CreateSystemContext(RequestType.Call),
                    ObjectIds.WellKnownRole_Observer,
                    CreateMethodState(),
                    CreateInputArguments(),
                    true,
                    s_logger),
                typeof(RoleMappingRuleChangedAuditEventState),
                "Attribute/Call",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditCreateSessionEvent",
                server => server.ReportAuditCreateSessionEvent(
                    AuditEntryId,
                    CreateSession(),
                    1_000,
                    s_logger),
                typeof(AuditCreateSessionEventState),
                "Session/CreateSession",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditActivateSessionEvent",
                server => server.ReportAuditActivateSessionEvent(
                    s_logger,
                    AuditEntryId,
                    CreateSession()),
                typeof(AuditActivateSessionEventState),
                "Session/ActivateSession",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditUrlMismatchEvent",
                server => server.ReportAuditUrlMismatchEvent(
                    AuditEntryId,
                    CreateSession(),
                    1_000,
                    "opc.tcp://wrong-host:4840",
                    s_logger),
                typeof(AuditUrlMismatchEventState),
                "Session/CreateSession",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditCloseSessionEvent",
                server => server.ReportAuditCloseSessionEvent(
                    AuditEntryId,
                    CreateSession(),
                    s_logger,
                    "Session/CloseSession"),
                typeof(AuditSessionEventState),
                "Session/CloseSession",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditTransferSubscriptionEvent",
                server => server.ReportAuditTransferSubscriptionEvent(
                    AuditEntryId,
                    CreateSession(),
                    StatusCodes.BadSubscriptionIdInvalid,
                    s_logger),
                typeof(AuditSessionEventState),
                "Session/TransferSubscriptions",
                false);
            yield return new AuditEventExpectation(
                "ReportCertificateUpdatedAuditEvent",
                server => server.ReportCertificateUpdatedAuditEvent(
                    CreateSystemContext(RequestType.Call),
                    ObjectIds.Server,
                    CreateMethodState(),
                    CreateInputArguments(),
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.ApplicationCertificateType,
                    s_logger),
                typeof(CertificateUpdatedAuditEventState),
                "Method/UpdateCertificate",
                true);
            yield return new AuditEventExpectation(
                "ReportCertificateUpdateRequestedAuditEvent",
                server => server.ReportCertificateUpdateRequestedAuditEvent(
                    CreateSystemContext(RequestType.Call),
                    ObjectIds.Server,
                    CreateMethodState(),
                    CreateInputArguments(),
                    s_logger),
                typeof(CertificateUpdateRequestedAuditEventState),
                "Method/UpdateCertificate",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditAddNodesEvent",
                server => server.ReportAuditAddNodesEvent(
                    CreateSystemContext(RequestType.AddNodes),
                    CreateAddNodesItems(),
                    "Add nodes",
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditAddNodesEventState),
                "NodeManagement/AddNodes",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditDeleteNodesEvent",
                server => server.ReportAuditDeleteNodesEvent(
                    CreateSystemContext(RequestType.DeleteNodes),
                    CreateDeleteNodesItems(),
                    "Delete nodes",
                    StatusCodes.BadNodeIdUnknown,
                    s_logger),
                typeof(AuditDeleteNodesEventState),
                "NodeManagement/DeleteNodes",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditAddReferencesEvent",
                server => server.ReportAuditAddReferencesEvent(
                    CreateSystemContext(RequestType.AddReferences),
                    CreateAddReferencesItems(),
                    "Add references",
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditAddReferencesEventState),
                "NodeManagement/AddReferences",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditDeleteReferencesEvent",
                server => server.ReportAuditDeleteReferencesEvent(
                    CreateSystemContext(RequestType.DeleteReferences),
                    CreateDeleteReferencesItems(),
                    "Delete references",
                    StatusCodes.BadReferenceTypeIdInvalid,
                    s_logger),
                typeof(AuditDeleteReferencesEventState),
                "NodeManagement/DeleteReferences",
                false);
            yield return new AuditEventExpectation(
                "ReportAuditOpenSecureChannelEvent",
                server => server.ReportAuditOpenSecureChannelEvent(
                    "channel-1",
                    CreateEndpointDescription(),
                    CreateOpenSecureChannelRequest(),
                    certificate,
                    null,
                    s_logger),
                typeof(AuditOpenSecureChannelEventState),
                "SecureChannel/OpenSecureChannel",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditCloseSecureChannelEvent",
                server => server.ReportAuditCloseSecureChannelEvent(
                    "channel-1",
                    null,
                    s_logger),
                typeof(AuditChannelEventState),
                "SecureChannel/CloseSecureChannel",
                true);
            yield return new AuditEventExpectation(
                "ReportAuditUpdateMethodEvent",
                server => server.ReportAuditUpdateMethodEvent(
                    CreateSystemContext(RequestType.Call),
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    CreateInputArguments(),
                    "Call method",
                    StatusCodes.Good,
                    s_logger),
                typeof(AuditUpdateMethodEventState),
                "Attribute/Call",
                true);
        }

        private static CapturingAuditEventServer CreateAuditServer(bool auditing = true)
        {
            return new CapturingAuditEventServer(CreateSystemContext(RequestType.Call), auditing);
        }

        private static ServerSystemContext CreateSystemContext(RequestType requestType)
        {
            NamespaceTable namespaceUris = new();
            StringTable serverUris = new();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);

            return new ServerSystemContext(server.Object, CreateOperationContext(requestType));
        }

        private static OperationContext CreateOperationContext(RequestType requestType)
        {
            var identity = new UserIdentity("audit-user", Array.Empty<byte>())
            {
                DisplayName = UserDisplayName
            };
            var requestHeader = new RequestHeader
            {
                AuditEntryId = AuditEntryId,
                RequestHandle = 123,
                Timestamp = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                TimeoutHint = 10_000
            };
            return new OperationContext(
                requestHeader,
                null,
                requestType,
                RequestLifetime.None,
                identity);
        }

        private static ISession CreateSession()
        {
            var identity = new UserIdentity("session-user", Array.Empty<byte>())
            {
                DisplayName = SessionUserDisplayName
            };
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId("session-id", 2));
            session.Setup(s => s.Identity).Returns(identity);
            session.Setup(s => s.EffectiveIdentity).Returns(identity);
            session.Setup(s => s.IdentityToken).Returns(identity.TokenHandler);
            session.Setup(s => s.PreferredLocales).Returns([]);
            session.Setup(s => s.SecureChannelId).Returns("secure-channel");
            session.Setup(s => s.ClientCertificate).Returns((Certificate)null);
            return session.Object;
        }

        private static WriteValue CreateWriteValue()
        {
            return new WriteValue
            {
                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(42))
            };
        }

        private static UpdateDataDetails CreateUpdateDataDetails()
        {
            return new UpdateDataDetails
            {
                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = [new DataValue(new Variant("new-value"))]
            };
        }

        private static UpdateStructureDataDetails CreateUpdateStructureDataDetails()
        {
            return new UpdateStructureDataDetails
            {
                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                PerformInsertReplace = PerformUpdateType.Replace,
                UpdateValues = [new DataValue(new Variant("new-annotation"))]
            };
        }

        private static UpdateEventDetails CreateUpdateEventDetails()
        {
            return new UpdateEventDetails
            {
                NodeId = ObjectIds.Server,
                PerformInsertReplace = PerformUpdateType.Update,
                Filter = new EventFilter(),
                EventData = new[] { CreateHistoryEventFieldList("new-event") }.ToArrayOf()
            };
        }

        private static DeleteRawModifiedDetails CreateDeleteRawModifiedDetails()
        {
            return new DeleteRawModifiedDetails
            {
                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                IsDeleteModified = true,
                StartTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc)
            };
        }

        private static DeleteAtTimeDetails CreateDeleteAtTimeDetails()
        {
            return new DeleteAtTimeDetails
            {
                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                ReqTimes =
                [
                    new DateTime(2025, 1, 1, 0, 30, 0, DateTimeKind.Utc)
                ]
            };
        }

        private static DeleteEventDetails CreateDeleteEventDetails()
        {
            return new DeleteEventDetails
            {
                NodeId = ObjectIds.Server,
                EventIds = [ByteString.From([1, 2, 3])]
            };
        }

        private static HistoryEventFieldList CreateHistoryEventFieldList(string value)
        {
            return new HistoryEventFieldList
            {
                EventFields = [new Variant(value)]
            };
        }

        private static MethodState CreateMethodState()
        {
            return new MethodState(null)
            {
                NodeId = MethodIds.Server_GetMonitoredItems,
                BrowseName = new QualifiedName(BrowseNames.GetMonitoredItems)
            };
        }

        private static ArrayOf<Variant> CreateInputArguments()
        {
            return [new Variant("input")];
        }

        private static ArrayOf<AddNodesItem> CreateAddNodesItems()
        {
            return
            [
                new AddNodesItem
                {
                    ParentNodeId = ObjectIds.ObjectsFolder,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    RequestedNewNodeId = new ExpandedNodeId("new-node", 2),
                    BrowseName = new QualifiedName("NewNode", 2),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                }
            ];
        }

        private static ArrayOf<DeleteNodesItem> CreateDeleteNodesItems()
        {
            return
            [
                new DeleteNodesItem
                {
                    NodeId = new NodeId("deleted-node", 2),
                    DeleteTargetReferences = true
                }
            ];
        }

        private static ArrayOf<AddReferencesItem> CreateAddReferencesItems()
        {
            return
            [
                new AddReferencesItem
                {
                    SourceNodeId = new NodeId("source-node", 2),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,
                    TargetNodeId = ObjectIds.ObjectsFolder,
                    TargetNodeClass = NodeClass.Object
                }
            ];
        }

        private static ArrayOf<DeleteReferencesItem> CreateDeleteReferencesItems()
        {
            return
            [
                new DeleteReferencesItem
                {
                    SourceNodeId = new NodeId("source-node", 2),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsForward = true,
                    TargetNodeId = ObjectIds.ObjectsFolder,
                    DeleteBidirectional = true
                }
            ];
        }

        private static EndpointDescription CreateEndpointDescription()
        {
            return new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
        }

        private static OpenSecureChannelRequest CreateOpenSecureChannelRequest()
        {
            return new OpenSecureChannelRequest
            {
                RequestHeader = new RequestHeader
                {
                    AuditEntryId = AuditEntryId,
                    Timestamp = new DateTime(2025, 1, 3, 4, 5, 6, DateTimeKind.Utc)
                },
                RequestType = SecurityTokenRequestType.Issue,
                RequestedLifetime = 60_000
            };
        }

        private static ServiceResultException CreateServiceResultException(StatusCode statusCode)
        {
            return new ServiceResultException(
                new ServiceResult(statusCode, new ServiceResult(statusCode)));
        }

        private static Certificate CreateCertificate()
        {
            return CertificateBuilder
                .Create("CN=Audit Events")
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private sealed class AuditEventExpectation
        {
            public AuditEventExpectation(
                string name,
                Action<CapturingAuditEventServer> report,
                Type eventType,
                string sourceName,
                bool status)
            {
                Name = name;
                Report = report;
                EventType = eventType;
                SourceName = sourceName;
                Status = status;
            }

            public string Name { get; }

            public Action<CapturingAuditEventServer> Report { get; }

            public Type EventType { get; }

            public string SourceName { get; }

            public bool Status { get; }
        }

        private sealed class CapturingAuditEventServer : IAuditEventServer
        {
            public CapturingAuditEventServer(ISystemContext defaultAuditContext, bool auditing)
            {
                DefaultAuditContext = defaultAuditContext;
                Auditing = auditing;
            }

            public bool Auditing { get; }

            public ISystemContext DefaultAuditContext { get; }

            public List<AuditEventState> Events { get; } = [];

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                Events.Add(e);
            }
        }
    }
}
