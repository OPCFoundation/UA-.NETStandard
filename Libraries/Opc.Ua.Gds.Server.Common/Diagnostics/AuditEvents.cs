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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Diagnostics
{
    /// <summary>
    /// Helpers that raise the GDS-specific audit events defined in
    /// OPC 10000-12.
    /// </summary>
    /// <remarks>
    /// Argument arrays passed to these helpers <b>must not</b> contain
    /// sensitive values such as private key passwords or private key bytes.
    /// Use <see cref="RedactedPrivateKeyPassword"/> and
    /// <see cref="RedactedPrivateKey"/> in place of those values so the
    /// resulting audit payload remains safe to persist.
    /// </remarks>
    public static class AuditEvents
    {
        /// <summary>
        /// Placeholder used in audit event <c>InputArguments</c> to indicate
        /// a private key password has been redacted.
        /// </summary>
        public const string RedactedPrivateKeyPassword = "<redacted>";

        /// <summary>
        /// Placeholder used in audit event <c>InputArguments</c> to indicate
        /// a private key byte string has been redacted.
        /// </summary>
        public static readonly ByteString RedactedPrivateKey = ByteString.Empty;

        /// <summary>
        /// Raise CertificateDeliveredAudit event
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object used for the method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event. Must not contain private key material.</param>
        /// <param name="logger">A contextual logger to log to</param>
        internal static void ReportCertificateDeliveredAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            ILogger logger)
        {
            try
            {
                var e = new CertificateDeliveredAuditEventState(null);

                var message = new TranslationInfo(
                    "CertificateUpdateRequestedAuditEvent",
                    "en-US",
                    "CertificateUpdateRequestedAuditEvent.");

                e.Initialize(systemContext, null, EventSeverity.Min, new LocalizedText(message), true, DateTime.UtcNow); // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.SourceName,
                    "Attribute/Call",
                    false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.LocalTime,
                    TimeZoneDataType.Local,
                    false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId ?? default, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.InputArguments,
                    inputArguments,
                    false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error while reporting CertificateDeliveredAuditEventState event.");
            }
        }

        /// <summary>
        /// Raise CertificateRequestedAudit event for
        /// <c>StartSigningRequest</c> / <c>StartNewKeyPairRequest</c>.
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">The id of the object used for the method</param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event. Must not contain private key passwords.</param>
        /// <param name="certificateGroupId">The id of the certificate group</param>
        /// <param name="certificateTypeId">the certificate type id</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="exception">The exception resulted after executing the StartNewKeyPairRequest StartNewSigningRequest method. If null, the operation was successfull.</param>
        internal static void ReportCertificateRequestedAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ILogger logger,
            Exception? exception = null)
        {
            try
            {
                var e = new CertificateRequestedAuditEventState(null);

                TranslationInfo message = default;
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
                    DateTime.UtcNow
                ); // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.SourceName,
                    "Attribute/Call",
                    false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.LocalTime,
                    TimeZoneDataType.Local,
                    false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId ?? default, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.InputArguments,
                    inputArguments,
                    false);

                e.SetChildValue(
                    systemContext,
                    BrowseNames.CertificateGroup,
                    certificateGroupId,
                    false);
                e.SetChildValue(
                    systemContext,
                    BrowseNames.CertificateType,
                    certificateTypeId,
                    false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error while reporting CertificateRequestedAuditEventState event.");
            }
        }

        /// <summary>
        /// Raise CertificateRevokedAudit event for
        /// <c>RevokeCertificate</c> (OPC 10000-12 §7.6.9).
        /// </summary>
        /// <param name="server">The server which reports audit events.</param>
        /// <param name="systemContext">The current system context.</param>
        /// <param name="objectId">
        /// The id of the object on which the method was called.
        /// </param>
        /// <param name="method">The method that triggered the audit event.</param>
        /// <param name="inputArguments">The input arguments used to call the method that triggered the audit event.</param>
        /// <param name="logger">A contextual logger to log to.</param>
        /// <param name="exception">
        /// If non-null, indicates the revoke operation failed; the audit
        /// event is raised with <c>Status=false</c>.
        /// </param>
        internal static void ReportCertificateRevokedAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            ILogger logger,
            Exception? exception = null)
        {
            try
            {
                var e = new CertificateRevokedAuditEventState(null);

                TranslationInfo message = exception == null
                    ? new TranslationInfo(
                        "CertificateRevokedAuditEvent",
                        "en-US",
                        "CertificateRevokedAuditEvent.")
                    : new TranslationInfo(
                        "CertificateRevokedAuditEvent",
                        "en-US",
                        $"CertificateRevokedAuditEvent - Exception: {exception.Message}.");

                e.Initialize(
                    systemContext,
                    null,
                    EventSeverity.Min,
                    new LocalizedText(message),
                    exception == null,
                    DateTime.UtcNow);

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.SourceName,
                    "Attribute/Call",
                    false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.LocalTime,
                    TimeZoneDataType.Local,
                    false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId ?? default, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.InputArguments,
                    inputArguments,
                    false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error while reporting CertificateRevokedAuditEventState event.");
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
        /// <param name="logger">A contextual logger to log to</param>
        internal static void ReportApplicationRegistrationChangedAuditEvent(
            this IAuditEventServer server,
            ISystemContext systemContext,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            ILogger logger)
        {
            try
            {
                var e = new ApplicationRegistrationChangedAuditEventState(null);

                var message = new TranslationInfo(
                    "ApplicationRegistrationChangedAuditEvent",
                    "en-US",
                    "ApplicationRegistrationChangedAuditEvent.");

                e.Initialize(systemContext, null, EventSeverity.Min, new LocalizedText(message), true, DateTime.UtcNow); // initializes Status, ActionTimeStamp, ServerId, ClientAuditEntryId, ClientUserId

                e.SetChildValue(systemContext, Ua.BrowseNames.SourceNode, objectId, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.SourceName,
                    "Attribute/Call",
                    false);

                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.LocalTime,
                    TimeZoneDataType.Local,
                    false);

                e.SetChildValue(systemContext, Ua.BrowseNames.MethodId, method?.NodeId ?? default, false);
                e.SetChildValue(
                    systemContext,
                    Ua.BrowseNames.InputArguments,
                    inputArguments,
                    false);

                server?.ReportAuditEvent(systemContext, e);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error while reporting ApplicationRegistrationChangedAuditEventState event.");
            }
        }
    }
}
