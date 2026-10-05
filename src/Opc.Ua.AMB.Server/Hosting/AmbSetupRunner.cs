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
