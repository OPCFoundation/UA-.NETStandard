/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to a object that receives audit event notifications.
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
            X509Certificate2 clientCertificate,
            Exception exception);

        /// <summary>
        /// Report the close secure channel audit event.
        /// </summary>
        /// <param name="globalChannelId">The global unique channel id.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        void ReportAuditCloseSecureChannelEvent(
            string globalChannelId,
            Exception exception);

        /// <summary>
        /// Report certificate audit event. 
        /// </summary>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="exception">The Exception that triggers a certificate audit event.</param>
        void ReportAuditCertificateEvent(
            X509Certificate2 clientCertificate,
            Exception exception);
    }
}
