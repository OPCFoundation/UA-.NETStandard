/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Diagnostics
{
    public static class AuditEvents
    {
        /// <summary>
        /// Raise CertificateDeliveredAudit event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object used for the method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event.</param>
        public static void ReportCertificateDeliveredAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            object[] inputArguments)
        {
            try
            {
                CertificateDeliveredAuditEventState e = new CertificateDeliveredAuditEventState(null);

                TranslationInfo message = new TranslationInfo(
                       "CertificateUpdateRequestedAuditEvent",
                       "en-US",
                       "CertificateUpdateRequestedAuditEvent.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   true,
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.SourceName, "Method/UpdateCertificate", false);
                e.SetChildValue(systemContext, Ua.BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.InputArguments, inputArguments, false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting CertificateDeliveredAuditEventState event.");
            }
        }

        /// <summary>
        /// Raise CertificateDeliveredAudit event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object used for the method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event.</param>
        /// <param name="certificateGroupId">The id of the certificate group</param>
        /// <param name="certificateTypeId">the certificate ype id</param>
        /// <param name="exception">The exception resulted after executing the StartNewKeyPairRequest StartNewSigningRequest method. If null, the operation was successfull.</param>
        public static void ReportCertificateRequestedAuditEvent(
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
                CertificateRequestedAuditEventState e = new CertificateRequestedAuditEventState(null);

                TranslationInfo message = null;
                if (exception == null)
                {
                    message = new TranslationInfo(
                       "CertificateRequestedAuditEvent",
                       "en-US",
                       "CertificateRequestedAuditEvent.");
                }
                else
                {
                    message = new TranslationInfo(
                      "CertificateRequestedAuditEvent",
                      "en-US",
                      $"CertificateRequestedAuditEvent - Exception: {exception.Message}.");
                }

                e.Initialize(
                  systemContext,
                  null,
                  EventSeverity.Min,
                  new LocalizedText(message),
                  exception == null,
                  DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.SourceName, "Method/UpdateCertificate", false);
                e.SetChildValue(systemContext, Ua.BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.InputArguments, inputArguments, false);

                e.SetChildValue(systemContext, BrowseNames.CertificateGroup, certificateGroupId, false);
                e.SetChildValue(systemContext, BrowseNames.CertificateType, certificateTypeId, false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting CertificateDeliveredAuditEventState event.");
            }
        }

        /// <summary>
        /// Raise ApplicationRegistrationChanged event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object used for register Application method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event.</param>
        public static void ReportApplicationRegistrationChangedAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            object[] inputArguments)
        {
            try
            {
                ApplicationRegistrationChangedAuditEventState e = new ApplicationRegistrationChangedAuditEventState(null);

                var message = new TranslationInfo(
                       "ApplicationRegistrationChangedAuditEvent",
                       "en-US",
                       "ApplicationRegistrationChangedAuditEvent.");

                e.Initialize(
                   systemContext,
                   null,
                   EventSeverity.Min,
                   new LocalizedText(message),
                   true,
                   DateTime.UtcNow);  // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.SourceName, "Method/UpdateCertificate", false);
                e.SetChildValue(systemContext, Ua.BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId, false);
                e.SetChildValue(systemContext, Ua.BrowseNames.InputArguments, inputArguments, false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting CertificateDeliveredAuditEventState event.");
            }
        }
    }
}
