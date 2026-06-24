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
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Application;

namespace Quickstarts.ConsoleReferencePubSub
{
    /// <summary>
    /// Unified OPC UA Part 14 PubSub reference sample built on the fluent
    /// <see cref="PubSubApplicationBuilder"/> + DI + .NET Generic Host surface.
    /// One executable exposes three command-line-selectable modes:
    /// <list type="bullet">
    /// <item><description>
    /// <c>publisher</c> - publishes sample DataSets over UDP/UADP or MQTT (UADP/JSON).
    /// </description></item>
    /// <item><description>
    /// <c>subscriber</c> - receives DataSets and logs each decoded message.
    /// </description></item>
    /// <item><description>
    /// <c>external</c> - bridges an external OPC UA server to PubSub through the
    /// <c>Opc.Ua.PubSub.Adapter</c> library (publisher / subscriber / responder).
    /// </description></item>
    /// </list>
    /// The build publishes as a NativeAOT-ready single-file executable.
    /// </summary>
    internal static class Program
    {
        private const string DefaultExternalEndpoint =
            "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

        public static async Task<int> Main(string[] args)
        {
            int exitCode = 0;

            var rootCommand = new RootCommand(
                "OPC UA Part 14 PubSub Reference sample. "
                + "Select a mode: publisher | subscriber | external.");

            rootCommand.Subcommands.Add(BuildPublisherCommand(code => exitCode = code));
            rootCommand.Subcommands.Add(BuildSubscriberCommand(code => exitCode = code));
            rootCommand.Subcommands.Add(BuildExternalCommand(code => exitCode = code));

            ParseResult parse = rootCommand.Parse(args);
            await parse.InvokeAsync().ConfigureAwait(false);
            return exitCode;
        }

        /// <summary>
        /// Builds the <c>publisher</c> subcommand: a UDP/UADP or MQTT publisher
        /// that publishes a built-in sample DataSet.
        /// </summary>
        private static Command BuildPublisherCommand(Action<int> setExitCode)
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
                    "Transport endpoint URL. Defaults: opc.udp://239.0.0.1:4840 (UDP), "
                    + "mqtt://localhost:1883 (MQTT)."
            };
            var intervalOption = new Option<int>("--interval")
            {
                Description = "Publishing interval in milliseconds.",
                DefaultValueFactory = _ => 1000
            };

            var command = new Command(
                "publisher",
                "Publish a sample DataSet over UDP/UADP or MQTT (UADP/JSON).")
            {
                profileOption,
                configFileOption,
                publisherIdOption,
                writerGroupIdOption,
                dataSetWriterIdOption,
                endpointOption,
                intervalOption
            };

