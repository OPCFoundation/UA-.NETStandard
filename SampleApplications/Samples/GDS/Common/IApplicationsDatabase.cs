/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Gds
{
    public enum CertificateRequestState
    {
        New,
        Approved,
        Rejected,
        Accepted
    }

    /// <summary>
    /// An abstract interface to the application database
    /// </summary>
    public interface IApplicationsDatabase
    {
        void Initialize();
        ushort NamespaceIndex { get; set; }
        NodeId RegisterApplication(ApplicationRecordDataType application);
        NodeId CreateCertificateRequest(
            NodeId applicationId,
            byte[] certificate,
            byte[] privateKey,
            string authorityId);
        void ApproveCertificateRequest(
            NodeId requestId, 
            bool isRejected);
        bool CompleteCertificateRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] certificate,
            out byte[] privateKey);

        void UnregisterApplication(
            NodeId applicationId,
            out byte[] certificate,
            out byte[] httpsCertificate);
        ApplicationRecordDataType GetApplication(NodeId applicationId);
        ApplicationRecordDataType[] FindApplications(string applicationUri);
        ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime);
        bool SetApplicationCertificate(
            NodeId applicationId, 
            byte[] certificate, 
            bool isHttpsCertificate);
        bool SetApplicationTrustLists(
            NodeId applicationId, 
            NodeId trustListId, 
            NodeId httpsTrustListId);
    }
}
