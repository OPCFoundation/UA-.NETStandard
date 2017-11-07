/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A Certificate Group Manager
    /// </summary>
    public class ServerConfigurationManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the group manager.
        /// </summary>
        public ServerConfigurationManager(
            ServerConfigurationState node,
            ApplicationConfiguration configuration
            )
        {
            node.ServerCapabilities.Value = configuration.ServerConfiguration.ServerCapabilities.ToArray();
            node.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            node.ServerCapabilities.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            node.SupportedPrivateKeyFormats.Value = configuration.ServerConfiguration.SupportedPrivateKeyFormats.ToArray();
            node.SupportedPrivateKeyFormats.ValueRank = ValueRanks.OneDimension;
            node.SupportedPrivateKeyFormats.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            node.MaxTrustListSize.Value = (uint)configuration.ServerConfiguration.MaxTrustListSize;
            node.MulticastDnsEnabled.Value = configuration.ServerConfiguration.MultiCastDnsEnabled;

            node.UpdateCertificate.OnCall += UpdateCertificate;
            node.CreateSigningRequest.OnCall += CreateSigningRequest;
            node.ApplyChanges.OnCallMethod += ApplyChanges;
            node.GetRejectedList.OnCall += GetRejectedList;

            m_node = node;
            m_rejectedListPath = configuration.SecurityConfiguration.RejectedCertificateStore.StorePath;
        }
        #endregion

        #region Private Methods
        private ServiceResult UpdateCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificate,
            byte[][] issuerCertificates,
            string privateKeyFormat,
            byte[] privateKey,
            ref bool applyChangesRequired)
        {
            return StatusCodes.BadUserAccessDenied;
        }


        private ServiceResult CreateSigningRequest(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                NodeId certificateGroupId,
                NodeId certificateTypeId,
                string subjectName,
                bool regeneratePrivateKey,
                byte[] nonce,
                ref byte[] certificateRequest)
        {
            return StatusCodes.BadUserAccessDenied;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return StatusCodes.BadUserAccessDenied;
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref byte[][] certificates)
        {
            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_rejectedListPath))
            {
                X509Certificate2Collection collection = store.Enumerate().Result;
                List<byte[]> rawList = new List<byte[]>();
                foreach (var cert in collection)
                {
                    rawList.Add(cert.RawData);
                }
                certificates = rawList.ToArray();
            }
            return StatusCodes.Good;
        }
        #endregion

        #region Private Fields
        private readonly string m_rejectedListPath;
        private ServerConfigurationState m_node;
        #endregion
    }
}
