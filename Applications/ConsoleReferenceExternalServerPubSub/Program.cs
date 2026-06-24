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

namespace Quickstarts.ConsoleReferenceExternalServerPubSub
{
    /// <summary>
    /// OPC UA Part 14 PubSub reference sample that bridges an <em>external</em>
    /// OPC UA server to PubSub through the <c>Opc.Ua.PubSub.Adapter</c> library.
    /// It demonstrates both directions of the adapter on the fluent + DI +
    /// .NET Generic Host surface:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>publisher</c> - reads nodes from an external server and publishes them
    /// over UDP/UADP, in either <see cref="ExternalReadMode.Cyclic"/> or
    /// <see cref="ExternalReadMode.Subscription"/> mode.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>subscriber</c> - receives PubSub DataSets and writes the values back to
    /// an external server's nodes.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>responder</c> - maps an inbound PubSub Action to an external server
    /// method call.
    /// </description>
    /// </item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The external endpoint defaults to the repository's ConsoleReferenceServer
    /// (<c>opc.tcp://localhost:62541/Quickstarts/ReferenceServer</c>) and can be
    /// pointed at any OPC UA server via <c>--endpoint</c> or the
    /// <c>OPCUA_EXTERNAL_ENDPOINT</c> environment variable. The sample builds and
    /// is AOT-publishable without a live server; it only contacts the server at
    /// run time.
    /// </remarks>
    internal static class Program
    {
        private const string DefaultExternalEndpoint =
            "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

        public static async Task<int> Main(string[] args)
        {
            var modeOption = new Option<string>("--mode")
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
                    + "OPCUA_EXTERNAL_ENDPOINT or "
                    + DefaultExternalEndpoint + "."
            };
            var pubSubEndpointOption = new Option<string>("--pubsub-endpoint")
            {
                Description = "UDP/UADP PubSub transport endpoint URL.",
                DefaultValueFactory = _ => ExternalServerPubSubConfiguration.DefaultPubSubEndpoint
            };

            var rootCommand = new RootCommand(
                "OPC UA Part 14 PubSub External Server Adapter Reference Sample")
            {
                modeOption,
                readModeOption,
                affinityOption,
                endpointOption,
                pubSubEndpointOption
            };

