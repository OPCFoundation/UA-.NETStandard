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
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Transports;

namespace Quickstarts.ConsoleReferencePublisher
{
    /// <summary>
    /// OPC UA Part 14 PubSub reference publisher built on the fluent
    /// <see cref="PubSubApplicationBuilder"/> + DI + .NET Generic Host
    /// surface (Part 14 §9.1.2). Demonstrates how to compose a UDP/UADP
    /// or MQTT (UADP / JSON) publisher in <c>~150</c> LOC and publish
    /// the build as a NativeAOT-ready single-file executable.
    /// </summary>
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var profileOption = new Option<string>("--profile")
            {
                Description =
                    "Transport profile: udp-uadp | mqtt-uadp | mqtt-json.",
                DefaultValueFactory = _ => "udp-uadp"
            };
            var configFileOption = new Option<string?>("--config-file")
            {
                Description = "Optional path to a Part 14 XML PubSub configuration."
            };
            var publisherIdOption = new Option<ushort>("--publisher-id")
            {
                Description = "PublisherId published in every NetworkMessage header.",
                DefaultValueFactory = _ => (ushort)1
            };
            var writerGroupIdOption = new Option<ushort>("--writer-group-id")
            {
                Description = "WriterGroupId for the single sample WriterGroup.",
                DefaultValueFactory = _ => (ushort)100
            };
            var dataSetWriterIdOption = new Option<ushort>("--data-set-writer-id")
            {
                Description = "DataSetWriterId for the single sample writer.",
                DefaultValueFactory = _ => (ushort)1
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description =
                    "Transport endpoint URL. Defaults: opc.udp://239.0.0.1:4840 (UDP), " +
                    "mqtt://localhost:1883 (MQTT)."
            };
            var intervalOption = new Option<int>("--interval")
            {
                Description = "Publishing interval in milliseconds.",
                DefaultValueFactory = _ => 1000
            };

            var rootCommand = new RootCommand(
                "OPC UA Part 14 PubSub Reference Publisher")
            {
                profileOption,
                configFileOption,
                publisherIdOption,
                writerGroupIdOption,
                dataSetWriterIdOption,
                endpointOption,
                intervalOption
            };

            int exitCode = 0;
            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string? profileArg = parseResult.GetValue(profileOption);
                if (!TryParseProfile(profileArg, out PublisherProfile profile))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --profile value '{profileArg}'. " +
                        "Expected one of: udp-uadp, mqtt-uadp, mqtt-json.")
                        .ConfigureAwait(false);
                    exitCode = 2;
                    return;
                }
                exitCode = await RunAsync(
                    profile,
                    parseResult.GetValue(configFileOption),
                    parseResult.GetValue(publisherIdOption),
                    parseResult.GetValue(writerGroupIdOption),
                    parseResult.GetValue(dataSetWriterIdOption),
                    parseResult.GetValue(endpointOption),
                    parseResult.GetValue(intervalOption),
                    cancellationToken).ConfigureAwait(false);
            });

            ParseResult parse = rootCommand.Parse(args);
            await parse.InvokeAsync().ConfigureAwait(false);
            return exitCode;
        }

        private static bool TryParseProfile(string? text, out PublisherProfile profile)
        {
            switch (text)
            {
                case "udp-uadp":
                    profile = PublisherProfile.UdpUadp;
                    return true;
                case "mqtt-uadp":
                    profile = PublisherProfile.MqttUadp;
                    return true;
                case "mqtt-json":
                    profile = PublisherProfile.MqttJson;
                    return true;
                default:
                    profile = PublisherProfile.UdpUadp;
                    return false;
            }
        }

        private static async Task<int> RunAsync(
            PublisherProfile profile,
            string? configFile,
            ushort publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            string? endpoint,
            int intervalMs,
            CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            string transportEndpoint = endpoint
                ?? PublisherConfigurationBuilder.DefaultEndpointFor(profile);
            var sampleSource = new SampleDataSetSource();

            builder.Services.AddSingleton(sampleSource);

            // Register the IPubSubApplication BEFORE AddPubSubPublisher so
            // TryAddSingleton inside the DI extension skips its default
            // factory. This lets the sample wire a fluent
            // PubSubApplicationBuilder that pre-registers a custom
            // IPublishedDataSetSource for live demo data.
            builder.Services.AddSingleton<IPubSubApplication>(sp =>
            {
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                PubSubApplicationBuilder pb = new PubSubApplicationBuilder(telemetry)
                    .WithApplicationId("urn:opcfoundation:ConsoleReferencePublisher")
                    .UseAllStandardEncoders()
                    .AddSecurityKeyProvider(SampleSecurity.CreateKeyProvider())
                    .AddDataSetSource(
                        PublisherConfigurationBuilder.DataSetName,
                        sp.GetRequiredService<SampleDataSetSource>());
                foreach (IPubSubTransportFactory factory
                    in sp.GetServices<IPubSubTransportFactory>())
                {
                    pb.AddTransportFactory(factory);
                }
                if (!string.IsNullOrEmpty(configFile))
                {
                    pb.UseConfigurationFile(configFile);
                }
                else
                {
                    pb.UseConfiguration(PublisherConfigurationBuilder.Build(
                        profile,
                        transportEndpoint,
                        publisherId,
                        writerGroupId,
                        dataSetWriterId,
                        intervalMs));
                }
                return pb.Build();
            });

            IOpcUaBuilder ua = builder.Services.AddOpcUa()
                .AddPubSubPublisher()
                .AddUdpTransport();
            if (profile != PublisherProfile.UdpUadp)
            {
                ua.AddMqttTransport();
            }

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferencePublisher");
            logger.LogInformation(
                "Application starting: profile={Profile} endpoint={Endpoint} " +
                "interval={Interval}ms publisherId={PublisherId} writerGroup={WriterGroupId}",
                profile, transportEndpoint, intervalMs, publisherId, writerGroupId);
            logger.LogInformation("Publisher started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }

    /// <summary>
    /// Wire profile selected via <c>--profile</c>.
    /// </summary>
    public enum PublisherProfile
    {
        /// <summary>
        /// UDP transport with UADP message mapping.
        /// </summary>
        UdpUadp = 0,

        /// <summary>
        /// MQTT broker transport with UADP message mapping.
        /// </summary>
        MqttUadp = 1,

        /// <summary>
        /// MQTT broker transport with JSON message mapping.
        /// </summary>
        MqttJson = 2
    }
}
