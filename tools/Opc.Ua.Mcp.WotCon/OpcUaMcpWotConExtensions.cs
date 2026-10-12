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
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers separate WoT management and document-processing catalogues with host-controlled egress policy.
    /// </summary>
    public static class OpcUaMcpWotConExtensions
    {
        /// <summary>
        /// Registers injectable WoT tools and a dedicated, policy-enforcing dependency HTTP client.
        /// No connection or binding executor is started by registration.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpWotCon(
            this IServiceCollection services, WotMcpOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new WotMcpOptions();
            options.Validate();
            services.TryAddSingleton(options);
            if (services.Any(service => service.ServiceType == typeof(WotRegistrationMarker)))
            {
                return services;
            }
            services.AddSingleton<WotRegistrationMarker>();
            services.TryAddSingleton<WotTools>();
            services.TryAddSingleton<WotDocumentTools>();
            services.AddHttpClient(WotDependencyHttp.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(provider =>
                    WotDependencyHttp.CreateHandler(provider.GetRequiredService<WotMcpOptions>()));
            return services;
        }

        /// <summary>
        /// Adds the selected WoT catalogue.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWotConTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaWotConTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Adds management and/or document tools without registering connection tools for document-only use.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWotConTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            bool remote = profiles.Contains(McpToolProfile.Wot) || profiles.Contains(McpToolProfile.Full);
            bool documents = profiles.Contains(McpToolProfile.WotDocuments) || profiles.Contains(McpToolProfile.Full);
            if (!remote && !documents)
            {
                return builder;
            }
            builder.WithRequestFilters(filters => filters.AddListToolsFilter(WotMcpFilters.AddInputSchemas));
            int firstRegistration = builder.Services.Count;
            if (remote)
            {
                builder.WithOpcUaConnectionTools().WithTools<WotTools>();
            }
            if (documents)
            {
                builder.WithTools<WotDocumentTools>();
            }
            for (int index = firstRegistration; index < builder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    builder.Services[index] = ServiceDescriptor.Describe(
                        typeof(McpServerTool),
                        services => WotMcpFilters.WrapInput((McpServerTool)factory(services)),
                        descriptor.Lifetime);
                }
            }
            return builder;
        }

        /// <summary>
        /// Binds host-only settings without reflection-based configuration binding.
        /// </summary>
        public static WotMcpOptions CreateWotMcpOptions(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var options = new WotMcpOptions();
            string[] origins = configuration.GetSection("McpServer:Wot:AllowedDependencyOrigins").GetChildren()
                .Select(child => child.Value ??
                    throw new InvalidOperationException("Every dependency origin must be a string."))
                .ToArray();
            if (origins.Length == 0)
            {
                string? configured = configuration["McpServer:Wot:AllowedDependencyOrigins"] ??
                    Environment.GetEnvironmentVariable("OPCUA_MCP_WOT_DEPENDENCY_ORIGINS");
                if (configured is not null)
                {
                    origins = configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);
                }
            }
            options.AllowedDependencyOrigins = origins.ToArrayOf();
            string? privateAddresses = configuration["McpServer:Wot:AllowPrivateDependencyAddresses"] ??
                Environment.GetEnvironmentVariable("OPCUA_MCP_WOT_ALLOW_PRIVATE_DEPENDENCIES");
            if (privateAddresses is not null)
            {
                if (!bool.TryParse(privateAddresses, out bool allowed))
                {
                    throw new InvalidOperationException("AllowPrivateDependencyAddresses must be a Boolean.");
                }
                options.AllowPrivateDependencyAddresses = allowed;
            }
            options.MaxDocumentBytes = Number(
                configuration, nameof(options.MaxDocumentBytes), options.MaxDocumentBytes);
            options.MaxDocuments = Number(configuration, nameof(options.MaxDocuments), options.MaxDocuments);
            options.MaxResolverDepth = Number(
                configuration, nameof(options.MaxResolverDepth), options.MaxResolverDepth);
            options.OperationTimeoutMilliseconds = Number(configuration, nameof(options.OperationTimeoutMilliseconds),
                options.OperationTimeoutMilliseconds);
            options.MaxConcurrentOperations = Number(configuration, nameof(options.MaxConcurrentOperations),
                options.MaxConcurrentOperations);
            string? total = configuration["McpServer:Wot:MaxTotalBytes"];
            if (total is not null)
            {
                if (!long.TryParse(total, NumberStyles.None, CultureInfo.InvariantCulture, out long bytes))
                {
                    throw new InvalidOperationException("MaxTotalBytes must be a positive integer.");
                }
                options.MaxTotalBytes = bytes;
            }
            options.Validate();
            return options;
        }

        /// <summary>
        /// Reads one optional numeric host setting without silently accepting malformed values.
        /// </summary>
        private static int Number(IConfiguration configuration, string name, int fallback)
        {
            string? value = configuration["McpServer:Wot:" + name];
            if (value is null)
            {
                return fallback;
            }
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : throw new InvalidOperationException(name + " must be a positive integer.");
        }

        /// <summary>
        /// Prevents duplicate named HTTP-handler configuration when modules are composed more than once.
        /// </summary>
        private sealed class WotRegistrationMarker
        {
        }
    }
}
