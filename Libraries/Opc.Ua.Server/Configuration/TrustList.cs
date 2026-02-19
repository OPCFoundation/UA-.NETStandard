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
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The implementation of a server trustlist.
    /// </summary>
    public class TrustList
    {
        private const int kDefaultTrustListCapacity = 1 * 1024 * 1024;

        /// <summary>
        /// 1MB default max trust list size
        /// </summary>
        private const int kDefaultMaxTrustListSize = 1 * 1024 * 1024;

        /// <summary>
        /// Initialize the trustlist with default values.
        /// </summary>
        public TrustList(
            TrustListState node,
            CertificateStoreIdentifier trustedListStore,
            CertificateStoreIdentifier issuerListStore,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            int maxTrustListSize = 0)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<TrustList>();
            m_node = node;
            m_trustedStore = trustedListStore;
            m_issuerStore = issuerListStore;
            m_readAccess = readAccess;
            m_writeAccess = writeAccess;
            // If maxTrustListSize is 0 (unlimited), use a sensible default limit
            m_maxTrustListSize = maxTrustListSize > 0 ? maxTrustListSize : kDefaultMaxTrustListSize;

            node.Open.OnCall = new OpenMethodStateMethodCallHandler(Open);
            node.Open.OnCallAsync = new OpenMethodStateMethodAsyncCallHandler(OpenAsync);
            node.OpenWithMasks.OnCall
                = new OpenWithMasksMethodStateMethodCallHandler(OpenWithMasks);
            node.OpenWithMasks.OnCallAsync
                = new OpenWithMasksMethodStateMethodAsyncCallHandler(OpenWithMasksAsync);
            node.Read.OnCall = new ReadMethodStateMethodCallHandler(Read);
            node.Read.OnCallAsync = new ReadMethodStateMethodAsyncCallHandler(ReadAsync);
            node.Write.OnCall = new WriteMethodStateMethodCallHandler(Write);
            node.Write.OnCallAsync = new WriteMethodStateMethodAsyncCallHandler(WriteAsync);
            node.Close.OnCall = new CloseMethodStateMethodCallHandler(Close);
            node.Close.OnCallAsync = new CloseMethodStateMethodAsyncCallHandler(CloseAsync);
            node.CloseAndUpdate.OnCall
                = new CloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
            node.CloseAndUpdate.OnCallAsync
                = new CloseAndUpdateMethodStateMethodAsyncCallHandler(CloseAndUpdateAsync);
            node.AddCertificate.OnCall
                = new AddCertificateMethodStateMethodCallHandler(AddCertificate);
            node.AddCertificate.OnCallAsync
                = new AddCertificateMethodStateMethodAsyncCallHandler(AddCertificateAsync);
            node.RemoveCertificate.OnCall
                = new RemoveCertificateMethodStateMethodCallHandler(RemoveCertificate);
            node.RemoveCertificate.OnCallAsync
                = new RemoveCertificateMethodStateMethodAsyncCallHandler(RemoveCertificateAsync);
        }

        /// <summary>
        /// Delegate to validate the access to the trust list.
        /// </summary>
        /// <param name="context">System context</param>
        /// <param name="trustedStore">the path to identify the trustList</param>
        public delegate void SecureAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore);

        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            OpenMethodStateResult result = OpenAsync(
                context,
                method,
                objectId,
                mode,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            fileHandle = result.FileHandle;
            return result.ServiceResult;
        }

        private ValueTask<OpenMethodStateResult> OpenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            CancellationToken cancellationToken)
        {
            return OpenCoreAsync(
                context,
                method,
                objectId,
                (OpenFileMode)mode,
                TrustListMasks.All,
                cancellationToken);
        }

        private ServiceResult OpenWithMasks(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            ref uint fileHandle)
        {
            OpenWithMasksMethodStateResult result = OpenWithMasksAsync(
                context,
                method,
                objectId,
                masks,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            fileHandle = result.FileHandle;
            return result.ServiceResult;
        }

        private async ValueTask<OpenWithMasksMethodStateResult> OpenWithMasksAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            CancellationToken cancellationToken)
        {
            OpenMethodStateResult result = await OpenCoreAsync(
                context,
                method,
                objectId,
                OpenFileMode.Read,
                (TrustListMasks)masks,
                cancellationToken).ConfigureAwait(false);

            return new OpenWithMasksMethodStateResult
            {
                ServiceResult = result.ServiceResult,
                FileHandle = result.FileHandle
            };
        }

        private async ValueTask<OpenMethodStateResult> OpenCoreAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            OpenFileMode mode,
            TrustListMasks masks,
            CancellationToken cancellationToken)
        {
            if (mode == OpenFileMode.Read)
            {
                HasSecureReadAccess(context);
            }
            else if ((int)mode == ((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting))
            {
                HasSecureWriteAccess(context);
            }
            else
            {
                return new OpenMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNotWritable,
                    FileHandle = 0
                };
            }

            uint fileHandle = 0;
            MemoryStream strm = null;

            try
            {
                var trustList = new TrustListDataType { SpecifiedLists = (uint)masks };

                ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open trusted certificate store.");
                    }

                    if (((int)masks & (int)TrustListMasks.TrustedCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = await store.EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (X509Certificate2 certificate in certificates)
                        {
                            trustList.TrustedCertificates.Add(certificate.RawData);
                        }
                    }

                    if (((int)masks & (int)TrustListMasks.TrustedCrls) != 0)
                    {
                        X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (X509CRL crl in crls)
                        {
                            trustList.TrustedCrls.Add(crl.RawData);
                        }
                    }
                }
                finally
                {
                    store.Close();
                }

                store = m_issuerStore.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open issuer certificate store.");
                    }

                    if (((int)masks & (int)TrustListMasks.IssuerCertificates) != 0)
                    {
                        X509Certificate2Collection certificates = await store.EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (X509Certificate2 certificate in certificates)
                        {
                            trustList.IssuerCertificates.Add(certificate.RawData);
                        }
                    }

                    if (((int)masks & (int)TrustListMasks.IssuerCrls) != 0)
                    {
                        X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (X509CRL crl in crls)
                        {
                            trustList.IssuerCrls.Add(crl.RawData);
                        }
                    }
                }
                finally
                {
                    store.Close();
                }

                if (mode == OpenFileMode.Read)
                {
                    strm = EncodeTrustListData(context, trustList);
                }
                else
                {
                    strm = new MemoryStream(kDefaultTrustListCapacity);
                }

                lock (m_lock)
                {
                    if (!m_sessionId.IsNull)
                    {
                        // to avoid deadlocks, last open always wins
                        m_sessionId = default;
                        m_strm = null;
                        m_node.OpenCount.Value = 0;
                    }

                    m_sessionId = (context as ISessionSystemContext)?.SessionId ?? default;
                    fileHandle = ++m_fileHandle;
                    m_totalBytesProcessed = 0; // Reset counter for new file operation
                    m_strm = strm;
                    m_node.OpenCount.Value = 1;
                }
            }
            catch
            {
                strm?.Dispose();
                throw;
            }

            return new OpenMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                FileHandle = fileHandle
            };
        }

        private ServiceResult Read(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref byte[] data)
        {
            ReadMethodStateResult result = ReadAsync(
                context,
                method,
                objectId,
                fileHandle,
                length,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            data = result.Data;
            return result.ServiceResult;
        }

        private ValueTask<ReadMethodStateResult> ReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            CancellationToken cancellationToken)
        {
            HasSecureReadAccess(context);

            byte[] data;

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != session.SessionId)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadUserAccessDenied,
                            "Session not authorized"),
                        Data = null
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "Invalid file handle"),
                        Data = null
                    });
                }

                // Check if we would exceed the maximum trust list size
                if (m_totalBytesProcessed + length > m_maxTrustListSize)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Trust list size exceeds maximum allowed size of {0} bytes",
                            m_maxTrustListSize),
                        Data = null
                    });
                }

                data = new byte[length];

                int bytesRead = m_strm.Read(data, 0, length);
                Debug.Assert(bytesRead >= 0);

                m_totalBytesProcessed += bytesRead;

                if (bytesRead < length)
                {
                    byte[] bytes = new byte[bytesRead];
                    Array.Copy(data, bytes, bytesRead);
                    data = bytes;
                }
            }

            return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Data = data
            });
        }

        private ServiceResult Write(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            byte[] data)
        {
            WriteMethodStateResult result = WriteAsync(
                context,
                method,
                objectId,
                fileHandle,
                data,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private ValueTask<WriteMethodStateResult> WriteAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            byte[] data,
            CancellationToken cancellationToken)
        {
            HasSecureWriteAccess(context);

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != session.SessionId)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                // Check if we would exceed the maximum trust list size
                if (m_totalBytesProcessed + data.Length > m_maxTrustListSize)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Trust list size exceeds maximum allowed size of {0} bytes",
                            m_maxTrustListSize)
                    });
                }

                m_strm.Write(data, 0, data.Length);
                m_totalBytesProcessed += data.Length;
            }

            return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ServiceResult Close(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            CloseMethodStateResult result = CloseAsync(
                context,
                method,
                objectId,
                fileHandle,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private ValueTask<CloseMethodStateResult> CloseAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            HasSecureReadAccess(context);

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != session.SessionId)
                {
                    return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                m_sessionId = default;
                m_strm = null;
                m_node.OpenCount.Value = 0;
            }

            return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ServiceResult CloseAndUpdate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref bool restartRequired)
        {
            CloseAndUpdateMethodStateResult result = CloseAndUpdateAsync(
                context,
                method,
                objectId,
                fileHandle,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            restartRequired = result.ApplyChangesRequired;
            return result.ServiceResult;
        }

        private async ValueTask<CloseAndUpdateMethodStateResult> CloseAndUpdateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            VariantCollection inputParameters = [fileHandle];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/CloseAndUpdate",
                method.NodeId,
                inputParameters,
                m_logger);
            HasSecureWriteAccess(context);

            ServiceResult result = StatusCodes.Good;

            MemoryStream strm = null;

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != session.SessionId)
                {
                    return new CloseAndUpdateMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied,
                        ApplyChangesRequired = false
                    };
                }

                if (m_fileHandle != fileHandle)
                {
                    return new CloseAndUpdateMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument,
                        ApplyChangesRequired = false
                    };
                }

                strm = m_strm;
            }

            try
            {
                TrustListDataType trustList = DecodeTrustListData(context, strm);
                int masks = (int)trustList.SpecifiedLists;

                X509Certificate2Collection issuerCertificates = null;
                X509CRLCollection issuerCrls = null;
                X509Certificate2Collection trustedCertificates = null;
                X509CRLCollection trustedCrls = null;

                // test integrity of all CRLs
                if ((masks & (int)TrustListMasks.IssuerCertificates) != 0)
                {
                    issuerCertificates = [];
                    foreach (byte[] cert in trustList.IssuerCertificates)
                    {
                        issuerCertificates.Add(X509CertificateLoader.LoadCertificate(cert));
                    }
                }
                if ((masks & (int)TrustListMasks.IssuerCrls) != 0)
                {
                    issuerCrls = [];
                    foreach (byte[] crl in trustList.IssuerCrls)
                    {
                        issuerCrls.Add(new X509CRL(crl));
                    }
                }
                if ((masks & (int)TrustListMasks.TrustedCertificates) != 0)
                {
                    trustedCertificates = [];
                    foreach (byte[] cert in trustList.TrustedCertificates)
                    {
                        trustedCertificates.Add(CertificateFactory.Create(cert));
                    }
                }
                if ((masks & (int)TrustListMasks.TrustedCrls) != 0)
                {
                    trustedCrls = [];
                    foreach (byte[] crl in trustList.TrustedCrls)
                    {
                        trustedCrls.Add(new X509CRL(crl));
                    }
                }

                // update store
                int updateMasks = (int)TrustListMasks.None;
                if ((masks & (int)TrustListMasks.IssuerCertificates) != 0 &&
                    await UpdateStoreCertificatesAsync(m_issuerStore, issuerCertificates, cancellationToken)
                        .ConfigureAwait(false))
                {
                    updateMasks |= (int)TrustListMasks.IssuerCertificates;
                }
                if ((masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                    await UpdateStoreCrlsAsync(m_issuerStore, issuerCrls, cancellationToken).ConfigureAwait(false))
                {
                    updateMasks |= (int)TrustListMasks.IssuerCrls;
                }
                if ((masks & (int)TrustListMasks.TrustedCertificates) != 0 &&
                    await UpdateStoreCertificatesAsync(m_trustedStore, trustedCertificates, cancellationToken)
                        .ConfigureAwait(false))
                {
                    updateMasks |= (int)TrustListMasks.TrustedCertificates;
                }
                if ((masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                    await UpdateStoreCrlsAsync(m_trustedStore, trustedCrls, cancellationToken).ConfigureAwait(false))
                {
                    updateMasks |= (int)TrustListMasks.TrustedCrls;
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
                lock (m_lock)
                {
                    m_sessionId = default;
                    m_strm = null;
                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                    m_node.OpenCount.Value = 0;
                }
            }

            // report the TrustListUpdatedAuditEvent
            m_node.ReportTrustListUpdatedAuditEvent(
                context,
                objectId,
                "Method/CloseAndUpdate",
                method.NodeId,
                inputParameters,
                result.StatusCode,
                m_logger);

            return new CloseAndUpdateMethodStateResult
            {
                ServiceResult = result,
                ApplyChangesRequired = false
            };
        }

        private ServiceResult AddCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] certificate,
            bool isTrustedCertificate)
        {
            AddCertificateMethodStateResult result = AddCertificateAsync(
                context,
                method,
                objectId,
                certificate,
                isTrustedCertificate,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private async ValueTask<AddCertificateMethodStateResult> AddCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] certificate,
            bool isTrustedCertificate,
            CancellationToken cancellationToken)
        {
            VariantCollection inputParameters = [certificate, isTrustedCertificate];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/AddCertificate",
                method.NodeId,
                inputParameters,
                m_logger);
            HasSecureWriteAccess(context);

            ServiceResult result = StatusCodes.Good;

            bool isSessionOpen;
            lock (m_lock)
            {
                isSessionOpen = !m_sessionId.IsNull;
            }

            if (isSessionOpen)
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
                    cert = CertificateFactory.Create(certificate);
                }
                catch
                {
                    // note: a previous version of the sample code accepted also CRL,
                    // but the behaviour was not as specified and removed
                    // https://mantis.opcfoundation.org/view.php?id=6342
                    result = StatusCodes.BadCertificateInvalid;
                }

                if (cert != null)
                {
                    CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                        ? m_trustedStore
                        : m_issuerStore;
                    ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                    try
                    {
                        if (store != null)
                        {
                            await store.AddAsync(cert, null, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        store?.Close();
                    }

                    lock (m_lock)
                    {
                        m_node.LastUpdateTime.Value = DateTime.UtcNow;
                    }
                }
            }

            // report the TrustListUpdatedAuditEvent
            m_node.ReportTrustListUpdatedAuditEvent(
                context,
                objectId,
                "Method/AddCertificate",
                method.NodeId,
                inputParameters,
                result.StatusCode,
                m_logger);

            return new AddCertificateMethodStateResult
            {
                ServiceResult = result
            };
        }

        private ServiceResult RemoveCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate)
        {
            RemoveCertificateMethodStateResult result = RemoveCertificateAsync(
                context,
                method,
                objectId,
                thumbprint,
                isTrustedCertificate,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private async ValueTask<RemoveCertificateMethodStateResult> RemoveCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate,
            CancellationToken cancellationToken)
        {
            VariantCollection inputParameters = [thumbprint, isTrustedCertificate];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/RemoveCertificate",
                method.NodeId,
                inputParameters,
                m_logger);

            HasSecureWriteAccess(context);
            ServiceResult result = StatusCodes.Good;

            bool isSessionOpen;
            lock (m_lock)
            {
                isSessionOpen = !m_sessionId.IsNull;
            }

            if (isSessionOpen)
            {
                result = StatusCodes.BadInvalidState;
            }
            else if (string.IsNullOrEmpty(thumbprint))
            {
                result = StatusCodes.BadInvalidArgument;
            }
            else
            {
                CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                    ? m_trustedStore
                    : m_issuerStore;
                using (ICertificateStore store = storeIdentifier.OpenStore(m_telemetry))
                {
                    if (store == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "Failed to open certificate store.");
                    }

                    X509Certificate2Collection certCollection = await store
                        .FindByThumbprintAsync(thumbprint, cancellationToken)
                        .ConfigureAwait(false);

                    if (certCollection.Count == 0)
                    {
                        result = StatusCodes.BadInvalidArgument;
                    }
                    else
                    {
                        // delete all CRLs signed by cert
                        var crlsToDelete = new X509CRLCollection();
                        X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (X509CRL crl in crls)
                        {
                            foreach (X509Certificate2 cert in certCollection)
                            {
                                if (X509Utils.CompareDistinguishedName(
                                        cert.SubjectName,
                                        crl.IssuerName) &&
                                    crl.VerifySignature(cert, false))
                                {
                                    crlsToDelete.Add(crl);
                                    break;
                                }
                            }
                        }

                        if (!await store.DeleteAsync(thumbprint, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            result = StatusCodes.BadInvalidArgument;
                        }
                        else
                        {
                            foreach (X509CRL crl in crlsToDelete)
                            {
                                if (!await store.DeleteCRLAsync(crl, cancellationToken)
                                    .ConfigureAwait(false))
                                {
                                    // intentionally ignore errors, try best effort
                                    m_logger.LogError(
                                        "RemoveCertificate: Failed to delete CRL {Crl}.",
                                        crl.ToString());
                                }
                            }
                        }
                    }
                }

                lock (m_lock)
                {
                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                }
            }

            // report the TrustListUpdatedAuditEvent
            m_node.ReportTrustListUpdatedAuditEvent(
                context,
                objectId,
                "Method/RemoveCertificate",
                method.NodeId,
                inputParameters,
                result.StatusCode,
                m_logger);

            return new RemoveCertificateMethodStateResult
            {
                ServiceResult = result
            };
        }

        private static MemoryStream EncodeTrustListData(
            ISystemContext context,
            TrustListDataType trustList)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            var strm = new MemoryStream();
            using (var encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, trustList, null);
            }
            strm.Position = 0;
            return strm;
        }

        private static TrustListDataType DecodeTrustListData(
            ISystemContext context,
            MemoryStream strm)
        {
            var trustList = new TrustListDataType();
            IServiceMessageContext messageContext = new ServiceMessageContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            strm.Position = 0;
            using (var decoder = new BinaryDecoder(strm, messageContext))
            {
                trustList.Decode(decoder);
            }
            return trustList;
        }

        private async Task<bool> UpdateStoreCrlsAsync(
            CertificateStoreIdentifier storeIdentifier,
            X509CRLCollection updatedCrls,
            CancellationToken cancellationToken = default)
        {
            bool result = true;
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open certificate store.");
                    }

                    X509CRLCollection storeCrls = await store.EnumerateCRLsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (X509CRL crl in storeCrls)
                    {
                        if (!updatedCrls.Remove(crl) &&
                            !await store.DeleteCRLAsync(crl, cancellationToken).ConfigureAwait(false))
                        {
                            result = false;
                        }
                    }
                    foreach (X509CRL crl in updatedCrls)
                    {
                        await store.AddCRLAsync(crl, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    store.Close();
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private async Task<bool> UpdateStoreCertificatesAsync(
            CertificateStoreIdentifier storeIdentifier,
            X509Certificate2Collection updatedCerts,
            CancellationToken cancellationToken = default)
        {
            bool result = true;
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open certificate store.");
                    }

                    X509Certificate2Collection storeCerts = await store.EnumerateAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (X509Certificate2 cert in storeCerts)
                    {
                        if (!updatedCerts.Contains(cert))
                        {
                            if (!await store.DeleteAsync(cert.Thumbprint, cancellationToken).ConfigureAwait(false))
                            {
                                result = false;
                            }
                        }
                        else
                        {
                            updatedCerts.Remove(cert);
                        }
                    }
                    foreach (X509Certificate2 cert in updatedCerts)
                    {
                        await store.AddAsync(cert, null, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    store.Close();
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
                m_readAccess.Invoke(context, m_trustedStore);
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
                m_writeAccess.Invoke(context, m_trustedStore);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        private readonly Lock m_lock = new();
        private readonly SecureAccess m_readAccess;
        private readonly SecureAccess m_writeAccess;
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private readonly CertificateStoreIdentifier m_trustedStore;
        private readonly CertificateStoreIdentifier m_issuerStore;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TrustListState m_node;
        private MemoryStream m_strm;
        private readonly int m_maxTrustListSize;
        private long m_totalBytesProcessed;
    }
}
