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

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    public class TrustList
    {
        #region Constructors
        public TrustList(Opc.Ua.TrustListState node, string trustedListPath, string issuerListPath)
        {
            m_node = node;
            m_trustedListPath = trustedListPath;
            m_issuerListPath = issuerListPath;

            node.Open.OnCall = new OpenMethodStateMethodCallHandler(Open);
            node.OpenWithMasks.OnCall = new OpenWithMasksMethodStateMethodCallHandler(OpenWithMasks);
            node.Read.OnCall = new ReadMethodStateMethodCallHandler(Read);
            node.Close.OnCall = new CloseMethodStateMethodCallHandler(Close);
            // note: TrustedList is not fully preset in current uanodes definition.
            node.CloseAndUpdate = new CloseAndUpdateMethodState(node);
            node.AddCertificate = new AddCertificateMethodState(node);
            node.RemoveCertificate = new RemoveCertificateMethodState(node);
            node.CloseAndUpdate.OnCall = new CloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
            node.AddCertificate.OnCall = new AddCertificateMethodStateMethodCallHandler(AddCertificate);
            node.RemoveCertificate.OnCall = new RemoveCertificateMethodStateMethodCallHandler(RemoveCertificate);
        }
        #endregion

        #region Private Methods
        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            return Open(context, method, objectId, mode, 0xF, ref fileHandle);
        }

        private ServiceResult OpenWithMasks(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            ref uint fileHandle)
        {
            return Open(context, method, objectId, 1, masks, ref fileHandle);
        }

        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            uint masks,
            ref uint fileHandle)
        {
            if (mode != 1)
            {
                return StatusCodes.BadNotWritable;
            }

            lock (m_lock)
            {
                if (m_sessionId != null)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                m_sessionId = context.SessionId;
                fileHandle = ++m_fileHandle;

                TrustListDataType trustList = new TrustListDataType()
                {
                    SpecifiedLists = masks
                };
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_trustedListPath))
                {
                    if ((masks & (uint)TrustListMasks.TrustedCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = store.Enumerate().Result;
                        foreach (var certificate in certificates)
                        {
                            trustList.TrustedCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & (uint)TrustListMasks.TrustedCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs())
                        {
                            trustList.TrustedCrls.Add(crl.RawData);
                        }
                    }
                }

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_issuerListPath))
                {
                    if ((masks & (uint)TrustListMasks.IssuerCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = store.Enumerate().Result;
                        foreach (var certificate in certificates)
                        {
                            trustList.IssuerCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & (uint)TrustListMasks.IssuerCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs())
                        {
                            trustList.IssuerCrls.Add(crl.RawData);
                        }
                    }
                }

                ServiceMessageContext messageContext = new ServiceMessageContext()
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Factory = context.EncodeableFactory
                };
                MemoryStream strm = new MemoryStream();
                BinaryEncoder encoder = new BinaryEncoder(strm, messageContext);
                encoder.WriteEncodeable(null, trustList, null);
                strm.Position = 0;
                m_strm = strm;

                m_node.OpenCount.Value = 1;
            }

            return ServiceResult.Good;
        }

        private ServiceResult Read(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref byte[] data)
        {
            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                data = new byte[length];

                int bytesRead = m_strm.Read(data, 0, length);

                if (bytesRead < 0)
                {
                    return StatusCodes.BadUnexpectedError;
                }

                if (bytesRead < length)
                {
                    byte[] bytes = new byte[bytesRead];
                    Array.Copy(data, bytes, bytesRead);
                    data = bytes;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult Close(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                m_sessionId = null;
                m_strm = null;
                m_node.OpenCount.Value = 0;
            }

            return ServiceResult.Good;
        }

        private ServiceResult CloseAndUpdate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref bool restartRequired)
        {
            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                m_sessionId = null;
                m_strm = null;
                m_node.OpenCount.Value = 0;
            }

            return ServiceResult.Good;
        }

        private ServiceResult AddCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] certificate,
            bool isTrustedCertificate)
        {
            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedListPath : m_issuerListPath))
            {
                store.Add(new X509Certificate2(certificate));
            }
            return ServiceResult.Good;
        }

        private ServiceResult RemoveCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string certificate,
            bool isTrustedCertificate)
        {
            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedListPath : m_issuerListPath))
            {
                store.Delete(new X509Certificate2(certificate).Thumbprint);
            }
            return ServiceResult.Good;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private readonly string m_trustedListPath;
        private readonly string m_issuerListPath;
        private TrustListState m_node;
        private Stream m_strm;
        #endregion

    }

}
