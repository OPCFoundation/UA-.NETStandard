/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Server;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Wires a source and a sink data channel together over the framing the sample was asked for.
    /// </summary>
    internal sealed class StreamingHarness : IAsyncDisposable
    {
        private StreamingHarness(
            DataChannelManager sourceManager,
            DataChannelManager sinkManager,
            DataChannel source,
            DataChannel sink,
            DataChannelFramingMode framingMode,
            uint channelId,
            DataChannelParametersDataType revisedParameters,
            ulong revisedTransportChannelId,
            Func<CancellationToken, ValueTask>? closeService = null,
            IAsyncDisposable? owner = null)
        {
            m_sourceManager = sourceManager;
            m_sinkManager = sinkManager;
            Source = source;
            Sink = sink;
            FramingMode = framingMode;
            ChannelId = channelId;
            RevisedParameters = revisedParameters;
            RevisedTransportChannelId = revisedTransportChannelId;
            m_closeService = closeService;
            m_owner = owner;
        }

        public DataChannel Source { get; }

        public DataChannel Sink { get; }

        public DataChannelFramingMode FramingMode { get; }

        public uint ChannelId { get; }

        public DataChannelParametersDataType RevisedParameters { get; }

        public ulong RevisedTransportChannelId { get; }

        public static Task<StreamingHarness> CreateAsync(SampleOptions options, CancellationToken ct)
        {
            return options.RunMode == SampleRunMode.Direct
                ? CreateDirectAsync(options, ct)
                : CreateServerAsync(options, ct);
        }

        public async ValueTask CloseDataChannelAsync(CancellationToken ct)
        {
            if (m_closeService != null)
            {
                await m_closeService(ct).ConfigureAwait(false);
                return;
            }

            Source.Close();
        }

        private static Task<StreamingHarness> CreateDirectAsync(SampleOptions options, CancellationToken ct)
        {
            _ = ct;
            ITelemetryContext telemetry = new ConsoleTelemetry();
            var bufferManager = new BufferManager("sample", 65536, telemetry);

            DataChannelSettings settings = SettingsFromOptions(options);
            bool quic = options.Transport == SampleTransport.Quic;

            var sourceTransport = new InProcessDataChannelTransport(bufferManager, telemetry, quic);
            var sinkTransport = new InProcessDataChannelTransport(bufferManager, telemetry, quic);

            var sourceManager = new DataChannelManager(sourceTransport, true, telemetry);
            var sinkManager = new DataChannelManager(sinkTransport, false, telemetry);

            sourceTransport.Peer = sinkManager;
            sinkTransport.Peer = sourceManager;

            const uint channelId = 1;
            NodeId sourceNodeId = SourceNodeId;

            DataChannel source = sourceManager.Register(channelId, sourceNodeId, settings, isSource: true);
            DataChannel sink = sinkManager.Register(channelId, sourceNodeId, settings, isSource: false);

            sourceManager.MarkOpen(channelId);
            sinkManager.MarkOpen(channelId);

            return Task.FromResult(new StreamingHarness(
                sourceManager,
                sinkManager,
                source,
                sink,
                quic ? DataChannelFramingMode.Quic : DataChannelFramingMode.Inline,
                channelId,
                settings.ToParameters(),
                0));
        }

        private static async Task<StreamingHarness> CreateServerAsync(SampleOptions options, CancellationToken ct)
        {
            if (options.Transport == SampleTransport.Quic)
            {
                throw new NotSupportedException(
                    "The end-to-end QUIC path is pending a public client-side stream allocator/binder. " +
                    "The server-side IServerDataChannelTransport seam exists, but the sample cannot yet " +
                    "bind the client DataChannelManager to the negotiated QUIC stream without reaching into QUIC internals.");
            }

            var state = new ServerStreamingState(options);
            DataChannelSampleServer.PendingState = state;

            const string endpointUrl = "opc.tcp://localhost:62550/ConsoleDataChannelStreaming";
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services
                .AddOpcUa()
                .AddServer<DataChannelSampleServer>(o =>
                {
                    const string applicationName = "ConsoleDataChannelStreamingServer";
                    o.ApplicationName = applicationName;
                    o.ApplicationUri = "urn:localhost:OPCFoundation:ConsoleDataChannelStreamingServer";
                    o.ProductUri = "uri:opcfoundation.org:ConsoleDataChannelStreamingServer";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.PkiRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OPC Foundation",
                        applicationName,
                        "pki");
                    o.RejectSHA1Certificates = true;
                    o.MinCertificateKeySize = 2048;
                    o.EndpointUrls.Add(endpointUrl);
                });

            builder.Services
                .AddOpcUa()
                .AddClient(o =>
                {
                    const string applicationName = "ConsoleDataChannelStreamingClient";
                    o.ApplicationName = applicationName;
                    o.ApplicationUri = "urn:localhost:OPCFoundation:ConsoleDataChannelStreamingClient";
                    o.ProductUri = "uri:opcfoundation.org:ConsoleDataChannelStreamingClient";
                    o.PkiRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OPC Foundation",
                        applicationName,
                        "pki");
                    o.AutoAcceptUntrustedCertificates = true;
                    o.RejectSHA1SignedCertificates = true;
                    o.MinimumCertificateKeySize = 2048;
                    o.Session = new ManagedSessionOptions
                    {
                        SessionName = "ConsoleDataChannelStreaming",
                        SessionTimeout = TimeSpan.FromSeconds(60)
                    };
                })
                .AddDiscoveryAndConnect(o =>
                {
                    o.DiscoveryUrl = endpointUrl;
                    o.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    o.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                });

            IHost host = builder.Build();
            await host.StartAsync(ct).ConfigureAwait(false);

            Func<CancellationToken, Task<ManagedSession>> connect =
                host.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession session = await ConnectWithRetryAsync(connect, ct).ConfigureAwait(false);

            ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
            OpenDataChannelResponse opened = await session.OpenDataChannelAsync(
                null,
                state.SourceNodeId,
                0,
                0,
                SettingsFromOptions(options).ToParameters(),
                ct).ConfigureAwait(false);

            UaSCUaBinaryTransportChannel tcpChannel =
                UnwrapTransportChannel(session.TransportChannel) as UaSCUaBinaryTransportChannel ??
                throw new InvalidOperationException(
                    $"The client transport is {session.TransportChannel.GetType().FullName}, not UASC binary.");

            DataChannelManager clientManager = tcpChannel.EnableDataChannels(
                isServer: false,
                telemetry,
                maxDataChannels: 16,
                maxCreditPerChannel: 1024 * 1024);

            DataChannel sink = clientManager.Register(
                opened.ChannelId,
                state.SourceNodeId,
                DataChannelSettings.FromParameters(opened.RevisedParameters),
                isSource: false,
                opened.RevisedTransportChannelId);
            clientManager.MarkOpen(opened.ChannelId);

            DataChannel source = await state.WaitForSourceAsync(ct).ConfigureAwait(false);
            return new StreamingHarness(
                state.ServerManager ?? throw new InvalidOperationException("Server data-channel manager was not created."),
                clientManager,
                source,
                sink,
                DataChannelFramingMode.Inline,
                opened.ChannelId,
                opened.RevisedParameters,
                opened.RevisedTransportChannelId,
                async closeCt => await session.CloseDataChannelAsync(
                    null,
                    opened.ChannelId,
                    StatusCodes.Good,
                    deleteQueued: false,
                    closeCt).ConfigureAwait(false),
                new ServerHarnessOwner(host, session));
        }

        public async ValueTask DisposeAsync()
        {
            await m_sourceManager.DisposeAsync().ConfigureAwait(false);
            await m_sinkManager.DisposeAsync().ConfigureAwait(false);
            if (m_owner != null)
            {
                await m_owner.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static DataChannelSettings SettingsFromOptions(SampleOptions options)
        {
            return new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = options.DeliveryMode,
                ContentType = "video/H264",
                MaxFrameSize = (uint)Math.Max(options.FrameSize, 1),
                InitialCredit = (uint)Math.Max(options.FrameSize * 16, 65536),
                FrameDeadline = options.DeliveryMode is DataChannelDeliveryMode.PartiallyReliable
                    or DataChannelDeliveryMode.Unreliable
                    ? 250u
                    : 0u
            };
        }

        private static async Task<ManagedSession> ConnectWithRetryAsync(
            Func<CancellationToken, Task<ManagedSession>> connect,
            CancellationToken ct)
        {
            Exception? last = null;
            for (int ii = 0; ii < 20; ii++)
            {
                try
                {
                    return await connect(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("The in-process OPC UA server did not become ready.", last);
        }

        private static ITransportChannel UnwrapTransportChannel(ITransportChannel channel)
        {
            if (channel.GetType().FullName != "Opc.Ua.ManagedTransportChannelLease")
            {
                return channel;
            }

            object? entry = channel.GetType()
                .GetProperty("Entry", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(channel);
            object? underlying = entry?.GetType()
                .GetProperty("Underlying", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(entry);
            return underlying as ITransportChannel ?? channel;
        }

        private static readonly NodeId SourceNodeId = new("Camera1", 1);
        private readonly DataChannelManager m_sourceManager;
        private readonly DataChannelManager m_sinkManager;
        private readonly Func<CancellationToken, ValueTask>? m_closeService;
        private readonly IAsyncDisposable? m_owner;
    }

    internal sealed class ServerStreamingState : IDataChannelSource
    {
        public ServerStreamingState(SampleOptions options)
        {
            SourceNodeId = new NodeId("Camera1", 1);
            Capabilities = new DataChannelSourceCapabilities
            {
                Direction = DataChannelDirection.SourceToSink,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = "video/H264",
                MaxFrameSize = (uint)Math.Max(options.FrameSize, 1),
                MaxChannels = 1,
                Priority = 1
            };
        }

        public NodeId SourceNodeId { get; }

        public NodeId NodeId => SourceNodeId;

        public DataChannelSourceCapabilities Capabilities { get; }

        public int ActiveChannelCount => m_channel == null ? 0 : 1;

        public DataChannelManager? ServerManager { get; set; }

        public void OnChannelOpened(DataChannel channel)
        {
            m_channel = channel;
            m_opened.TrySetResult(channel);
        }

        public void OnChannelClosed(DataChannel channel, StatusCode reason)
        {
            if (ReferenceEquals(m_channel, channel))
            {
                m_channel = null;
            }
        }

        public async Task<DataChannel> WaitForSourceAsync(CancellationToken ct)
        {
            using CancellationTokenRegistration registration = ct.Register(
                static state => ((TaskCompletionSource<DataChannel>)state!).TrySetCanceled(),
                m_opened);
            return await m_opened.Task.ConfigureAwait(false);
        }

        private DataChannel? m_channel;
        private readonly TaskCompletionSource<DataChannel> m_opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class DataChannelSampleServer : StandardServer
    {
        public DataChannelSampleServer(ITelemetryContext telemetry, TimeProvider timeProvider)
            : base(telemetry, timeProvider)
        {
            ServerStreamingState state = PendingState ?? throw new InvalidOperationException("No sample state was provided.");
            DataChannelSources.Register(state);
            DataChannelCapabilities = new DataChannelServerCapabilities
            {
                MaxDataChannels = 16,
                MaxFrameSize = 64 * 1024,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            };
            DataChannelTransport = new TcpServerDataChannelTransport(state);
        }

        public static ServerStreamingState? PendingState { get; set; }
    }

    internal sealed class TcpServerDataChannelTransport(ServerStreamingState state) : IServerDataChannelTransport
    {
        public bool TryGetManager(
            SecureChannelContext secureChannelContext,
            DataChannelServerCapabilities capabilities,
            ITelemetryContext telemetry,
            out DataChannelManager manager,
            out uint maxFrameSize,
            out bool isReliable)
        {
            if (!UaSCDataChannelSecureChannelRegistry.TryGet(
                secureChannelContext.SecureChannelId,
                out UaSCUaBinaryChannel? channel) ||
                channel == null)
            {
                manager = null!;
                maxFrameSize = 0;
                isReliable = true;
                return false;
            }

            manager = channel.EnableDataChannels(
                isServer: true,
                telemetry,
                capabilities.MaxDataChannels,
                capabilities.MaxCreditPerChannel);
            state.ServerManager = manager;
            maxFrameSize = capabilities.MaxFrameSize;
            isReliable = true;
            return true;
        }

        public ValueTask<ulong> AllocateServerStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
        }

        public ValueTask BindClientStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
        }

        public void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason)
        {
        }
    }

    internal sealed class ServerHarnessOwner(IHost host, ManagedSession session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await session.DisposeAsync().ConfigureAwait(false);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
        }
    }

    /// <summary>
    /// Carries frames between two managers in one process, encoding and decoding each one so the sample exercises the real codec.
    /// </summary>
    internal sealed class InProcessDataChannelTransport : IDataChannelTransport
    {
        public InProcessDataChannelTransport(BufferManager bufferManager, ITelemetryContext telemetry, bool quic)
        {
            BufferManager = bufferManager;
            TimeProvider = TimeProvider.System;
            FramingMode = quic ? DataChannelFramingMode.Quic : DataChannelFramingMode.Inline;
            HasTransportFlowControl = quic;
            m_telemetry = telemetry;
        }

        public DataChannelManager? Peer { get; set; }

        public DataChannelFramingMode FramingMode { get; }

        public int MaxFrameBodySize => 16384;

        public bool HasTransportFlowControl { get; }

        public BufferManager BufferManager { get; }

        public TimeProvider TimeProvider { get; }

        public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            byte[] encoded = new byte[frame.EncodedSize];
            DataChannelFrameCodec.Encode(encoded, frame);

            if (DataChannelFrameCodec.TryDecode(encoded, 0, out DataChannelFrame received, out _))
            {
                Peer?.HandleFrame(received);
            }

            return default;
        }

        public void OnProtocolFault(DataChannelFrameError error)
        {
            Console.Error.WriteLine($"protocol fault: {error}");
        }

        private readonly ITelemetryContext m_telemetry;
    }

    internal sealed class ConsoleTelemetry : TelemetryContextBase
    {
        public ConsoleTelemetry()
#pragma warning disable CA2000
            : base(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Warning)))
#pragma warning restore CA2000
        {
        }
    }
}
