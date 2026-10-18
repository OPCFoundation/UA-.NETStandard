// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Forwards real encrypted UDP packets and can temporarily hold one direction without interpreting QUIC.
    /// </summary>
    internal sealed class NativePacketRelay : IAsyncDisposable
    {
        /// <summary>
        /// Starts a bounded loopback relay to the actual native listener.
        /// </summary>
        internal NativePacketRelay(string serverAddress)
        {
            m_server = IPEndPoint.Parse(serverAddress);
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                m_socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                Address = m_socket.LocalEndPoint!.ToString()!;
                m_pump = PumpAsync();
            }
            catch
            {
                m_socket.Dispose();
                m_shutdown.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the actual relay address used by the native client.
        /// </summary>
        internal string Address { get; }

        /// <summary>
        /// Holds subsequent packets in one direction while allowing the opposite direction to continue.
        /// </summary>
        /// <exception cref="InvalidOperationException">A hold is already active.</exception>
        internal void Hold(bool fromClient)
        {
            lock (m_gate)
            {
                if (m_holding)
                {
                    throw new InvalidOperationException("A packet hold is already active.");
                }
                m_holding = true;
                m_holdFromClient = fromClient;
                m_packetHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        /// <summary>
        /// Waits until the real socket receives a packet in the held direction.
        /// </summary>
        internal Task WaitForHeldPacketAsync(CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                return m_packetHeld.Task.WaitAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Releases the bounded queue of original encrypted packets without modifying their bytes.
        /// </summary>
        internal async ValueTask ReleaseAsync(CancellationToken cancellationToken)
        {
            Packet[] pending;
            lock (m_gate)
            {
                m_holding = false;
                pending = [.. m_held];
                m_held.Clear();
            }
            foreach (Packet packet in pending)
            {
                await SendAsync(packet.Bytes, packet.Destination, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        /// <summary>
        /// Receives and forwards only the actual client and listener endpoints.
        /// </summary>
        /// <exception cref="InvalidOperationException">An endpoint or packet-queue invariant was violated.</exception>
        private async Task PumpAsync()
        {
            byte[] buffer = new byte[65536];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                SocketReceiveFromResult received = await m_socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, any, m_shutdown.Token).ConfigureAwait(false);
                EndPoint destination;
                lock (m_gate)
                {
                    bool fromClient = !received.RemoteEndPoint.Equals(m_server);
                    if (fromClient)
                    {
                        m_client ??= received.RemoteEndPoint;
                        if (!m_client.Equals(received.RemoteEndPoint))
                        {
                            throw new InvalidOperationException("An unrelated peer reached the test relay.");
                        }
                        destination = m_server;
                    }
                    else
                    {
                        destination = m_client ??
                            throw new InvalidOperationException("The listener replied before a client was known.");
                    }
                    if (m_holding && fromClient == m_holdFromClient)
                    {
                        if (m_held.Count == 64)
                        {
                            throw new InvalidOperationException("The bounded packet hold queue is full.");
                        }
                        m_held.Add(new Packet(buffer.AsSpan(0, received.ReceivedBytes).ToArray(), destination));
                        m_packetHeld.TrySetResult();
                        continue;
                    }
                }
                await SendAsync(buffer.AsMemory(0, received.ReceivedBytes), destination, m_shutdown.Token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Requires the real socket to accept the complete unchanged datagram.
        /// </summary>
        /// <exception cref="InvalidOperationException">The socket did not send the complete datagram.</exception>
        private async ValueTask SendAsync(
            ReadOnlyMemory<byte> bytes,
            EndPoint destination,
            CancellationToken cancellationToken)
        {
            int sent = await m_socket.SendToAsync(bytes, SocketFlags.None, destination, cancellationToken)
                .ConfigureAwait(false);
            if (sent != bytes.Length)
            {
                throw new InvalidOperationException("The relay did not send the complete datagram.");
            }
        }

        /// <summary>
        /// Cancels and drains the actual socket pump before releasing its owned buffers and socket.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            try
            {
                await m_shutdown.CancelAsync().ConfigureAwait(false);
                await m_pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
                // Socket cancellation is the expected termination of the real packet relay.
            }
            finally
            {
                m_socket.Dispose();
                m_shutdown.Dispose();
                lock (m_gate)
                {
                    m_held.Clear();
                }
            }
        }

        /// <summary>
        /// Holds one unmodified encrypted datagram and its actual forwarding destination.
        /// </summary>
        private readonly record struct Packet(byte[] Bytes, EndPoint Destination);

        private readonly Lock m_gate = new();
        private readonly Socket m_socket;
        private readonly IPEndPoint m_server;
        private readonly Task m_pump;
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly List<Packet> m_held = [];
        private TaskCompletionSource m_packetHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private EndPoint? m_client;
        private Task? m_disposal;
        private bool m_holding;
        private bool m_holdFromClient;
    }
}
