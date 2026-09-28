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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Quickstarts.ReferenceServer;

namespace InlineMethodModel
{
    /// <summary>
    /// A source-generated node manager for a model whose method declares its
    /// arguments inline, without a method type.
    /// </summary>
    [NodeManager(NamespaceUri = InlineMethodTestModel.NamespaceUri)]
    public sealed partial class InlineMethodNodeManager
    {
        partial void Configure(IInlineMethodNodeManagerBuilder builder)
        {
            builder.Thermostat.Boost
                .OnCall(degrees => InlineMethodTestModel.InitialSetpoint + degrees);
        }
    }

    /// <summary>
    /// Captures the node manager instance the server creates so the tests can
    /// assert against its address space.
    /// </summary>
    public sealed class InlineMethodNodeManagerCapturingFactory :
        InlineMethodNodeManagerFactory
    {
        /// <summary>
        /// The manager created for the running server.
        /// </summary>
        public InlineMethodNodeManager Manager { get; private set; }

        /// <inheritdoc/>
        public override ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new InlineMethodNodeManager(server, configuration);
            Manager = manager;
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }

    /// <summary>
    /// Constants shared by the inline method tests.
    /// </summary>
    public static class InlineMethodTestModel
    {
        /// <summary>
        /// The model namespace generated from
        /// <c>Fluent/Assets/InlineMethod.xml</c>.
        /// </summary>
        public const string NamespaceUri = "urn:opcfoundation.org:2026-09:InlineMethod";

        /// <summary>
        /// The setpoint the Boost handler adds its input to.
        /// </summary>
        public const double InitialSetpoint = 21.0;
    }

    /// <summary>
    /// A reference server that hosts the inline method node manager.
    /// </summary>
    public sealed class InlineMethodServer : ReferenceServer
    {
        /// <summary>
        /// Initializes the server and registers the inline method node manager.
        /// </summary>
        public InlineMethodServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            NodeManagerFactory = new InlineMethodNodeManagerCapturingFactory();
            AddNodeManager(NodeManagerFactory);
        }

        /// <summary>
        /// The factory which captured the created node manager.
        /// </summary>
        public InlineMethodNodeManagerCapturingFactory NodeManagerFactory { get; }
    }
}
