/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#if CLIENT_ASYNC

#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Obsolete warnings for service calls which should not be used when using the Session API.
    /// </summary>
    public partial class Session : SessionClientBatched, ISession, IDisposable
    {
        /// <inheritdoc/>
        [Obsolete("Call Create instead. Service Call doesn't create Session.")]
        public override ResponseHeader CreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize) => base.CreateSession(
                requestHeader, clientDescription, serverUri,
                endpointUrl, sessionName, clientNonce,
                clientCertificate, requestedSessionTimeout, maxResponseMessageSize,
                out sessionId, out authenticationToken, out revisedSessionTimeout,
                out serverNonce, out serverCertificate, out serverEndpoints,
                out serverSoftwareCertificates, out serverSignature, out maxRequestMessageSize);

        /// <inheritdoc/>
        [Obsolete("Call Create instead. Service Call doesn't create Session.")]
        public override Task<CreateSessionResponse> CreateSessionAsync(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct) => base.CreateSessionAsync(
                requestHeader, clientDescription, serverUri,
                endpointUrl, sessionName, clientNonce,
                clientCertificate, requestedSessionTimeout, maxResponseMessageSize, ct);

        /// <inheritdoc/>
        [Obsolete("Call Close instead. Service Call doesn't clean up Session.")]
        public override ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions) => base.CloseSession(requestHeader, deleteSubscriptions);

        /// <inheritdoc/>
        [Obsolete("Call CloseAsync instead. Service Call doesn't clean up Session.")]
        public override Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct) => base.CloseSessionAsync(requestHeader, deleteSubscriptions, ct);
    }
}
#endif
