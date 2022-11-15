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

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The implementation of a server trustlist.
    /// </summary>
    public class TrustList
    {
        const int kDefaultTrustListCapacity = 0x10000;

        #region Constructors
        /// <summary>
        /// Initialize the trustlist with default values.
        /// </summary>
        public TrustList(
            TrustListState node,
            string trustedListPath,
            string issuerListPath,
            SecureAccess readAccess,
            SecureAccess writeAccess)
        {
            m_node = node;
            m_trustedStorePath = trustedListPath;
            m_issuerStorePath = issuerListPath;
            m_readAccess = readAccess;
            m_writeAccess = writeAccess;

            node.Open.OnCall = new OpenMethodStateMethodCallHandler(Open);
            node.OpenWithMasks.OnCall = new OpenWithMasksMethodStateMethodCallHandler(OpenWithMasks);
            node.Read.OnCall = new ReadMethodStateMethodCallHandler(Read);
            node.Write.OnCall = new WriteMethodStateMethodCallHandler(Write);
            node.Close.OnCall = new CloseMethodStateMethodCallHandler(Close);
            node.CloseAndUpdate.OnCall = new CloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
            node.AddCertificate.OnCall = new AddCertificateMethodStateMethodCallHandler(AddCertificate);
            node.RemoveCertificate.OnCall = new RemoveCertificateMethodStateMethodCallHandler(RemoveCertificate);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Delegate to validate the access to the trust list.
        /// </summary>
        /// <param name="context"></param>
        public delegate void SecureAccess(ISystemContext context);
        #endregion

        #region Private Methods
        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            return Open(context, method, objectId, (OpenFileMode)mode, TrustListMasks.All, ref fileHandle);
        }

        private ServiceResult OpenWithMasks(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            ref uint fileHandle)
        {
            return Open(context, method, objectId, OpenFileMode.Read, (TrustListMasks)masks, ref fileHandle);
        }

        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            OpenFileMode mode,
            TrustListMasks masks,
            ref uint fileHandle)
        {
            HasSecureReadAccess(context);

            if (mode == OpenFileMode.Read)
            {
                HasSecureReadAccess(context);
            }
            else if (mode == (OpenFileMode.Write | OpenFileMode.EraseExisting))
            {
                HasSecureWriteAccess(context);
            }
            else
            {
                return StatusCodes.BadNotWritable;
            }

            lock (m_lock)
            {
                if (m_sessionId != null)
                {
                    // to avoid deadlocks, last open always wins
                    m_sessionId = null;
                    m_strm = null;
                    m_node.OpenCount.Value = 0;
                }

                m_readMode = mode == OpenFileMode.Read;
                m_sessionId = context.SessionId;
                fileHandle = ++m_fileHandle;

                TrustListDataType trustList = new TrustListDataType() {
                    SpecifiedLists = (uint)masks
                };

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_trustedStorePath))
                {
                    if ((masks & TrustListMasks.TrustedCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = store.Enumerate().GetAwaiter().GetResult();
                        foreach (var certificate in certificates)
                        {
                            trustList.TrustedCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & TrustListMasks.TrustedCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs().GetAwaiter().GetResult())
                        {
                            trustList.TrustedCrls.Add(crl.RawData);
                        }
                    }
                }

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_issuerStorePath))
                {
                    if ((masks & TrustListMasks.IssuerCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = store.Enumerate().GetAwaiter().GetResult();
                        foreach (var certificate in certificates)
                        {
                            trustList.IssuerCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & TrustListMasks.IssuerCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs().GetAwaiter().GetResult())
                        {
                            trustList.IssuerCrls.Add(crl.RawData);
                        }
                    }
                }

                if (m_readMode)
                {
                    m_strm = EncodeTrustListData(context, trustList);
                }
                else
                {
                    m_strm = new MemoryStream(kDefaultTrustListCapacity);
                }

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
            HasSecureReadAccess(context);

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

        private ServiceResult Write(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            byte[] data)
        {
            HasSecureWriteAccess(context);

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

                m_strm.Write(data, 0, data.Length);

            }

            return ServiceResult.Good;
        }


        private ServiceResult Close(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            HasSecureReadAccess(context);

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
            HasSecureWriteAccess(context);

            ServiceResult result = StatusCodes.Good;

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

                try
                {

                    TrustListDataType trustList = DecodeTrustListData(context, m_strm);
                    TrustListMasks masks = (TrustListMasks)trustList.SpecifiedLists;

                    X509Certificate2Collection issuerCertificates = null;
                    X509CRLCollection issuerCrls = null;
                    X509Certificate2Collection trustedCertificates = null;
                    X509CRLCollection trustedCrls = null;

                    // test integrity of all CRLs
                    if ((masks & TrustListMasks.IssuerCertificates) != 0)
                    {
                        issuerCertificates = new X509Certificate2Collection();
                        foreach (var cert in trustList.IssuerCertificates)
                        {
                            issuerCertificates.Add(new X509Certificate2(cert));
                        }
                    }
                    if ((masks & TrustListMasks.IssuerCrls) != 0)
                    {
                        issuerCrls = new X509CRLCollection();
                        foreach (var crl in trustList.IssuerCrls)
                        {
                            issuerCrls.Add(new X509CRL(crl));
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCertificates) != 0)
                    {
                        trustedCertificates = new X509Certificate2Collection();
                        foreach (var cert in trustList.TrustedCertificates)
                        {
                            trustedCertificates.Add(new X509Certificate2(cert));
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCrls) != 0)
                    {
                        trustedCrls = new X509CRLCollection();
                        foreach (var crl in trustList.TrustedCrls)
                        {
                            trustedCrls.Add(new X509CRL(crl));
                        }
                    }

                    // update store
                    // test integrity of all CRLs
                    TrustListMasks updateMasks = TrustListMasks.None;
                    if ((masks & TrustListMasks.IssuerCertificates) != 0)
                    {
                        if (UpdateStoreCertificates(m_issuerStorePath, issuerCertificates).GetAwaiter().GetResult())
                        {
                            updateMasks |= TrustListMasks.IssuerCertificates;
                        }
                    }
                    if ((masks & TrustListMasks.IssuerCrls) != 0)
                    {
                        if (UpdateStoreCrls(m_issuerStorePath, issuerCrls).GetAwaiter().GetResult())
                        {
                            updateMasks |= TrustListMasks.IssuerCrls;
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCertificates) != 0)
                    {
                        if (UpdateStoreCertificates(m_trustedStorePath, trustedCertificates).GetAwaiter().GetResult())
                        {
                            updateMasks |= TrustListMasks.TrustedCertificates;
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCrls) != 0)
                    {
                        if (UpdateStoreCrls(m_trustedStorePath, trustedCrls).GetAwaiter().GetResult())
                        {
                            updateMasks |= TrustListMasks.TrustedCrls;
                        }
                    }

                    if (masks != updateMasks)
                    {
                        result = StatusCodes.BadCertificateInvalid;
                    }
                }
                catch
                {
                    result = StatusCodes.BadCertificateInvalid;
                }
                finally
                {
                    m_sessionId = null;
                    m_strm = null;
                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                    m_node.OpenCount.Value = 0;
                }
            }

            restartRequired = false;

            // report the TrustListUpdatedAuditEvent
            object[] inputParameters = new object[] { fileHandle };
            m_node.ReportTrustListUpdatedAuditEvent(context, objectId, "Method/CloseAndUpdate", method.NodeId, inputParameters, result.StatusCode);

            return result;
        }

        private ServiceResult AddCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] certificate,
            bool isTrustedCertificate)
        {
            HasSecureWriteAccess(context);

            ServiceResult result = StatusCodes.Good;
            lock (m_lock)
            {

                if (m_sessionId != null)
                {
                    result = StatusCodes.BadInvalidState;
                }
                else if (certificate == null)
                {
                    result = StatusCodes.BadInvalidArgument;
                }
                else
                {
                    X509Certificate2 cert = null;
                    try
                    {
                        cert = new X509Certificate2(certificate);
                    }
                    catch
                    {
                        // note: a previous version of the sample code accepted also CRL,
                        // but the behaviour was not as specified and removed
                        // https://mantis.opcfoundation.org/view.php?id=6342
                        result = StatusCodes.BadCertificateInvalid;
                    }

                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedStorePath : m_issuerStorePath))
                    {
                        if (cert != null)
                        {
                            store.Add(cert).GetAwaiter().GetResult();
                        }
                    }

                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                }
            }

            // report the TrustListUpdatedAuditEvent
            object[] inputParameters = new object[] { certificate, isTrustedCertificate };
            m_node.ReportTrustListUpdatedAuditEvent(context, objectId, "Method/AddCertificate", method.NodeId, inputParameters, result.StatusCode);

            return result;

        }

        private ServiceResult RemoveCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate)
        {
            HasSecureWriteAccess(context);
            ServiceResult result = StatusCodes.Good;
            lock (m_lock)
            {

                if (m_sessionId != null)
                {
                    result = StatusCodes.BadInvalidState;
                }
                else if (String.IsNullOrEmpty(thumbprint))
                {
                    result = StatusCodes.BadInvalidArgument;
                }
                else
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedStorePath : m_issuerStorePath))
                    {
                        var certCollection = store.FindByThumbprint(thumbprint).GetAwaiter().GetResult();

                        if (certCollection.Count == 0)
                        {
                            result = StatusCodes.BadInvalidArgument;
                        }
                        else
                        {
                            // delete all CRLs signed by cert
                            var crlsToDelete = new X509CRLCollection();
                            foreach (var crl in store.EnumerateCRLs().GetAwaiter().GetResult())
                            {
                                foreach (var cert in certCollection)
                                {
                                    if (X509Utils.CompareDistinguishedName(cert.SubjectName, crl.IssuerName) &&
                                        crl.VerifySignature(cert, false))
                                    {
                                        crlsToDelete.Add(crl);
                                        break;
                                    }
                                }
                            }

                            if (!store.Delete(thumbprint).GetAwaiter().GetResult())
                            {
                                result = StatusCodes.BadInvalidArgument;
                            }
                            else
                            {
                                foreach (var crl in crlsToDelete)
                                {
                                    if (!store.DeleteCRL(crl).GetAwaiter().GetResult())
                                    {
                                        // intentionally ignore errors, try best effort
                                        Utils.LogError("RemoveCertificate: Failed to delete CRL {0}.", crl.ToString());
                                    }
                                }
                            }
                        }
                    }

                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                }
            }

            // report the TrustListUpdatedAuditEvent
            object[] inputParameters = new object[] { thumbprint };
            m_node.ReportTrustListUpdatedAuditEvent(context, objectId, "Method/RemoveCertificate", method.NodeId, inputParameters, result.StatusCode);

            return result;
        }

        private Stream EncodeTrustListData(
            ISystemContext context,
            TrustListDataType trustList
            )
        {
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            MemoryStream strm = new MemoryStream();
            BinaryEncoder encoder = new BinaryEncoder(strm, messageContext);
            encoder.WriteEncodeable(null, trustList, null);
            strm.Position = 0;
            return strm;
        }

        private TrustListDataType DecodeTrustListData(
            ISystemContext context,
            Stream strm)
        {
            TrustListDataType trustList = new TrustListDataType();
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            strm.Position = 0;
            BinaryDecoder decoder = new BinaryDecoder(strm, messageContext);
            trustList.Decode(decoder);
            decoder.Close();
            return trustList;
        }

        private async Task<bool> UpdateStoreCrls(
            string storePath,
            X509CRLCollection updatedCrls)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCrls = await store.EnumerateCRLs().ConfigureAwait(false);
                    foreach (var crl in storeCrls)
                    {
                        if (!updatedCrls.Contains(crl))
                        {
                            if (!await store.DeleteCRL(crl).ConfigureAwait(false))
                            {
                                result = false;
                            }
                        }
                        else
                        {
                            updatedCrls.Remove(crl);
                        }
                    }
                    foreach (var crl in updatedCrls)
                    {
                        await store.AddCRL(crl).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private async Task<bool> UpdateStoreCertificates(
            string storePath,
            X509Certificate2Collection updatedCerts)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCerts = await store.Enumerate().ConfigureAwait(false);
                    foreach (var cert in storeCerts)
                    {
                        if (!updatedCerts.Contains(cert))
                        {
                            if (!await store.Delete(cert.Thumbprint).ConfigureAwait(false))
                            {
                                result = false;
                            }
                        }
                        else
                        {
                            updatedCerts.Remove(cert);
                        }
                    }
                    foreach (var cert in updatedCerts)
                    {
                        await store.Add(cert).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private void HasSecureReadAccess(ISystemContext context)
        {
            if (m_readAccess != null)
            {
                m_readAccess.Invoke(context);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        private void HasSecureWriteAccess(ISystemContext context)
        {
            if (m_writeAccess != null)
            {
                m_writeAccess.Invoke(context);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private SecureAccess m_readAccess;
        private SecureAccess m_writeAccess;
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private readonly string m_trustedStorePath;
        private readonly string m_issuerStorePath;
        private TrustListState m_node;
        private Stream m_strm;
        private bool m_readMode;
        #endregion

    }

}
