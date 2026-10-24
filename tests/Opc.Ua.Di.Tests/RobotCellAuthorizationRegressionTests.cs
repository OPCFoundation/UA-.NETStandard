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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Exercises the sample's real callback wiring without a listening server or sample animation loops.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class RobotCellAuthorizationRegressionTests
    {
        /// <summary>
        /// An anonymous write on the second robot must not reach the protected first robot's callback.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF22R2CannotMutateProtectedR1Async()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);

            ServiceResult secondRobot = await WriteAsync(harness.R2, harness.Anonymous, 25).ConfigureAwait(false);
            Assert.That(secondRobot.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(100));
            Assert.That(ReadDouble(harness.R2), Is.EqualTo(100));

            ServiceResult protectedRobot = await WriteAsync(harness.R1, harness.Anonymous, 25).ConfigureAwait(false);
            Assert.That(protectedRobot.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(100));

            ServiceResult owner = await WriteAsync(harness.R1, harness.Operator, 25).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(owner), Is.True, owner.ToString());
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(25));
            Assert.That(ReadDouble(harness.R2), Is.EqualTo(100), "R1 is the only speed-override command target");
        }

        /// <summary>
        /// The authorized command target still accepts both endpoints of the supported speed range.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(100.0)]
        [Category("SecurityRegression")]
        public async Task SecurityF22AuthorizedR1KeepsItsOwnSpeedTargetAsync(double value)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);

            ServiceResult result = await WriteAsync(harness.R1, harness.Operator, value).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(value));
            Assert.That(ReadDouble(harness.R2), Is.EqualTo(100));
            ServiceResult secondRobot = await WriteAsync(harness.R2, harness.Operator, 25).ConfigureAwait(false);
            Assert.That(secondRobot.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(value));
        }

        /// <summary>
        /// Isolating the callback must retain the builder's validation before any target is changed.
        /// </summary>
        [TestCase(-0.01)]
        [TestCase(100.01)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [Category("SecurityRegression")]
        public async Task SecurityF22InvalidSpeedDoesNotChangeEitherRobotAsync(double value)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);

            ServiceResult result = await WriteAsync(harness.R1, harness.Operator, value).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(ReadDouble(harness.R1), Is.EqualTo(100));
            Assert.That(ReadDouble(harness.R2), Is.EqualTo(100));
        }

        /// <summary>
        /// Uses the same asynchronous Value-attribute entry point as the server's Write service.
        /// </summary>
        private static ValueTask<ServiceResult> WriteAsync(
            BaseVariableState variable,
            ISystemContext context,
            double value)
        {
            return variable.WriteAttributeAsync(context, Attributes.Value, default, new DataValue(Variant.From(value)));
        }

        /// <summary>
        /// Reads a speed value without boxing the Variant.
        /// </summary>
        private static double ReadDouble(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out double value), Is.True);
            return value;
        }

        /// <summary>
        /// Hosts the actual Robotics model and RobotCell configuration on in-memory server services.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates a model owner and two distinct request contexts.
            /// </summary>
            private Harness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var server = new Mock<IServerInternal>();
                var master = new Mock<IMasterNodeManager>();
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:security:f22:server");
                m_queues = new MonitoredItemQueueFactory(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NodeManager).Returns(master.Object);
                server.SetupGet(value => value.SessionManager).Returns(Mock.Of<ISessionManager>());
                server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(m_queues);
                master.SetupGet(value => value.ConfigurationNodeManager)
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

                var configuration = new ApplicationConfiguration(telemetry)
                {
                    ApplicationUri = "urn:security:f22:server",
                    ServerConfiguration = new ServerConfiguration()
                };
                m_core = new CoreNodeManager(server.Object, configuration);
                server.SetupGet(value => value.CoreNodeManager).Returns(m_core);
                master.SetupGet(value => value.CoreNodeManager).Returns(m_core);
                m_manager = new RoboticsNodeManager(
                    server.Object,
                    configuration,
                    [
                        new RoboticsModelProvider(),
                        new global::Robotics.OpenUsdModelProvider(),
                        new global::Robotics.RslModelProvider(),
                        new global::Robotics.GposModelProvider()
                    ],
                    new RoboticsServerOptions
                    {
                        InstanceNamespaceUri = "urn:security:f22:instances"
                    });
                m_cell = new global::Robotics.RobotCell(
                    telemetry.CreateLogger<global::Robotics.RobotCell>(),
                    new global::Robotics.CellChoreographer(faultProbability: 0));

                Anonymous = m_manager.SystemContext.Copy();
                Anonymous.SessionId = new NodeId("anonymous", 1);
                Anonymous.UserIdentity = new UserIdentity();
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                identity.SetupGet(value => value.DisplayName).Returns("operator");
                identity.SetupGet(value => value.GrantedRoleIds)
                    .Returns([Ua.ObjectIds.WellKnownRole_AuthenticatedUser]);
                Operator = m_manager.SystemContext.Copy();
                Operator.SessionId = new NodeId("operator", 1);
                Operator.UserIdentity = identity.Object;
            }

            /// <summary>
            /// Gets the anonymous request context.
            /// </summary>
            public ServerSystemContext Anonymous { get; }

            /// <summary>
            /// Gets the authenticated request context.
            /// </summary>
            public ServerSystemContext Operator { get; }

            /// <summary>
            /// Gets R1's real command variable.
            /// </summary>
            public BaseVariableState R1 { get; private set; } = null!;

            /// <summary>
            /// Gets R2's real observation variable.
            /// </summary>
            public BaseVariableState R2 { get; private set; } = null!;

            /// <summary>
            /// Builds the real model and sample without starting a server or the animations.
            /// </summary>
            public static async ValueTask<Harness> CreateAsync()
            {
                var harness = new Harness();
                try
                {
                    await harness.m_core.ImportNodesAsync(
                        harness.m_core.SystemContext,
                        new NodeStateCollection().AddOpcUa(harness.m_core.SystemContext),
                        isInternal: true).ConfigureAwait(false);
                    await harness.m_manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    IRoboticsBuildContext context = harness.m_manager.CreateRoboticsBuildContext();
                    bool materialised = await harness.m_cell.ConfigureAddressSpaceAsync(context, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(materialised, Is.True, "the real sample cell must have been built successfully");
                    NodeState cell = harness.Child(context.DeviceSet, "RobotCell");
                    NodeState devices = harness.Child(cell, "MotionDevices");
                    harness.R1 = harness.SpeedOverride(harness.Child(devices, "R1"));
                    harness.R2 = harness.SpeedOverride(harness.Child(devices, "R2"));
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_cell.Dispose();
                try
                {
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await m_core.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_queues.Dispose();
                    }
                }
            }

            /// <summary>
            /// Finds the actual variable under a robot's ParameterSet.
            /// </summary>
            private BaseVariableState SpeedOverride(NodeState robot)
            {
                return (BaseVariableState)Child(Child(robot, "ParameterSet"), "SpeedOverride");
            }

            /// <summary>
            /// Resolves a materialised child without depending on model namespace indices.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The requested child is absent from the materialised parent.
            /// </exception>
            private BaseInstanceState Child(NodeState parent, string browseName)
            {
                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_manager.SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    if (string.Equals(child.BrowseName.Name, browseName, StringComparison.Ordinal))
                    {
                        return child;
                    }
                }
                throw new InvalidOperationException($"Missing {parent.BrowseName}/{browseName}.");
            }

            /// <summary>The real address-space owner.</summary>
            private readonly RoboticsNodeManager m_manager;

            /// <summary>The offline owner of the real OPC UA base model.</summary>
            private readonly CoreNodeManager m_core;

            /// <summary>The real sample configurator.</summary>
            private readonly global::Robotics.RobotCell m_cell;

            /// <summary>The test-owned monitored-item queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
