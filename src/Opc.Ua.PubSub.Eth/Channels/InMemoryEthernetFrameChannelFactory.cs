// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// In-memory loopback <see cref="IEthernetFrameChannelFactory"/>.
    /// Channels created from the same factory instance and bound to the
    /// same interface name and EtherType share a virtual broadcast
    /// domain: a frame sent on one channel is delivered to every other
    /// open channel on the same bus. No privileged sockets are used, so
    /// it is deterministic and safe to run in CI.
    /// </summary>
    /// <remarks>
    /// Mirrors a switched Ethernet segment without loopback to the
    /// sender: a publisher and a subscriber are created as two separate
    /// channels on the same bus, and the subscriber observes the
    /// publisher's frames.
    /// </remarks>
    public sealed class InMemoryEthernetFrameChannelFactory : IEthernetFrameChannelFactory
    {
        private readonly System.Threading.Lock m_sync = new();

        private readonly Dictionary<string, List<InMemoryEthernetFrameChannel>> m_buses
            = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public IEthernetFrameChannel Create(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            string key = BuildKey(parameters);
            return new InMemoryEthernetFrameChannel(this, key, parameters, telemetry, timeProvider);
        }

        /// <summary>
        /// Registers a channel on the loopback bus identified by
        /// <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Bus key (interface name + EtherType).</param>
        /// <param name="channel">The channel to attach.</param>
        internal void Attach(string key, InMemoryEthernetFrameChannel channel)
        {
            lock (m_sync)
            {
                if (!m_buses.TryGetValue(key, out List<InMemoryEthernetFrameChannel>? list))
                {
                    list = [];
                    m_buses[key] = list;
                }
                if (!list.Contains(channel))
                {
                    list.Add(channel);
                }
            }
        }

        /// <summary>
        /// Removes a channel from the loopback bus identified by
        /// <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Bus key (interface name + EtherType).</param>
        /// <param name="channel">The channel to detach.</param>
        internal void Detach(string key, InMemoryEthernetFrameChannel channel)
        {
            lock (m_sync)
            {
                if (m_buses.TryGetValue(key, out List<InMemoryEthernetFrameChannel>? list))
                {
                    list.Remove(channel);
                    if (list.Count == 0)
                    {
                        m_buses.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Delivers a frame from <paramref name="sender"/> to every other
        /// channel attached to the same bus.
        /// </summary>
        /// <param name="key">Bus key (interface name + EtherType).</param>
        /// <param name="sender">The publishing channel (not delivered to).</param>
        /// <param name="frame">The raw frame bytes.</param>
        internal void Publish(
            string key,
            InMemoryEthernetFrameChannel sender,
            ReadOnlySpan<byte> frame)
        {
            InMemoryEthernetFrameChannel[] targets;
            lock (m_sync)
            {
                if (!m_buses.TryGetValue(key, out List<InMemoryEthernetFrameChannel>? list))
                {
                    return;
                }
                targets = [.. list];
            }
            for (int i = 0; i < targets.Length; i++)
            {
                if (!ReferenceEquals(targets[i], sender))
                {
                    targets[i].Deliver(frame);
                }
            }
        }

        private static string BuildKey(EthChannelParameters parameters)
        {
            string name = string.IsNullOrEmpty(parameters.InterfaceName)
                ? "default"
                : parameters.InterfaceName!;
            return string.Concat(
                name,
                "|",
                parameters.EtherType.ToString("X4", CultureInfo.InvariantCulture));
        }
    }
}
