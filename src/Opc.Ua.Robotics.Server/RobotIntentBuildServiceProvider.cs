// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;

namespace Opc.Ua.Robotics.Server
{
    internal sealed class RobotIntentBuildServiceProvider : IServiceProvider
    {
        public const string MissingExecutorMessage =
            "No Robot Intent executor is registered. Register an IIntentExecutor with " +
            "AddRobotIntentExecutor<TExecutor>() for DI hosting, " +
            "AddRobotIntentExecutor<TExecutor>(controllerBrowseName) per controller, " +
            "or create the direct build context with " +
            "CreateRobotIntentBuildContext(IIntentExecutor, CancellationToken).";

        private RobotIntentBuildServiceProvider(IServiceProvider? services, IIntentExecutor? executor)
        {
            m_services = services;
            m_executor = executor;
        }

        public static IServiceProvider RequireExecutor(IServiceProvider? services)
        {
            return new RobotIntentBuildServiceProvider(services, executor: null);
        }

        public static IServiceProvider ForExecutor(IIntentExecutor executor, IServiceProvider? services)
        {
            return new RobotIntentBuildServiceProvider(services, executor);
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IIntentExecutor))
            {
                if (m_executor != null)
                {
                    return m_executor;
                }
                object? service = m_services?.GetService(serviceType);
                if (service != null && service is not RobotIntentRejectingExecutor)
                {
                    return service;
                }
                return null;
            }
            return m_services?.GetService(serviceType);
        }

        internal bool TryGetExecutor(IntentControllerState controller, out IIntentExecutor? executor)
        {
            if (m_services != null)
            {
                foreach (RobotIntentControllerExecutorRegistration registration in
                    m_services.GetServices<RobotIntentControllerExecutorRegistration>())
                {
                    if (registration.TryGetExecutor(controller, out executor))
                    {
                        return true;
                    }
                }
            }
            executor = null;
            return false;
        }

        private readonly IServiceProvider? m_services;
        private readonly IIntentExecutor? m_executor;
    }

    internal sealed class RobotIntentControllerExecutorRegistration
    {
        public RobotIntentControllerExecutorRegistration(string controllerBrowseName, IIntentExecutor executor)
        {
            if (string.IsNullOrWhiteSpace(controllerBrowseName))
            {
                throw new ArgumentException(
                    "A non-empty controller browse name is required.",
                    nameof(controllerBrowseName));
            }
            m_controllerBrowseName = controllerBrowseName;
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public bool TryGetExecutor(IntentControllerState controller, out IIntentExecutor? executor)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }
            if (string.Equals(controller.BrowseName.Name, m_controllerBrowseName, StringComparison.Ordinal))
            {
                executor = m_executor;
                return true;
            }
            executor = null;
            return false;
        }

        private readonly string m_controllerBrowseName;
        private readonly IIntentExecutor m_executor;
    }
}
