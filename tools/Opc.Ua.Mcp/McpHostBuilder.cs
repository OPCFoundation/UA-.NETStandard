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

        /// <summary>
        /// Registers the OPC UA client, session/PubSub managers and Pcap
        /// diagnostics services used by the MCP tools.
        /// </summary>
        public static void ConfigureServices(
            IServiceCollection services,
            PcapOptions pcapOptions,
            OpcUaMcpOptions? OpcUaMcpOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(pcapOptions);

            services.AddOpcUaMcpCore(OpcUaMcpOptions ?? CreateOpcUaMcpOptions());
            services.AddOpcUaMcpPubSub();
            services.AddOpcUaMcpRobotics();
            services.AddOpcUaMcpVision();
            services.AddOpcUaMcpDiagnostics(options =>
            {
                options.BaseFolder = pcapOptions.BaseFolder;
                options.MaxActiveSessions = pcapOptions.MaxActiveSessions;
                options.EnableDiagnosticsTools = pcapOptions.EnableDiagnosticsTools;
            });
            services.AddOpcUaMcpPubSubDiagnostics();
        }

        /// <summary>
        /// Creates the <see cref="OpcUaMcpOptions"/> from the well-known
        /// environment variables consumed by the MCP server tools.
        /// </summary>
        public static OpcUaMcpOptions CreateOpcUaMcpOptions()
        {
            return OpcUaMcpOptions.FromEnvironment();
        }

        /// <summary>
        /// Creates the <see cref="OpcUaMcpOptions"/> from configuration and an optional CLI override
        /// expressed as a comma or plus separated list of profile names, so a host can compose
        /// several bounded profiles - <c>vision,robotics</c>, for instance - into one MCP server.
        /// </summary>
        public static OpcUaMcpOptions CreateOpcUaMcpOptions(
            IConfiguration configuration,
            string? toolProfileOverride)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            OpcUaMcpOptions options = CreateOpcUaMcpOptions();
            if (!string.IsNullOrWhiteSpace(toolProfileOverride))
            {
                ApplyConfiguredProfile(options, toolProfileOverride);
                return options;
            }

            string? configuredProfile = configuration["McpServer:ToolProfile"] ??
                Environment.GetEnvironmentVariable("OPCUA_MCP_TOOL_PROFILE");
            ApplyConfiguredProfile(options, configuredProfile);
            return options;
        }

        private static void ApplyConfiguredProfile(OpcUaMcpOptions options, string? configuredProfile)
        {
            if (string.IsNullOrWhiteSpace(configuredProfile))
            {
                return;
            }

            if (!McpToolProfileSet.TryParse(
                    configuredProfile,
                    out McpToolProfileSet toolProfiles,
                    out string? error))
            {
                throw new InvalidOperationException(error);
            }

            if (toolProfiles.Count == 1)
            {
                foreach (McpToolProfile single in toolProfiles.Enumerate())
                {
                    options.ToolProfile = single;
                }
                options.ToolProfiles = McpToolProfileSet.Empty;
            }
            else
            {
                options.ToolProfiles = toolProfiles;
            }
        }

        /// <summary>
        /// Reads the <c>Pcap:EnableDiagnosticsTools</c> configuration value
        /// into a new <see cref="PcapOptions"/> instance. Non-boolean or
        /// missing values leave the default (disabled) in place.
        /// </summary>
        public static PcapOptions CreatePcapOptions(IConfiguration configuration)
        {
            return OpcUaMcpDiagnosticsExtensions.CreatePcapOptions(configuration);
        }

        /// <summary>
        /// Determines whether the Pcap diagnostics MCP tools (dump_keys,
        /// decode_pcap_with_keys, replay_pcap) should be registered, honoring
        /// both the <see cref="PcapOptions.EnableDiagnosticsTools"/> flag and
        /// the <c>OPCUA_PCAP_ENABLE_DIAGNOSTICS</c> environment variable.
        /// </summary>
        public static bool AreDiagnosticsToolsEnabled(PcapOptions pcapOptions)
        {
            return OpcUaMcpDiagnosticsExtensions.AreDiagnosticsToolsEnabled(pcapOptions);
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

            mcpServerBuilder
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(toolProfile)
                .WithOpcUaPubSubTools(toolProfile)
                .WithOpcUaRoboticsTools(toolProfile)
                .WithOpcUaVisionTools(toolProfile)
                .WithOpcUaDiagnosticsTools(toolProfile, diagnosticsToolsEnabled)
                .WithOpcUaPubSubDiagnosticsTools(toolProfile, diagnosticsToolsEnabled);

            mcpServerBuilder.WithResources<SessionResources>();
        }

        /// <summary>
        /// Registers the MCP tool types selected by the composed
        /// <paramref name="toolProfiles"/>, so a single MCP server can carry the
        /// tools of several bounded profiles at once - vision plus robotics for
        /// a vision-guided pick-and-place agent, for example.
        /// </summary>
        /// <remarks>
        /// This calls each package's <c>McpToolProfileSet</c> overload in
        /// turn. The core package's overload registers
        /// <c>ConnectionTools</c> exactly once for any set that includes at
        /// least one session-scoped profile, so composing Vision and
        /// Robotics yields one set of connection tools rather than two.
        /// </remarks>
        public static void ConfigureMcpTools(
            IMcpServerBuilder mcpServerBuilder,
            McpToolProfileSet toolProfiles,
            bool diagnosticsToolsEnabled)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            if (toolProfiles.Count <= 1)
            {
                McpToolProfile single = McpToolProfile.Full;
                foreach (McpToolProfile profile in toolProfiles.Enumerate())
                {
                    single = profile;
                }
                ConfigureMcpTools(mcpServerBuilder, single, diagnosticsToolsEnabled);
                return;
            }

            mcpServerBuilder
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(toolProfiles)
                .WithOpcUaPubSubTools(toolProfiles)
                .WithOpcUaRoboticsTools(toolProfiles)
                .WithOpcUaVisionTools(toolProfiles)
                .WithOpcUaDiagnosticsTools(toolProfiles, diagnosticsToolsEnabled)
                .WithOpcUaPubSubDiagnosticsTools(toolProfiles, diagnosticsToolsEnabled);

            mcpServerBuilder.WithResources<SessionResources>();
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
            EventId = McpHostEventIds.Program + 0,
            Level = LogLevel.Warning,
            Message =
                "OPC UA Pcap diagnostics MCP tools (dump_keys, decode_pcap_with_keys, replay_pcap) are ENABLED. " +
                "These tools disclose symmetric channel keys and can be used to replay captured traffic. " +
                "Ensure the MCP transport is authenticated and audited.")]
        public static partial void PcapDiagnosticsToolsEnabled(this ILogger logger);
    }
}
