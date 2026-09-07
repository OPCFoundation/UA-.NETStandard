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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Robotics.Server.Hosting
{
    /// <summary>
    /// Runs Robot Intent startup configurators.
    /// </summary>
    public interface IRobotIntentPostSetupRunner
    {
        /// <summary>
        /// Runs configurators for a node manager.
        /// </summary>
        ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            RobotIntentRootState root,
            RobotIntentServerOptions options,
            CancellationToken cancellationToken);
    }

    internal interface IRobotIntentPostSetupConfigurator
    {
        Type TargetManagerType { get; }

        ValueTask RunAsync(IRobotIntentBuildContext context);
    }

    internal sealed class RobotIntentPostSetupRunner : IRobotIntentPostSetupRunner
    {
        public RobotIntentPostSetupRunner(
            IServiceProvider services,
            IEnumerable<IRobotIntentPostSetupConfigurator> configurators)
        {
            m_services = services;
            m_configurators = configurators.ToArray().ToArrayOf();
        }

        public async ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            RobotIntentRootState root,
            RobotIntentServerOptions options,
            CancellationToken cancellationToken)
        {
            var context = new RobotIntentBuildContext(
                manager,
                root,
                options,
                cancellationToken,
                RobotIntentBuildServiceProvider.RequireExecutor(m_services));
            for (int ii = 0; ii < m_configurators.Count; ii++)
            {
                IRobotIntentPostSetupConfigurator configurator = m_configurators[ii];
                if (configurator.TargetManagerType.IsAssignableFrom(manager.GetType()))
                {
                    await configurator.RunAsync(context).ConfigureAwait(false);
                }
            }
        }

        private readonly IServiceProvider m_services;
        private readonly ArrayOf<IRobotIntentPostSetupConfigurator> m_configurators;
    }

    internal sealed class RobotIntentHostStartupTask : IServerStartupTask
    {
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            foreach (RobotIntentNodeManager robotIntentNodeManager in
                server.FindNodeManagers<RobotIntentNodeManager>())
            {
                robotIntentNodeManager.StartIntentControllerHosts();
            }
            return default;
        }
    }
}
