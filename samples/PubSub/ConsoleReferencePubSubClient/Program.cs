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
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Samples;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// Unified OPC UA Part 14 PubSub reference sample built on the fluent
    /// <see cref="PubSubApplicationBuilder"/> + DI + .NET Generic Host surface.
    /// One executable exposes three command-line-selectable modes:
    /// <list type="bullet">
    /// <item><description>
    /// <c>publisher</c> - publishes sample DataSets over UDP/UADP, Ethernet/UADP, MQTT, or Kafka (UADP/JSON).
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

        private const string ExternalPublisherOptionsName = "ExternalPublisher";
        private const string ExternalSubscriberOptionsName = "ExternalSubscriber";
        private const string ExternalResponderOptionsName = "ExternalResponder";

        public static async Task<int> Main(string[] args)
        {
            int exitCode = 0;

            var rootCommand = new RootCommand(
                "OPC UA Part 14 PubSub Reference sample. " +
                "Select a mode: publisher | subscriber | external.");

            rootCommand.Subcommands.Add(BuildPublisherCommand(code => exitCode = code));
            rootCommand.Subcommands.Add(BuildSubscriberCommand(code => exitCode = code));
            rootCommand.Subcommands.Add(BuildExternalCommand(code => exitCode = code));

            // In System.CommandLine 2.0.11, help skips validation, but retains unmatched tokens.
            ParseResult parse = rootCommand.Parse(args);
            // The shared helper checks root options. This executable also owns typed subcommand options.
            foreach (Option option in parse.CommandResult.Command.Options)
            {
                if (option is not Option<bool>)
                {
                    continue;
                }
                foreach (string argument in args)
                {
                    int separator = argument.IndexOfAny(['=', ':']);
                    if (separator >= 0 &&
                        argument[..separator] == option.Name &&
                        !bool.TryParse(argument[(separator + 1)..], out _))
                    {
                        await Console.Error.WriteLineAsync(
                            $"{option.Name} expects true or false. Use --help for valid options.")
                            .ConfigureAwait(false);
                        return 1;
                    }
                }
            }
            if (parse.UnmatchedTokens.Count != 0)
            {
                await Console.Error.WriteLineAsync(
                    $"Unrecognized command or argument: {string.Join(", ", parse.UnmatchedTokens)}. " +
                    "Use --help for valid options; boolean values must be true or false.").ConfigureAwait(false);
                return 1;
            }
            int invocationExitCode = await SampleCommandLine.InvokeAsync(
                rootCommand, args, Console.Out, Console.Error).ConfigureAwait(false);
            return invocationExitCode != 0 ? invocationExitCode : exitCode;
        }

        /// <summary>
        /// Builds the <c>publisher</c> subcommand: a UDP/UADP, Ethernet/UADP, MQTT, or Kafka publisher
        /// that publishes a built-in sample DataSet.
        /// </summary>
        private static Command BuildPublisherCommand(Action<int> setExitCode)
        {
            var profileOption = new Option<string>("--profile")
            {
                Description =
                    "Transport profile: udp-uadp | eth-uadp | mqtt-uadp | mqtt-json | " +
                    "kafka-uadp | kafka-json.",
                DefaultValueFactory = _ => "udp-uadp"
            };
            var configFileOption = new Option<string?>("--config-file")
            {
                Description = "Optional path to a Part 14 XML PubSub configuration."
            };
            var publisherIdOption = new Option<ushort>("--publisher-id")
            {
                Description = "PublisherId published in every NetworkMessage header.",
                DefaultValueFactory = _ => 1
            };
            var writerGroupIdOption = new Option<ushort>("--writer-group-id")
            {
                Description = "WriterGroupId for the single sample WriterGroup.",
                DefaultValueFactory = _ => 100
            };
            var dataSetWriterIdOption = new Option<ushort>("--data-set-writer-id")
            {
                Description = "DataSetWriterId for the single sample writer.",
                DefaultValueFactory = _ => 1
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description =
                    "Transport endpoint URL. Defaults: opc.udp://239.0.0.1:4840 (UDP), " +
                    "opc.eth://01-00-5E-7F-00-01 (Ethernet), mqtt://localhost:1883 (MQTT), " +
                    "kafka://localhost:9092 (Kafka)."
            };
            var intervalOption = new Option<int>("--interval")
            {
                Description = "Publishing interval in milliseconds.",
                DefaultValueFactory = _ => 1000
            };

            var command = new Command(
                "publisher",
                "Publish a sample DataSet over UDP/UADP, Ethernet/UADP, MQTT, or Kafka (UADP/JSON).")
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
                        $"Unknown --profile value '{profileArg}'. " +
                        "Expected one of: udp-uadp, eth-uadp, mqtt-uadp, mqtt-json, kafka-uadp, kafka-json.")
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
        /// Builds the <c>subscriber</c> subcommand: a UDP/UADP, Ethernet/UADP, MQTT, or Kafka subscriber
        /// that logs each decoded DataSetMessage.
        /// </summary>
        private static Command BuildSubscriberCommand(Action<int> setExitCode)
        {
            var profileOption = new Option<string>("--profile")
            {
                Description =
                    "Transport profile: udp-uadp | eth-uadp | mqtt-uadp | mqtt-json | " +
                    "kafka-uadp | kafka-json.",
                DefaultValueFactory = _ => "udp-uadp"
            };
            var configFileOption = new Option<string?>("--config-file")
            {
                Description = "Optional path to a Part 14 XML PubSub configuration."
            };
            var publisherFilterOption = new Option<ushort>("--publisher-id-filter")
            {
                Description = "PublisherId filter applied by the reader.",
                DefaultValueFactory = _ => 1
            };
            var writerGroupFilterOption = new Option<ushort>("--writer-group-id-filter")
            {
                Description = "WriterGroupId filter applied by the reader.",
                DefaultValueFactory = _ => 100
            };
            var dataSetWriterFilterOption = new Option<ushort>("--data-set-writer-id-filter")
            {
                Description = "DataSetWriterId filter applied by the reader.",
                DefaultValueFactory = _ => 1
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description =
                    "Transport endpoint URL. Defaults: opc.udp://239.0.0.1:4840 (UDP), " +
                    "opc.eth://01-00-5E-7F-00-01 (Ethernet), mqtt://localhost:1883 (MQTT), " +
                    "kafka://localhost:9092 (Kafka)."
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
                        $"Unknown --profile value '{profileArg}'. " +
                        "Expected one of: udp-uadp, eth-uadp, mqtt-uadp, mqtt-json, kafka-uadp, kafka-json.")
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
                Description =
                    "Adapter directions to run, comma- or plus-separated: publisher | subscriber | responder.",
                DefaultValueFactory = _ => "publisher"
            };
            var readModeOption = new Option<string>("--read-mode")
            {
                Description =
                    "Publisher source strategy: cyclic (Read each cycle) | " +
                    "subscription (client Subscription cache).",
                DefaultValueFactory = _ => "cyclic"
            };
            var affinityOption = new Option<string>("--affinity")
            {
                Description =
                    "Subscription grouping when --read-mode=subscription: " +
                    "writergroup | datasetwriter.",
                DefaultValueFactory = _ => "writergroup"
            };
            var endpointOption = new Option<string?>("--endpoint")
            {
                Description =
                    "External OPC UA server endpoint URL. Defaults to " +
                    "OPCUA_EXTERNAL_ENDPOINT or " +
                    DefaultExternalEndpoint +
                    "."
            };
            var pubSubEndpointOption = new Option<string>("--pubsub-endpoint")
            {
                Description = "UDP/UADP PubSub transport endpoint URL.",
                DefaultValueFactory = _ => ExternalServerPubSubConfiguration.DefaultPubSubEndpoint
            };
            var hotReloadOption = new Option<bool>("--hot-reload")
            {
                Description =
                    "Enable the external bridge hot-reload demo using appsettings.json and pubsub-config.xml."
            };
            var validateOption = new Option<bool>("--validate-configuration")
            {
                Description = "Report effective adapter security options without connecting or starting PubSub."
            };
            var securityNoneOption = new Option<bool>("--security-none")
            {
                Description =
                    "Use unsigned, unencrypted external OPC UA connections (development only). " +
                    "Explicit false overrides configuration, including on reload.",
                Arity = ArgumentArity.ZeroOrOne
            };
            var unsecuredActionsOption = new Option<bool>("--allow-unsecured-actions")
            {
                Description =
                    "Accept unauthenticated PubSub actions (development only; independent of --security-none).",
                Arity = ArgumentArity.ZeroOrOne
            };
            var watchConfigurationOption = new Option<bool>("--watch-configuration")
            {
                Description =
                    "Keep reporting reloaded adapter options without connecting; " +
                    "requires --hot-reload and --validate-configuration."
            };

            var command = new Command(
                "external",
                "Bridge an external OPC UA server to PubSub (publisher | subscriber | responder).")
            {
                directionOption,
                readModeOption,
                affinityOption,
                endpointOption,
                pubSubEndpointOption,
                hotReloadOption,
                validateOption,
                securityNoneOption,
                unsecuredActionsOption,
                watchConfigurationOption
            };

            command.SetAction(async (parseResult, cancellationToken) =>
            {
                if (parseResult.GetValue(watchConfigurationOption) &&
                    (!parseResult.GetValue(hotReloadOption) || !parseResult.GetValue(validateOption)))
                {
                    await Console.Error.WriteLineAsync(
                        "--watch-configuration requires --hot-reload and --validate-configuration.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                if (!TryParseBridgeMode(parseResult.GetValue(directionOption), out BridgeMode mode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --mode value '{parseResult.GetValue(directionOption)}'. " +
                        "Expected one or more of: publisher, subscriber, responder.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                if (!TryParseReadMode(parseResult.GetValue(readModeOption), out ReadMode readMode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --read-mode value '{parseResult.GetValue(readModeOption)}'. " +
                        "Expected one of: cyclic, subscription.")
                        .ConfigureAwait(false);
                    setExitCode(2);
                    return;
                }
                if (!TryParseAffinity(
                    parseResult.GetValue(affinityOption), out SubscriptionAffinity affinity))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --affinity value '{parseResult.GetValue(affinityOption)}'. " +
                        "Expected one of: writergroup, datasetwriter.")
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
                    parseResult.GetValue(hotReloadOption),
                    parseResult.GetValue(validateOption),
                    parseResult.GetResult(securityNoneOption) is { Implicit: false }
                        ? parseResult.GetValue(securityNoneOption) : null,
                    parseResult.GetResult(unsecuredActionsOption) is { Implicit: false }
                        ? parseResult.GetValue(unsecuredActionsOption) : null,
                    parseResult.GetValue(watchConfigurationOption),
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
                switch (profile)
                {
                    case PublisherProfile.UdpUadp:
                        break;
                    case PublisherProfile.EthUadp:
                        publisher.AddEthTransport();
                        break;
                    case PublisherProfile.MqttUadp:
                    case PublisherProfile.MqttJson:
                        publisher.AddMqttTransport();
                        break;
                    case PublisherProfile.KafkaUadp:
                    case PublisherProfile.KafkaJson:
                        publisher.AddKafkaTransport();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(profile), profile, null);
                }
                publisher.ConfigureApplication(app =>
                {
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSubClient:Publisher");
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
                .CreateLogger("ConsoleReferencePubSubClient.Publisher");
            logger.PublisherStarting(profile, transportEndpoint, intervalMs, publisherId, writerGroupId);
            logger.PublisherStarted();
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
                switch (profile)
                {
                    case SubscriberProfile.UdpUadp:
                        break;
                    case SubscriberProfile.EthUadp:
                        subscriber.AddEthTransport();
                        break;
                    case SubscriberProfile.MqttUadp:
                    case SubscriberProfile.MqttJson:
                        subscriber.AddMqttTransport();
                        break;
                    case SubscriberProfile.KafkaUadp:
                    case SubscriberProfile.KafkaJson:
                        subscriber.AddKafkaTransport();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(profile), profile, null);
                }
                subscriber.ConfigureApplication(app =>
                {
                    app.WithApplicationId("urn:opcfoundation:ConsoleReferencePubSubClient:Subscriber");
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
                .CreateLogger("ConsoleReferencePubSubClient.Subscriber");
            logger.SubscriberStarting(profile, transportEndpoint, publisherIdFilter, writerGroupIdFilter);
            logger.SubscriberStarted();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunExternalAsync(
            BridgeMode mode,
            ReadMode readMode,
            SubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint,
            bool hotReload,
            bool validateConfiguration,
            bool? securityNone,
            bool? unsecuredActions,
            bool watchConfiguration,
            CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = hotReload
                ? Host.CreateApplicationBuilder(
                    new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory })
                : Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            var overrides = new Dictionary<string, string?>();
            if (securityNone.HasValue)
            {
                overrides[ExternalBridgeHostPolicy.SecurityNoneKey] = securityNone.Value.ToString();
            }
            if (unsecuredActions.HasValue)
            {
                overrides[ExternalBridgeHostPolicy.UnsecuredActionsKey] = unsecuredActions.Value.ToString();
            }
            builder.Configuration.AddInMemoryCollection(overrides);
            builder.Services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "ConsoleReferencePubSubClient";
                options.ApplicationUri = "urn:localhost:OPCFoundation:ConsoleReferencePubSubClient";
                options.ProductUri = "urn:opcfoundation.org:ConsoleReferencePubSubClient";
                options.PkiRoot = Path.Combine(
                    AppContext.BaseDirectory, "pki", "Opc.Ua.PubSub.Adapter");
                options.AutoAcceptUntrustedCertificates = false;
                options.RejectSHA1SignedCertificates = true;
                options.MinimumCertificateKeySize = 2048;
            });
            builder.Services.AddSingleton(sp => new ExternalBridgeHostPolicy(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ITelemetryContext>(),
                sp.GetRequiredService<IOpcUaApplicationConfigurationProvider>()));
            builder.Services.AddOptions<ServerPublisherOptions>(
                hotReload ? ExternalPublisherOptionsName : Options.DefaultName)
                .PostConfigure<ExternalBridgeHostPolicy>((options, policy) => policy.Apply(options.Connection));
            builder.Services.AddOptions<ServerSubscriberOptions>(
                hotReload ? ExternalSubscriberOptionsName : Options.DefaultName)
                .PostConfigure<ExternalBridgeHostPolicy>((options, policy) => policy.Apply(options.Connection));
            builder.Services.AddOptions<ServerActionResponderOptions>(
                hotReload ? ExternalResponderOptionsName : Options.DefaultName)
                .PostConfigure<ExternalBridgeHostPolicy>((options, policy) => policy.Apply(options));

            string? configFile = null;
            XmlPubSubConfigurationStore? hotReloadStore = null;
            string appSettingsFile = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (hotReload)
            {
                (configFile, hotReloadStore) = await ConfigureExternalBridgeHotReloadAsync(
                    builder,
                    mode,
                    pubSubEndpoint,
                    validateConfiguration,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ConfigureExternalBridge(builder, mode, readMode, affinity, externalEndpoint, pubSubEndpoint);
            }

            using XmlPubSubConfigurationStore? ownedStore = hotReloadStore;
            using IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferencePubSubClient.External");
            if (validateConfiguration)
            {
                ReportExternalBridgeOptions(host.Services, logger, mode, hotReload);
                if (watchConfiguration)
                {
                    await WatchExternalBridgeOptionsAsync(host.Services, logger, mode, cancellationToken)
                        .ConfigureAwait(false);
                }
                return 0;
            }
            logger.ExternalServerPubSubBridgeStarting(mode, readMode, affinity, externalEndpoint, pubSubEndpoint);
            if (hotReload)
            {
                logger.HotReloadEnabled(appSettingsFile, ExternalPublisherOptionsName, configFile);
            }
            logger.BridgeStarted();
            await host.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static void ReportExternalBridgeOptions(
            IServiceProvider services,
            ILogger logger,
            BridgeMode modes,
            bool hotReload)
        {
            if (modes.HasFlag(BridgeMode.Publisher))
            {
                ServerPublisherOptions options = services
                    .GetRequiredService<IOptionsMonitor<ServerPublisherOptions>>()
                    .Get(hotReload ? ExternalPublisherOptionsName : Options.DefaultName);
                logger.BridgeSecurityOptions(
                    "Publisher", options.Connection.SecurityMode, false,
                    options.Connection.ApplicationConfiguration?.SecurityConfiguration.AutoAcceptUntrustedCertificates);
            }
            if (modes.HasFlag(BridgeMode.Subscriber))
            {
                ServerSubscriberOptions options = services
                    .GetRequiredService<IOptionsMonitor<ServerSubscriberOptions>>()
                    .Get(hotReload ? ExternalSubscriberOptionsName : Options.DefaultName);
                logger.BridgeSecurityOptions(
                    "Subscriber", options.Connection.SecurityMode, false,
                    options.Connection.ApplicationConfiguration?.SecurityConfiguration.AutoAcceptUntrustedCertificates);
            }
            if (modes.HasFlag(BridgeMode.Responder))
            {
                ServerActionResponderOptions options = services
                    .GetRequiredService<IOptionsMonitor<ServerActionResponderOptions>>()
                    .Get(hotReload ? ExternalResponderOptionsName : Options.DefaultName);
                logger.BridgeSecurityOptions(
                    "Responder", options.Connection.SecurityMode, options.AllowUnsecured,
                    options.Connection.ApplicationConfiguration?.SecurityConfiguration.AutoAcceptUntrustedCertificates);
            }
        }

        private static async Task WatchExternalBridgeOptionsAsync(
            IServiceProvider services,
            ILogger logger,
            BridgeMode modes,
            CancellationToken cancellationToken)
        {
            using IDisposable? publisher = modes.HasFlag(BridgeMode.Publisher)
                ? services.GetRequiredService<IOptionsMonitor<ServerPublisherOptions>>().OnChange(
                    (options, _) => logger.BridgeSecurityOptions(
                        "Publisher", options.Connection.SecurityMode, false,
                        options.Connection.ApplicationConfiguration?.SecurityConfiguration
                            .AutoAcceptUntrustedCertificates))
                : null;
            using IDisposable? subscriber = modes.HasFlag(BridgeMode.Subscriber)
                ? services.GetRequiredService<IOptionsMonitor<ServerSubscriberOptions>>().OnChange(
                    (options, _) => logger.BridgeSecurityOptions(
                        "Subscriber", options.Connection.SecurityMode, false,
                        options.Connection.ApplicationConfiguration?.SecurityConfiguration
                            .AutoAcceptUntrustedCertificates))
                : null;
            using IDisposable? responder = modes.HasFlag(BridgeMode.Responder)
                ? services.GetRequiredService<IOptionsMonitor<ServerActionResponderOptions>>().OnChange(
                    (options, _) => logger.BridgeSecurityOptions(
                        "Responder", options.Connection.SecurityMode, options.AllowUnsecured,
                        options.Connection.ApplicationConfiguration?.SecurityConfiguration
                            .AutoAcceptUntrustedCertificates))
                : null;
            logger.WatchingAdapterConfiguration();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal termination of the configuration-only watcher.
            }
        }

        /// <summary>
        /// Wires the selected external bridge directions on one UDP/UADP PubSub
        /// application and one host.
        /// </summary>
        private static void ConfigureExternalBridge(
            HostApplicationBuilder builder,
            BridgeMode modes,
            ReadMode readMode,
            SubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub =>
            {
                IPubSubBuilder bridge = pubsub;
                if (modes.HasFlag(BridgeMode.Publisher))
                {
                    bridge = bridge.AddPublisher();
                }
                if (modes.HasFlag(BridgeMode.Subscriber) || modes.HasFlag(BridgeMode.Responder))
                {
                    bridge = bridge.AddSubscriber();
                }

                bridge = bridge
                    .AddUdpTransport()
                    .ConfigureApplication(app => app.WithApplicationId(
                        "urn:opcfoundation:ConsoleReferencePubSubClient:ExternalBridge"))
                    .UseConfiguration(
                        ExternalServerPubSubConfiguration.BuildConfiguration(modes, pubSubEndpoint));

                if (modes.HasFlag(BridgeMode.Publisher))
                {
                    bridge = bridge.AddServerAsPublisher(options =>
                    {
                        options.Connection.EndpointUrl = externalEndpoint;
                        options.ReadMode = readMode;
                        options.Affinity = affinity;
                    });
                }
                if (modes.HasFlag(BridgeMode.Subscriber))
                {
                    bridge = bridge.AddServerAsSubscriber(options =>
                    {
                        options.Connection.EndpointUrl = externalEndpoint;
                    });
                }
                if (modes.HasFlag(BridgeMode.Responder))
                {
                    bridge.AddServerAsActionResponder(options =>
                    {
                        options.Connection.EndpointUrl = externalEndpoint;
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
                    });
                }
            });
        }

        private static async Task<(string ConfigFile, XmlPubSubConfigurationStore? Store)>
            ConfigureExternalBridgeHotReloadAsync(
            HostApplicationBuilder builder,
            BridgeMode modes,
            string pubSubEndpoint,
            bool validateConfiguration,
            CancellationToken cancellationToken)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(logging => logging.AddConsole());
            string configFile = Path.Combine(AppContext.BaseDirectory, "pubsub-config.xml");
            XmlPubSubConfigurationStore? store = validateConfiguration
                ? null
                : new XmlPubSubConfigurationStore(configFile, telemetry, watchForChanges: true);
            bool configured = false;
            try
            {
                if (store is not null)
                {
                    await store.SaveAsync(
                        ExternalServerPubSubConfiguration.BuildConfiguration(modes, pubSubEndpoint),
                        cancellationToken).ConfigureAwait(false);
                }

                builder.Services.AddOpcUa().AddPubSub(pubsub =>
                {
                    IPubSubBuilder bridge = pubsub;
                    if (modes.HasFlag(BridgeMode.Publisher))
                    {
                        bridge = bridge.AddPublisher();
                    }
                    if (modes.HasFlag(BridgeMode.Subscriber) || modes.HasFlag(BridgeMode.Responder))
                    {
                        bridge = bridge.AddSubscriber();
                    }

                    bridge = bridge
                        .AddUdpTransport()
                        .ConfigureApplication(app => app.WithApplicationId(
                            "urn:opcfoundation:ConsoleReferencePubSubClient:ExternalBridge"));
                    // Validation uses the same options pipeline without writing or watching an XML file.
                    bridge = store is null
                        ? bridge.UseConfiguration(
                            ExternalServerPubSubConfiguration.BuildConfiguration(modes, pubSubEndpoint))
                        : bridge.WithConfigurationStore(store);

                    if (modes.HasFlag(BridgeMode.Publisher))
                    {
                        bridge = bridge.AddServerAsPublisher(
                            ExternalPublisherOptionsName,
                            builder.Configuration.GetSection(ExternalPublisherOptionsName));
                    }
                    if (modes.HasFlag(BridgeMode.Subscriber))
                    {
                        bridge = bridge.AddServerAsSubscriber(
                            ExternalSubscriberOptionsName,
                            builder.Configuration.GetSection(ExternalSubscriberOptionsName));
                    }
                    if (modes.HasFlag(BridgeMode.Responder))
                    {
                        bridge.AddServerAsActionResponder(
                            ExternalResponderOptionsName,
                            builder.Configuration.GetSection(ExternalResponderOptionsName));
                    }
                });

                if (modes.HasFlag(BridgeMode.Responder))
                {
                    builder.Services.Configure<ServerActionResponderOptions>(
                        ExternalResponderOptionsName,
                        options =>
                        {
                            options.MethodMap.Add(
                                "ResetCounters",
                                NodeId.Parse("ns=2;s=Demo.External.Methods"),
                                NodeId.Parse("ns=2;s=Demo.External.ResetCounters"));
                            options.Targets.Add(new PubSubActionTarget
                            {
                                DataSetWriterId = 1,
                                ActionName = "ResetCounters"
                            });
                        });
                }

                configured = true;
                return (configFile, store);
            }
            finally
            {
                if (!configured)
                {
                    store?.Dispose();
                }
            }
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
                case "eth-uadp":
                    profile = PublisherProfile.EthUadp;
                    return true;
                case "kafka-uadp":
                    profile = PublisherProfile.KafkaUadp;
                    return true;
                case "kafka-json":
                    profile = PublisherProfile.KafkaJson;
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
                case "eth-uadp":
                    profile = SubscriberProfile.EthUadp;
                    return true;
                case "kafka-uadp":
                    profile = SubscriberProfile.KafkaUadp;
                    return true;
                case "kafka-json":
                    profile = SubscriberProfile.KafkaJson;
                    return true;
                default:
                    profile = SubscriberProfile.UdpUadp;
                    return false;
            }
        }

        private static bool TryParseBridgeMode(string? text, out BridgeMode mode)
        {
            mode = BridgeMode.None;
            if (string.IsNullOrWhiteSpace(text))
            {
                mode = BridgeMode.Publisher;
                return true;
            }

            string[] tokens = text.Split(
                [',', '+'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                mode = BridgeMode.Publisher;
                return true;
            }

            foreach (string token in tokens)
            {
                switch (token)
                {
                    case "publisher":
                        mode |= BridgeMode.Publisher;
                        break;
                    case "subscriber":
                        mode |= BridgeMode.Subscriber;
                        break;
                    case "responder":
                        mode |= BridgeMode.Responder;
                        break;
                    default:
                        mode = BridgeMode.Publisher;
                        return false;
                }
            }

            return mode != BridgeMode.None;
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
    /// Sample-host consent applied after binding each adapter options snapshot.
    /// Library defaults and connection credentials are left unchanged.
    /// </summary>
    internal sealed class ExternalBridgeHostPolicy
    {
        public ExternalBridgeHostPolicy(
            IConfiguration configuration,
            ITelemetryContext telemetry,
            IOpcUaApplicationConfigurationProvider applicationConfiguration)
        {
            m_configuration = configuration;
            m_logger = telemetry.CreateLogger<ExternalBridgeHostPolicy>();
            m_applicationConfiguration = applicationConfiguration;
        }

        internal void Apply(ServerConnectionOptions connection)
        {
            // Avoid the adapter's convenience configuration, which auto-accepts untrusted peers.
            connection.ApplicationConfiguration ??= m_applicationConfiguration.Configuration;
            connection.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
            bool securityNone = m_configuration.GetValue<bool>(SecurityNoneKey);
            if (securityNone)
            {
                m_logger.UnsecuredExternalConnection();
                connection.SecurityMode = MessageSecurityMode.None;
                connection.SecurityPolicyUri = SecurityPolicies.None;
            }
            else
            {
                connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                if (connection.SecurityPolicyUri == SecurityPolicies.None)
                {
                    connection.SecurityPolicyUri = null;
                }
            }
        }

        internal void Apply(ServerActionResponderOptions options)
        {
            Apply(options.Connection);
            bool allowUnsecured = m_configuration.GetValue<bool>(UnsecuredActionsKey);
            if (allowUnsecured)
            {
                m_logger.UnsecuredPubSubActions();
            }
            options.AllowUnsecured = allowUnsecured;
        }

        internal const string SecurityNoneKey = "ExternalBridge:UseSecurityNone";
        internal const string UnsecuredActionsKey = "ExternalBridge:AllowUnsecuredActions";

        private readonly IConfiguration m_configuration;
        private readonly ILogger m_logger;
        private readonly IOpcUaApplicationConfigurationProvider m_applicationConfiguration;
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
        MqttJson = 2,

        /// <summary>
        /// Ethernet (Layer 2) transport with UADP message mapping.
        /// </summary>
        EthUadp = 3,

        /// <summary>
        /// Kafka broker transport with UADP message mapping.
        /// </summary>
        KafkaUadp = 4,

        /// <summary>
        /// Kafka broker transport with JSON message mapping.
        /// </summary>
        KafkaJson = 5
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
        MqttJson = 2,

        /// <summary>
        /// Ethernet (Layer 2) transport with UADP message mapping.
        /// </summary>
        EthUadp = 3,

        /// <summary>
        /// Kafka broker transport with UADP message mapping.
        /// </summary>
        KafkaUadp = 4,

        /// <summary>
        /// Kafka broker transport with JSON message mapping.
        /// </summary>
        KafkaJson = 5
    }

    /// <summary>
    /// The external-server adapter direction selected via <c>external --mode</c>.
    /// </summary>
    [Flags]
    public enum BridgeMode
    {
        /// <summary>
        /// No external bridge direction selected.
        /// </summary>
        None = 0,

        /// <summary>
        /// Read an external server and publish its data over PubSub.
        /// </summary>
        Publisher = 1,

        /// <summary>
        /// Receive PubSub data and write it back to an external server.
        /// </summary>
        Subscriber = 2,

        /// <summary>
        /// Map an inbound PubSub Action to an external server method call.
        /// </summary>
        Responder = 4
    }

    internal static partial class ProgramLog
    {
        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 0, Level = LogLevel.Information,
            Message = "Publisher starting: profile={Profile} endpoint={Endpoint} interval={Interval}ms " +
                "publisherId={PublisherId} writerGroup={WriterGroupId}")]
        public static partial void PublisherStarting(
            this ILogger logger,
            PublisherProfile profile,
            string endpoint,
            int interval,
            ushort publisherId,
            ushort writerGroupId);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 1, Level = LogLevel.Information,
            Message = "Publisher started. Press Ctrl-C to exit.")]
        public static partial void PublisherStarted(this ILogger logger);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 2, Level = LogLevel.Information,
            Message = "Subscriber starting: profile={Profile} endpoint={Endpoint} " +
                "publisherFilter={PublisherFilter} writerGroupFilter={WriterGroupFilter}")]
        public static partial void SubscriberStarting(
            this ILogger logger,
            SubscriberProfile profile,
            string endpoint,
            ushort publisherFilter,
            ushort writerGroupFilter);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 3, Level = LogLevel.Information,
            Message = "Subscriber started. Press Ctrl-C to exit.")]
        public static partial void SubscriberStarted(this ILogger logger);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 4, Level = LogLevel.Information,
            Message = "External-server PubSub bridge starting: mode={Mode} readMode={ReadMode} " +
                "affinity={Affinity} externalServer={ExternalEndpoint} pubSub={PubSubEndpoint}")]
        public static partial void ExternalServerPubSubBridgeStarting(
            this ILogger logger,
            BridgeMode mode,
            ReadMode readMode,
            SubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 5, Level = LogLevel.Information,
            Message = "Hot reload enabled. Edit {AppSettingsFile} (for example, change " +
                "{PublisherOptionsName}:ReadMode to Subscription) or {ConfigFile} (for example, add or remove " +
                "a DataSetWriter) and save to reconfigure the running bridge.")]
        public static partial void HotReloadEnabled(
            this ILogger logger,
            string appSettingsFile,
            string publisherOptionsName,
            string? configFile);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 6, Level = LogLevel.Information,
            Message = "Bridge started. Press Ctrl-C to exit.")]
        public static partial void BridgeStarted(this ILogger logger);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 7, Level = LogLevel.Information,
            Message = "{Direction}: SecurityMode={SecurityMode}; AllowUnsecuredActions={AllowUnsecuredActions}; " +
                "AutoAcceptUntrustedCertificates={AutoAcceptUntrustedCertificates}")]
        public static partial void BridgeSecurityOptions(
            this ILogger logger,
            string direction,
            MessageSecurityMode securityMode,
            bool allowUnsecuredActions,
            bool? autoAcceptUntrustedCertificates);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 8, Level = LogLevel.Warning,
            Message = "DEVELOPMENT ONLY: --security-none / ExternalBridge:UseSecurityNone enables " +
                "external OPC UA messages that are not signed or encrypted. " +
                "Use --security-none=false and provision trusted certificates outside an isolated lab.")]
        public static partial void UnsecuredExternalConnection(this ILogger logger);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 9, Level = LogLevel.Warning,
            Message = "DEVELOPMENT ONLY: --allow-unsecured-actions / ExternalBridge:AllowUnsecuredActions " +
                "accepts unauthenticated PubSub actions that can invoke external server methods. " +
                "Use --allow-unsecured-actions=false outside an isolated lab.")]
        public static partial void UnsecuredPubSubActions(this ILogger logger);

        [LoggerMessage(EventId = ConsoleReferencePubSubClientEventIds.Program + 10, Level = LogLevel.Information,
            Message = "Watching adapter configuration without starting PubSub or connecting. Press Ctrl-C to exit.")]
        public static partial void WatchingAdapterConfiguration(this ILogger logger);
    }
}