            int exitCode = 0;
            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                if (!TryParseMode(parseResult.GetValue(modeOption), out BridgeMode mode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --mode value '{parseResult.GetValue(modeOption)}'. "
                        + "Expected one of: publisher, subscriber, responder.")
                        .ConfigureAwait(false);
                    exitCode = 2;
                    return;
                }
                if (!TryParseReadMode(parseResult.GetValue(readModeOption), out ExternalReadMode readMode))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --read-mode value '{parseResult.GetValue(readModeOption)}'. "
                        + "Expected one of: cyclic, subscription.")
                        .ConfigureAwait(false);
                    exitCode = 2;
                    return;
                }
                if (!TryParseAffinity(
                    parseResult.GetValue(affinityOption), out ExternalSubscriptionAffinity affinity))
                {
                    await Console.Error.WriteLineAsync(
                        $"Unknown --affinity value '{parseResult.GetValue(affinityOption)}'. "
                        + "Expected one of: writergroup, datasetwriter.")
                        .ConfigureAwait(false);
                    exitCode = 2;
                    return;
                }

                string externalEndpoint = parseResult.GetValue(endpointOption)
                    ?? Environment.GetEnvironmentVariable("OPCUA_EXTERNAL_ENDPOINT")
                    ?? DefaultExternalEndpoint;

                exitCode = await RunAsync(
                    mode,
                    readMode,
                    affinity,
                    externalEndpoint,
                    parseResult.GetValue(pubSubEndpointOption)
                        ?? ExternalServerPubSubConfiguration.DefaultPubSubEndpoint,
                    cancellationToken).ConfigureAwait(false);
            });

            ParseResult parse = rootCommand.Parse(args);
            await parse.InvokeAsync().ConfigureAwait(false);
            return exitCode;
        }

        private static async Task<int> RunAsync(
            BridgeMode mode,
            ExternalReadMode readMode,
            ExternalSubscriptionAffinity affinity,
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
                    ConfigurePublisher(builder, readMode, affinity, externalEndpoint, pubSubEndpoint);
                    break;
                case BridgeMode.Subscriber:
                    ConfigureSubscriber(builder, externalEndpoint, pubSubEndpoint);
                    break;
                case BridgeMode.Responder:
                    ConfigureResponder(builder, externalEndpoint, pubSubEndpoint);
                    break;
            }

            IHost host = builder.Build();
            ILogger logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ConsoleReferenceExternalServerPubSub");
            logger.LogInformation(
                "External-server PubSub bridge starting: mode={Mode} readMode={ReadMode} "
                + "affinity={Affinity} externalServer={ExternalEndpoint} pubSub={PubSubEndpoint}",
                mode, readMode, affinity, externalEndpoint, pubSubEndpoint);
            logger.LogInformation("Bridge started. Press Ctrl-C to exit.");
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Wires the PUBLISHER direction: a UDP/UADP publisher whose
        /// PublishedDataSet variables are sampled from an external OPC UA server.
        /// The PubSub configuration is supplied with <c>UseConfiguration</c>
        /// <em>before</em> <c>AddExternalServerPublisher</c> so the adapter can
        /// enumerate the configured PublishedDataSets and attach an external read
        /// source to each.
        /// </summary>
        private static void ConfigurePublisher(
            HostApplicationBuilder builder,
            ExternalReadMode readMode,
            ExternalSubscriptionAffinity affinity,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddPublisher()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ExternalServerPubSubPublisher"))
                // Supply the PubSub configuration first ...
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildPublisherConfiguration(pubSubEndpoint))
                // ... then bind every PublishedDataSet to the external server.
                .AddExternalServerPublisher(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    // The demo connects unsecured for zero-config interop. A
                    // production bridge must use SignAndEncrypt with a provisioned
                    // application instance certificate.
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                    // Cyclic: issue a Read each publish cycle.
                    // Subscription: maintain a client Subscription cache.
                    options.ReadMode = readMode;
                    // Only consulted in Subscription mode: one Subscription per
                    // WriterGroup (cadence owner) or per DataSetWriter.
                    options.Affinity = affinity;
                }));
        }

        /// <summary>
        /// Wires the SUBSCRIBER direction: a UDP/UADP subscriber whose received
        /// DataSet fields are written back to an external OPC UA server through
        /// the DataSetReader's TargetVariables. The configuration is supplied
        /// before <c>AddExternalServerSubscriber</c> so the adapter can register
        /// one external write sink per DataSetReader.
        /// </summary>
        private static void ConfigureSubscriber(
            HostApplicationBuilder builder,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ExternalServerPubSubSubscriber"))
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildSubscriberConfiguration(pubSubEndpoint))
                .AddExternalServerSubscriber(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                }));
        }

        /// <summary>
        /// Wires the ACTION RESPONDER direction: an inbound PubSub Action is
        /// mapped to a method call on an external OPC UA server. The responder
        /// reuses the subscriber configuration so it can receive Action requests,
        /// and the action target is mapped to an external object/method through
        /// the responder's MethodMap.
        /// </summary>
        private static void ConfigureResponder(
            HostApplicationBuilder builder,
            string externalEndpoint,
            string pubSubEndpoint)
        {
            builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport()
                .ConfigureApplication(app =>
                    app.WithApplicationId("urn:opcfoundation:ExternalServerPubSubResponder"))
                .UseConfiguration(
                    ExternalServerPubSubConfiguration.BuildSubscriberConfiguration(pubSubEndpoint))
                .AddExternalServerActionResponder(options =>
                {
                    options.Connection.EndpointUrl = externalEndpoint;
                    options.Connection.SecurityMode = MessageSecurityMode.None;
                    options.AllowUnsecured = true;
                    // Map the "ResetCounters" action to an external method call.
                    options.MethodMap.Add(
                        "ResetCounters",
                        new NodeId("Demo.External.Methods", 2),
                        new NodeId("Demo.External.ResetCounters", 2));
                    options.Targets.Add(new PubSubActionTarget
                    {
                        DataSetWriterId = 1,
                        ActionName = "ResetCounters"
                    });
                }));
        }

        private static bool TryParseMode(string? text, out BridgeMode mode)
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

        private static bool TryParseReadMode(string? text, out ExternalReadMode readMode)
        {
            switch (text)
            {
                case "cyclic":
                    readMode = ExternalReadMode.Cyclic;
                    return true;
                case "subscription":
                    readMode = ExternalReadMode.Subscription;
                    return true;
                default:
                    readMode = ExternalReadMode.Cyclic;
                    return false;
            }
        }

        private static bool TryParseAffinity(string? text, out ExternalSubscriptionAffinity affinity)
        {
            switch (text)
            {
                case "writergroup":
                    affinity = ExternalSubscriptionAffinity.WriterGroup;
                    return true;
                case "datasetwriter":
                    affinity = ExternalSubscriptionAffinity.DataSetWriter;
                    return true;
                default:
                    affinity = ExternalSubscriptionAffinity.WriterGroup;
                    return false;
            }
        }
    }

    /// <summary>
    /// The adapter direction selected via <c>--mode</c>.
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
