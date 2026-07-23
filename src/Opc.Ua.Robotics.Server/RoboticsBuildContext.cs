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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    internal sealed class RoboticsBuildContext : IRoboticsBuildContext
    {
        private readonly NodeManagerBuilder m_nodes;
        private readonly IDiPostSetupContext? m_postSetupContext;
        private bool m_sealed;

        public RoboticsBuildContext(
            DiNodeManager manager,
            RoboticsServerOptions options,
            CancellationToken cancellationToken,
            IDiPostSetupContext? postSetupContext = null)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            options = RoboticsModelProviderUtilities.ValidateOptions(options);
            CancellationToken = cancellationToken;
            m_postSetupContext = postSetupContext;

            Context = manager.SystemContext;
            int roboticsNamespaceIndex = Context.NamespaceUris.GetIndex(
                Opc.Ua.Robotics.Namespaces.Robotics);
            if (roboticsNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics namespace '{0}' is not registered.",
                    Opc.Ua.Robotics.Namespaces.Robotics);
            }

            int instanceNamespaceIndex = Context.NamespaceUris.GetIndex(
                options.InstanceNamespaceUri);
            if (instanceNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics instance namespace '{0}' is not registered.",
                    options.InstanceNamespaceUri);
            }
            InstanceNamespaceIndex = (ushort)instanceNamespaceIndex;

            var deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                Context.NamespaceUris);
            DeviceSet = manager.FindPredefinedNode(deviceSetId) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DI DeviceSet node is not available in the Robotics address space.");

            m_nodes = manager.CreateFluentBuilder(InstanceNamespaceIndex);
        }

        public DiNodeManager Manager { get; }

        public ISystemContext Context { get; }

        public INodeManagerBuilder Nodes => m_nodes;

        public ushort InstanceNamespaceIndex { get; }

        public NodeState DeviceSet { get; }

        public CancellationToken CancellationToken { get; }

        public T GetRequiredService<T>() where T : notnull
        {
            if (m_postSetupContext == null)
            {
                throw new InvalidOperationException(
                    "Application services are unavailable for a directly created Robotics build context.");
            }
            return m_postSetupContext.GetRequiredService<T>();
        }

        public void Seal()
        {
            if (m_sealed)
            {
                return;
            }
            m_nodes.Seal();
            m_sealed = true;
        }
    }
}
