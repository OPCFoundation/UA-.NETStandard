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

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The state of a certificate request.
    /// </summary>
    public enum CertificateRequestState
    {
        /// <summary>
        /// The certificate request is New.
        /// </summary>
        New,

        /// <summary>
        /// The certificate request is Approved.
        /// </summary>
        Approved,

        /// <summary>
        /// The certificate request is Rejected.
        /// </summary>
        Rejected,

        /// <summary>
        /// The certificate request is Accepted.
        /// </summary>
        Accepted
    }

    /// <summary>
    /// An abstract interface to the application database
    /// </summary>
    public interface ICertificateRequest
    {
        /// <summary>
        /// Initialize a certificate request.
        /// </summary>
        void Initialize();

        /// <summary>
        /// The namesapce index.
        /// </summary>
        ushort NamespaceIndex { get; set; }

        /// <summary>
        /// Start a signing request for an application.
        /// </summary>
        /// <param name="applicationId">The id of the application.</param>
        /// <param name="certificateGroupId">The target group for the signing request.</param>
        /// <param name="certificateTypeId">The certificate type.</param>
        /// <param name="certificateRequest">The certificate signing request (CSR).</param>
        /// <param name="authorityId">The authority requesting the certificate.</param>
        /// <returns>The id of the signing request.</returns>
        NodeId StartSigningRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            byte[] certificateRequest,
            string authorityId);

        /// <summary>
        /// Start a request for a new key pair.
        /// </summary>
        /// <param name="applicationId">The id of the application.</param>
        /// <param name="certificateGroupId">The target group for the signing request.</param>
        /// <param name="certificateTypeId">The certificate type.</param>
        /// <param name="subjectName">The subject for the certificate</param>
        /// <param name="domainNames">The domain names for the certficate.</param>
        /// <param name="privateKeyFormat">The private key format, PEM or PFX.</param>
        /// <param name="privateKeyPassword">The password for the private key.</param>
        /// <param name="authorityId">The authority requesting the certificate.</param>
        /// <returns>The id of the key pair request.</returns>
        NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            string authorityId);

        /// <summary>
        /// Approve or reject a request.
        /// </summary>
        /// <param name="requestId">The id of the request.</param>
        /// <param name="isRejected">Whether the request is rejected.</param>
        void ApproveRequest(
            NodeId requestId,
            bool isRejected);

        /// <summary>
        /// Accept the request.
        /// </summary>
        /// <param name="requestId">The request id.</param>
        /// <param name="certificate">The accepted certificate.</param>
        void AcceptRequest(
            NodeId requestId,
            byte[] certificate);

        /// <summary>
        /// Finish the request.
        /// </summary>
        /// <param name="applicationId">The id of the application.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="certificateGroupId">The group id.</param>
        /// <param name="certificateTypeId">The certificate type.</param>
        /// <param name="signedCertificate">The signed certificate.</param>
        /// <param name="privateKey">The private key, if requested.</param>
        CertificateRequestState FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out byte[] signedCertificate,
            out byte[] privateKey
            );

        /// <summary>
        /// Read a certificate request.
        /// </summary>
        /// <param name="applicationId">The id of the application.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="certificateGroupId">The group id.</param>
        /// <param name="certificateTypeId">The certificate type.</param>
        /// <param name="certificateRequest"></param>
        /// <param name="subjectName">The subject for the certificate</param>
        /// <param name="domainNames">The domain names for the certficate.</param>
        /// <param name="privateKeyFormat">The private key format, PEM or PFX.</param>
        /// <param name="privateKeyPassword">The password for the private key.</param>
        CertificateRequestState ReadRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out byte[] certificateRequest,
            out string subjectName,
            out string[] domainNames,
            out string privateKeyFormat,
            out string privateKeyPassword);

    }
}
