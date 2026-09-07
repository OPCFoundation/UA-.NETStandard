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
