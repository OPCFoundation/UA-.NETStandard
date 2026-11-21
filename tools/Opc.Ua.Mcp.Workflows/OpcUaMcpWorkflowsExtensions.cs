// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers one consolidated package with five independently composable client workflow profiles.
    /// </summary>
    public static class OpcUaMcpWorkflowsExtensions
    {
        /// <summary>
        /// Registers bounded workflow services without replacing host-selected policy, secrets, streams or time.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpWorkflows(
            this IServiceCollection services, WorkflowOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new WorkflowOptions();
            options.Validate();
            services.TryAddSingleton(options);
            services.AddOpcUaMcpSecretReferenceDefaults();
            services.TryAddSingleton<IWorkflowStreams, WorkflowStreams>();
            services.TryAddSingleton<IWorkflowSessionFactory, WorkflowSessionFactory>();
            services.TryAddSingleton(provider => new HistoryCursorRegistry(
                provider.GetRequiredService<OpcUaSessionManager>(),
                provider.GetRequiredService<WorkflowOptions>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetRequiredService<IWorkflowSessionFactory>()));
            services.TryAddTransient<RoleWorkflowTools>();
            services.TryAddTransient<UserWorkflowTools>();
            services.TryAddTransient<FileWorkflowTools>();
            services.TryAddTransient<AlarmWorkflowTools>();
            services.TryAddTransient<HistoryWorkflowTools>();
            return services;
        }

        /// <summary>
        /// Selects one workflow catalogue without granting authority.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWorkflowTools(
            this IMcpServerBuilder builder, McpToolProfile profile = McpToolProfile.Full)
        {
            return builder.WithOpcUaWorkflowTools(new McpToolProfileSet(profile));
        }

        /// <summary>
        /// Composes selected workflow profiles with deduplicated connection tools and exact input contracts.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaWorkflowTools(
            this IMcpServerBuilder builder, McpToolProfileSet profiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            bool full = profiles.Contains(McpToolProfile.Full);
            bool roles = full || profiles.Contains(McpToolProfile.Roles);
            bool users = full || profiles.Contains(McpToolProfile.Users);
            bool files = full || profiles.Contains(McpToolProfile.Files);
            bool alarms = full || profiles.Contains(McpToolProfile.Alarms);
            bool history = full || profiles.Contains(McpToolProfile.History);
            if (!roles && !users && !files && !alarms && !history)
            {
                return builder;
            }
            builder.WithOpcUaConnectionTools();
            int first = builder.Services.Count;
            if (AddMarker<FilterMarker>(builder.Services))
            {
                builder.WithRequestFilters(filters => filters.AddListToolsFilter(WorkflowFilters.Catalogue));
            }
            if (roles && AddMarker<RolesMarker>(builder.Services))
            {
                builder.WithTools<RoleWorkflowTools>(WorkflowToolJson.Options);
            }
            if (users && AddMarker<UsersMarker>(builder.Services))
            {
                builder.WithTools<UserWorkflowTools>(WorkflowToolJson.Options);
            }
            if (files && AddMarker<FilesMarker>(builder.Services))
            {
                builder.WithTools<FileWorkflowTools>(WorkflowToolJson.Options);
            }
            if (alarms && AddMarker<AlarmsMarker>(builder.Services))
            {
                builder.WithTools<AlarmWorkflowTools>(WorkflowToolJson.Options);
            }
            if (history && AddMarker<HistoryMarker>(builder.Services))
            {
                builder.WithTools<HistoryWorkflowTools>(WorkflowToolJson.Options);
            }
            for (int index = first; index < builder.Services.Count; index++)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(McpServerTool) &&
                    descriptor.ImplementationFactory is { } factory)
                {
                    builder.Services[index] = ServiceDescriptor.Describe(typeof(McpServerTool),
                        services => WorkflowFilters.Wrap((McpServerTool)factory(services)), descriptor.Lifetime);
                }
            }
            return builder;
        }

        /// <summary>
        /// Registers each catalogue once when several single/composed builder calls are mixed.
        /// </summary>
        /// <typeparam name="T">A private registration marker.</typeparam>
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
        /// Marks the shared catalogue filter.
        /// </summary>
        private sealed class FilterMarker;

        /// <summary>
        /// Marks role-tool registration.
        /// </summary>
        private sealed class RolesMarker;

        /// <summary>
        /// Marks user-tool registration.
        /// </summary>
        private sealed class UsersMarker;

        /// <summary>
        /// Marks file-tool registration.
        /// </summary>
        private sealed class FilesMarker;

        /// <summary>
        /// Marks alarm-tool registration.
        /// </summary>
        private sealed class AlarmsMarker;

        /// <summary>
        /// Marks historical-tool registration.
        /// </summary>
        private sealed class HistoryMarker;
    }
}
