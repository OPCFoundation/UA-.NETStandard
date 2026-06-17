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
using Opc.Ua.PubSub.Transports;

namespace Quickstarts.ConsoleReferenceSubscriber
{
    /// <summary>
    /// OPC UA Part 14 PubSub reference subscriber built on the fluent
    /// <see cref="PubSubApplicationBuilder"/> + DI + .NET Generic Host
    /// surface (Part 14 §9.1.2). Logs each decoded DataSetMessage to
    /// the console via the registered
    /// <see cref="ConsoleLoggingSink"/>.
    /// </summary>
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var profileOption = new Option<string>("--profile")
            {
                Description = "Transport profile: udp-uadp | mqtt-uadp | mqtt-json.",
                DefaultValueFactory = _ => "udp-uadp"
            };
            var configFileOption = new Option<string?>("--config-file")
            {
                Description = "Optional path to a Part 14 XML PubSub configuration."
            };
            var publisherFilterOption = new Option<ushort>("--publisher-id-filter")
            {
                Description = "PublisherId filter applied by the reader.",
                DefaultValueFactory = _ => (ushort)1
            };
            var writerGroupFilterOption = new Option<ushort>("--writer-group-id-filter")
            {
                Description = "WriterGroupId filter applied by the reader.",
                DefaultValueFactory = _ => (ushort)100
            };
            var dataSetWriterFilterOption = new Option<ushort>("--data-set-writer-id-filter")
            {
                Description = "DataSetWriterId filter applied by the reader.",
                DefaultValueFactory = _ => (ushort)1
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description = "Transport endpoint URL. Defaults: opc.udp://239.0.0.1:4840 "
                    + "(UDP), mqtt://localhost:1883 (MQTT)."
            };

            var rootCommand = new RootCommand(
                "OPC UA Part 14 PubSub Reference Subscriber")
            {
                profileOption,
                configFileOption,
                publisherFilterOption,
                writerGroupFilterOption,
                dataSetWriterFilterOption,
                endpointOption
            };

            int exitCode = 0;
            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string? profileArg = parseResult.GetValue(profileOption);
                if (!TryParseProfile(profileArg, out SubscriberProfile profile))
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
                    parseResult.GetValue(publisherFilterOption),
                    parseResult.GetValue(writerGroupFilterOption),
                    parseResult.GetValue(dataSetWriterFilterOption),
                    parseResult.GetValue(endpointOption),
                    cancellationToken).ConfigureAwait(false);
            });

            ParseResult parse = rootCommand.Parse(args);
            await parse.InvokeAsync().ConfigureAwait(false);
            return exitCode;
        }

        private static bool TryParseProfile(string? text, out SubscriberProfile profile)
        {
            switch (text)
            {
                case "udp-uadp":
                    profile = SubscriberProfile.UdpUadp;
                    return true;
                case "mqtt-uadp":
                    profile = SubscriberProfile.MqttUadp;
                    return true;
                case "mqtt-json":
                    profile = SubscriberProfile.MqttJson;
                    return true;
                default:
                    profile = SubscriberProfile.UdpUadp;
                    return false;
            }
        }

        private static async Task<int> RunAsync(
            SubscriberProfile profile,
            string? configFile,
            ushort publisherIdFilter,
            ushort writerGroupIdFilter,
            ushort dataSetWriterIdFilter,
            string? endpoint,
            CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            string transportEndpoint = endpoint
                ?? SubscriberConfigurationBuilder.DefaultEndpointFor(profile);

            // Register the IPubSubApplication BEFORE AddPubSubSubscriber so
            // TryAddSingleton inside the DI extension skips its default
            // factory. This lets the sample wire a fluent
            // PubSubApplicationBuilder that pre-registers a console-logging
            // sink for the configured DataSetReader.
            builder.Services.AddSingleton<IPubSubApplication>(sp =>
            {
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                ILogger<ConsoleLoggingSink> sinkLogger = sp
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<ConsoleLoggingSink>();
                var sink = new ConsoleLoggingSink(sinkLogger);
                PubSubApplicationBuilder pb = new PubSubApplicationBuilder(telemetry)
                    .WithApplicationId("urn:opcfoundation:ConsoleReferenceSubscriber")
                    .UseAllStandardEncoders()
                    .AddSecurityKeyProvider(SampleSecurity.CreateKeyProvider())
                    .AddSubscribedDataSetSink(
                        SubscriberConfigurationBuilder.ReaderName, sink);
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
                    pb.UseConfiguration(SubscriberConfigurationBuilder.Build(
                        profile,
                        transportEndpoint,
                        publisherIdFilter,
                        writerGroupIdFilter,
                        dataSetWriterIdFilter));
                }
                return pb.Build();
            });

            IOpcUaBuilder ua = builder.Services.AddOpcUa()
                .AddPubSubSubscriber()
                .AddUdpTransport();
            if (profile != SubscriberProfile.UdpUadp)
            {
                ua.AddMqttTransport();
            }

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferenceSubscriber");
            logger.LogInformation(
                "Application starting: profile={Profile} endpoint={Endpoint} " +
                "publisherFilter={PublisherFilter} writerGroupFilter={WriterGroupFilter}",
                profile,
                transportEndpoint,
                publisherIdFilter,
                writerGroupIdFilter);
            logger.LogInformation("Subscriber started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }

    /// <summary>
    /// Wire profile selected via <c>--profile</c>.
    /// </summary>
    public enum SubscriberProfile
    {
        /// <summary>UDP transport with UADP message mapping.</summary>
        UdpUadp = 0,

        /// <summary>MQTT broker transport with UADP message mapping.</summary>
        MqttUadp = 1,

        /// <summary>MQTT broker transport with JSON message mapping.</summary>
        MqttJson = 2
    }
}
