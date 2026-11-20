/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies Robot Intent hosts are live after address-space creation, without an external startup task.
    /// </summary>
    [TestFixture]
    public class IntentHostingStartupTests
    {
        [Test]
        public async Task AddressSpaceCreationStartsControllerHost()
        {
            var fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var runner = new StartupProbeRunner();
                var manager = new RobotIntentNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    new RobotIntentServerOptions(),
                    runner);

                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                RequestControlMethodStateResult result = await runner.Controller!.RequestControl!.OnCallAsync!(
                    manager.SystemContext,
                    runner.Controller.RequestControl,
                    runner.Controller.NodeId,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(runner.GuardResult, Is.Not.Null);
                    Assert.That(runner.GuardResult!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(result.Granted, Is.False);
                });
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class StartupProbeRunner : IRobotIntentPostSetupRunner
        {
            public IntentControllerState? Controller { get; private set; }

            public ServiceResult? GuardResult { get; private set; }

            public async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                IRobotIntentBuildContext context = ((RobotIntentNodeManager)manager)
                    .CreateRobotIntentBuildContext(new TestIntentExecutor(), cancellationToken);
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "Controller",
                    controller => controller
                        .WithMaxQueueDepth(1)
                        .Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                Controller = builder.State;
                RequestControlMethodStateResult result = await Controller.RequestControl!.OnCallAsync!(
                    context.Context,
                    Controller.RequestControl,
                    Controller.NodeId,
                    cancellationToken).ConfigureAwait(false);
                GuardResult = result.ServiceResult;
            }
        }
    }
}
