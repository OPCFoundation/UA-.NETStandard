/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Default <see cref="IDiPostSetupRunner"/>: enumerates registered
    /// configurators, filters by manager type, invokes each one in
    /// registration order, and propagates the first exception.
    /// </summary>
    internal sealed class DiPostSetupRunner : IDiPostSetupRunner
    {
        private readonly IServiceProvider m_services;
        private readonly IEnumerable<IDiPostSetupConfigurator> m_configurators;
        private readonly ILogger<DiPostSetupRunner>? m_logger;

        public DiPostSetupRunner(
            IServiceProvider services,
            IEnumerable<IDiPostSetupConfigurator> configurators,
            ILogger<DiPostSetupRunner>? logger = null)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_configurators = configurators
                ?? throw new ArgumentNullException(nameof(configurators));
            m_logger = logger;
        }

        public async ValueTask RunAsync(
            DiNodeManager manager,
            CancellationToken cancellationToken)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            var context = new DiPostSetupContext(manager, m_services, cancellationToken);
            Type managerType = manager.GetType();
            int index = 0;

            foreach (IDiPostSetupConfigurator configurator in m_configurators)
            {
                if (!configurator.TargetManagerType.IsAssignableFrom(managerType))
                {
                    index++;
                    continue;
                }

                try
                {
                    await configurator.RunAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger?.LogError(ex,
                        "DI post-setup configurator #{Index} (target {TargetType}) failed for manager {ManagerType}.",
                        index,
                        configurator.TargetManagerType.FullName,
                        managerType.FullName);

                    // Fail-fast: abort startup with diagnostic context.
                    throw new InvalidOperationException(
                        $"DI post-setup configurator #{index} (target '{configurator.TargetManagerType.FullName}') " +
                        $"failed for manager '{managerType.FullName}'. See inner exception.",
                        ex);
                }

                index++;
            }
        }
    }
}
