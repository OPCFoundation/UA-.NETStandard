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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Wires the standard <c>Server.RequestServerStateChange</c> method for Maintenance-driven Failover
    /// (OPC 10000-4 §6.6.5).
    /// </summary>
    public sealed class RequestServerStateChangeStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="options">The method wiring options.</param>
        /// <param name="serviceLevelController">Optional service-level controller.</param>
        public RequestServerStateChangeStartupTask(
            RequestServerStateChangeOptions options,
            IServiceLevelController? serviceLevelController = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_serviceLevelController = serviceLevelController;
        }

        /// <summary>
        /// Creates the task with default options.
        /// </summary>
        /// <param name="serviceLevelController">Optional service-level controller.</param>
        public RequestServerStateChangeStartupTask(IServiceLevelController? serviceLevelController = null)
            : this(new RequestServerStateChangeOptions(), serviceLevelController)
        {
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ServerObjectState? serverObject = server.ServerObject;
            if (serverObject == null)
            {
                return default;
            }

            m_server = server;
            RequestServerStateChangeMethodState? requestServerStateChange =
                server.DiagnosticsNodeManager?.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange) ??
                serverObject.RequestServerStateChange;
            if (requestServerStateChange != null)
            {
                requestServerStateChange.OnCall = OnRequestServerStateChange;
            }

            return default;
        }

        private ServiceResult OnRequestServerStateChange(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ServerState state,
            DateTimeUtc estimatedReturnTime,
            uint secondsTillShutdown,
            LocalizedText reason,
            bool restart)
        {
            try
            {
                ValidateAdminAccess(context);

                IServerInternal? server = m_server;
                ServerObjectState? serverObject = server?.ServerObject;
                if (server == null || serverObject == null)
                {
                    return new ServiceResult(StatusCodes.BadServerHalted);
                }

                byte serviceLevel = SelectServiceLevel(state);
                ApplyServiceLevel(serverObject, server.DefaultSystemContext, serviceLevel);
                ApplyServerStatus(serverObject, server.DefaultSystemContext, state, estimatedReturnTime,
                    secondsTillShutdown, reason);
                return ServiceResult.Good;
            }
            catch (ServiceResultException sre)
            {
                return sre.Result;
            }
        }

        private void ValidateAdminAccess(ISystemContext context)
        {
            if (m_options.AdminAccessValidator != null)
            {
                m_options.AdminAccessValidator(context);
                return;
            }

            IConfigurationNodeManager? configurationNodeManager = m_server?.ConfigurationNodeManager;
            if (configurationNodeManager == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied,
                    "A configuration node manager is required to validate administrator access.");
            }

            configurationNodeManager.HasApplicationSecureAdminAccess(context);
        }

        private byte SelectServiceLevel(ServerState state)
        {
            if (m_options.ServiceLevelSelector != null)
            {
                return m_options.ServiceLevelSelector(state);
            }

            return state switch
            {
                ServerState.Running => ServiceLevels.Maximum,
                ServerState.Shutdown => ServiceLevels.Maintenance,
                ServerState.Suspended => ServiceLevels.Maintenance,
                _ => ServiceLevels.NoData
            };
        }

        private void ApplyServiceLevel(ServerObjectState serverObject, ISystemContext context, byte serviceLevel)
        {
            if (m_serviceLevelController != null)
            {
                m_serviceLevelController.SetServiceLevel(serviceLevel);
                return;
            }

            if (serverObject.ServiceLevel != null)
            {
                serverObject.ServiceLevel.Value = serviceLevel;
                serverObject.ServiceLevel.ClearChangeMasks(context, false);
            }
        }

        private static void ApplyServerStatus(
            ServerObjectState serverObject,
            ISystemContext context,
            ServerState state,
            DateTimeUtc estimatedReturnTime,
            uint secondsTillShutdown,
            LocalizedText reason)
        {
            if (serverObject.EstimatedReturnTime != null)
            {
                serverObject.EstimatedReturnTime.Value = estimatedReturnTime;
                serverObject.EstimatedReturnTime.ClearChangeMasks(context, false);
            }

            if (serverObject.ServerStatus?.State != null)
            {
                serverObject.ServerStatus.State.Value = state;
                serverObject.ServerStatus.State.ClearChangeMasks(context, false);
            }
            if (serverObject.ServerStatus?.SecondsTillShutdown != null)
            {
                serverObject.ServerStatus.SecondsTillShutdown.Value = secondsTillShutdown;
                serverObject.ServerStatus.SecondsTillShutdown.ClearChangeMasks(context, false);
            }
            if (serverObject.ServerStatus?.ShutdownReason != null)
            {
                serverObject.ServerStatus.ShutdownReason.Value = reason;
                serverObject.ServerStatus.ShutdownReason.ClearChangeMasks(context, false);
            }
        }

        private readonly RequestServerStateChangeOptions m_options;
        private readonly IServiceLevelController? m_serviceLevelController;
        private IServerInternal? m_server;
    }
}
