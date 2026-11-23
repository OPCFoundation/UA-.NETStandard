// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.PubSub.Pcap;

namespace UaLens.Diagnostics.Capture
{
    /// <summary>
    /// Limits a private workspace registry and deliberately excludes channel-key snapshots.
    /// </summary>
    internal sealed class DiagnosticClientCaptureRegistry(
        IChannelCaptureRegistry registry, DiagnosticCaptureBudget budget) : IChannelCaptureRegistry, IFrameCaptureSink
    {
        /// <inheritdoc/>
        public IFrameCaptureSink? CurrentObserver => Volatile.Read(ref m_observer);

        /// <inheritdoc/>
        public IFrameCaptureSink? SetObserver(IFrameCaptureSink? observer)
        {
            lock (m_gate)
            {
                if (registry.CurrentObserver is { } current && !ReferenceEquals(current, this))
                {
                    throw new InvalidOperationException("Another capture already owns this client registry.");
                }
                IFrameCaptureSink? previous = m_observer;
                m_observer = observer;
                if (observer is null)
                {
                    registry.TryClearObserver(this);
                }
                else
                {
                    registry.SetObserver(this);
                }
                return previous;
            }
        }

        /// <inheritdoc/>
        public bool TryClearObserver(IFrameCaptureSink expected)
        {
            lock (m_gate)
            {
                if (!ReferenceEquals(m_observer, expected))
                {
                    return false;
                }
                budget.Stop();
                m_observer = null;
                return registry.TryClearObserver(this);
            }
        }

        /// <inheritdoc/>
        public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
        {
            lock (m_gate)
            {
                if (m_observer is { } observer && !chunk.IsEmpty && budget.TryAccept(chunk.Length))
                {
                    observer.OnFrameSent(channelId, chunk);
                }
            }
        }

        /// <inheritdoc/>
        public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
        {
            lock (m_gate)
            {
                if (m_observer is { } observer && !chunk.IsEmpty && budget.TryAccept(chunk.Length))
                {
                    observer.OnFrameReceived(channelId, chunk);
                }
            }
        }

        /// <inheritdoc/>
        public void OnTokenActivated(uint channelId, ChannelToken currentToken, ChannelToken? previousToken)
        {
            // Packet-data consent never grants consent to collect decryption keys.
        }

        private readonly Lock m_gate = new();
        private IFrameCaptureSink? m_observer;
    }

    /// <summary>
    /// Bounds PubSub payload copies while preserving source ownership and complete datagrams.
    /// </summary>
    internal sealed class DiagnosticPubSubCaptureRegistry(
        IPubSubCaptureRegistry registry, DiagnosticCaptureBudget budget)
        : IPubSubCaptureRegistry, IPubSubCaptureObserver
    {
        /// <inheritdoc/>
        public IPubSubCaptureObserver? CurrentObserver => Volatile.Read(ref m_observer);

        /// <inheritdoc/>
        public void SetObserver(IPubSubCaptureObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (m_gate)
            {
                if (registry.CurrentObserver is { } current && !ReferenceEquals(current, this))
                {
                    throw new InvalidOperationException("Another capture already owns this PubSub registry.");
                }
                m_observer = observer;
                registry.SetObserver(this);
            }
        }

        /// <inheritdoc/>
        public bool TryClearObserver(IPubSubCaptureObserver observer)
        {
            lock (m_gate)
            {
                if (!ReferenceEquals(m_observer, observer))
                {
                    return false;
                }
                budget.Stop();
                m_observer = null;
                return registry.TryClearObserver(this);
            }
        }

        /// <inheritdoc/>
        public void OnFrameCaptured(in PubSubCaptureContext context, ReadOnlySpan<byte> payload)
        {
            lock (m_gate)
            {
                if (m_observer is { } observer && budget.TryAccept(payload.Length))
                {
                    observer.OnFrameCaptured(in context, payload);
                }
            }
        }

        private readonly Lock m_gate = new();
        private IPubSubCaptureObserver? m_observer;
    }
}
