// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Waits for optional exporter-backed direct binding without affecting baseline readiness.
        /// </summary>
        internal ValueTask<DataChannelResult> WaitForDirectBindingAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<DataChannelResult>(m_directBinding.Task.WaitAsync(cancellationToken));
        }

        /// <summary>
        /// Reports whether both protected binding proofs have been verified.
        /// </summary>
        internal bool IsDirectBindingReady
        {
            get
            {
                lock (m_gate)
                {
                    return m_directBindingReady && !m_closed;
                }
            }
        }

        /// <summary>
        /// Reports whether this authenticated carrier exposes its selected direct-binding exporter.
        /// </summary>
        internal bool IsDirectBindingProviderAvailable =>
            m_carrier is IDataChannelDirectBindingProvider { IsAvailable: true } &&
            m_carrier.Kind is DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc;

        /// <summary>
        /// Starts the initiator proof after HELLO without delaying baseline readiness.
        /// </summary>
        private void StartDirectBindingLocked()
        {
            if (m_carrier is not IDataChannelDirectBindingProvider provider || !provider.IsAvailable ||
                m_carrier.Identity is null ||
                m_carrier.Kind is not (DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc))
            {
                m_directBinding.TrySetResult(DataChannelResult.Unsupported);
                return;
            }
            if (m_carrier.Identity.IsTransportInitiator && !m_directBindingStarted)
            {
                m_directBindingStarted = true;
                m_directBindingTask = Task.Run(EstablishDirectBindingAsync);
            }
        }

        /// <summary>
        /// Creates the initiator proof and waits for the authenticated acceptor response.
        /// </summary>
        private async Task EstablishDirectBindingAsync()
        {
            try
            {
                using var deadline = new CancellationTokenSource(m_options.OpenTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    deadline.Token, m_shutdown.Token);
                DataChannelContextIdentity identity = GetIdentity();
                IDataChannelDirectBindingProvider provider =
                    (IDataChannelDirectBindingProvider)m_carrier;
                FlowBindingTranscript transcript = CreateBindingTranscript(identity, provider);
                ByteString digest = FlowDirectBindingCryptography.ComputeTranscriptDigest(in transcript);
                ByteString exporter = await provider.ExportKeyingMaterialAsync(digest, linked.Token)
                    .ConfigureAwait(false);
                byte[]? exporterKey = TakeExporterKey(exporter);
                if (exporterKey == null)
                {
                    CompleteDirectBinding(DataChannelResult.Unsupported);
                    return;
                }

                try
                {
                    uint operationId;
                    Task<DataChannelResult> sent;
                    lock (m_gate)
                    {
                        if (m_closed)
                        {
                            CompleteDirectBinding(DataChannelResult.Closed);
                            return;
                        }
                        m_directBindingDigest = digest.Copy();
                        m_directBindingExporter = exporterKey;
                        exporterKey = null;
                        sent = QueueControlLocked(
                            new FlowBinding(
                                0,
                                digest,
                                FlowDirectBindingCryptography.CreateProof(
                                    new ByteString(m_directBindingExporter.AsMemory()), 0, digest)),
                            0,
                            0,
                            DataChannelPath.Management);
                        operationId = m_localControlId;
                        m_directBindingOperationId = operationId;
                    }
                    DataChannelResult sendResult = await sent.WaitAsync(linked.Token).ConfigureAwait(false);
                    if (sendResult != DataChannelResult.Good)
                    {
                        CompleteDirectBinding(sendResult);
                        return;
                    }
                    await m_directBinding.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    ClearExporterKey(exporterKey);
                }
            }
            catch (OperationCanceledException)
            {
                CompleteDirectBinding(m_shutdown.IsCancellationRequested
                    ? DataChannelResult.Closed
                    : DataChannelResult.Timeout);
            }
            catch (DataChannelException exception)
            {
                CompleteDirectBinding(exception.Result);
            }
            catch (FlowCodecException)
            {
                CompleteDirectBinding(DataChannelResult.SecurityFailure);
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
                CompleteDirectBinding(DataChannelResult.Unsupported);
            }
        }

        /// <summary>
        /// Handles a peer proof or its explicit unsupported result outside the ingress lock.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDirectBindingAsync(
            FlowControl control,
            FlowRecord record,
            CancellationToken cancellationToken)
        {
            if (control is FlowOutcome { Operation: FlowOperation.Status, Flow: 0 } status)
            {
                lock (m_gate)
                {
                    if (m_carrier.Identity.IsTransportInitiator &&
                        record.MessageId == m_directBindingOperationId &&
                        !m_directBindingReady)
                    {
                        DataChannelResult result = status.Status switch
                        {
                            FlowWireStatus.Unsupported => DataChannelResult.Unsupported,
                            FlowWireStatus.Timeout => DataChannelResult.Timeout,
                            _ => DataChannelResult.SecurityFailure
                        };
                        if (result == DataChannelResult.SecurityFailure)
                        {
                            CloseLocked(result);
                        }
                        CompleteDirectBindingLocked(result);
                        m_peerControlId = Math.Max(m_peerControlId, record.MessageId);
                        return result == DataChannelResult.SecurityFailure ? result : DataChannelResult.Good;
                    }
                    CloseLocked(DataChannelResult.SecurityFailure);
                    CompleteDirectBindingLocked(DataChannelResult.SecurityFailure);
                    return DataChannelResult.SecurityFailure;
                }
            }

            if (control is not FlowBinding binding || record.Revision != 0)
            {
                return DataChannelResult.InvalidState;
            }
            return m_carrier.Identity.IsTransportInitiator
                ? await ReceiveDirectBindingResponseAsync(binding, record, cancellationToken).ConfigureAwait(false)
                : await ReceiveDirectBindingRequestAsync(binding, record, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the acceptor proof against the initiator's outstanding transcript.
        /// </summary>
        private ValueTask<DataChannelResult> ReceiveDirectBindingResponseAsync(
            FlowBinding binding,
            FlowRecord record,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_directBindingReady)
                {
                    if (record.MessageId == m_directBindingOperationId &&
                        SameBinding(binding, m_directBindingResponse))
                    {
                        return new ValueTask<DataChannelResult>(DataChannelResult.Good);
                    }
                    CloseLocked(DataChannelResult.SecurityFailure);
                    return new ValueTask<DataChannelResult>(DataChannelResult.SecurityFailure);
                }
                if (!m_directBindingStarted ||
                    record.MessageId != m_directBindingOperationId ||
                    binding.Role != 1 ||
                    binding.TranscriptDigest != m_directBindingDigest ||
                    record.MessageId < m_peerControlId ||
                    m_directBindingExporter is not { Length: 32 } exporterKey ||
                    !FlowDirectBindingCryptography.VerifyProof(
                        new ByteString(exporterKey.AsMemory()), 1, m_directBindingDigest, binding.Proof))
                {
                    CloseLocked(DataChannelResult.SecurityFailure);
                    CompleteDirectBindingLocked(DataChannelResult.SecurityFailure);
                    return new ValueTask<DataChannelResult>(DataChannelResult.SecurityFailure);
                }
                m_peerControlId = record.MessageId;
                m_directBindingResponse = new FlowBinding(
                    binding.Role, binding.TranscriptDigest.Copy(), binding.Proof.Copy());
                m_directBindingReady = true;
                CompleteDirectBindingLocked(DataChannelResult.Good);
                return new ValueTask<DataChannelResult>(DataChannelResult.Good);
            }
        }

        /// <summary>
        /// Verifies the initiator proof, then returns the acceptor proof with the same operation ID.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDirectBindingRequestAsync(
            FlowBinding binding,
            FlowRecord record,
            CancellationToken cancellationToken)
        {
            using var deadline = new CancellationTokenSource(m_options.OpenTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            FlowBinding? duplicateResponse = null;
            IDataChannelDirectBindingProvider? provider =
                m_carrier as IDataChannelDirectBindingProvider;
            lock (m_gate)
            {
                if (record.MessageId == m_directPeerBindingOperationId)
                {
                    if (!SameBinding(binding, m_directPeerBinding))
                    {
                        CloseLocked(DataChannelResult.SecurityFailure);
                        CompleteDirectBindingLocked(DataChannelResult.SecurityFailure);
                        return DataChannelResult.SecurityFailure;
                    }
                    duplicateResponse = m_directBindingResponse;
                    if (duplicateResponse == null)
                    {
                        return DataChannelResult.WouldBlock;
                    }
                    return QueueDuplicateDirectBindingResponseLocked(
                        duplicateResponse, record.MessageId);
                }
                if (m_directBindingStarted || record.MessageId <= m_peerControlId ||
                    binding.Role != 0 || provider == null || !provider.IsAvailable)
                {
                    if (binding.Role != 0 || m_directBindingStarted ||
                        record.MessageId <= m_peerControlId)
                    {
                        CloseLocked(DataChannelResult.SecurityFailure);
                        CompleteDirectBindingLocked(DataChannelResult.SecurityFailure);
                        return DataChannelResult.SecurityFailure;
                    }
                    return QueueDirectBindingStatusLocked(record.MessageId, DataChannelResult.Unsupported);
                }
                m_directBindingStarted = true;
                m_directPeerBindingOperationId = record.MessageId;
                m_directPeerBinding = new FlowBinding(
                    binding.Role, binding.TranscriptDigest.Copy(), binding.Proof.Copy());
            }

            try
            {
                DataChannelContextIdentity identity = GetIdentity();
                FlowBindingTranscript transcript = CreateBindingTranscript(identity, provider);
                ByteString digest = FlowDirectBindingCryptography.ComputeTranscriptDigest(in transcript);
                if (digest != binding.TranscriptDigest)
                {
                    return FailDirectBinding(DataChannelResult.SecurityFailure);
                }
                ByteString exporter = await provider.ExportKeyingMaterialAsync(digest, linked.Token)
                    .ConfigureAwait(false);
                byte[]? exporterKey = TakeExporterKey(exporter);
                if (exporterKey == null)
                {
                    lock (m_gate)
                    {
                        return QueueDirectBindingStatusLocked(record.MessageId, DataChannelResult.Unsupported);
                    }
                }
                try
                {
                    if (!FlowDirectBindingCryptography.VerifyProof(
                        new ByteString(exporterKey.AsMemory()), 0, digest, binding.Proof))
                    {
                        return FailDirectBinding(DataChannelResult.SecurityFailure);
                    }

                    Task<DataChannelResult> sent;
                    lock (m_gate)
                    {
                        if (m_closed || record.MessageId <= m_localControlId)
                        {
                            CloseLocked(DataChannelResult.SecurityFailure);
                            CompleteDirectBindingLocked(DataChannelResult.SecurityFailure);
                            return DataChannelResult.SecurityFailure;
                        }
                        m_peerControlId = record.MessageId;
                        m_localControlId = record.MessageId;
                        m_directBindingDigest = digest.Copy();
                        m_directBindingExporter = exporterKey;
                        exporterKey = null;
                        var response = new FlowBinding(
                            1,
                            digest,
                            FlowDirectBindingCryptography.CreateProof(
                                new ByteString(m_directBindingExporter.AsMemory()), 1, digest));
                        m_directBindingResponse = response;
                        sent = QueueControlLocked(
                            response, 0, record.MessageId, DataChannelPath.Management, () =>
                            {
                                m_directBindingReady = true;
                                CompleteDirectBindingLocked(DataChannelResult.Good);
                                return DataChannelResult.Good;
                            });
                    }
                    DataChannelResult result = await sent.WaitAsync(linked.Token).ConfigureAwait(false);
                    if (result == DataChannelResult.Good)
                    {
                        lock (m_gate)
                        {
                            if (!m_closed)
                            {
                                m_directBindingReady = true;
                                CompleteDirectBindingLocked(DataChannelResult.Good);
                            }
                        }
                    }
                    else
                    {
                        CompleteDirectBinding(result);
                    }
                    return result;
                }
                finally
                {
                    ClearExporterKey(exporterKey);
                }
            }
            catch (OperationCanceledException)
            {
                if (m_shutdown.IsCancellationRequested)
                {
                    CompleteDirectBinding(DataChannelResult.Closed);
                    return DataChannelResult.Closed;
                }
                lock (m_gate)
                {
                    return QueueDirectBindingStatusLocked(
                        record.MessageId,
                        deadline.IsCancellationRequested ? DataChannelResult.Timeout : DataChannelResult.Closed);
                }
            }
            catch (DataChannelException exception)
            {
                lock (m_gate)
                {
                    return QueueDirectBindingStatusLocked(record.MessageId, exception.Result);
                }
            }
            catch (FlowCodecException)
            {
                return FailDirectBinding(DataChannelResult.SecurityFailure);
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
                lock (m_gate)
                {
                    return QueueDirectBindingStatusLocked(record.MessageId, DataChannelResult.Unsupported);
                }
            }
        }

        /// <summary>
        /// Replays the cached proof response for an exact duplicate operation.
        /// </summary>
        private DataChannelResult QueueDuplicateDirectBindingResponseLocked(
            FlowBinding response,
            uint operationId)
        {
            if (operationId > m_localControlId)
            {
                m_localControlId = operationId;
            }
            QueueControlLocked(response, 0, operationId, DataChannelPath.Management);
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Returns an explicit unsupported outcome without closing the baseline context.
        /// </summary>
        private DataChannelResult QueueDirectBindingStatusLocked(
            uint operationId,
            DataChannelResult result)
        {
            if (operationId > m_localControlId)
            {
                m_localControlId = operationId;
            }
            FlowWireStatus status = result switch
            {
                DataChannelResult.Unsupported => FlowWireStatus.Unsupported,
                DataChannelResult.Timeout => FlowWireStatus.Timeout,
                _ => FlowWireStatus.SecurityFailure
            };
            QueueControlLocked(
                new FlowOutcome(FlowOperation.Status, 0, status),
                0,
                operationId,
                DataChannelPath.Management);
            CompleteDirectBindingLocked(result);
            return result;
        }

        /// <summary>
        /// Closes the context for a transcript, role, or proof mismatch.
        /// </summary>
        private DataChannelResult FailDirectBinding(DataChannelResult result)
        {
            lock (m_gate)
            {
                CloseLocked(result);
                CompleteDirectBindingLocked(result);
                return result;
            }
        }

        /// <summary>
        /// Completes the optional binding result under runtime synchronization.
        /// </summary>
        private void CompleteDirectBinding(DataChannelResult result)
        {
            lock (m_gate)
            {
                CompleteDirectBindingLocked(result);
            }
        }

        /// <summary>
        /// Completes the optional binding result once.
        /// </summary>
        private void CompleteDirectBindingLocked(DataChannelResult result)
        {
            if (m_directBindingExporter is byte[] exporter)
            {
                CryptoUtils.ZeroMemory(exporter);
                m_directBindingExporter = null;
            }
            m_directBinding.TrySetResult(result);
        }

        /// <summary>
        /// Takes the provider-owned exporter array and clears the transferred material immediately.
        /// </summary>
        private static byte[]? TakeExporterKey(ByteString exporter)
        {
            if (exporter.IsNull || !MemoryMarshal.TryGetArray(exporter.Memory, out ArraySegment<byte> segment) ||
                segment.Array == null)
            {
                return null;
            }
            if (segment.Count != 32)
            {
                CryptoUtils.ZeroMemory(segment.Array.AsSpan(segment.Offset, segment.Count));
                return null;
            }
            byte[] key = exporter.ToArray();
            CryptoUtils.ZeroMemory(segment.Array.AsSpan(segment.Offset, segment.Count));
            return key;
        }

        /// <summary>
        /// Clears a provisional key that was not transferred into the context state.
        /// </summary>
        private static void ClearExporterKey(byte[]? exporterKey)
        {
            if (exporterKey != null)
            {
                CryptoUtils.ZeroMemory(exporterKey);
            }
        }

        /// <summary>
        /// Creates the same initiator-ordered transcript at both authenticated peers.
        /// </summary>
        private static FlowBindingTranscript CreateBindingTranscript(
            DataChannelContextIdentity identity,
            IDataChannelDirectBindingProvider provider)
        {
            DataChannelPeerIdentity peer = identity.Peer;
            bool initiator = peer.IsTransportInitiator;
            return new FlowBindingTranscript(
                provider.TransportProfileUri,
                provider.Capability,
                identity.ContextId,
                identity.Generation,
                initiator ? peer.LocalApplicationUri : peer.PeerApplicationUri,
                initiator ? peer.PeerApplicationUri : peer.LocalApplicationUri,
                initiator ? peer.LocalKeyThumbprint : peer.PeerKeyThumbprint,
                initiator ? peer.PeerKeyThumbprint : peer.LocalKeyThumbprint,
                initiator ? identity.LocalChallenge : identity.PeerChallenge,
                initiator ? identity.PeerChallenge : identity.LocalChallenge,
                provider.Authority,
                provider.Path);
        }

        /// <summary>
        /// Compares the exact contents of a repeated binding message.
        /// </summary>
        private static bool SameBinding(FlowBinding left, FlowBinding? right)
        {
            return right != null && left.Role == right.Role &&
                left.TranscriptDigest == right.TranscriptDigest &&
                left.Proof == right.Proof;
        }

        private readonly TaskCompletionSource<DataChannelResult> m_directBinding =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? m_directBindingTask;
        private ByteString m_directBindingDigest;
        private byte[]? m_directBindingExporter;
        private FlowBinding? m_directPeerBinding;
        private FlowBinding? m_directBindingResponse;
        private uint m_directPeerBindingOperationId;
        private uint m_directBindingOperationId;
        private bool m_directBindingStarted;
        private bool m_directBindingReady;
    }
}
