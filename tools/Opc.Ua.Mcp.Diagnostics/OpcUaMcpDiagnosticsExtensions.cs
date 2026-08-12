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
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers the OPC UA protocol diagnostics MCP tools - UA-TCP packet
    /// capture, decode and replay - on a host that is building its own MCP
    /// server.
    /// </summary>
    /// <remarks>
    /// The decode and replay tools disclose symmetric channel keys and can
    /// re-send captured traffic, so they stay behind
    /// <see cref="AreDiagnosticsToolsEnabled"/> and are off unless a host opts
    /// in.
    /// </remarks>
    public static class OpcUaMcpDiagnosticsExtensions
    {
        /// <summary>
        /// Registers the capture, formatter and replay services the
        /// diagnostics tools resolve.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <param name="configure">
        /// Optional callback applied to the Pcap options. The diagnostics gate
        /// defaults to disabled and must be opted into explicitly.
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        public static IServiceCollection AddOpcUaMcpDiagnostics(
            this IServiceCollection services,
            Action<PcapOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddPcap(options => configure?.Invoke(options));
            services.AddPcapFormatters();
            services.AddPcapReplay();
            return services;
        }

        /// <summary>
        /// Registers the diagnostics tools when <paramref name="toolProfile"/>
        /// selects them.
        /// </summary>
        /// <remarks>
        /// A profile that does not name diagnostics contributes nothing rather
        /// than failing, so a host can pass one profile to every OPC UA tool
        /// package it references. The key-disclosing decode and replay tools
        /// are registered only when
        /// <paramref name="diagnosticsToolsEnabled"/> is <c>true</c>. The bounded
        /// <see cref="McpToolProfile.Diagnostics"/> catalogue also carries the
        /// connection tools, because capturing traffic is only useful next to the
        /// tools that generate it. <see cref="McpToolProfile.Full"/> already carries
        /// them through the core package, so they are not added twice.
        /// </remarks>
        /// <param name="mcpServerBuilder">The MCP server builder.</param>
        /// <param name="toolProfile">The profile selecting the tool set.</param>
        /// <param name="diagnosticsToolsEnabled">
        /// Whether the key-disclosing tools are opted in.
        /// </param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="mcpServerBuilder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="toolProfile"/> is not a defined profile.
        /// </exception>
        public static IMcpServerBuilder WithOpcUaDiagnosticsTools(
            this IMcpServerBuilder mcpServerBuilder,
            McpToolProfile toolProfile = McpToolProfile.Full,
            bool diagnosticsToolsEnabled = false)
        {
            ArgumentNullException.ThrowIfNull(mcpServerBuilder);

            switch (toolProfile)
            {
                case McpToolProfile.Diagnostics:
                case McpToolProfile.Full:
                    mcpServerBuilder.WithTools<PacketCaptureTools>();

                    if (diagnosticsToolsEnabled)
                    {
                        mcpServerBuilder
                            .WithTools<PacketDecodeTools>()
                            .WithTools<PacketReplayTools>();
                    }

                    if (toolProfile == McpToolProfile.Diagnostics)
                    {
                        // Capturing traffic is only useful next to the connection tools that
                        // generate it. Full already gets them from the core package, so adding
                        // them there would register the same tools twice.
                        mcpServerBuilder.WithTools<ConnectionTools>();
                    }

                    break;
                case McpToolProfile.Core:
                case McpToolProfile.Services:
                case McpToolProfile.Administration:
                case McpToolProfile.PubSub:
                case McpToolProfile.Robotics:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(toolProfile),
                        toolProfile,
                        "Unknown MCP tool profile.");
            }

            return mcpServerBuilder;
        }

        /// <summary>
        /// Reads the <c>Pcap:EnableDiagnosticsTools</c> configuration value
        /// into a new <see cref="PcapOptions"/> instance. Non-boolean or
        /// missing values leave the default (disabled) in place.
        /// </summary>
        /// <param name="configuration">The configuration to read from.</param>
        /// <returns>The parsed options.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <c>null</c>.
        /// </exception>
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
        /// Determines whether the key-disclosing diagnostics tools
        /// (<c>dump_keys</c>, <c>decode_pcap_with_keys</c>,
        /// <c>replay_pcap</c>) should be registered, honoring both the
        /// <see cref="PcapOptions.EnableDiagnosticsTools"/> flag and the
        /// <c>OPCUA_PCAP_ENABLE_DIAGNOSTICS</c> environment variable.
        /// </summary>
        /// <param name="pcapOptions">The options to inspect.</param>
        /// <returns><c>true</c> when the tools are opted in.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="pcapOptions"/> is <c>null</c>.
        /// </exception>
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
    }
}
