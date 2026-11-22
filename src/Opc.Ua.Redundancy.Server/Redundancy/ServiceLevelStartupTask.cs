// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
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
            Action<byte> publishLevel = server is IServerServiceLevelControl control
                ? control.ClaimServiceLevelControl()
                : level =>
                {
                    serverObject.ServiceLevel.Value = level;
                    serverObject.ServiceLevel.ClearChangeMasks(context, false);
                };
            ApplyLevel(server, publishLevel, m_serviceLevelProvider.GetServiceLevel());
            m_serviceLevelProvider.ServiceLevelChanged += level => ApplyLevel(server, publishLevel, level);
            return default;
        }

        private static void ApplyLevel(
            IServerContext server,
            Action<byte> publishLevel,
            byte level)
        {
            publishLevel(
                server is INodeIdFactoryProvider { NodeIdFactory: ReplicaNodeIdFactory { IsCompatible: false } }
                    ? (byte)0
                    : level);
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
    }
}
