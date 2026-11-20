/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Network;
using Opc.Ua.Client.Programs;
using Opc.Ua.Client.Roles;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent registrations for per-session OPC UA client feature factories.
    /// </summary>
    public static class OpcUaSubClientBuilderExtensions
    {
        /// <summary>
        /// Registers the session-bound Part 22 network-model client factory.
        /// </summary>
        public static IOpcUaBuilder AddNetworkModelClient(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<NetworkModelClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the session-bound Part 22 network-model client factory.
        /// </summary>
        public static IOpcUaClientBuilder AddNetworkModelClient(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddNetworkModelClient();
            return builder;
        }

        /// <summary>
        /// Registers the session-bound Part 10 Program client factory.
        /// </summary>
        public static IOpcUaBuilder AddProgramClient(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<ProgramClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the session-bound Part 10 Program client factory.
        /// </summary>
        public static IOpcUaClientBuilder AddProgramClient(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddProgramClient();
            return builder;
        }

        /// <summary>
        /// Registers the historian client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddHistorian(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<HistoryClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the historian client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddHistorian(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddHistorian();
            return builder;
        }

        /// <summary>
        /// Registers the role-management client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddRoleManagement(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<RoleManagementClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the role-management client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddRoleManagement(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddRoleManagement();
            return builder;
        }

        /// <summary>
        /// Registers the file-transfer client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddFileTransfer(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<FileTransferClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the file-transfer client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddFileTransfer(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddFileTransfer();
            return builder;
        }

        /// <summary>
        /// Registers the alias-name client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddAliasNames(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<AliasNameClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the alias-name client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddAliasNames(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAliasNames();
            return builder;
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
