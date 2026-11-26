// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Raft.Transport;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Resolves the credentials and routes during asynchronous Raft startup without blocking DI resolution.
    /// The Raft node owns this transport and starts it before using it.
    /// </summary>
    internal sealed class KubernetesRaftTransport : IRaftTransport
    {
        /// <summary>
        /// Initializes the deferred transport without loading certificates or opening a socket.
        /// </summary>
        public KubernetesRaftTransport(
            IServiceProvider services,
            KubernetesRaftConsensusOptions options,
            IPEndPoint listenEndPoint,
            string statefulSet,
            ulong memberId)
        {
            m_services = services;
            m_options = options;
            m_listenEndPoint = listenEndPoint;
            m_statefulSet = statefulSet;
            m_memberId = memberId;
        }

        /// <inheritdoc/>
        public event Action<ReadOnlyMemory<byte>>? FrameReceived;

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_options.ReadyTimeout);
            try
            {
                await StartTransportAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out resolving Raft startup credentials or peer endpoints.");
            }
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(
            ulong recipient,
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            TlsRaftTransport transport = m_transport ??
                throw new InvalidOperationException("The Raft transport is not running.");
            return transport.SendAsync(recipient, frame, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_transport == null)
            {
                return;
            }

            m_transport.FrameReceived -= ForwardFrame;
            try
            {
                await m_transport.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_transport = null;
                m_localCertificate?.Dispose();
                m_localCertificate = null;
            }
        }

        private async ValueTask StartTransportAsync(CancellationToken ct)
        {
            var resolveCertificate = m_options.ResolveMemberCertificateAsync ??
                throw new InvalidOperationException("The Raft certificate resolver is required.");
            var certificates = new List<X509Certificate2>(m_options.ReplicaCount);
            var peers = new List<TlsRaftPeer>(m_options.ReplicaCount - 1);
            var endPoints = new HashSet<IPEndPoint> { m_listenEndPoint };
            TlsRaftTransport? transport = null;
            try
            {
                X509Certificate2 localCertificate = await ResolveCertificateAsync(m_memberId).ConfigureAwait(false);
                if (!localCertificate.HasPrivateKey)
                {
                    throw new InvalidOperationException("The local Raft TLS certificate must have its private key.");
                }

                for (ulong memberId = 1; memberId <= (ulong)m_options.ReplicaCount; memberId++)
                {
                    if (memberId == m_memberId)
                    {
                        continue;
                    }
                    X509Certificate2 peerCertificate = await ResolveCertificateAsync(memberId).ConfigureAwait(false);
                    IPEndPoint endPoint = await ResolvePeerAsync(memberId, ct).ConfigureAwait(false);
                    if (!endPoints.Add(endPoint))
                    {
                        throw new InvalidOperationException($"Raft member {memberId} has a duplicate endpoint.");
                    }
                    peers.Add(new TlsRaftPeer(memberId, endPoint, peerCertificate));
                }

                ct.ThrowIfCancellationRequested();
                transport = new TlsRaftTransport(
                    m_memberId, m_listenEndPoint, localCertificate, peers, m_options.TransportOptions);
                transport.FrameReceived += ForwardFrame;
                await transport.StartAsync(ct).ConfigureAwait(false);
                m_localCertificate = localCertificate;
                m_transport = transport;
                transport = null;
            }
            finally
            {
                try
                {
                    if (transport != null)
                    {
                        transport.FrameReceived -= ForwardFrame;
                        await transport.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    foreach (X509Certificate2 certificate in certificates)
                    {
                        if (!ReferenceEquals(certificate, m_localCertificate))
                        {
                            certificate.Dispose();
                        }
                    }
                }
            }

            async ValueTask<X509Certificate2> ResolveCertificateAsync(ulong memberId)
            {
                X509Certificate2 certificate = await resolveCertificate(
                    m_services, memberId, memberId == m_memberId, ct)
                    .ConfigureAwait(false) ??
                    throw new InvalidOperationException($"No TLS certificate was resolved for Raft member {memberId}.");
                certificates.Add(certificate);
                ct.ThrowIfCancellationRequested();
                DateTime utcNow = DateTime.UtcNow;
                if (utcNow < certificate.NotBefore.ToUniversalTime() ||
                    utcNow > certificate.NotAfter.ToUniversalTime())
                {
                    throw new InvalidOperationException(
                        $"The TLS certificate for Raft member {memberId} is not valid.");
                }
                return certificate;
            }
        }

        private async ValueTask<IPEndPoint> ResolvePeerAsync(ulong memberId, CancellationToken ct)
        {
            string host = string.Create(
                CultureInfo.InvariantCulture, $"{m_statefulSet}-{memberId - 1}.{m_options.HeadlessServiceName}");
            if (!string.IsNullOrEmpty(m_options.Namespace))
            {
                host += $".{m_options.Namespace}.svc";
            }
            var defaultEndPoint = new DnsEndPoint(host, m_options.RaftPort);

            IPEndPoint? endPoint;
            if (m_options.ResolvePeerEndPointAsync is { } resolvePeer)
            {
                endPoint = await resolvePeer(m_services, memberId, defaultEndPoint, ct).ConfigureAwait(false);
            }
            else
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                IPAddress? address = Array.Find(addresses, ip => ip.AddressFamily == m_listenEndPoint.AddressFamily)
                    ?? (addresses.Length == 0 ? null : addresses[0]);
                endPoint = address != null ? new IPEndPoint(address, m_options.RaftPort) : null;
            }
            if (endPoint == null || endPoint.Port == 0 ||
                endPoint.Address.Equals(IPAddress.Any) || endPoint.Address.Equals(IPAddress.IPv6Any))
            {
                throw new InvalidOperationException($"No usable endpoint was resolved for Raft member {memberId}.");
            }
            return endPoint;
        }

        private void ForwardFrame(ReadOnlyMemory<byte> frame)
        {
            FrameReceived?.Invoke(frame);
        }

        private readonly IServiceProvider m_services;
        private readonly KubernetesRaftConsensusOptions m_options;
        private readonly IPEndPoint m_listenEndPoint;
        private readonly string m_statefulSet;
        private readonly ulong m_memberId;
        private TlsRaftTransport? m_transport;
        private X509Certificate2? m_localCertificate;
    }
}
