/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An interface to report audit events in the server.
    /// </summary>
    public interface IAuditEventServer
    {
        /// <summary>
        /// If auditing is enabled.
        /// </summary>
        bool Auditing { get; }

        /// <summary>
        /// The default system context for the audit events.
        /// </summary>
        ISystemContext DefaultAuditContext { get; }

        /// <summary>
        /// Called by any component to report an audit event.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The event.</param>
        void ReportAuditEvent(ISystemContext context, AuditEventState e);
    }

    /// <summary>
    /// The implementation of audit events.
    /// </summary>
    public static class AuditEvents
    {
        #region Report Audit Events
        /// <summary>
        /// Report Audit event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="operationContext">Client operation info</param>
        /// <param name="methodName">Audit method name</param>
        /// <param name="serviceResultException">The service exception that includes also a status code</param>
        public static void ReportAuditEvent(
            this IAuditEventServer server,
            OperationContext operationContext,
            string methodName,
            ServiceResultException serviceResultException)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                AuditEventState e = new AuditEventState(null);

                TranslationInfo message = new TranslationInfo(
                   "AuditEvent",
                   "en-US",
                   $"Method {methodName} failed. Result: {serviceResultException.Message}.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(serviceResultException.StatusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, $"Attribute/{methodName}", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, operationContext?.UserIdentity?.DisplayName, false);
                e.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, operationContext?.AuditEntryId, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditWriteUpdate event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="writeValue">The value to write.</param>
        /// <param name="oldValue">The old value of the node.</param>
        /// <param name="statusCode">The resulted status code.</param>
        public static void ReportAuditWriteUpdateEvent(
            this IAuditEventServer server,
            SystemContext systemContext,
            WriteValue writeValue,
            object oldValue,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            if (systemContext?.OperationContext == null || systemContext?.UserIdentity == null)
            {
                return;
            }

            try
            {
                AuditWriteUpdateEventState e = new AuditWriteUpdateEventState(null);

                TranslationInfo message = new TranslationInfo(
                   "AuditWriteUpdateEvent",
                    "en-US",
                    "AuditWriteUpdateEvent.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, writeValue.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Attribute/Write", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, systemContext?.UserIdentity?.DisplayName, false);
                e.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, systemContext?.AuditEntryId, false);

                e.SetChildValue(systemContext, BrowseNames.AttributeId, writeValue.AttributeId, false);
                e.SetChildValue(systemContext, BrowseNames.IndexRange, writeValue.IndexRange, false);

                object newValue = writeValue.Value?.Value;
                if (writeValue.ParsedIndexRange != NumericRange.Empty)
                {
                    writeValue.ParsedIndexRange.UpdateRange(ref newValue, writeValue.Value?.Value);
                }

                e.SetChildValue(systemContext, BrowseNames.NewValue, newValue, false);
                e.SetChildValue(systemContext, BrowseNames.OldValue, oldValue, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditWriteUpdateEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditHistoryValueUpdate event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="updateDataDetails">Update data details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryValueUpdateEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            UpdateDataDetails updateDataDetails,
            DataValue[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryValueUpdateEventState e = new AuditHistoryValueUpdateEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                        systemContext,
                        "AuditHistoryValueUpdateEvent",
                        "Attribute/HistoryValueUpdate",
                        updateDataDetails,
                        statusCode);

                e.SetChildValue(systemContext, BrowseNames.UpdatedNode, updateDataDetails.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.PerformInsertReplace, updateDataDetails.PerformInsertReplace, false);
                e.SetChildValue(systemContext, BrowseNames.NewValues, updateDataDetails.UpdateValues.ToArray(), false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryValueUpdateEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditHistoryValueUpdate event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="updateStructureDataDetails">Update structure data details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryAnnotationUpdateEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            UpdateStructureDataDetails updateStructureDataDetails,
            DataValue[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryAnnotationUpdateEventState e = new AuditHistoryAnnotationUpdateEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                    systemContext,
                    "AuditHistoryAnnotationUpdateEvent",
                    "Attribute/HistoryAnnotationUpdate",
                    updateStructureDataDetails,
                    statusCode);

                e.SetChildValue(systemContext, BrowseNames.PerformInsertReplace, updateStructureDataDetails.PerformInsertReplace, false);
                e.SetChildValue(systemContext, BrowseNames.NewValues, updateStructureDataDetails.UpdateValues?.ToArray(), false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryValueUpdateEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditHistoryEventUpdate event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="updateEventDetails">Update event details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryEventUpdateEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            UpdateEventDetails updateEventDetails,
            HistoryEventFieldList[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryEventUpdateEventState e = new AuditHistoryEventUpdateEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                    systemContext,
                    "AuditHistoryEventUpdateEvent",
                    "Attribute/HistoryEventUpdate",
                    updateEventDetails,
                    statusCode);

                e.SetChildValue(systemContext, BrowseNames.UpdatedNode, updateEventDetails.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.PerformInsertReplace, updateEventDetails.PerformInsertReplace, false);
                e.SetChildValue(systemContext, BrowseNames.Filter, updateEventDetails.Filter, false);
                e.SetChildValue(systemContext, BrowseNames.NewValues, updateEventDetails.EventData.ToArray(), false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryEventUpdateEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditHistoryRawModifyDelete event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="deleteRawModifiedDetails">History raw modified details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryRawModifyDeleteEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            DeleteRawModifiedDetails deleteRawModifiedDetails,
            DataValue[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryRawModifyDeleteEventState e = new AuditHistoryRawModifyDeleteEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                    systemContext,
                    "AuditHistoryRawModifyDeleteEvent",
                    "Attribute/HistoryRawModifyDelete",
                    deleteRawModifiedDetails,
                    statusCode);

                e.SetChildValue(systemContext, BrowseNames.UpdatedNode, deleteRawModifiedDetails.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.IsDeleteModified, deleteRawModifiedDetails.IsDeleteModified, false);
                e.SetChildValue(systemContext, BrowseNames.StartTime, deleteRawModifiedDetails.StartTime, false);
                e.SetChildValue(systemContext, BrowseNames.EndTime, deleteRawModifiedDetails.EndTime, false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryRawModifyDeleteEvent event.");
            }
        }

        /// <summary>
        /// Reports an AuditHistoryAtTimeDelete event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="deleteAtTimeDetails">History delete at time details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryAtTimeDeleteEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            DeleteAtTimeDetails deleteAtTimeDetails,
            DataValue[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryAtTimeDeleteEventState e = new AuditHistoryAtTimeDeleteEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                    systemContext,
                    "AuditHistoryAtTimeDeleteEvent",
                    "Attribute/HistoryAtTimeDelete",
                    deleteAtTimeDetails,
                    statusCode);

                e.SetChildValue(systemContext, BrowseNames.UpdatedNode, deleteAtTimeDetails.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.ReqTimes, deleteAtTimeDetails.ReqTimes.ToArray(), false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryAtTimeDeleteEvent event.");
            }

        }

        /// <summary>
        /// Reports an AuditHistoryEventDelete event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="deleteEventDetails">History delete event details</param>
        /// <param name="oldValues">The old values</param>
        /// <param name="statusCode">The resulting status code</param>
        public static void ReportAuditHistoryEventDeleteEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            DeleteEventDetails deleteEventDetails,
            DataValue[] oldValues,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditHistoryEventDeleteEventState e = new AuditHistoryEventDeleteEventState(null);

                InitializeAuditHistoryUpdateEvent(e,
                    systemContext,
                    "AuditHistoryEventDeleteEvent",
                    "Attribute/HistoryEventDelete",
                    deleteEventDetails,
                    statusCode);

                e.SetChildValue(systemContext, BrowseNames.UpdatedNode, deleteEventDetails.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.EventIds, deleteEventDetails.EventIds.ToArray(), false);
                e.SetChildValue(systemContext, BrowseNames.OldValues, oldValues, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditHistoryEventDeleteEvent event.");
            }
        }

        /// <summary>
        ///  Reports all audit events for client certificate ServiceResultException. It goes recursively for all service results stored in the exception
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="exception">The Exception that triggers a certificate audit event.</param>
        public static void ReportAuditCertificateEvent(
            this IAuditEventServer server,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                while (exception != null)
                {
                    if (exception is ServiceResultException sre && sre.InnerResult != null)
                    {
                        // Each validation step has a unique error status and audit event type that shall be reported if the check fails.
                        server.ReportAuditCertificateEvent(systemContext, clientCertificate, sre);
                    }
                    exception = exception.InnerException;
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportAuditCertificateDataMismatch event.");
            }
        }

        /// <summary>
        /// Report the audit certificate event for the specified ServiceResultException
        /// </summary>
        private static void ReportAuditCertificateEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            X509Certificate2 clientCertificate,
            ServiceResultException sre)
        {
            try
            {
                if (StatusCode.IsBad(sre.InnerResult.Code))
                {
                    AuditCertificateEventState auditCertificateEventState = null;
                    switch (sre.StatusCode)
                    {
                        case StatusCodes.BadCertificateTimeInvalid:
                        case StatusCodes.BadCertificateIssuerTimeInvalid:
                            // create AuditCertificateExpiredEventType
                            auditCertificateEventState = new AuditCertificateExpiredEventState(null);
                            break;
                        case StatusCodes.BadCertificateInvalid:
                        case StatusCodes.BadCertificateChainIncomplete:
                        case StatusCodes.BadCertificatePolicyCheckFailed:
                            // create AuditCertificateInvalidEventType
                            auditCertificateEventState = new AuditCertificateInvalidEventState(null);
                            break;
                        case StatusCodes.BadCertificateUntrusted:
                            // create AuditCertificateUntrustedEventType
                            auditCertificateEventState = new AuditCertificateUntrustedEventState(null);
                            break;
                        case StatusCodes.BadCertificateRevoked:
                        case StatusCodes.BadCertificateIssuerRevoked:
                        case StatusCodes.BadCertificateRevocationUnknown:
                        case StatusCodes.BadCertificateIssuerRevocationUnknown:
                            // create AuditCertificateRevokedEventType
                            auditCertificateEventState = new AuditCertificateRevokedEventState(null);
                            break;
                        case StatusCodes.BadCertificateUseNotAllowed:
                        case StatusCodes.BadCertificateIssuerUseNotAllowed:
                            // create AuditCertificateMismatchEventType
                            auditCertificateEventState = new AuditCertificateMismatchEventState(null);
                            break;
                    }
                    if (auditCertificateEventState != null)
                    {
                        auditCertificateEventState.Initialize(
                              systemContext,
                              null,
                              EventSeverity.Min,
                              sre.Message,
                              false,
                              DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                        auditCertificateEventState.SetChildValue(systemContext, BrowseNames.SourceName, "Security/Certificate", false);

                        // set AuditSecurityEventType fields
                        auditCertificateEventState.SetChildValue(systemContext, BrowseNames.StatusCodeId, (StatusCode)sre.InnerResult.StatusCode, false);

                        // set AuditCertificateEventType fields
                        auditCertificateEventState.SetChildValue(systemContext, BrowseNames.Certificate, clientCertificate?.RawData, false);

                        server.ReportAuditEvent(systemContext, auditCertificateEventState);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportAuditCertificateDataMismatch event.");
            }
        }

        /// <summary>
        /// Reports the AuditCertificateDataMismatchEventType for Invalid Uri
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="clientCertificate">The client certificate</param>
        /// <param name="invalidHostName">The string that represents the host name passed in as part of the URL that is found to be invalid. If the host name was not invalid it can be null.</param>
        /// <param name="invalidUri">The URI that was passed in and found to not match what is contained in the certificate. If the URI was not invalid it can be null.</param>
        /// <param name="statusCode">The status code.</param>
        public static void ReportAuditCertificateDataMismatchEvent(
            this IAuditEventServer server,
            X509Certificate2 clientCertificate,
            string invalidHostName,
            string invalidUri,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                // create AuditCertificateDataMismatchEventType
                AuditCertificateDataMismatchEventState e = new AuditCertificateDataMismatchEventState(null);

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   null,
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceName, "Security/Certificate", false);
                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                // set AuditSecurityEventType fields
                e.SetChildValue(systemContext, BrowseNames.StatusCodeId, statusCode, false);
                // set AuditCertificateEventType fields
                e.SetChildValue(systemContext, BrowseNames.Certificate, clientCertificate?.RawData, false);
                // set AuditCertificateDataMismatchEventState fields
                e.SetChildValue(systemContext, BrowseNames.InvalidUri, invalidUri, false);
                e.SetChildValue(systemContext, BrowseNames.InvalidHostname, invalidHostName, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportAuditCertificateDataMismatchEvent event.");
            }
        }

        /// <summary>
        /// Report the AuditCancelEventState
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="sessionId">Session id of the current session</param>
        /// <param name="requestHandle">The handle of the canceled request</param>
        /// <param name="statusCode">The resulted status code of cancel request.</param>
        public static void ReportAuditCancelEvent(
            this IAuditEventServer server,
            NodeId sessionId,
            uint requestHandle,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                // create AuditCancelEventState
                AuditCancelEventState e = new AuditCancelEventState(null);

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   $"Cancel requested for sessionId: {sessionId} with requestHandle: {requestHandle}",
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceName, "Session/Cancel", false);
                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                // set AuditSecurityEventType fields
                e.SetChildValue(systemContext, BrowseNames.StatusCodeId, statusCode, false);
                // set AuditSessionEventType fields
                e.SetChildValue(systemContext, BrowseNames.SessionId, sessionId, false);
                // set AuditCancelEventType fields
                e.SetChildValue(systemContext, BrowseNames.RequestHandle, requestHandle, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportAuditCancelEvent event.");
            }
        }

        /// <summary>
        /// Reports a RoleMappingRuleChangedAuditEvent when a method is called on a RoleType instance
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="roleStateObjectId"></param>
        /// <param name="method"></param>
        /// <param name="inputArguments"></param>
        /// <param name="status"></param>
        public static void ReportAuditRoleMappingRuleChangedEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId roleStateObjectId,
            MethodState method,
            object[] inputArguments,
            bool status)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                // create RoleMappingRuleChangedAuditEventState
                RoleMappingRuleChangedAuditEventState e = new RoleMappingRuleChangedAuditEventState(null);

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   $"RoleMappingRuleChanged - {method?.BrowseName}",
                   status,
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceName, "Attribute/Call", false);
                e.SetChildValue(systemContext, BrowseNames.SourceNode, roleStateObjectId, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                // set AuditUpdateMethodEventType fields
                e.SetChildValue(systemContext, BrowseNames.MethodId, method?.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.InputArguments, inputArguments, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportRoleMappingRuleChangedAuditEvent event.");
            }
        }

        /// <summary>
        /// Reports an audit create session event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="auditEntryId">The audit entry id.</param>
        /// <param name="session">The session object that was created.</param>
        /// <param name="revisedSessionTimeout">The revised session timeout</param>
        /// <param name="exception">The exception received during create session request</param> 
        public static void ReportAuditCreateSessionEvent(
            this IAuditEventServer server,
            string auditEntryId,
            Session session,
            double revisedSessionTimeout,
            Exception exception = null)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                // raise an audit event.
                AuditCreateSessionEventState e = new AuditCreateSessionEventState(null);

                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                      "AuditCreateSessionEvent",
                      "en-US",
                      $"Session with ID:{session?.Id} was created.");
                }
                else
                {
                    message = new TranslationInfo(
                      "AuditCreateSessionEvent",
                      "en-US",
                      $"Error while creating session with ID:{session?.Id}: Exception: {exception.Message}.");
                }

                InitializeAuditSessionEvent(systemContext, e, message, exception == null, session, auditEntryId);

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, "System/CreateSession", false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Session/CreateSession", false);

                // set AuditCreateSessionEventState fields
                e.SetChildValue(systemContext, BrowseNames.ClientCertificate, session?.ClientCertificate?.RawData, false);
                e.SetChildValue(systemContext, BrowseNames.ClientCertificateThumbprint, session?.ClientCertificate?.Thumbprint, false);
                e.SetChildValue(systemContext, BrowseNames.RevisedSessionTimeout, revisedSessionTimeout, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditCreateSessionEvent event for SessionId {0}.", session?.Id);
            }
        }

        /// <summary>
        /// Reports an audit activate session event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="auditEntryId">The audit entry id.</param>
        /// <param name="session">The session that is activated.</param>
        /// <param name="softwareCertificates">The software certificates</param>
        /// <param name="exception">The exception received during activate session request</param> 
        public static void ReportAuditActivateSessionEvent(
            this IAuditEventServer server,
            string auditEntryId,
            Session session,
            IList<SoftwareCertificate> softwareCertificates,
            Exception exception = null)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                AuditActivateSessionEventState e = new AuditActivateSessionEventState(null);

                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                     "AuditActivateSessionEvent",
                    "en-US",
                    $"Session with Id:{session.Id} was activated.");
                }
                else
                {
                    message = new TranslationInfo(
                      "AuditActivateSessionEvent",
                      "en-US",
                      $"Error while activate session with ID:{session?.Id}. Exception: {exception.Message}.");
                }

                InitializeAuditSessionEvent(systemContext, e, message, exception == null, session, auditEntryId);

                e.SetChildValue(systemContext, BrowseNames.SourceName, "Session/ActivateSession", false);
                e.SetChildValue(systemContext, BrowseNames.UserIdentityToken, Utils.Clone(session?.IdentityToken), false);

                if (softwareCertificates != null)
                {
                    // build the list of SignedSoftwareCertificate
                    List<SignedSoftwareCertificate> signedSoftwareCertificates = new List<SignedSoftwareCertificate>();
                    foreach (SoftwareCertificate softwareCertificate in softwareCertificates)
                    {
                        SignedSoftwareCertificate item = new SignedSoftwareCertificate();
                        item.CertificateData = softwareCertificate.SignedCertificate.RawData;
                        signedSoftwareCertificates.Add(item);
                    }
                    e.SetChildValue(systemContext, BrowseNames.ClientSoftwareCertificates, signedSoftwareCertificates.ToArray(), false);
                }

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Error while reporting AuditActivateSessionEvent event for SessionId {0}.", session?.Id);
            }
        }

        /// <summary>
        /// Reports an audit Url Mismatch event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="auditEntryId">The audit entry id.</param>
        /// <param name="session">The session object that was created.</param>
        /// <param name="revisedSessionTimeout">The revised session timeout</param>
        /// <param name="endpointUrl">The invalid endpoint url</param> 
        public static void ReportAuditUrlMismatchEvent(
            this IAuditEventServer server,
            string auditEntryId,
            Session session,
            double revisedSessionTimeout,
            string endpointUrl)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                AuditUrlMismatchEventState e = new AuditUrlMismatchEventState(null);

                TranslationInfo message = new TranslationInfo(
                     "AuditUrlMismatchEvent",
                     "en-US",
                     $"Session with ID:{session.Id} was created but the endpoint URL does not match the domain names in the server certificate.");

                InitializeAuditSessionEvent(systemContext, e, message, false, session, auditEntryId);

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, "System/CreateSession", false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Session/CreateSession", false);

                // set AuditCreateSessionEventState fields
                e.SetChildValue(systemContext, BrowseNames.ClientCertificate, session?.ClientCertificate?.RawData, false);
                e.SetChildValue(systemContext, BrowseNames.ClientCertificateThumbprint, session?.ClientCertificate?.Thumbprint, false);
                e.SetChildValue(systemContext, BrowseNames.RevisedSessionTimeout, revisedSessionTimeout, false);

                // set AuditUrlMismatchEventState
                e.SetChildValue(systemContext, BrowseNames.EndpointUrl, endpointUrl, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Error while reporting AuditUrlMismatchEvent event for SessionId {0}.", session?.Id);
            }
        }

        /// <summary>
        /// Reports an audit close session event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="auditEntryId">The audit entry id.</param>
        /// <param name="session">The session object that was created.</param>
        /// <param name="sourceName">Session/CloseSession when the session is closed by request
        /// “Session/Timeout” for a Session timeout
        /// “Session/Terminated” for all other cases.</param>
        public static void ReportAuditCloseSessionEvent(
            this IAuditEventServer server,
            string auditEntryId,
            Session session,
            string sourceName = "Session/Terminated")
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                // raise an audit event.
                AuditSessionEventState e = new AuditSessionEventState(null);

                TranslationInfo message = new TranslationInfo(
                    "AuditCloseSessionEvent",
                    "en-US",
                    $"Session with ID:{session?.Id} was closed.");

                InitializeAuditSessionEvent(systemContext, e, message, true, session, auditEntryId);

                e.SetChildValue(systemContext, BrowseNames.SourceName, sourceName, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditSessionEventState close event for SessionId {0}.", session?.Id);
            }
        }

        /// <summary>
        /// Reports an audit session event for the transfer subscription.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="auditEntryId">The audit entry id.</param>
        /// <param name="session">The session object that was created.</param>
        /// <param name="statusCode">The status code resulting .</param>
        public static void ReportAuditTransferSubscriptionEvent(
            this IAuditEventServer server,
            string auditEntryId,
            Session session,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                ISystemContext systemContext = server.DefaultAuditContext;

                // raise an audit event.
                AuditSessionEventState e = new AuditSessionEventState(null);

                TranslationInfo message = new TranslationInfo(
                    "AuditSessionEventState",
                    "en-US",
                    $"Transfer subscription for session ID:{session?.Id} has statusCode {statusCode}.");

                InitializeAuditSessionEvent(systemContext, e, message, StatusCode.IsGood(statusCode), session, auditEntryId);

                e.SetChildValue(systemContext, BrowseNames.SourceNode, session.Id, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Session/TransferSubscriptions", false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditSessionEventState close event for SessionId {0}.", session?.Id);
            }
        }

        /// <summary>
        /// Raise CertificateUpdatedAudit event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object ued for update certificate method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event.</param>
        /// <param name="certificateGroupId">The id of the certificate group</param>
        /// <param name="certificateTypeId">the certificate ype id</param>
        /// <param name="exception">The exception resulted after executing the UpdateCertificate method. If null, the operation was successfull.</param>
        public static void ReportCertificateUpdatedAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            object[] inputArguments,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            Exception exception = null)
        {
            try
            {
                CertificateUpdatedAuditEventState e = new CertificateUpdatedAuditEventState(null);

                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                       "CertificateUpdatedAuditEvent",
                       "en-US",
                       "CertificateUpdatedAuditEvent.");
                }
                else
                {
                    message = new TranslationInfo(
                      "CertificateUpdatedAuditEvent",
                      "en-US",
                      $"CertificateUpdatedAuditEvent - Exception: {exception.Message}.");
                }

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   exception == null,
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Method/UpdateCertificate", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.MethodId, method.NodeId, false);
                e.SetChildValue(systemContext, BrowseNames.InputArguments, inputArguments, false);

                e.SetChildValue(systemContext, BrowseNames.CertificateGroup, certificateGroupId, false);
                e.SetChildValue(systemContext, BrowseNames.CertificateType, certificateTypeId, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportCertificateUpdatedAuditEvent event.");
            }
        }

        /// <summary>
        /// Report the AuditAddNodesEvent
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="addNodesItems">The added nodes information.</param>
        /// <param name="customMessage">Custom message for add nodes audit event.</param>
        /// <param name="statusCode">The resulting status code.</param>
        public static void ReportAuditAddNodesEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            AddNodesItem[] addNodesItems,
            string customMessage,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditAddNodesEventState e = new AuditAddNodesEventState(null);

                TranslationInfo message = new TranslationInfo(
                           "AuditAddNodesEventState",
                           "en-US",
                           $"'{customMessage}' returns StatusCode: {statusCode.ToString(null, CultureInfo.InvariantCulture)}.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "NodeManagement/AddNodes", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.NodesToAdd, addNodesItems, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditAddNodesEvent event.");
            }
        }

        /// <summary>
        /// Reports the AuditDeleteNodesEvent.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="nodesToDelete">The delete nodes information.</param>
        /// <param name="customMessage">Custom message for delete nodes.</param>
        /// <param name="statusCode">The resulting status code.</param>
        public static void ReportAuditDeleteNodesEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            DeleteNodesItem[] nodesToDelete,
            string customMessage,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                AuditDeleteNodesEventState e = new AuditDeleteNodesEventState(null);

                TranslationInfo message = new TranslationInfo(
                           "AuditDeleteNodesEventState",
                           "en-US",
                           $"'{customMessage}' returns StatusCode: {statusCode.ToString(null, CultureInfo.InvariantCulture)}.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "NodeManagement/DeleteNodes", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.NodesToDelete, nodesToDelete, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditDeleteNodesEvent event.");
            }
        }

        /// <summary>
        /// Report the open secure channel audit event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="globalChannelId">The global unique channel id.</param>
        /// <param name="endpointDescription">The endpoint description used for the request.</param>
        /// <param name="request">The incoming <see cref="OpenSecureChannelRequest"/></param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        public static void ReportAuditOpenSecureChannelEvent(
            this IAuditEventServer server,
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                // raise an audit event.
                AuditOpenSecureChannelEventState e = new AuditOpenSecureChannelEventState(null);
                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                        "AuditOpenSecureChannelEvent",
                        "en-US",
                        "AuditOpenSecureChannelEvent");
                }
                else
                {
                    message = new TranslationInfo(
                        "AuditOpenSecureChannelEvent",
                        "en-US",
                        $"AuditOpenSecureChannelEvent - Exception: {exception.Message}.");
                }

                StatusCode statusCode = StatusCodes.Good;
                while (exception != null && !(exception is ServiceResultException))
                {
                    exception = exception.InnerException;
                }

                if (exception is ServiceResultException sre)
                {
                    if (sre.InnerResult != null)
                    {
                        statusCode = sre.InnerResult.StatusCode;
                    }
                    else
                    {
                        statusCode = sre.StatusCode;
                    }
                }

                ISystemContext systemContext = server.DefaultAuditContext;

                DateTime actionTimestamp = DateTime.UtcNow;
                if (request?.RequestHeader?.Timestamp != null)
                {
                    actionTimestamp = request.RequestHeader.Timestamp;
                }

                e.Initialize(
                    systemContext,
                    null,
                    EventSeverity.Min,
                    new LocalizedText(message),
                    exception == null,
                    actionTimestamp);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceName, "SecureChannel/OpenSecureChannel", false);
                e.SetChildValue(systemContext, BrowseNames.ClientUserId, "System/OpenSecureChannel", false);
                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                // set AuditSecurityEventType fields
                e.SetChildValue(systemContext, BrowseNames.StatusCodeId, statusCode, false);

                // set AuditChannelEventType fields
                e.SetChildValue(systemContext, BrowseNames.SecureChannelId, globalChannelId, false);

                // set AuditOpenSecureChannelEventType fields
                e.SetChildValue(systemContext, BrowseNames.ClientCertificate, clientCertificate?.RawData, false);
                e.SetChildValue(systemContext, BrowseNames.ClientCertificateThumbprint, clientCertificate?.Thumbprint, false);
                e.SetChildValue(systemContext, BrowseNames.RequestType, request?.RequestType, false);
                e.SetChildValue(systemContext, BrowseNames.SecurityPolicyUri, endpointDescription?.SecurityPolicyUri, false);
                e.SetChildValue(systemContext, BrowseNames.SecurityMode, endpointDescription?.SecurityMode, false);
                e.SetChildValue(systemContext, BrowseNames.RequestedLifetime, request?.RequestedLifetime, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditOpenSecureChannelEvent event.");
            }
        }

        /// <summary>
        /// Report the close secure channel audit event.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="globalChannelId">The global unique channel id.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        public static void ReportAuditCloseSecureChannelEvent(
            this IAuditEventServer server,
            string globalChannelId,
            Exception exception)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }

            try
            {
                // raise an audit event.
                AuditChannelEventState e = new AuditChannelEventState(null);

                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                        "AuditCloseSecureChannelEvent",
                        "en-US",
                        "AuditCloseSecureChannelEvent");
                }
                else
                {
                    message = new TranslationInfo(
                        "AuditCloseSecureChannelEvent",
                        "en-US",
                        $"AuditCloseSecureChannelEvent - Exception: {exception.Message}.");
                }

                StatusCode statusCode = StatusCodes.Good;
                while (exception != null && !(exception is ServiceResultException))
                {
                    exception = exception.InnerException;
                }
                if (exception is ServiceResultException sre)
                {
                    statusCode = sre.InnerResult.StatusCode;
                }

                ISystemContext systemContext = server.DefaultAuditContext;

                e.Initialize(
                    systemContext,
                    null,
                    EventSeverity.Min,
                    new LocalizedText(message),
                    exception == null,
                    DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceName, "SecureChannel/CloseSecureChannel", false);

                string clientUserId = "System/CloseSecureChannel";
                //operationContext.UserIdentity?.DisplayName, or ”System/CloseSecureChannel”

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, clientUserId, false);
                e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                // set AuditSecurityEventType fields
                e.SetChildValue(systemContext, BrowseNames.StatusCodeId, statusCode, false);

                // set AuditChannelEventType fields
                e.SetChildValue(systemContext, BrowseNames.SecureChannelId, globalChannelId, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditOpenSecureChannelEvent event.");
            }
        }


        /// <summary>
        /// Reports the AuditUpdateMethodEventType
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">Server information.</param>
        /// <param name="objectId">The id of the object where the method is executed.</param>
        /// <param name="methodId">The NodeId of the object that the method resides in.</param>
        /// <param name="inputArgs">The InputArguments of the method </param>
        /// <param name="customMessage">Custom message for delete nodes.</param>
        /// <param name="statusCode">The resulting status code.</param>
        public static void ReportAuditUpdateMethodEvent(this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            NodeId methodId,
            object[] inputArgs,
            string customMessage,
            StatusCode statusCode)
        {
            if (server?.Auditing != true)
            {
                // current server does not support auditing
                return;
            }
            try
            {
                AuditUpdateMethodEventState e = new AuditUpdateMethodEventState(null);

                TranslationInfo message = new TranslationInfo(
                           "AuditUpdateMethodEventState",
                           "en-US",
                           $"'{customMessage}' returns StatusCode: {statusCode.ToString(null, CultureInfo.InvariantCulture)}.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, "Attribute/Call", false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.ClientUserId, systemContext?.UserIdentity?.DisplayName, false);
                e.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, systemContext?.AuditEntryId, false);

                e.SetChildValue(systemContext, BrowseNames.MethodId, methodId, false);
                e.SetChildValue(systemContext, BrowseNames.InputArguments, inputArgs, false);

                server.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditDeleteNodesEvent event.");
            }
        }

        /// <summary>
        /// Reports an TrustListUpdatedAudit event.
        /// </summary>
        /// <param name="node">The trustlist node.</param>
        /// <param name="systemContext">The current system context</param>
        /// <param name="objectId">The object id where the truest list update methods was called</param>
        /// <param name="sourceName">The source name string</param>
        /// <param name="methodId">The id of the method that was called</param>
        /// <param name="inputParameters">The input parameters of the called method</param>
        /// <param name="statusCode">The status code resulted when the TrustList was updated </param>
        public static void ReportTrustListUpdatedAuditEvent(
            this TrustListState node,
            ISystemContext systemContext,
            NodeId objectId,
            string sourceName,
            NodeId methodId,
            object[] inputParameters,
            StatusCode statusCode)
        {
            try
            {
                TrustListUpdatedAuditEventState e = new TrustListUpdatedAuditEventState(null);

                TranslationInfo message = new TranslationInfo(
                   "TrustListUpdatedAuditEvent",
                   "en-US",
                   $"TrustListUpdatedAuditEvent result is: {statusCode.ToString(null, CultureInfo.InvariantCulture)}");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   StatusCode.IsGood(statusCode),
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, BrowseNames.SourceName, sourceName, false);
                e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, BrowseNames.MethodId, methodId, false);
                e.SetChildValue(systemContext, BrowseNames.InputArguments, inputParameters, false);

                node?.ReportEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting ReportTrustListUpdatedAuditEvent event.");
            }
        }
        #endregion Report Audit Events

        #region Private helpers
        /// <summary>
        /// Initialize the properties of an AuditUpdateEventState.
        /// </summary>
        /// <param name="e">AuditUpdate event reference</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="auditEventName">Audit event name</param>
        /// <param name="sourceName">Source name</param>
        /// <param name="historyUpdateDetails">History update details</param>
        /// <param name="statusCode">The resulting status code</param>
        private static void InitializeAuditHistoryUpdateEvent(
            AuditUpdateEventState e,
            ISystemContext systemContext,
            string auditEventName,
            string sourceName,
            HistoryUpdateDetails historyUpdateDetails,
            StatusCode statusCode)
        {
            TranslationInfo message = new TranslationInfo(
               auditEventName,
               "en-US",
               $"{auditEventName} has Result: {statusCode.ToString(null, CultureInfo.InvariantCulture)}.");

            e.Initialize(
               systemContext,
               null,
               EventSeverity.Min,
               new LocalizedText(message),
               StatusCode.IsGood(statusCode),
               DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

            e.SetChildValue(systemContext, BrowseNames.SourceNode, historyUpdateDetails.NodeId, false);
            e.SetChildValue(systemContext, BrowseNames.SourceName, sourceName, false);
            e.SetChildValue(systemContext, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

            e.SetChildValue(systemContext, BrowseNames.ClientUserId, systemContext?.UserIdentity?.DisplayName, false);
            e.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, systemContext?.AuditEntryId, false);

            e.SetChildValue(systemContext, BrowseNames.ParameterDataTypeId, historyUpdateDetails.TypeId, false);
        }

        /// <summary>
        /// Initializes a session audit event.
        /// </summary>
        private static void InitializeAuditSessionEvent(
            ISystemContext systemContext,
            AuditEventState e,
            TranslationInfo message,
            bool status,
            Session session,
            string auditEntryId)
        {
            e.Initialize(
                systemContext,
                null,
                EventSeverity.Min,
                new LocalizedText(message),
                status,
                DateTime.UtcNow);

            e.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);

            // set AuditEventType properties
            e.SetChildValue(systemContext, BrowseNames.ClientUserId, session?.Identity?.DisplayName, false);
            e.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, auditEntryId, false);
            // set AuditCreateSessionEventType & AuditActivateSessionsEventType properties
            e.SetChildValue(systemContext, BrowseNames.SecureChannelId, session?.SecureChannelId, false);
            // set AuditSessionEventType 
            e.SetChildValue(systemContext, BrowseNames.SessionId, session?.Id, false);
        }
        #endregion
    }
}
