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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies per-controller executor registration and direct service fallback behaviour.
    /// </summary>
    [TestFixture]
    public class RobotIntentExecutorRegistrationBehaviorTests
    {
        /// <summary>
        /// An application commonly injects its executor by concrete type to observe the
        /// device it is driving. Resolving <see cref="IIntentExecutor"/> must therefore
        /// yield that same instance: a second executor would run the intents while the
        /// application watched a device that never moved.
        /// </summary>
        [Test]
        public void GenericExecutorRegistrationSharesOneInstanceWithConcreteType()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(static _ => { });

            builder.AddRobotIntentExecutor<ConstructedExecutor>();

            using ServiceProvider provider = services.BuildServiceProvider();
            var byInterface = provider.GetRequiredService<IIntentExecutor>();
            var byConcreteType = provider.GetRequiredService<ConstructedExecutor>();

            Assert.That(byInterface, Is.SameAs(byConcreteType));
        }

        [Test]
        public void NamedGenericExecutorRegistrationResolvesOnlyMatchingController()        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(static _ => { });

            builder.AddRobotIntentExecutor<ConstructedExecutor>("NamedController");

            using ServiceProvider provider = services.BuildServiceProvider();
            RobotIntentControllerExecutorRegistration registration = provider
                .GetRequiredService<RobotIntentControllerExecutorRegistration>();
            var matching = new IntentControllerState(null)
            {
                BrowseName = new QualifiedName("NamedController", 2)
            };
            var other = new IntentControllerState(null)
            {
                BrowseName = new QualifiedName("OtherController", 2)
            };

            Assert.Multiple(() =>
            {
                Assert.That(registration.TryGetExecutor(matching, out IIntentExecutor? resolved), Is.True);
                Assert.That(resolved, Is.TypeOf<ConstructedExecutor>());
                Assert.That(registration.TryGetExecutor(other, out IIntentExecutor? missing), Is.False);
                Assert.That(missing, Is.Null);
            });
        }

        [Test]
        public void NamedInstanceExecutorRegistrationValidatesInputs()
        {
            var executor = new ConstructedExecutor();
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(static _ => { });

            builder.AddRobotIntentExecutor("CellA", executor);
            using ServiceProvider provider = services.BuildServiceProvider();
            RobotIntentControllerExecutorRegistration registration = provider
                .GetRequiredService<RobotIntentControllerExecutorRegistration>();
            var controller = new IntentControllerState(null)
            {
                BrowseName = new QualifiedName("CellA", 2)
            };

            Assert.Multiple(() =>
            {
                Assert.That(registration.TryGetExecutor(controller, out IIntentExecutor? resolved), Is.True);
                Assert.That(resolved, Is.SameAs(executor));
                Assert.That(
                    () => ((IOpcUaServerBuilder)null!).AddRobotIntentExecutor<ConstructedExecutor>("CellA"),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
                Assert.That(
                    () => ((IOpcUaServerBuilder)null!).AddRobotIntentExecutor("CellA", executor),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
                Assert.That(
                    () => new RobotIntentControllerExecutorRegistration(" ", executor),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("controllerBrowseName"));
                Assert.That(
                    () => registration.TryGetExecutor(null!, out _),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("controller"));
            });
        }

        [Test]
        public async Task DirectBuildContextWithoutControllerRegistryReportsNoPerControllerExecutor()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IIntentExecutor, ConstructedExecutor>();
            using ServiceProvider provider = services.BuildServiceProvider();
            await using var fixture = new RobotIntentFixture(provider);
            await fixture.StartAsync().ConfigureAwait(false);
            var context = new RobotIntentBuildContext(
                fixture.Manager,
                fixture.Manager.Root,
                new RobotIntentServerOptions(),
                CancellationToken.None,
                provider);
            var controller = new IntentControllerState(null)
            {
                BrowseName = new QualifiedName("Unregistered", 2)
            };

            bool found = context.TryGetIntentExecutor(controller, out IIntentExecutor? executor);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.False);
                Assert.That(executor, Is.Null);
            });
        }

        [Test]
        public void IntentExecutionUsesNullNodeIdContractForUnavailableController()
        {
            var execution = new IntentExecution(
                "intent",
                new WaitIntentDataType(),
                new NullProgress());

            Assert.Multiple(() =>
            {
                Assert.That(default(NodeId).IsNull, Is.True);
                Assert.That(NodeId.Null.IsNull, Is.True);
                Assert.That(execution.ControllerId.IsNull, Is.True);
                Assert.That(execution.ControllerName, Is.Empty);
            });
        }

        [Test]
        public void IntentExecutionPreservesAvailableControllerIdentity()
        {
            var controllerId = new NodeId("controller", 2);

            var execution = new IntentExecution(
                "intent",
                new WaitIntentDataType(),
                new NullProgress(),
                controllerId,
                "Controller");

            Assert.Multiple(() =>
            {
                Assert.That(execution.ControllerId.IsNull, Is.False);
                Assert.That(execution.ControllerId, Is.EqualTo(controllerId));
                Assert.That(execution.ControllerName, Is.EqualTo("Controller"));
            });
        }

        private sealed class ConstructedExecutor : IIntentExecutor
        {
            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private sealed class NullProgress : IIntentProgress
        {
            public void ReportProgress(double fraction)
            {
            }

            public void ReportPose(Pose3DDataType pose)
            {
            }

            public void ReportTrajectoryDeviation(
                double pathPositionDeviation,
                double goalPositionDeviation,
                double elapsedMilliseconds,
                bool final)
            {
            }
        }

        private sealed class RobotIntentFixture : IAsyncDisposable
        {
            public RobotIntentFixture(IServiceProvider services)
            {
                m_services = services;
            }

            public RobotIntentNodeManager Manager { get; private set; } = null!;

            public async Task StartAsync()
            {
                m_fixture = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
                Manager = new RobotIntentNodeManager(
                    server.CurrentInstance,
                    m_fixture.Config,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    new RobotIntentServerOptions(),
                    null,
                    m_services);
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }

            private readonly IServiceProvider m_services;
            private ServerFixture<StandardServer>? m_fixture;
        }
    }
}