            command.SetAction(async (parseResult, cancellationToken) =>
            {
                string? profileArg = parseResult.GetValue(profileOption);
                if (!TryParsePublisherProfile(profileArg, out PublisherProfile profile))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --profile value '{profileArg}'. "
                        + "Expected one of: udp-uadp, mqtt-uadp, mqtt-json.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                setExitCode(await RunPublisherAsync(
                    profile,
                    parseResult.GetValue(configFileOption),
                    parseResult.GetValue(publisherIdOption),
                    parseResult.GetValue(writerGroupIdOption),
                    parseResult.GetValue(dataSetWriterIdOption),
                    parseResult.GetValue(endpointOption),
                    parseResult.GetValue(intervalOption),
                    cancellationToken).ConfigureAwait(false));
            });

            return command;
        }

        /// <summary>
        /// Builds the <c>subscriber</c> subcommand: a UDP/UADP or MQTT subscriber
        /// that logs each decoded DataSetMessage.
        /// </summary>
        private static Command BuildSubscriberCommand(Action<int> setExitCode)
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

            var command = new Command(
                "subscriber",
                "Receive DataSets and log each decoded message.")
            {
                profileOption,
                configFileOption,
                publisherFilterOption,
                writerGroupFilterOption,
                dataSetWriterFilterOption,
                endpointOption
            };

            command.SetAction(async (parseResult, cancellationToken) =>
            {
                string? profileArg = parseResult.GetValue(profileOption);
                if (!TryParseSubscriberProfile(profileArg, out SubscriberProfile profile))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --profile value '{profileArg}'. "
                        + "Expected one of: udp-uadp, mqtt-uadp, mqtt-json.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                setExitCode(await RunSubscriberAsync(
                    profile,
                    parseResult.GetValue(configFileOption),
                    parseResult.GetValue(publisherFilterOption),
                    parseResult.GetValue(writerGroupFilterOption),
                    parseResult.GetValue(dataSetWriterFilterOption),
                    parseResult.GetValue(endpointOption),
                    cancellationToken).ConfigureAwait(false));
            });

            return command;
        }

        /// <summary>
        /// Builds the <c>external</c> subcommand: bridges an external OPC UA server
        /// to PubSub via the adapter library, in publisher / subscriber / responder
        /// direction.
        /// </summary>
        private static Command BuildExternalCommand(Action<int> setExitCode)
        {
            var directionOption = new Option<string>("--mode")
            {
                Description = "Adapter direction to run: publisher | subscriber | responder.",
                DefaultValueFactory = _ => "publisher"
            };
            var readModeOption = new Option<string>("--read-mode")
            {
                Description =
                    "Publisher source strategy: cyclic (Read each cycle) | "
                    + "subscription (client Subscription cache).",
                DefaultValueFactory = _ => "cyclic"
            };
            var affinityOption = new Option<string>("--affinity")
            {
                Description =
                    "Subscription grouping when --read-mode=subscription: "
                    + "writergroup | datasetwriter.",
                DefaultValueFactory = _ => "writergroup"
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description =
                    "External OPC UA server endpoint URL. Defaults to "
                    + "OPCUA_EXTERNAL_ENDPOINT or " + DefaultExternalEndpoint + "."
            };
            var pubSubEndpointOption = new Option<string>("--pubsub-endpoint")
            {
                Description = "UDP/UADP PubSub transport endpoint URL.",
                DefaultValueFactory = _ => ExternalServerPubSubConfiguration.DefaultPubSubEndpoint
            };

            var command = new Command(
                "external",
                "Bridge an external OPC UA server to PubSub (publisher | subscriber | responder).")
            {
                directionOption,
                readModeOption,
                affinityOption,
                endpointOption,
                pubSubEndpointOption
            };

            command.SetAction(async (parseResult, cancellationToken) =>
            {
                if (!TryParseBridgeMode(parseResult.GetValue(directionOption), out BridgeMode mode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --mode value '{parseResult.GetValue(directionOption)}'. "
                        + "Expected one of: publisher, subscriber, responder.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                if (!TryParseReadMode(parseResult.GetValue(readModeOption), out ReadMode readMode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --read-mode value '{parseResult.GetValue(readModeOption)}'. "
                        + "Expected one of: cyclic, subscription.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                if (!TryParseAffinity(
                    parseResult.GetValue(affinityOption), out SubscriptionAffinity affinity))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --affinity value '{parseResult.GetValue(affinityOption)}'. "
                        + "Expected one of: writergroup, datasetwriter.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }

                string externalEndpoint = parseResult.GetValue(endpointOption)
                    ?? Environment.GetEnvironmentVariable("OPCUA_EXTERNAL_ENDPOINT")
                    ?? DefaultExternalEndpoint;

                setExitCode(await RunExternalAsync(
                    mode,
                    readMode,
                    affinity,
                    externalEndpoint,
                    parseResult.GetValue(pubSubEndpointOption)
                        ?? ExternalServerPubSubConfiguration.DefaultPubSubEndpoint,
                    cancellationToken).ConfigureAwait(false));
            });

            return command;
        }

        private static async Task<int> RunPublisherAsync(
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

            builder.Services.AddOpcUa().AddPubSub(pubsub =>
            {
                IPubSubBuilder publisher = pubsub
                    .AddPublisher()
                    .AddUdpTransport()
                    .AddSecurityKeyProvider(SampleSecurity.CreateKeyProvider())
                    .AddDataSetSource(PublisherConfigurationBuilder.DataSetName, sampleSource);
                if (profile != PublisherProfile.UdpUadp)
                {
                    publisher.AddMqttTransport();
                }
                publisher.ConfigureApplication(app =>
                {
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:Publisher");
                    if (!string.IsNullOrEmpty(configFile))
                    {
                        app.UseConfigurationFile(configFile);
                    }
                    else
                    {
                        app.UseConfiguration(PublisherConfigurationBuilder.Build(
                            profile,
                            transportEndpoint,
                            publisherId,
                            writerGroupId,
                            dataSetWriterId,
                            intervalMs));
                    }
                });
            });

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferencePubSub.Publisher");
            logger.LogInformation(
                "Publisher starting: profile={Profile} endpoint={Endpoint} "
                + "interval={Interval}ms publisherId={PublisherId} writerGroup={WriterGroupId}",
                profile, transportEndpoint, intervalMs, publisherId, writerGroupId);
            logger.LogInformation("Publisher started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunSubscriberAsync(
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

            builder.Services.AddOpcUa().AddPubSub(pubsub =>
            {
                IPubSubBuilder subscriber = pubsub
                    .AddSubscriber()
                    .AddUdpTransport()
                    .AddSecurityKeyProvider(SampleSecurity.CreateKeyProvider())
                    .AddSubscribedDataSetSink(
                        SubscriberConfigurationBuilder.ReaderName,
                        sp => new ConsoleLoggingSink(
                            sp.GetRequiredService<ILoggerFactory>()
                                .CreateLogger<ConsoleLoggingSink>()));
                if (profile != SubscriberProfile.UdpUadp)
                {
                    subscriber.AddMqttTransport();
                }
                subscriber.ConfigureApplication(app =>
                {
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:Subscriber");
                    if (!string.IsNullOrEmpty(configFile))
                    {
                        app.UseConfigurationFile(configFile);
                    }
                    else
                    {
                        app.UseConfiguration(SubscriberConfigurationBuilder.Build(
                            profile,
                            transportEndpoint,
                            publisherIdFilter,
                            writerGroupIdFilter,
                            dataSetWriterIdFilter));
                    }
                });
            });

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferencePubSub.Subscriber");
            logger.LogInformation(
                "Subscriber starting: profile={Profile} endpoint={Endpoint} "
                + "publisherFilter={PublisherFilter} writerGroupFilter={WriterGroupFilter}",
                profile,
                transportEndpoint,
                publisherIdFilter,
                writerGroupIdFilter);
            logger.LogInformation("Subscriber started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunExternalAsync(
            BridgeMode mode,
            ReadMode readMode,
            SubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint,
            CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            switch (mode)
            {
                case BridgeMode.Publisher:
                    ConfigureExternalPublisher(builder, readMode, affinity, externalEndpoint, pubSubEndpoint);
                    break;
                case BridgeMode.Subscriber:
                    ConfigureExternalSubscriber(builder, externalEndpoint, pubSubEndpoint);
                    break;
                case BridgeMode.Responder:
                    ConfigureExternalResponder(builder, externalEndpoint, pubSubEndpoint);
                    break;
            }

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferencePubSub.External");
            logger.LogInformation(
                "External-server PubSub bridge starting: mode={Mode} readMode={ReadMode} "
                + "affinity={Affinity} externalServer={ExternalEndpoint} pubSub={PubSubEndpoint}",
                mode, readMode, affinity, externalEndpoint, pubSubEndpoint);
            logger.LogInformation("Bridge started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Wires the external PUBLISHER direction: a UDP/UADP publisher whose
        /// PublishedDataSet variables are sampled from an external OPC UA server.
        /// The PubSub configuration is supplied with <c>UseConfiguration</c>
        /// before <c>AddServerAsPublisher</c> so the adapter can enumerate
        /// the configured PublishedDataSets and attach an external read source.
        /// </summary>
        private static void ConfigureExternalPublisher(
            HostApplicationBuilder builder,
            ReadMode readMode,
            SubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddPublisher()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:ExternalPublisher"))
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildPublisherConfiguration(pubSubEndpoint))
                .AddServerAsPublisher(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    // The demo connects unsecured for zero-config interop. A
                    // production bridge must use SignAndEncrypt with a provisioned
                    // application instance certificate.
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                    options.ReadMode = readMode;
                    options.Affinity = affinity;
                }));
        }

        /// <summary>
        /// Wires the external SUBSCRIBER direction: a UDP/UADP subscriber whose
        /// received DataSet fields are written back to an external OPC UA server
        /// through the DataSetReader's TargetVariables.
        /// </summary>
        private static void ConfigureExternalSubscriber(
            HostApplicationBuilder builder,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:ExternalSubscriber"))
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildSubscriberConfiguration(pubSubEndpoint))
                .AddServerAsSubscriber(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                }));
        }

        /// <summary>
        /// Wires the external ACTION RESPONDER direction: an inbound PubSub Action
        /// is mapped to a method call on an external OPC UA server.
        /// </summary>
        private static void ConfigureExternalResponder(
            HostApplicationBuilder builder,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:ExternalResponder"))
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildSubscriberConfiguration(pubSubEndpoint))
                .AddServerAsActionResponder(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                    options.AllowUnsecured = true;
                    // Map the "ResetCounters" action to an external method call.
                    options.MethodMap.Add(
                        "ResetCounters",
                        NodeId.Parse("ns=2;s=Demo.External.Methods"),
                        NodeId.Parse("ns=2;s=Demo.External.ResetCounters"));
                    options.Targets.Add(new PubSubActionTarget
                    {
                        DataSetWriterId = 1,
                        ActionName = "ResetCounters"
                    });
                }));
        }

        private static bool TryParsePublisherProfile(string? text, out PublisherProfile profile)
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

        private static bool TryParseSubscriberProfile(string? text, out SubscriberProfile profile)
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

        private static bool TryParseBridgeMode(string? text, out BridgeMode mode)
        {
            switch (text)
            {
                case "publisher":
                    mode = BridgeMode.Publisher;
                    return true;
                case "subscriber":
                    mode = BridgeMode.Subscriber;
                    return true;
                case "responder":
                    mode = BridgeMode.Responder;
                    return true;
                default:
                    mode = BridgeMode.Publisher;
                    return false;
            }
        }

        private static bool TryParseReadMode(string? text, out ReadMode readMode)
        {
            switch (text)
            {
                case "cyclic":
                    readMode = ReadMode.Cyclic;
                    return true;
                case "subscription":
                    readMode = ReadMode.Subscription;
                    return true;
                default:
                    readMode = ReadMode.Cyclic;
                    return false;
            }
        }

        private static bool TryParseAffinity(string? text, out SubscriptionAffinity affinity)
        {
            switch (text)
            {
                case "writergroup":
                    affinity = SubscriptionAffinity.WriterGroup;
                    return true;
                case "datasetwriter":
                    affinity = SubscriptionAffinity.DataSetWriter;
                    return true;
                default:
                    affinity = SubscriptionAffinity.WriterGroup;
                    return false;
            }
        }
    }

    /// <summary>
    /// Publisher transport/message profile selected via <c>publisher --profile</c>.
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

    /// <summary>
    /// Subscriber transport/message profile selected via <c>subscriber --profile</c>.
    /// </summary>
    public enum SubscriberProfile
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

    /// <summary>
    /// The external-server adapter direction selected via <c>external --mode</c>.
    /// </summary>
    public enum BridgeMode
    {
        /// <summary>
        /// Read an external server and publish its data over PubSub.
        /// </summary>
        Publisher = 0,

        /// <summary>
        /// Receive PubSub data and write it back to an external server.
        /// </summary>
        Subscriber = 1,

        /// <summary>
        /// Map an inbound PubSub Action to an external server method call.
        /// </summary>
        Responder = 2
    }
}
