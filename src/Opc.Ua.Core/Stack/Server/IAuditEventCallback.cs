// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to an object that receives audit event notifications.
    /// </summary>
    public interface IAuditEventCallback
    {
        /// <summary>
        /// Report the open secure channel audit event.
        /// </summary>
        /// <param name="globalChannelId">The global unique channel id.</param>
        /// <param name="endpointDescription">The endpoint description used for the request.</param>
        /// <param name="request">The incoming <see cref="OpenSecureChannelRequest"/></param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            Certificate clientCertificate,
            Exception exception);

        /// <summary>
        /// Report the close secure channel audit event.
        /// </summary>
        /// <param name="globalChannelId">The global unique channel id.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception);

        /// <summary>
        /// Report certificate audit event.
        /// </summary>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="exception">The Exception that triggers a certificate audit event.</param>
        void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception);
    }
}
