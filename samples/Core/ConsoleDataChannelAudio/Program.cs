/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using ClientSession = Opc.Ua.Client.ISession;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Streams a looping melody from a Server to a Client over an OPC UA data
    /// channel, and plays it back.
    /// </summary>
    /// <remarks>
    /// The point of the sample is the shape of the problem rather than the
    /// audio: a continuous, latency-sensitive stream of opaque bytes that OPC
    /// UA has no other primitive for. Polling it with Read would be
    /// nonsensical, a Subscription carrying ByteString values would pay the
    /// encoding and Publish round trip for every packet, and PubSub would carry
    /// it beside the SecureChannel rather than on it. A data channel carries it
    /// on the SecureChannel that is already open, alongside the Service traffic
    /// and without disturbing it.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Runs the sample until Ctrl-C.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static async Task<int> Main(string[] args)
        {
            int frameMilliseconds = 20;

            if (args.Length > 0 &&
                (!int.TryParse(args[0], out frameMilliseconds) ||
                    frameMilliseconds is < 1 or > 1000))
            {
                Console.Error.WriteLine("Usage: ConsoleDataChannelAudio [frame-milliseconds]");
                return 1;
            }

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            try
            {
                await RunAsync(frameMilliseconds, stop.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        private static async Task RunAsync(int frameMilliseconds, CancellationToken ct)
        {
            using var source = new AudioStreamingSource(frameMilliseconds);
            AudioSampleServer.PendingSource = source;

            string endpointUrl = $"opc.tcp://localhost:{EndpointPort}/ConsoleDataChannelAudio";
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            ConfigureServer(builder, endpointUrl);
            ConfigureClient(builder, endpointUrl);

            using IHost host = builder.Build();
            await host.StartAsync(ct).ConfigureAwait(false);

            try
            {
                await StreamAsync(host, source, endpointUrl, frameMilliseconds, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                await source.StopAsync().ConfigureAwait(false);
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task StreamAsync(
            IHost host,
            AudioStreamingSource source,
            string endpointUrl,
            int frameMilliseconds,
            CancellationToken ct)
        {
            ClientSession session = await ConnectWithRetryAsync(host.Services, endpointUrl, ct)
                .ConfigureAwait(false);

            await using (session.ConfigureAwait(false))
            {
                var clientChannel = session.TransportChannel as UaSCUaBinaryTransportChannel
                    ?? throw new InvalidOperationException("The client transport is not UASC binary.");

                // Ready to receive before asking for the channel: the Server may
                // send as soon as the response is dispatched.
                DataChannelManager channels = clientChannel.SecureChannel!.EnableDataChannels(
                    isServer: false,
                    host.Services.GetRequiredService<ITelemetryContext>());

                OpenDataChannelResponse opened = await session.OpenDataChannelAsync(
                    null,
                    source.SourceNodeId,
                    0,
                    0,
                    new DataChannelParametersDataType
                    {
                        Direction = DataChannelDirection.SourceToSink,
                        DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                        ContentType = AudioFormat.ContentType,
                        MaxFrameSize = (uint)AudioFormat.BytesForDuration(frameMilliseconds),
                        InitialCredit = (uint)AudioFormat.BytesForDuration(frameMilliseconds * 32),
                        Priority = 1
                    },
                    ct).ConfigureAwait(false);

                DataChannel sink = channels.Register(
                    opened.ChannelId,
                    source.SourceNodeId,
                    DataChannelSettings.FromParameters(opened.RevisedParameters),
                    isSource: false,
                    opened.RevisedTransportChannelId);
                channels.MarkOpen(opened.ChannelId);

                string wavPath = Path.Combine(
                    Path.GetTempPath(),
                    "ConsoleDataChannelAudio.wav");

                using IAudioSink player = AudioSink.Create(wavPath);

                Console.WriteLine();
                Console.WriteLine("OPC UA data channel audio streaming");
                Console.WriteLine($"  endpoint     {endpointUrl}");
                Console.WriteLine($"  content type {AudioFormat.ContentType} " +
                    $"({AudioFormat.SampleRate} Hz, {AudioFormat.BitsPerSample}-bit, mono)");
                Console.WriteLine($"  frame        {frameMilliseconds} ms " +
                    $"({AudioFormat.BytesForDuration(frameMilliseconds)} bytes)");
                Console.WriteLine($"  loop         {source.LoopDuration.TotalSeconds:F1} s, repeating");
                Console.WriteLine($"  sink         {player.Description}");
                Console.WriteLine();
                Console.WriteLine("Press Ctrl-C to stop.");
                Console.WriteLine();

                await ReceiveAsync(sink, player, ct).ConfigureAwait(false);

                await session.CloseDataChannelAsync(
                    null,
                    opened.ChannelId,
                    StatusCodes.Good,
                    deleteQueued: true,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task ReceiveAsync(
            DataChannel sink,
            IAudioSink player,
            CancellationToken ct)
        {
            ulong frames = 0;
            ulong bytes = 0;
            DateTime nextReport = DateTime.UtcNow.AddSeconds(1);

            while (!ct.IsCancellationRequested)
            {
                using DataChannelMessage? message = await sink.ReadAsync(ct).ConfigureAwait(false);

                if (message == null)
                {
                    break;
                }

                player.Write(message.Payload.Span);
                frames++;
                bytes += (ulong)message.Payload.Length;

                if (DateTime.UtcNow >= nextReport)
                {
                    DataChannelDiagnosticsDataType diagnostics = sink.GetDiagnostics();
                    Console.Write(
                        $"\r  {frames} frames, {bytes / 1024} KiB, " +
                        $"credit stalls {diagnostics.CreditStalls}   ");

                    // The line carries no newline so it overwrites itself on a
                    // terminal; a redirected stream would otherwise hold it in
                    // the buffer and show nothing at all.
                    Console.Out.Flush();
                    nextReport = DateTime.UtcNow.AddSeconds(1);
                }
            }
        }

        private static void ConfigureServer(HostApplicationBuilder builder, string endpointUrl)
        {
            builder.Services.AddOpcUa()
                .AddServer<AudioSampleServer>(o =>
                {
                    const string applicationName = "ConsoleDataChannelAudioServer";
                    o.ApplicationName = applicationName;
                    o.ApplicationUri = "urn:localhost:OPCFoundation:ConsoleDataChannelAudioServer";
                    o.ProductUri = "uri:opcfoundation.org:ConsoleDataChannelAudioServer";
                    o.AutoAcceptUntrustedCertificates = true;
                    o.PkiRoot = PkiRoot(applicationName);
                    o.RejectSHA1Certificates = true;
                    o.MinCertificateKeySize = 2048;
                    o.EndpointUrls.Add(endpointUrl);
                });
        }

        private static void ConfigureClient(HostApplicationBuilder builder, string endpointUrl)
        {
            builder.Services.AddOpcUa()
                .AddClient(o =>
                {
                    const string applicationName = "ConsoleDataChannelAudioClient";
                    o.ApplicationName = applicationName;
                    o.ApplicationUri = "urn:localhost:OPCFoundation:ConsoleDataChannelAudioClient";
                    o.ProductUri = "uri:opcfoundation.org:ConsoleDataChannelAudioClient";
                    o.PkiRoot = PkiRoot(applicationName);
                    o.AutoAcceptUntrustedCertificates = true;
                    o.RejectSHA1SignedCertificates = true;
                    o.MinimumCertificateKeySize = 2048;
                })
                .AddDiscoveryAndConnect(o =>
                {
                    o.DiscoveryUrl = endpointUrl;
                    o.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    o.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                });
        }

        private static string PkiRoot(string applicationName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OPC Foundation",
                applicationName,
                "pki");
        }

        private static async Task<ClientSession> ConnectWithRetryAsync(
            IServiceProvider services,
            string endpointUrl,
            CancellationToken ct)
        {
            Exception? last = null;

            for (int ii = 0; ii < 20; ii++)
            {
                try
                {
                    return await ConnectAsync(services, endpointUrl, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("The in-process server did not become ready.", last);
        }

        private static async Task<ClientSession> ConnectAsync(
            IServiceProvider services,
            string endpointUrl,
            CancellationToken ct)
        {
            ApplicationConfiguration configuration = await services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                .GetAsync(ct)
                .ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await services
                .GetRequiredService<IOpcUaDiscoveryService>()
                .GetEndpointsAsync(endpointUrl, ct: ct)
                .ConfigureAwait(false);

            EndpointDescription? description = null;

            foreach (EndpointDescription candidate in endpoints)
            {
                if (candidate.TransportProfileUri == Profiles.UaTcpTransport &&
                    candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256)
                {
                    description = candidate;
                    break;
                }
            }

            if (description == null)
            {
                throw new InvalidOperationException(
                    "No SignAndEncrypt/Basic256Sha256 opc.tcp endpoint was returned.");
            }

            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(configuration));

            return await services.GetRequiredService<ISessionFactory>().CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                "ConsoleDataChannelAudio",
                60_000,
                new UserIdentity(),
                default,
                ct).ConfigureAwait(false);
        }

        private const int EndpointPort = 62560;
    }

    /// <summary>
    /// A Server that publishes the audio source and grants it to any activated
    /// Session on a signed and encrypted SecureChannel.
    /// </summary>
    internal sealed class AudioSampleServer : StandardServer
    {
        /// <summary>
        /// Creates the server.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Time provider.</param>
        public AudioSampleServer(ITelemetryContext telemetry, TimeProvider timeProvider)
            : base(telemetry, timeProvider)
        {
            AudioStreamingSource source = PendingSource
                ?? throw new InvalidOperationException("No audio source was provided.");

            DataChannelSources.Register(source);

            // Registering a source states that it exists, not that any given
            // user may read it. The default authorizer resolves the source in
            // the AddressSpace and applies its RolePermissions; a source kept
            // outside the AddressSpace, as this one is, has no such metadata and
            // would be denied.
            DataChannelAuthorizer = new AudioSourceAuthorizer(source.SourceNodeId);
            DataChannelCapabilities = new DataChannelServerCapabilities
            {
                MaxDataChannels = 4,
                MaxFrameSize = 64 * 1024,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            };
        }

        /// <summary>
        /// The source the next server instance publishes.
        /// </summary>
        public static AudioStreamingSource? PendingSource { get; set; }
    }

    /// <summary>
    /// Grants the one source this sample publishes.
    /// </summary>
    internal sealed class AudioSourceAuthorizer(NodeId sourceNodeId) : IDataChannelAuthorizer
    {
        /// <inheritdoc/>
        public ValueTask<bool> IsAuthorizedAsync(
            DataChannelRequestContext context,
            NodeId requestedSourceNodeId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            // Part 4 errata §7.2: a channel carrying payload towards the source
            // is a write, so only the outbound direction this sample publishes
            // is granted.
            bool authorized =
                requestedSourceNodeId == sourceNodeId &&
                direction == DataChannelDirection.SourceToSink &&
                context.IsSessionActivated &&
                context.SecurityMode == MessageSecurityMode.SignAndEncrypt;

            return new ValueTask<bool>(authorized);
        }
    }
}
