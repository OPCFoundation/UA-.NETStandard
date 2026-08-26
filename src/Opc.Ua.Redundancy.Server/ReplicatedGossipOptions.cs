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
using System.Net;
using Crdt;
using Crdt.Transport;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: shared CRDT gossip configuration: replica identity, time source, the
    /// transport that disseminates state between replicas, and decoding limits.
    /// </summary>
    public abstract class ReplicatedGossipOptions
    {
        /// <summary>
        /// Gets a value indicating whether CRDT gossip state decoding is
        /// binary-compatible in this compiled assembly.
        /// </summary>
        /// <remarks>
        /// The gossip decoding limits are configured through the external CRDT
        /// library's <c>CrdtReaderOptions</c>, whose properties use
        /// <see langword="init"/> accessors. On the <c>netstandard2.1</c> build
        /// the CRDT library resolves the <c>init</c> marker
        /// (<c>System.Runtime.CompilerServices.IsExternalInit</c>) to a type it
        /// defines internally, whereas the .NET 5+ builds resolve it to the one
        /// in the base class library. When a <c>netstandard2.1</c>-compiled
        /// assembly runs on a modern .NET runtime (which loads the .NET build of
        /// the CRDT library) the <c>init</c> setter signatures no longer match
        /// and invoking them throws <see cref="MissingMethodException"/>. This
        /// probe therefore returns <see langword="true"/> for the .NET 5+ and
        /// .NET Framework builds and <see langword="false"/> only for the
        /// <c>netstandard2.1</c> build, allowing callers and tests to react at
        /// runtime instead of assuming compile-time availability.
        /// </remarks>
        public static bool IsGossipStateDecodingSupported =>
#if NET5_0_OR_GREATER || NETFRAMEWORK
            true;
#else
            false;
#endif

        /// <summary>
        /// Gets or sets this replica's stable CRDT identity. Defaults to a new
        /// random identity; supply a stable value per replica in production.
        /// </summary>
        public ReplicaId ReplicaId { get; set; } = ReplicaId.New();

        /// <summary>
        /// Gets or sets the time source used by the logical clock.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <summary>
        /// Gets or sets the factory that creates the gossip transport. When
        /// <c>null</c>, an isolated in-process transport is used (single-process
        /// / development only — no cross-node replication); configure
        /// <see cref="UseTcpGossip"/> or <see cref="UseUdpGossip"/> for a real
        /// deployment.
        /// </summary>
        public Func<IServiceProvider, ITransport>? TransportFactory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a custom <see cref="TransportFactory"/> authenticates peers.
        /// </summary>
        /// <remarks>
        /// This only applies when assigning <see cref="TransportFactory"/> directly. The built-in
        /// <see cref="UseTcpGossip"/> helper sets the authentication state from its mutual-TLS options.
        /// </remarks>
        public bool TransportFactoryProvidesAuthenticatedGossip { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether network gossip may start without authenticated transport.
        /// </summary>
        /// <remarks>
        /// Leave this at the secure default (<c>false</c>) for production. Set it to <c>true</c> only for
        /// isolated development or test fabrics where forged CRDT frames cannot be injected by another host.
        /// TCP gossip is considered authenticated when mutual TLS is configured; UDP gossip has no built-in
        /// peer authentication and therefore requires this explicit opt-out.
        /// </remarks>
        public bool AllowUnauthenticatedGossip { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of replicated map entries accepted
        /// when decoding received state.
        /// </summary>
        public int MaxEntryCount { get; set; } = 1_000_000;

        /// <summary>
        /// Gets or sets the maximum encoded key/payload size (bytes) accepted
        /// when decoding received state.
        /// </summary>
        public int MaxPayloadBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the port offset applied to gossip peer endpoints pushed through an
        /// <see cref="IGossipPeerSink"/> at runtime. The address-space fabric uses <c>0</c>; the session fabric
        /// uses <c>1</c> to match the address-space port + 1 convention.
        /// </summary>
        internal int GossipPeerPortOffset { get; set; }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: configures a TCP gossip transport. Peers added via
        /// <see cref="AddPeer"/> are attached to the created transport.
        /// </summary>
        /// <param name="address">The local bind address.</param>
        /// <param name="port">The local bind port (<c>0</c> for an OS-assigned port).</param>
        /// <param name="gossipInterval">Optional anti-entropy gossip interval.</param>
        /// <param name="tls">Optional TLS / mutual-TLS configuration. Mutual TLS is required unless
        /// <see cref="AllowUnauthenticatedGossip"/> is explicitly enabled.</param>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        public void UseTcpGossip(
            IPAddress address,
            int port,
            TimeSpan? gossipInterval = null,
            GossipTlsOptions? tls = null)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            TransportFactory = _ =>
            {
                var transportOptions = new TcpGossipTransportOptions
                {
                    Address = address,
                    Port = port,
                    Tls = tls
                };
                if (gossipInterval.HasValue)
                {
                    transportOptions.GossipInterval = gossipInterval.Value;
                }

                var transport = new TcpGossipTransport(transportOptions);
                transport.AddPeers(m_peers);
                return transport;
            };
            m_transportSecurityMode = IsMutualTlsConfigured(tls) ?
                GossipTransportSecurityMode.AuthenticatedNetwork :
                GossipTransportSecurityMode.UnauthenticatedNetwork;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: configures a UDP datagram gossip transport. Peers added via
        /// <see cref="AddPeer"/> are attached to the created transport.
        /// </summary>
        /// <param name="address">The local bind address.</param>
        /// <param name="port">The local bind port (<c>0</c> for an OS-assigned port).</param>
        /// <param name="gossipInterval">Optional anti-entropy gossip interval.</param>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is <c>null</c>.</exception>
        public void UseUdpGossip(IPAddress address, int port, TimeSpan? gossipInterval = null)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            TransportFactory = _ =>
            {
                var transport = new UdpGossipTransport(
                    address, port, gossipInterval ?? TimeSpan.FromMilliseconds(500));
                transport.AddPeers(m_peers);
                return transport;
            };
            m_transportSecurityMode = GossipTransportSecurityMode.UnauthenticatedNetwork;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: adds a peer endpoint to gossip with. Applied to the transport
        /// created by <see cref="UseTcpGossip"/> / <see cref="UseUdpGossip"/>.
        /// </summary>
        /// <param name="endpoint">The peer endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public void AddPeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            m_peers.Add(endpoint);
        }

        /// <summary>
        /// Builds the decoding limits for received state.
        /// </summary>
        internal CrdtReaderOptions CreateReaderOptions()
        {
#if NETSTANDARD
            // Crdt's netstandard build declares MaxCollectionCount/MaxStringBytes/MaxDepth as init-only
            // setters whose modreq references an IsExternalInit type defined *inside* the netstandard Crdt
            // assembly (netstandard has no BCL IsExternalInit). When this netstandard-compiled assembly is
            // loaded on a .NET runtime - as the nightly "NETStandard 2.1" test leg does on net8.0 - the
            // matching net8.0 Crdt assembly is loaded instead, whose setters carry a modreq to the BCL
            // IsExternalInit. That modreq mismatch makes the C#-emitted init call unresolvable at runtime
            // (MissingMethodException on set_MaxCollectionCount). Reflection binds by name and ignores the
            // modreq, so it resolves against whichever Crdt build is actually loaded.
            var options = new CrdtReaderOptions();
            SetLimit(nameof(CrdtReaderOptions.MaxCollectionCount), MaxEntryCount);
            SetLimit(nameof(CrdtReaderOptions.MaxStringBytes), MaxPayloadBytes);
            SetLimit(nameof(CrdtReaderOptions.MaxDepth), CrdtReaderOptions.Default.MaxDepth);
            return options;

            void SetLimit(string name, int value)
            {
                System.Reflection.PropertyInfo property = typeof(CrdtReaderOptions).GetProperty(name)
                    ?? throw new MissingMemberException(nameof(CrdtReaderOptions), name);
                property.SetValue(options, value);
            }
#else
            return new CrdtReaderOptions
            {
                MaxCollectionCount = MaxEntryCount,
                MaxStringBytes = MaxPayloadBytes,
                MaxDepth = CrdtReaderOptions.Default.MaxDepth
            };
#endif
        }

        /// <summary>
        /// Creates the transport for one replica. When no factory is
        /// configured, an isolated in-process network is created and returned
        /// via <paramref name="defaultNetwork"/> so the caller can dispose it.
        /// </summary>
        internal ITransport CreateTransport(IServiceProvider services, out InMemoryNetwork? defaultNetwork)
        {
            if (TransportFactory != null)
            {
                if (TransportFactoryProvidesAuthenticatedGossip)
                {
                    m_transportSecurityMode = GossipTransportSecurityMode.AuthenticatedNetwork;
                }
                ThrowIfUnauthenticatedNetworkGossip();
                defaultNetwork = null;
                ITransport transport = TransportFactory(services);
                RegisterRuntimePeerSink(services, transport);
                return transport;
            }

            var network = new InMemoryNetwork();
            defaultNetwork = network;
            return network.CreateTransport();
        }

        /// <summary>
        /// Bridges an optional <see cref="IGossipPeerSink"/> to a live gossip transport so peers discovered
        /// after startup are added to its peer set. Applies <see cref="GossipPeerPortOffset"/> so the session
        /// fabric reaches each peer at its address-space port + 1.
        /// </summary>
        private void RegisterRuntimePeerSink(IServiceProvider services, ITransport transport)
        {
            if (services?.GetService(typeof(IGossipPeerSink)) is not IGossipPeerSink sink)
            {
                return;
            }

            int offset = GossipPeerPortOffset;
            switch (transport)
            {
                case TcpGossipTransport tcp:
                    sink.Register(ep => tcp.AddPeer(WithOffset(ep, offset)));
                    break;
                case UdpGossipTransport udp:
                    sink.Register(ep => udp.AddPeer(WithOffset(ep, offset)));
                    break;
            }
        }

        private static IPEndPoint WithOffset(IPEndPoint endpoint, int offset)
        {
            return offset == 0 ? endpoint : new IPEndPoint(endpoint.Address, endpoint.Port + offset);
        }

        private void ThrowIfUnauthenticatedNetworkGossip()
        {
            if (m_transportSecurityMode == GossipTransportSecurityMode.AuthenticatedNetwork ||
                AllowUnauthenticatedGossip)
            {
                return;
            }

            throw new InvalidOperationException(
                "CRDT network gossip is configured without authenticated transport. Address-space CRDT entries " +
                "are last-writer-wins; an unauthenticated peer can forge a higher-clock frame and replace values " +
                "served to clients. Configure TCP gossip with mutual TLS (server certificate, required client " +
                "certificates, client certificate, and remote certificate validation), or explicitly set " +
                $"{nameof(TransportFactoryProvidesAuthenticatedGossip)} for authenticated custom transports, or set " +
                $"{nameof(AllowUnauthenticatedGossip)} to true for isolated development/test fabrics.");
        }

        private static bool IsMutualTlsConfigured(GossipTlsOptions? tls)
        {
            return tls?.ServerCertificate != null &&
                tls.RequireClientCertificate &&
                tls.ClientCertificates != null &&
                tls.ClientCertificates.Count > 0 &&
                tls.RemoteCertificateValidationCallback != null;
        }

        private enum GossipTransportSecurityMode
        {
            InProcess,
            AuthenticatedNetwork,
            UnauthenticatedNetwork
        }

        private readonly List<IPEndPoint> m_peers = [];
        private GossipTransportSecurityMode m_transportSecurityMode = GossipTransportSecurityMode.InProcess;
    }
}
