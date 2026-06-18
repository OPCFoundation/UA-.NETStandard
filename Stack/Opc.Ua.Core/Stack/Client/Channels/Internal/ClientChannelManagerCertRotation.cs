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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    internal sealed class ClientChannelManagerCertRotation
    {
        public ClientChannelManagerCertRotation(IChannelCertRotationHost host)
        {
            m_host = host;
        }

        public void UpdateClientCertificate(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain)
        {
            m_host.ReplaceClientCertificate(clientCertificate, clientCertificateChain);
        }

        public async ValueTask ReconnectAllAsync(CancellationToken ct = default)
        {
            ChannelEntry[] snapshot = m_host.SnapshotEntries();
            if (snapshot.Length == 0)
            {
                return;
            }

            await Task.WhenAll(
                snapshot.Select(e => e.RequestReconnectAsync(ct)))
                .ConfigureAwait(false);
        }

        public void WireCertificateRotation()
        {
            ICertificateManager? certificateManager = m_host.Configuration?.CertificateManager;
            if (certificateManager == null)
            {
                return;
            }

            m_certificateChangeSubscription = certificateManager.CertificateChanges.Subscribe(
                new CertificateChangeObserver(this));
        }

        public void DisposeCertificateRotation()
        {
            IDisposable? subscription = Interlocked.Exchange(
                ref m_certificateChangeSubscription,
                null);
            subscription?.Dispose();

            lock (m_certificateRotationLock)
            {
                m_pendingCertificateChange = null;
            }
        }

        private void OnCertificateChanged(CertificateChangeEvent evt)
        {
            if (!IsApplicationCertificateUpdate(evt) ||
                m_host.IsDisposed)
            {
                return;
            }

            lock (m_certificateRotationLock)
            {
                m_pendingCertificateChange = evt;
                if (m_certificateRotationTask != null)
                {
                    return;
                }

                var task = Task.Run(ProcessCertificateChangesAsync, CancellationToken.None);
                m_certificateRotationTask = task;
                m_host.SetCertificateRotationTask(task);
            }
        }

        private async Task ProcessCertificateChangesAsync()
        {
            while (true)
            {
                CertificateChangeEvent? evt;
                lock (m_certificateRotationLock)
                {
                    evt = m_pendingCertificateChange;
                    m_pendingCertificateChange = null;
                    if (evt == null)
                    {
                        m_certificateRotationTask = null;
                        m_host.SetCertificateRotationTask(null);
                        return;
                    }
                }

                if (m_host.IsDisposed)
                {
                    continue;
                }

                try
                {
                    (Certificate? certificate, CertificateCollection? chain) =
                        await LoadChangedApplicationCertificateAsync(evt, CancellationToken.None)
                            .ConfigureAwait(false);

                    if (certificate == null)
                    {
                        continue;
                    }

                    if (m_host.IsDisposed)
                    {
                        certificate.Dispose();
                        chain?.Dispose();
                        continue;
                    }

                    UpdateClientCertificate(certificate, chain);
                    await ReconnectAllAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_host.Logger?.LogWarning(
                        ex,
                        "ClientChannelManager: application certificate rotation reconnect failed.");
                }
            }
        }

        private async Task<(Certificate? Certificate, CertificateCollection? Chain)>
            LoadChangedApplicationCertificateAsync(
                CertificateChangeEvent evt,
                CancellationToken ct)
        {
            Certificate? certificate = evt.NewCertificate?.AddRef();
            if (certificate is { HasPrivateKey: false })
            {
                certificate.Dispose();
                certificate = null;
            }

            if (certificate == null)
            {
                ITelemetryContext telemetry = m_host.Configuration.CreateMessageContext().Telemetry;
                certificate = await TryLoadConfiguredApplicationCertificateAsync(evt, telemetry, ct)
                    .ConfigureAwait(false);
            }

            if (certificate == null)
            {
                return (null, null);
            }

            CertificateCollection? chain = await LoadCertificateChainAsync(certificate, ct).ConfigureAwait(false);
            if (chain == null && evt.IssuerChain != null)
            {
                chain = evt.IssuerChain.AddRef();
            }

            return (certificate, chain);
        }

        private async Task<Certificate?> TryLoadConfiguredApplicationCertificateAsync(
            CertificateChangeEvent evt,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            foreach (string securityPolicy in GetCandidateSecurityPolicies(evt.CertificateType))
            {
                try
                {
                    Certificate? certificate = await m_host.Configuration.SecurityConfiguration
                        .FindApplicationCertificateAsync(securityPolicy, privateKey: true, telemetry, ct)
                        .ConfigureAwait(false);
                    if (certificate != null)
                    {
                        return certificate;
                    }
                }
                catch (Exception ex)
                {
                    m_host.Logger?.LogDebug(
                        ex,
                        "ClientChannelManager: application certificate reload for {SecurityPolicy} failed.",
                        securityPolicy);
                }
            }

            return null;
        }

        private async Task<CertificateCollection?> LoadCertificateChainAsync(
            Certificate clientCertificate,
            CancellationToken ct)
        {
            if (!m_host.Configuration.SecurityConfiguration.SendCertificateChain)
            {
                return null;
            }

            CertificateCollection clientCertificateChain = [clientCertificate];
            var issuers = new List<CertificateIssuerReference>();
            try
            {
                ICertificateManager? certificateManager = m_host.Configuration.CertificateManager;
                if (certificateManager != null)
                {
                    await certificateManager.GetIssuersAsync(clientCertificate, issuers, ct)
                        .ConfigureAwait(false);
                }

                for (int i = 0; i < issuers.Count; i++)
                {
                    clientCertificateChain.Add(issuers[i].Certificate);
                }
            }
            catch
            {
                clientCertificateChain.Dispose();
                throw;
            }
            finally
            {
                for (int i = 0; i < issuers.Count; i++)
                {
                    issuers[i].Certificate?.Dispose();
                }
            }

            return clientCertificateChain;
        }

        private IEnumerable<string> GetCandidateSecurityPolicies(NodeId? certificateType)
        {
            ArrayOf<string> supportedSecurityPolicies =
                m_host.Configuration.SecurityConfiguration.SupportedSecurityPolicies;
            for (int i = 0; i < supportedSecurityPolicies.Count; i++)
            {
                string securityPolicy = supportedSecurityPolicies[i];
                if (securityPolicy == SecurityPolicies.None)
                {
                    continue;
                }

                IList<NodeId> certificateTypes = CertificateIdentifier.MapSecurityPolicyToCertificateTypes(
                    securityPolicy);
                for (int j = 0; j < certificateTypes.Count; j++)
                {
                    if (CertificateTypesMatch(certificateTypes[j], certificateType))
                    {
                        yield return securityPolicy;
                        break;
                    }
                }
            }

            yield return SecurityPolicies.Basic256Sha256;
        }

        private bool IsApplicationCertificateUpdate(CertificateChangeEvent evt)
        {
            if (evt.Kind != CertificateChangeKind.ApplicationCertificateUpdated)
            {
                return false;
            }

            CertificateIdentifier? applicationCertificate =
                m_host.Configuration.SecurityConfiguration.ApplicationCertificate;
            if (applicationCertificate == null ||
                !CertificateTypesMatch(applicationCertificate.CertificateType, evt.CertificateType))
            {
                return false;
            }

            return CertificateIdentifierMatches(applicationCertificate, evt, m_host.Logger);
        }

        private static bool CertificateIdentifierMatches(
            CertificateIdentifier applicationCertificate,
            CertificateChangeEvent evt,
            ILogger? logger)
        {
            if (!string.IsNullOrEmpty(applicationCertificate.Thumbprint))
            {
                return CertificateThumbprintMatches(applicationCertificate.Thumbprint, evt.OldCertificate) ||
                    CertificateThumbprintMatches(applicationCertificate.Thumbprint, evt.NewCertificate);
            }

            if (applicationCertificate.RawData != null && applicationCertificate.RawData.Length > 0)
            {
                return CertificateRawDataMatches(applicationCertificate.RawData, evt.OldCertificate) ||
                    CertificateRawDataMatches(applicationCertificate.RawData, evt.NewCertificate);
            }

            if (!string.IsNullOrEmpty(applicationCertificate.SubjectName))
            {
                return CertificateSubjectMatches(applicationCertificate.SubjectName, evt.OldCertificate) ||
                    CertificateSubjectMatches(applicationCertificate.SubjectName, evt.NewCertificate);
            }

            // No identity field configured at all — refuse to claim the
            // rotation event. Returning true here would let any unrelated
            // application's certificate update be adopted as our own (in
            // shared-CertificateManager scenarios). The application MUST
            // declare its identity via at least one of Thumbprint /
            // RawData / SubjectName for cert rotation to apply.
            logger?.LogWarning(
                "ClientChannelManager: ApplicationCertificate has no Thumbprint, RawData, " +
                "or SubjectName configured. Ignoring CertificateChangeEvent because the " +
                "rotated certificate cannot be matched to this application's identity. " +
                "Configure ApplicationCertificate.SubjectName (at minimum) to enable " +
                "secure cert-rotation adoption.");
            return false;
        }

        private static bool CertificateSubjectMatches(string subjectName, Certificate? certificate)
        {
            return certificate != null &&
                X509Utils.CompareDistinguishedName(subjectName, certificate.Subject);
        }

        private static bool CertificateThumbprintMatches(string thumbprint, Certificate? certificate)
        {
            return certificate != null &&
                string.Equals(thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CertificateRawDataMatches(byte[] rawData, Certificate? certificate)
        {
            return certificate != null && rawData.AsSpan().SequenceEqual(certificate.RawData);
        }

        private static bool CertificateTypesMatch(NodeId configuredType, NodeId? changedType)
        {
            NodeId effectiveChangedType = changedType.GetValueOrDefault();
            bool changedTypeIsNull = !changedType.HasValue || effectiveChangedType.IsNull;

            if (configuredType.IsNull)
            {
                return changedTypeIsNull ||
                    effectiveChangedType == ObjectTypeIds.RsaSha256ApplicationCertificateType;
            }

            if (changedTypeIsNull)
            {
                return configuredType == ObjectTypeIds.RsaSha256ApplicationCertificateType;
            }

            if (configuredType == ObjectTypeIds.ApplicationCertificateType)
            {
                return effectiveChangedType != ObjectTypeIds.HttpsCertificateType;
            }

            return configuredType == effectiveChangedType;
        }

        private sealed class CertificateChangeObserver : IObserver<CertificateChangeEvent>
        {
            public CertificateChangeObserver(ClientChannelManagerCertRotation owner)
            {
                m_owner = owner;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(CertificateChangeEvent value)
            {
                m_owner.OnCertificateChanged(value);
            }

            private readonly ClientChannelManagerCertRotation m_owner;
        }

        private readonly Lock m_certificateRotationLock = new();
        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Disposed by DisposeCertificateRotation; " +
                "TODO: inline if CA2213 learns Interlocked.Exchange disposal tracking.")]
        private IDisposable? m_certificateChangeSubscription;
        private CertificateChangeEvent? m_pendingCertificateChange;
        private Task? m_certificateRotationTask;

        private readonly IChannelCertRotationHost m_host;
    }
}
