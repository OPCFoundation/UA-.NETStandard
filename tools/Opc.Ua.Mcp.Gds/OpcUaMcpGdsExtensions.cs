/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Embeds four separately selectable GDS workflow catalogues without opening a new UA connection.
    /// </summary>
    public static class OpcUaMcpGdsExtensions
    {
        /// <summary>
        /// Registers domain tools and bounded ownership. An unconfigured secret gateway denies issuance;
        /// call AddOpcUaMcpSecrets with an explicitly selected writable host store to enable persistence.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpGds(
            this IServiceCollection services, GdsMcpOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new GdsMcpOptions();
            options.Validate();
            services.TryAddSingleton(options);
            services.AddOpcUaMcpSecretReferenceDefaults();
            services.TryAddSingleton<GdsOperationState>();
            services.TryAddSingleton<GdsDirectoryTools>();
            services.TryAddSingleton<GdsCertificateTools>();
            services.TryAddSingleton<GdsCredentialTools>();
            services.TryAddSingleton<GdsOnboardingTools>();
            return services;
        }

        /// <summary>
        /// Adds one selected profile; Full selects catalogues but does not grant any capability.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaGdsTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaGdsTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Composes GDS, certificates, credentials and onboarding with one deduplicated connection set.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaGdsTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            bool full = profiles.Contains(McpToolProfile.Full);
            bool directory = full || profiles.Contains(McpToolProfile.Gds);
            bool certificates = full || profiles.Contains(McpToolProfile.Certificates);
            bool credentials = full || profiles.Contains(McpToolProfile.Credentials);
            bool onboarding = full || profiles.Contains(McpToolProfile.Onboarding);
            if (!directory && !certificates && !credentials && !onboarding)
            {
                return builder;
            }
            builder.WithOpcUaConnectionTools();
            int first = builder.Services.Count;
            if (AddMarker<FilterMarker>(builder.Services))
            {
                builder.WithRequestFilters(filters => filters.AddListToolsFilter(GdsMcpFilters.Catalogue));
            }
            if (directory && AddMarker<DirectoryMarker>(builder.Services))
            {
                builder.WithTools<GdsDirectoryTools>(s_toolJsonOptions);
            }
            if (certificates && AddMarker<CertificateMarker>(builder.Services))
            {
                builder.WithTools<GdsCertificateTools>(s_toolJsonOptions);
            }
            if (credentials && AddMarker<CredentialMarker>(builder.Services))
            {
                builder.WithTools<GdsCredentialTools>(s_toolJsonOptions);
            }
            if (onboarding && AddMarker<OnboardingMarker>(builder.Services))
            {
                builder.WithTools<GdsOnboardingTools>(s_toolJsonOptions);
            }
            for (int index = first; index < builder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    builder.Services[index] = ServiceDescriptor.Describe(typeof(McpServerTool),
                        services => GdsMcpFilters.Wrap((McpServerTool)factory(services)), descriptor.Lifetime);
                }
            }
            return builder;
        }

        /// <summary>
        /// Adds a registration marker once, even when separate profile selections are composed repeatedly.
        /// </summary>
        /// <typeparam name="T">The private registration marker.</typeparam>
        private static bool AddMarker<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            IServiceCollection services)
            where T : class
        {
            if (services.Any(descriptor => descriptor.ServiceType == typeof(T)))
            {
                return false;
            }
            services.AddSingleton<T>();
            return true;
        }

        /// <summary>
        /// Composes generated domain metadata with SDK protocol/scalar metadata rather than enabling reflection.
        /// </summary>
        private static JsonSerializerOptions CreateToolJsonOptions()
        {
            var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    GdsInputJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver!)
            };
            options.MakeReadOnly();
            return options;
        }

        private static readonly JsonSerializerOptions s_toolJsonOptions = CreateToolJsonOptions();

        /// <summary>
        /// Marks the module's single list filter.
        /// </summary>
        private sealed class FilterMarker;

        /// <summary>
        /// Marks directory tool registration.
        /// </summary>
        private sealed class DirectoryMarker;

        /// <summary>
        /// Marks certificate tool registration.
        /// </summary>
        private sealed class CertificateMarker;

        /// <summary>
        /// Marks credential tool registration.
        /// </summary>
        private sealed class CredentialMarker;

        /// <summary>
        /// Marks onboarding tool registration.
        /// </summary>
        private sealed class OnboardingMarker;
    }
}
