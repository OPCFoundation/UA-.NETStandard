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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that drives the live <c>Server.ServiceLevel</c>
    /// node from an <see cref="IServiceLevelProvider"/>: it sets the initial
    /// value and updates the node (firing notifications) whenever the
    /// provider's level changes. With the default
    /// <see cref="ConstantServiceLevelProvider"/> this reports a fixed 255,
    /// preserving single-instance behavior.
    /// </summary>
    public sealed class ServiceLevelStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="serviceLevelProvider">The service-level source.</param>
        public ServiceLevelStartupTask(IServiceLevelProvider serviceLevelProvider)
        {
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ServerObjectState? serverObject = server.ServerObject;
            if (serverObject?.ServiceLevel == null)
            {
                return default;
            }

            ISystemContext context = server.DefaultSystemContext;
            ApplyLevel(serverObject, context, m_serviceLevelProvider.GetServiceLevel());
            m_serviceLevelProvider.ServiceLevelChanged += level => ApplyLevel(serverObject, context, level);
            return default;
        }

        private static void ApplyLevel(ServerObjectState serverObject, ISystemContext context, byte level)
        {
            if (serverObject.ServiceLevel == null)
            {
                return;
            }
            serverObject.ServiceLevel.Value = level;
            serverObject.ServiceLevel.ClearChangeMasks(context, false);
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
    }
}
