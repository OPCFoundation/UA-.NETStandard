// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
