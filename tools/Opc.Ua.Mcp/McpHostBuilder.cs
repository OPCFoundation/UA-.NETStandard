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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Pcap;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Builds the dependency-injection graph and MCP tool registrations shared
    /// by the stdio and HTTP/SSE entry points in <c>Program.cs</c>. Extracted
    /// from the top-level statements program so the startup wiring can be
    /// unit tested directly instead of only being exercised end-to-end by
    /// launching the whole host process.
    /// </summary>
    internal static class McpHostBuilder
    {
        private const string kApplicationName = "OPC UA MCP Server";
        private const string kApplicationUri = "urn:localhost:UA:McpServer";
        private const string kProductUri = "uri:opcfoundation.org:McpServer";

        /// <summary>
        /// Registers the OPC UA client, session/PubSub managers and Pcap
        /// diagnostics services used by the MCP tools.
        /// </summary>
        public static void ConfigureServices(
            IServiceCollection services,
            PcapOptions pcapOptions,
            McpServerOptions? mcpServerOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(pcapOptions);

            services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = kApplicationName;
                options.ApplicationUri = kApplicationUri;
                options.ProductUri = kProductUri;
            });
            services.AddSingleton<OpcUaSessionManager>();
            services.AddSingleton<PubSubRuntimeManager>();
            services.AddSingleton(mcpServerOptions ?? CreateMcpServerOptions());
            services.AddPcap(options =>
            {
                options.BaseFolder = pcapOptions.BaseFolder;
                options.MaxActiveSessions = pcapOptions.MaxActiveSessions;
                options.EnableDiagnosticsTools = pcapOptions.EnableDiagnosticsTools;
            });
            services.AddPcapFormatters();
            services.AddPcapReplay();
            services.AddPubSubPcap();
        }

        /// <summary>
        /// Creates the <see cref="McpServerOptions"/> from the well-known
        /// environment variables consumed by the MCP server tools.
        /// </summary>
        public static McpServerOptions CreateMcpServerOptions()
        {
            return new McpServerOptions
            {
                NodeSetExportRoot = Environment.GetEnvironmentVariable("OPCUA_MCP_NODESET_EXPORT_ROOT"),
                PcapBaseFolder = Environment.GetEnvironmentVariable("OPCUA_MCP_PCAP_BASE_FOLDER")
            };
        }

        /// <summary>
        /// Creates the <see cref="McpServerOptions"/> from configuration and an optional CLI override.
        /// </summary>
        public static McpServerOptions CreateMcpServerOptions(
            IConfiguration configuration,
            McpToolProfile? toolProfileOverride)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            McpServerOptions options = CreateMcpServerOptions();
            if (toolProfileOverride.HasValue)
            {
                options.ToolProfile = toolProfileOverride.Value;
                return options;
            }

            string? configuredProfile = configuration["McpServer:ToolProfile"] ??
                Environment.GetEnvironmentVariable("OPCUA_MCP_TOOL_PROFILE");
            if (string.IsNullOrWhiteSpace(configuredProfile))
            {
                return options;
            }

            if (!Enum.TryParse(configuredProfile, ignoreCase: true, out McpToolProfile toolProfile) ||
                !Enum.IsDefined(toolProfile))
            {
                throw new InvalidOperationException(
                    $"Unknown MCP tool profile '{configuredProfile}'. " +
                    $"Valid profiles: {string.Join(", ", Enum.GetNames<McpToolProfile>())}.");
            }

            options.ToolProfile = toolProfile;
            return options;
        }

        /// <summary>
        /// Reads the <c>Pcap:EnableDiagnosticsTools</c> configuration value
        /// into a new <see cref="PcapOptions"/> instance. Non-boolean or
        /// missing values leave the default (disabled) in place.
        /// </summary>
        public static PcapOptions CreatePcapOptions(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var options = new PcapOptions();

            string? enableDiagnosticsTools = configuration["Pcap:EnableDiagnosticsTools"];
            if (bool.TryParse(enableDiagnosticsTools, out bool parsedEnableDiagnosticsTools))
            {
                options.EnableDiagnosticsTools = parsedEnableDiagnosticsTools;
            }

            return options;
        }

        /// <summary>
        /// Determines whether the Pcap diagnostics MCP tools (dump_keys,
        /// decode_pcap_with_keys, replay_pcap) should be registered, honoring
        /// both the <see cref="PcapOptions.EnableDiagnosticsTools"/> flag and
        /// the <c>OPCUA_PCAP_ENABLE_DIAGNOSTICS</c> environment variable.
        /// </summary>
        public static bool AreDiagnosticsToolsEnabled(PcapOptions pcapOptions)
        {
            ArgumentNullException.ThrowIfNull(pcapOptions);

            return pcapOptions.EnableDiagnosticsTools ||
                string.Equals(
                    Environment.GetEnvironmentVariable("OPCUA_PCAP_ENABLE_DIAGNOSTICS"),
                    "1",
                    StringComparison.Ordinal) ||
                string.Equals(
                    Environment.GetEnvironmentVariable("OPCUA_PCAP_ENABLE_DIAGNOSTICS"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Registers the standard MCP tool types, and conditionally the Pcap
        /// diagnostics-only tools, on the supplied MCP server builder.
        /// </summary>
        public static void ConfigureMcpTools(IMcpServerBuilder mcpServerBuilder, bool diagnosticsToolsEnabled)
        {
            ConfigureMcpTools(mcpServerBuilder, McpToolProfile.Full, diagnosticsToolsEnabled);
        }

        /// <summary>
        /// Registers the MCP tool types selected by <paramref name="toolProfile"/>.
        /// </summary>
        public static void ConfigureMcpTools(
            IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile,
            bool diagnosticsToolsEnabled)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            mcpServerBuilder.WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(McpRequestFilters.ValidateRequiredArguments);
                filters.AddListToolsFilter(McpSchemaFilters.AddExplicitRequiredArrays);
            });

            switch (toolProfile)
            {
                case McpToolProfile.Core:
                    ConfigureCoreTools(mcpServerBuilder);
                    break;
                case McpToolProfile.Services:
                    ConfigureServiceTools(mcpServerBuilder);
                    break;
                case McpToolProfile.Administration:
                    ConfigureAdministrationTools(mcpServerBuilder);
                    break;
                case McpToolProfile.PubSub:
                    ConfigurePubSubTools(mcpServerBuilder, diagnosticsToolsEnabled);
                    break;
                case McpToolProfile.Diagnostics:
                    ConfigureDiagnosticsTools(mcpServerBuilder, diagnosticsToolsEnabled);
                    break;
                case McpToolProfile.Full:
                    ConfigureFullTools(mcpServerBuilder, diagnosticsToolsEnabled);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(toolProfile),
                        toolProfile,
                        "Unknown MCP tool profile.");
            }

            mcpServerBuilder.WithResources<SessionResources>();
        }

        private static void ConfigureCoreTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>();
        }

        private static void ConfigureServiceTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<AttributeServiceTools>()
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>()
                .WithTools<DiscoveryServiceTools>()
                .WithTools<MethodServiceTools>()
                .WithTools<MonitoredItemServiceTools>()
                .WithTools<NodeManagementServiceTools>()
                .WithTools<SubscriptionServiceTools>()
                .WithTools<ViewServiceTools>();
        }

        private static void ConfigureAdministrationTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithTools<ConfigurationReadTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<NodeSetExportTools>()
                .WithTools<PkiTools>();
        }

        private static void ConfigurePubSubTools(
            IMcpServerBuilder mcpServerBuilder,
            bool diagnosticsToolsEnabled)
        {
            mcpServerBuilder
                .WithTools<PubSubActionTools>()
                .WithTools<PubSubCaptureTools>()
                .WithTools<PubSubDiscoveryTools>()
                .WithTools<PubSubRuntimeTools>();

            if (diagnosticsToolsEnabled)
            {
                mcpServerBuilder.WithTools<PubSubDecodeTools>();
            }
        }

        private static void ConfigureDiagnosticsTools(
            IMcpServerBuilder mcpServerBuilder,
            bool diagnosticsToolsEnabled)
        {
            mcpServerBuilder
                .WithTools<ConnectionTools>()
                .WithTools<PacketCaptureTools>();

            if (diagnosticsToolsEnabled)
            {
                mcpServerBuilder
                    .WithTools<PacketDecodeTools>()
                    .WithTools<PacketReplayTools>();
            }
        }

        private static void ConfigureFullTools(
            IMcpServerBuilder mcpServerBuilder,
            bool diagnosticsToolsEnabled)
        {
            mcpServerBuilder
                .WithTools<AttributeServiceTools>()
                .WithTools<ConfigurationTools>()
                .WithTools<ConfigurationUpdateTools>()
                .WithTools<ConnectionTools>()
                .WithTools<ConvenienceTools>()
                .WithTools<DiscoveryServiceTools>()
                .WithTools<MethodServiceTools>()
                .WithTools<MonitoredItemServiceTools>()
                .WithTools<NodeManagementServiceTools>()
                .WithTools<NodeSetExportTools>()
                .WithTools<PacketCaptureTools>()
                .WithTools<PkiTools>()
                .WithTools<PubSubActionTools>()
                .WithTools<PubSubCaptureTools>()
                .WithTools<PubSubDiscoveryTools>()
                .WithTools<PubSubRuntimeTools>()
                .WithTools<SubscriptionServiceTools>()
                .WithTools<ViewServiceTools>();

            if (diagnosticsToolsEnabled)
            {
                mcpServerBuilder
                    .WithTools<PacketDecodeTools>()
                    .WithTools<PacketReplayTools>()
                    .WithTools<PubSubDecodeTools>();
            }
        }

        /// <summary>
        /// Emits a warning log entry when the Pcap diagnostics MCP tools are
        /// enabled, since they disclose symmetric channel keys.
        /// </summary>
        public static void LogDiagnosticsToolsWarning(IServiceProvider services, bool diagnosticsToolsEnabled)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (!diagnosticsToolsEnabled)
            {
                return;
            }

            ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger("Opc.Ua.Mcp.Program");
            logger.PcapDiagnosticsToolsEnabled();
        }

        /// <summary>
        /// Configures the shared console logging pipeline used by both the
        /// stdio and HTTP/SSE hosts. When <paramref name="useStdioTransport"/>
        /// is <c>true</c>, all log levels are routed to standard error so
        /// they never collide with the stdio JSON-RPC transport on stdout.
        /// </summary>
        public static void ConfigureLogging(ILoggingBuilder logging, bool useStdioTransport = false)
        {
            ArgumentNullException.ThrowIfNull(logging);

            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddSimpleConsole(options =>
            {
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            logging.Services.Configure<ConsoleLoggerOptions>(o =>
                o.LogToStandardErrorThreshold = useStdioTransport ? LogLevel.Trace : LogLevel.Error);
        }
    }

    internal static partial class ProgramLog
    {
        [LoggerMessage(
            EventId = McpServerEventIds.Program + 0,
            Level = LogLevel.Warning,
            Message =
                "OPC UA Pcap diagnostics MCP tools (dump_keys, decode_pcap_with_keys, replay_pcap) are ENABLED. " +
                "These tools disclose symmetric channel keys and can be used to replay captured traffic. " +
                "Ensure the MCP transport is authenticated and audited.")]
        public static partial void PcapDiagnosticsToolsEnabled(this ILogger logger);
    }
}
