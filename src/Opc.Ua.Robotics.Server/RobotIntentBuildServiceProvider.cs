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
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server
{
    internal sealed class RobotIntentBuildServiceProvider : IServiceProvider
    {
        public const string MissingExecutorMessage =
            "No Robot Intent executor is registered. Register an IIntentExecutor with " +
            "AddRobotIntentExecutor<TExecutor>() for DI hosting, or create the direct build context with " +
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

        private readonly IServiceProvider? m_services;
        private readonly IIntentExecutor? m_executor;
    }
}
