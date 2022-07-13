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
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to a object that receives notifications from the listener when a message arrives.
    /// </summary>
    public interface ITransportListenerCallback
    {
        /// <summary>
        /// Begins processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="channeId">A unique identifier for the secure channel which is the source of the request.</param>
        /// <param name="endpointDescription">The description of the endpoint which the secure channel is using.</param>
        /// <param name="request">The incoming request.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="callbackData">The callback data.</param>
        /// <returns>The result which must be passed to the EndProcessRequest method.</returns>
        /// <seealso cref="EndProcessRequest" />
        /// <seealso cref="ITransportListener" />
        IAsyncResult BeginProcessRequest(
            string channeId,
            EndpointDescription endpointDescription,
            IServiceRequest request,
            AsyncCallback callback,
            object callbackData);

        /// <summary>
        /// Ends processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="result">The result returned by the BeginProcessRequest method.</param>
        /// <returns>The response to return over the secure channel.</returns>
        /// <seealso cref="BeginProcessRequest" />
        IServiceResponse EndProcessRequest(IAsyncResult result);

        /// <summary>
        /// Report the open secure channel audit event
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="request">The incuming <see cref="OpenSecureChannelRequest"/></param>
        /// <param name="clientCertificate">The cliet certificate.</param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        void ReportAuditOpenSecureChannelEvent(TcpServerChannel channel, OpenSecureChannelRequest request, X509Certificate2 clientCertificate, Exception exception);

        /// <summary>
        /// Report the close secure channel audit event
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="exception">The exception resulted from the open secure channel request.</param>
        void ReportAuditCloseSecureChannelEvent(TcpServerChannel channel, Exception exception);

        /// <summary>
        /// Report certificate audit event 
        /// </summary>
        /// <param name="clientCertificate">The cliet certificate.</param>
        /// <param name="exception">The Exception that triggers a certificate audit event.</param>
        void ReportAuditCertificateEvent(X509Certificate2 clientCertificate, Exception exception);

    }
}
