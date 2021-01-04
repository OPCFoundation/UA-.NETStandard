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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The implementation of a server trustlist.
    /// </summary>
    public class TrustList
    {
        const int DefaultTrustListCapacity = 0x10000;

        #region Constructors
        /// <summary>
        /// Initialize the trustlist with default values.
        /// </summary>
        public TrustList(Opc.Ua.TrustListState node, string trustedListPath, string issuerListPath, SecureAccess readAccess, SecureAccess writeAccess)
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
                        X509Certificate2Collection certificates = store.Enumerate().Result;
                        foreach (var certificate in certificates)
                        {
                            trustList.TrustedCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & TrustListMasks.TrustedCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs())
                        {
                            trustList.TrustedCrls.Add(crl.RawData);
                        }
                    }
                }

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_issuerStorePath))
                {
                    if ((masks & TrustListMasks.IssuerCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = store.Enumerate().Result;
                        foreach (var certificate in certificates)
                        {
                            trustList.IssuerCertificates.Add(certificate.RawData);
                        }
                    }

                    if ((masks & TrustListMasks.IssuerCrls) != 0)
                    {
                        foreach (var crl in store.EnumerateCRLs())
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
                    m_strm = new MemoryStream(DefaultTrustListCapacity);
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
                    List<X509CRL> issuerCrls = null;
                    X509Certificate2Collection trustedCertificates = null;
                    List<X509CRL> trustedCrls = null;

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
                        issuerCrls = new List<X509CRL>();
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
                        trustedCrls = new List<X509CRL>();
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
                        if (UpdateStoreCertificates(m_issuerStorePath, issuerCertificates))
                        {
                            updateMasks |= TrustListMasks.IssuerCertificates;
                        }
                    }
                    if ((masks & TrustListMasks.IssuerCrls) != 0)
                    {
                        if (UpdateStoreCrls(m_issuerStorePath, issuerCrls))
                        {
                            updateMasks |= TrustListMasks.IssuerCrls;
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCertificates) != 0)
                    {
                        if (UpdateStoreCertificates(m_trustedStorePath, trustedCertificates))
                        {
                            updateMasks |= TrustListMasks.TrustedCertificates;
                        }
                    }
                    if ((masks & TrustListMasks.TrustedCrls) != 0)
                    {
                        if (UpdateStoreCrls(m_trustedStorePath, trustedCrls))
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

            lock (m_lock)
            {

                if (m_sessionId != null)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (certificate == null)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                X509Certificate2 cert = null;
                X509CRL crl = null;
                try
                {
                    cert = new X509Certificate2(certificate);
                }
                catch
                {
                    try
                    {
                        crl = new X509CRL(certificate);
                    }
                    catch
                    {
                        return StatusCodes.BadCertificateInvalid;
                    }
                }

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedStorePath : m_issuerStorePath))
                {
                    if (cert != null)
                    {
                        store.Add(cert).Wait();
                    }
                    if (crl != null)
                    {
                        store.AddCRL(crl);
                    }
                }

                m_node.LastUpdateTime.Value = DateTime.UtcNow;
            }

            return ServiceResult.Good;
        }

        private ServiceResult RemoveCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate)
        {
            HasSecureWriteAccess(context);

            lock (m_lock)
            {

                if (m_sessionId != null)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (String.IsNullOrEmpty(thumbprint))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(isTrustedCertificate ? m_trustedStorePath : m_issuerStorePath))
                {
                    var certCollection = store.FindByThumbprint(thumbprint).Result;

                    if (certCollection.Count == 0)
                    {
                        return StatusCodes.BadInvalidArgument;
                    }

                    // delete all CRLs signed by cert
                    var crlsToDelete = new List<X509CRL>();
                    foreach (var crl in store.EnumerateCRLs())
                    {
                        foreach (var cert in certCollection)
                        {
                            if (X509Utils.CompareDistinguishedName(cert.Subject, crl.Issuer) &&
                                crl.VerifySignature(cert, false))
                            {
                                crlsToDelete.Add(crl);
                                break;
                            }
                        }
                    }

                    if (!store.Delete(thumbprint).Result)
                    {
                        return StatusCodes.BadInvalidArgument;
                    }

                    foreach (var crl in crlsToDelete)
                    {
                        if (!store.DeleteCRL(crl))
                        {
                            // intentionally ignore errors, try best effort
                            Utils.Trace("RemoveCertificate: Failed to delete CRL {0}.", crl.ToString());
                        }
                    }
                }

                m_node.LastUpdateTime.Value = DateTime.UtcNow;
            }

            return ServiceResult.Good;
        }

        private Stream EncodeTrustListData(
            ISystemContext context,
            TrustListDataType trustList
            )
        {
            ServiceMessageContext messageContext = new ServiceMessageContext() {
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
            ServiceMessageContext messageContext = new ServiceMessageContext() {
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

        private bool UpdateStoreCrls(
            string storePath,
            IList<X509CRL> updatedCrls)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCrls = store.EnumerateCRLs();
                    foreach (var crl in storeCrls)
                    {
                        if (!updatedCrls.Contains(crl))
                        {
                            if (!store.DeleteCRL(crl))
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
                        store.AddCRL(crl);
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private bool UpdateStoreCertificates(
            string storePath,
            X509Certificate2Collection updatedCerts)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCerts = store.Enumerate().Result;
                    foreach (var cert in storeCerts)
                    {
                        if (!updatedCerts.Contains(cert))
                        {
                            if (!store.Delete(cert.Thumbprint).Result)
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
                        store.Add(cert).Wait();
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
