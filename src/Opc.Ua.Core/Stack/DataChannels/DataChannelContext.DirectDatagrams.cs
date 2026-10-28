// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Gets the actual direct datagram capacity, independently of baseline UASC DATA support.
        /// </summary>
        private int DirectDatagramCapacity => m_carrier is IDataChannelDirectDatagramProvider provider
            ? provider.MaxDirectDatagramSize : 0;

        /// <summary>
        /// Installs one context-owned unreliable receiver before acknowledging any datagram association.
        /// </summary>
        private async ValueTask<bool> RegisterDirectDatagramReceiverAsync()
        {
            Task<bool> registered;
            Task receiver;
            lock (m_gate)
            {
                if (m_closed)
                {
                    return false;
                }
                if (m_directDatagramTask == null || m_directDatagramTask.IsCompleted)
                {
                    m_directDatagramRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource<bool> registration = m_directDatagramRegistered;
                    m_directDatagramTask = Task.Run(() => ReceiveDirectDatagramsAsync(registration));
                }
                registered = m_directDatagramRegistered.Task;
                receiver = m_directDatagramTask;
            }
            bool admitted = await registered.ConfigureAwait(false);
            if (!admitted)
            {
                await receiver.ConfigureAwait(false);
            }
            return admitted;
        }

        /// <summary>
        /// Keeps one bounded receive owner while invalid or unadmitted input cannot stall other traffic.
        /// </summary>
        private async Task ReceiveDirectDatagramsAsync(TaskCompletionSource<bool> registration)
        {
            try
            {
                var provider = (IDataChannelDirectDatagramProvider)m_carrier;
                int capacity = provider.MaxDirectDatagramSize;
                if (capacity <= DirectDatagramCodec.HeaderSize)
                {
                    registration.TrySetResult(false);
                    return;
                }
                DataChannelApplicationBudget.DataChannelCarrierReservation? reservation = null;
                try
                {
                    if (!m_budget.TryReserveCarrier(capacity, 0, 1, out reservation))
                    {
                        registration.TrySetResult(false);
                        return;
                    }
                    while (!m_shutdown.IsCancellationRequested)
                    {
                        ValueTask<ByteString> pending = provider.ReceiveDirectDatagramAsync(m_shutdown.Token);
                        registration.TrySetResult(true);
                        ByteString datagram = await pending.ConfigureAwait(false);
                        if (datagram.Length <= capacity)
                        {
                            await ReceiveDirectDatagramAsync(datagram, m_shutdown.Token).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    reservation?.Dispose();
                }
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
                registration.TrySetResult(false);
            }
            catch (DataChannelException exception) when (!registration.Task.IsCompleted)
            {
                registration.TrySetResult(false);
                m_logger.FlowCarrierFailure(exception);
            }
            catch (Exception exception)
            {
                registration.TrySetResult(false);
                m_logger.FlowCarrierFailure(exception);
                lock (m_gate)
                {
                    if (!m_closed)
                    {
                        CloseLocked(DataChannelResult.Closed);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the token before applying the existing revision, replay, rate and queue admission.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDirectDatagramAsync(
            ByteString payload, CancellationToken cancellationToken)
        {
            int capacity = DirectDatagramCapacity;
            if (capacity <= DirectDatagramCodec.HeaderSize)
            {
                return DataChannelResult.Unsupported;
            }
            try
            {
                DirectDatagram datagram = DirectDatagramCodec.DecodeExact(
                    payload, m_codecLimits, capacity);
                FlowState flow;
                lock (m_gate)
                {
                    if (m_closed || !m_directBindingReady ||
                        !m_directAssociations.TryGetReceiver(datagram.Token, out FlowState? owner) ||
                        !owner.IsDatagram)
                    {
                        return DataChannelResult.UnknownChannel;
                    }
                    flow = owner;
                    DataChannelResult valid = ValidateDirectPayloadLocked(flow, datagram.Body);
                    if (valid != DataChannelResult.Good)
                    {
                        return valid;
                    }
                }
                var record = new FlowRecord(FlowRecordKind.Data, datagram.Revision, datagram.Sequence, datagram.Body);
                return await ReceiveDataAsync(flow.Id, record, true, cancellationToken).ConfigureAwait(false);
            }
            catch (FlowCodecException exception)
            {
                return FlowModel.FromWire(exception.Status);
            }
        }

        /// <summary>
        /// Enforces the admitted UADP class and any source-to-resource publisher authorization.
        /// </summary>
        private DataChannelResult ValidateDirectPayloadLocked(FlowState flow, ByteString body)
        {
            if (!IsSupportedUascPayload(flow, body))
            {
                return DataChannelResult.InvalidArgument;
            }
            if (m_uascPublisherRoutes.Count == 0)
            {
                return DataChannelResult.Good;
            }
            UadpNetworkMessageHeader header = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(body);
            DataChannelResult route = ResolveUascPublisherRoute(header, out string? resource);
            if (route != DataChannelResult.Good)
            {
                return route;
            }
            return string.Equals(resource, flow.Parameters.ResourceUri, StringComparison.Ordinal)
                ? DataChannelResult.Good : DataChannelResult.NotAuthorized;
        }

        /// <summary>The one context-level datagram ingress worker.</summary>
        private Task? m_directDatagramTask;

        /// <summary>Receive registration must precede ASSOCIATE acknowledgement and READY.</summary>
        private TaskCompletionSource<bool> m_directDatagramRegistered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
