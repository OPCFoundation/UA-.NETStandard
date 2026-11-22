// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public InlineMethodNodeManager Manager { get; private set; } = null!;

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
