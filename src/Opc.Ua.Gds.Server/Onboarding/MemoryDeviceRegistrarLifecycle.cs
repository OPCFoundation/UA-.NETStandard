/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// A snapshot of one authenticated device and its software status.
    /// </summary>
    public sealed record DeviceRegistrarDevice(
        string ProductInstanceUri,
        NodeId DcaApplicationId,
        ByteString DcaCertificate,
        bool SoftwareStatus,
        string SoftwareRevision,
        ArrayOf<DeviceRegistrarApplication> Applications)
    {
        /// <summary>
        /// Gets the issued credential awaiting proof on an authenticated channel; it does not retire the active key.
        /// </summary>
        public ByteString PendingDcaCertificate { get; init; }

        /// <summary>
        /// Gets whether all current and future DCA credentials are prohibited from configuring other applications.
        /// </summary>
        public bool IsSingletonRestricted { get; init; }
    }

    /// <summary>
    /// A relation to an application registered in the shared certificate-manager database.
    /// </summary>
    public sealed record DeviceRegistrarApplication(NodeId ApplicationId, string ProtocolUri);

    /// <summary>
    /// Process-local lifecycle storage for tests and explicitly transient deployments.
    /// State is lost on restart; this provider does not provide durable or distributed storage.
    /// </summary>
    public sealed class MemoryDeviceRegistrarLifecycle :
        IDeviceRegistrarLifecycle,
        IDeviceRegistrarIdentityLifecycle,
        IDeviceRegistrarCertificateLifecycle,
        IDeviceRegistrarApplicationScopeLifecycle
    {
        /// <inheritdoc/>
        public ValueTask RecordCandidateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString selectedIdentity,
            bool requiresSoftwareUpdate,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(productInstanceUri) ||
                !Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _) ||
                applicationId.IsNull || selectedIdentity.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                foreach (KeyValuePair<string, DeviceRegistrarIdentityCandidate> pair in m_candidates)
                {
                    if (pair.Key != productInstanceUri &&
                        (pair.Value.ApplicationId == applicationId || pair.Value.SelectedIdentity == selectedIdentity))
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                    }
                }
                foreach (DeviceRegistrarDevice device in m_devices.Values)
                {
                    if (device.ProductInstanceUri != productInstanceUri &&
                        (OwnsApplication(device, applicationId) || device.DcaCertificate == selectedIdentity ||
                            device.PendingDcaCertificate == selectedIdentity))
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                    }
                }
                if (m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? existing))
                {
                    if (existing.DcaApplicationId != applicationId)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                }
                else
                {
                    m_devices.Add(productInstanceUri, new DeviceRegistrarDevice(
                        productInstanceUri, applicationId, ByteString.Empty, false, string.Empty, []));
                }
                m_candidates[productInstanceUri] = new DeviceRegistrarIdentityCandidate(
                    productInstanceUri, applicationId, ByteString.From(selectedIdentity.Span),
                    !requiresSoftwareUpdate);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarIdentityCandidate?> FindCandidateByCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!channelCertificate.IsEmpty)
            {
                lock (m_lock)
                {
                    foreach (DeviceRegistrarIdentityCandidate candidate in m_candidates.Values)
                    {
                        DeviceRegistrarDevice device = m_devices[candidate.ProductInstanceUri];
                        if (candidate.SelectedIdentity == channelCertificate ||
                            device.DcaCertificate == channelCertificate)
                        {
                            return new ValueTask<DeviceRegistrarIdentityCandidate?>(candidate with
                            {
                                SelectedIdentity = ByteString.From(candidate.SelectedIdentity.Span),
                                CertificateIssuanceAllowed =
                                    (candidate.CertificateIssuanceAllowed &&
                                        string.IsNullOrEmpty(device.SoftwareRevision)) || device.SoftwareStatus
                            });
                        }
                    }
                }
            }
            return new ValueTask<DeviceRegistrarIdentityCandidate?>((DeviceRegistrarIdentityCandidate?)null);
        }

        /// <summary>
        /// Adds an already-approved DCA certificate and its existing certificate-manager identifier.
        /// Callers must complete ticket/identity validation or the configured external certificate handoff first.
        /// Refreshing an identity preserves software status and managed-application relations.
        /// </summary>
        public ValueTask RegisterDeviceAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(productInstanceUri) ||
                !Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _) ||
                applicationId.IsNull ||
                certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                foreach (DeviceRegistrarIdentityCandidate candidate in m_candidates.Values)
                {
                    if (candidate.ProductInstanceUri != productInstanceUri &&
                        candidate.SelectedIdentity == certificate)
                    {
                        throw new ServiceResultException(StatusCodes.BadEntryExists);
                    }
                }
                foreach (DeviceRegistrarDevice candidate in m_devices.Values)
                {
                    if (candidate.ProductInstanceUri != productInstanceUri &&
                        (candidate.DcaCertificate == certificate ||
                            candidate.PendingDcaCertificate == certificate ||
                            OwnsApplication(candidate, applicationId)))
                    {
                        throw new ServiceResultException(StatusCodes.BadEntryExists);
                    }
                }
                if (m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? existing))
                {
                    if (existing.DcaApplicationId != applicationId)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                    m_devices[productInstanceUri] = existing with
                    {
                        DcaCertificate = ByteString.From(certificate.Span),
                        PendingDcaCertificate = ByteString.Empty
                    };
                }
                else
                {
                    m_devices.Add(productInstanceUri, new DeviceRegistrarDevice(
                        productInstanceUri,
                        applicationId,
                        ByteString.From(certificate.Span),
                        false,
                        string.Empty,
                        []));
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RecordIssuedDcaCertificateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            lock (m_lock)
            {
                if (!m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device) ||
                    device.DcaApplicationId != applicationId)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                foreach (DeviceRegistrarDevice other in m_devices.Values)
                {
                    if (other.ProductInstanceUri != productInstanceUri &&
                        (other.DcaCertificate == certificate || other.PendingDcaCertificate == certificate))
                    {
                        throw new ServiceResultException(StatusCodes.BadEntryExists);
                    }
                }
                foreach (DeviceRegistrarIdentityCandidate candidate in m_candidates.Values)
                {
                    if (candidate.ProductInstanceUri != productInstanceUri && candidate.SelectedIdentity == certificate)
                    {
                        throw new ServiceResultException(StatusCodes.BadEntryExists);
                    }
                }
                if (device.DcaCertificate == certificate || device.PendingDcaCertificate == certificate)
                {
                    return default;
                }
                if (!device.PendingDcaCertificate.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadRequestNotAllowed, "A previously issued DCA credential awaits installation.");
                }
                m_devices[productInstanceUri] = device with
                {
                    PendingDcaCertificate = ByteString.From(certificate.Span)
                };
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> ConfirmDcaCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (channelCertificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            lock (m_lock)
            {
                string? product = null;
                foreach (DeviceRegistrarDevice device in m_devices.Values)
                {
                    if (device.PendingDcaCertificate == channelCertificate)
                    {
                        product = device.ProductInstanceUri;
                        break;
                    }
                }
                if (product != null)
                {
                    DeviceRegistrarDevice device = m_devices[product];
                    m_devices[product] = device with
                    {
                        DcaCertificate = ByteString.From(channelCertificate.Span),
                        PendingDcaCertificate = ByteString.Empty
                    };
                    return new ValueTask<bool>(true);
                }
            }
            return new ValueTask<bool>(false);
        }

        /// <summary>
        /// Returns a device snapshot, or null when the device is unknown.
        /// </summary>
        public ValueTask<DeviceRegistrarDevice?> FindDeviceAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device);
                return new ValueTask<DeviceRegistrarDevice?>(device == null ? null : device with
                {
                    DcaCertificate = ByteString.From(device.DcaCertificate.Span),
                    PendingDcaCertificate = ByteString.From(device.PendingDcaCertificate.Span),
                    Applications = device.Applications.ToArray().ToArrayOf()
                });
            }
        }

        /// <inheritdoc/>
        public ValueTask<string?> FindDeviceByCertificateAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!certificate.IsEmpty)
            {
                lock (m_lock)
                {
                    foreach (DeviceRegistrarDevice device in m_devices.Values)
                    {
                        if (device.DcaCertificate == certificate)
                        {
                            return new ValueTask<string?>(device.ProductInstanceUri);
                        }
                    }
                }
            }
            return new ValueTask<string?>((string?)null);
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarApplicationBinding?> FindApplicationAsync(
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            if (applicationId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                foreach (DeviceRegistrarDevice device in m_devices.Values)
                {
                    if (device.DcaApplicationId == applicationId)
                    {
                        return new ValueTask<DeviceRegistrarApplicationBinding?>(
                            new DeviceRegistrarApplicationBinding(
                                device.ProductInstanceUri, applicationId, true, string.Empty));
                    }
                    foreach (DeviceRegistrarApplication application in device.Applications)
                    {
                        if (application.ApplicationId == applicationId)
                        {
                            return new ValueTask<DeviceRegistrarApplicationBinding?>(
                                new DeviceRegistrarApplicationBinding(
                                    device.ProductInstanceUri, applicationId, false, application.ProtocolUri));
                        }
                    }
                }
            }
            return new ValueTask<DeviceRegistrarApplicationBinding?>((DeviceRegistrarApplicationBinding?)null);
        }

        /// <inheritdoc/>
        public ValueTask<bool> UpdateSoftwareStatusAsync(
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            if (softwareRevision == null)
            {
                throw new ArgumentNullException(nameof(softwareRevision));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device))
                {
                    return new ValueTask<bool>(false);
                }
                m_devices[productInstanceUri] = device with
                {
                    SoftwareStatus = status,
                    SoftwareRevision = softwareRevision
                };
            }
            return new ValueTask<bool>(true);
        }

        /// <inheritdoc/>
        public ValueTask RestrictToDcaApplicationAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                m_devices[productInstanceUri] = device with { IsSingletonRestricted = true };
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RequireManagedApplicationAllowedAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                if (device.IsSingletonRestricted)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask AssociateApplicationAsync(
            string productInstanceUri,
            NodeId applicationId,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            if (applicationId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            if (protocolUri == null)
            {
                throw new ArgumentNullException(nameof(protocolUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_devices.TryGetValue(productInstanceUri, out DeviceRegistrarDevice? device))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                if (device.IsSingletonRestricted)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                }
                foreach (DeviceRegistrarDevice candidate in m_devices.Values)
                {
                    if (candidate.ProductInstanceUri != productInstanceUri && OwnsApplication(candidate, applicationId))
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                    }
                }
                var applications = new List<DeviceRegistrarApplication>(device.Applications.Count + 1);
                foreach (DeviceRegistrarApplication application in device.Applications)
                {
                    if (application.ApplicationId != applicationId)
                    {
                        applications.Add(application);
                    }
                }
                applications.Add(new DeviceRegistrarApplication(applicationId, protocolUri));
                m_devices[productInstanceUri] = device with { Applications = applications.ToArray().ToArrayOf() };
            }
            return default;
        }

        /// <summary>
        /// Copies the complete state for a transactional persistence provider without sharing mutable byte storage.
        /// </summary>
        internal DeviceRegistrarLifecycleState CaptureState()
        {
            lock (m_lock)
            {
                var devices = new DeviceRegistrarDevice[m_devices.Count];
                int index = 0;
                foreach (DeviceRegistrarDevice device in m_devices.Values)
                {
                    devices[index++] = device with
                    {
                        DcaCertificate = ByteString.From(device.DcaCertificate.Span),
                        PendingDcaCertificate = ByteString.From(device.PendingDcaCertificate.Span),
                        Applications = device.Applications.ToArray().ToArrayOf()
                    };
                }
                var candidates = new DeviceRegistrarIdentityCandidate[m_candidates.Count];
                index = 0;
                foreach (DeviceRegistrarIdentityCandidate candidate in m_candidates.Values)
                {
                    candidates[index++] = candidate with
                    {
                        SelectedIdentity = ByteString.From(candidate.SelectedIdentity.Span)
                    };
                }
                return new DeviceRegistrarLifecycleState(devices, candidates);
            }
        }

        /// <summary>
        /// Treats DCA identifiers and managed identifiers as one shared GDS ownership namespace.
        /// </summary>
        private static bool OwnsApplication(DeviceRegistrarDevice device, NodeId applicationId)
        {
            if (device.DcaApplicationId == applicationId)
            {
                return true;
            }
            foreach (DeviceRegistrarApplication application in device.Applications)
            {
                if (application.ApplicationId == applicationId)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, DeviceRegistrarDevice> m_devices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DeviceRegistrarIdentityCandidate> m_candidates =
            new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Carries an independently owned lifecycle checkpoint within the persistence implementation.
    /// </summary>
    internal sealed record DeviceRegistrarLifecycleState(
        ArrayOf<DeviceRegistrarDevice> Devices,
        ArrayOf<DeviceRegistrarIdentityCandidate> Candidates);
}
