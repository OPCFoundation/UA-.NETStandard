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
using System.Security.Cryptography.X509Certificates;

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
            X509Certificate2 clientCertificate,
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
        void ReportAuditCertificateEvent(X509Certificate2 clientCertificate, Exception exception);
    }
}
