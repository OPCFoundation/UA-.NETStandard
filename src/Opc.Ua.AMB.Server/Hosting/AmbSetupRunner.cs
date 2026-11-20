// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Hosting
{
    /// <summary>
    /// A delegate registered with <c>ConfigureAssetManagement</c>.
    /// </summary>
    internal sealed class AmbSetupConfigurator
    {
        public AmbSetupConfigurator(Func<IAssetManagementSetupContext, ValueTask> configure)
        {
            Configure = configure;
        }

        public Func<IAssetManagementSetupContext, ValueTask> Configure { get; }
    }

    /// <summary>
    /// Runs the <c>ConfigureAssetManagement</c> delegates in registration
    /// order; the first failure aborts the server startup.
    /// </summary>
    internal sealed class AmbSetupRunner
    {
        public AmbSetupRunner(IServiceProvider services, IEnumerable<AmbSetupConfigurator> configurators)
        {
            m_services = services;
            m_configurators = configurators;
        }

        public async ValueTask RunAsync(
            AmbNodeManager manager,
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            var context = new SetupContext(manager, builder, m_services, cancellationToken);
            int index = 0;
            foreach (AmbSetupConfigurator configurator in m_configurators)
            {
                try
                {
                    await configurator.Configure(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"ConfigureAssetManagement delegate #{index} failed. See inner exception.",
                        ex);
                }
                index++;
            }
        }

        private readonly IServiceProvider m_services;
        private readonly IEnumerable<AmbSetupConfigurator> m_configurators;

        private sealed class SetupContext : IAssetManagementSetupContext
        {
            public SetupContext(
                AmbNodeManager manager,
                INodeManagerBuilder builder,
                IServiceProvider services,
                CancellationToken cancellationToken)
            {
                Manager = manager;
                Builder = builder;
                m_services = services;
                CancellationToken = cancellationToken;
            }

            public AmbNodeManager Manager { get; }

            public IAssetManagement Assets => Manager.Assets;

            public INodeManagerBuilder Builder { get; }

            public CancellationToken CancellationToken { get; }

            public T GetRequiredService<T>() where T : notnull
            {
                return m_services.GetRequiredService<T>();
            }

            public T? GetService<T>() where T : class
            {
                return m_services.GetService<T>();
            }

            private readonly IServiceProvider m_services;
        }
    }
}
