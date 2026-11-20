// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Di.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    internal sealed class RobotIntentBuildContext : IRobotIntentBuildContext
    {
        public RobotIntentBuildContext(
            AsyncCustomNodeManager manager,
            RobotIntentRootState root,
            RobotIntentServerOptions options,
            CancellationToken cancellationToken,
            IServiceProvider? services = null)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            CancellationToken = cancellationToken;
            Context = manager.SystemContext;
            m_services = services;

            int namespaceIndex = Context.NamespaceUris.GetIndex(options.InstanceNamespaceUri);
            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robot Intent instance namespace '{0}' is not registered.",
                    options.InstanceNamespaceUri);
            }
            InstanceNamespaceIndex = (ushort)namespaceIndex;
            Nodes = manager is DiNodeManager diManager
                ? diManager.CreateFluentBuilder(InstanceNamespaceIndex)
                : manager is FluentNodeManagerBase fluentManager
                    ? fluentManager.CreateFluentBuilder(InstanceNamespaceIndex)
                    : throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The Robot Intent node manager does not expose a fluent builder.");
        }

        public AsyncCustomNodeManager Manager { get; }

        public ISystemContext Context { get; }

        public INodeManagerBuilder Nodes { get; }

        public ushort InstanceNamespaceIndex { get; }

        public RobotIntentRootState Root { get; }

        public CancellationToken CancellationToken { get; }

        public T GetRequiredService<T>() where T : notnull
        {
            if (m_services == null)
            {
                throw new InvalidOperationException(
                    "Application services are unavailable for a directly created Robot Intent build context.");
            }
            return m_services.GetRequiredService<T>();
        }

        internal bool TryGetService<T>(out T? service)
            where T : class
        {
            service = m_services?.GetService<T>();
            return service != null;
        }

        internal bool TryGetIntentExecutor(IntentControllerState controller, out IIntentExecutor? executor)
        {
            if (m_services is RobotIntentBuildServiceProvider robotIntentServices)
            {
                return robotIntentServices.TryGetExecutor(controller, out executor);
            }
            executor = null;
            return false;
        }

        private readonly IServiceProvider? m_services;
    }
}
