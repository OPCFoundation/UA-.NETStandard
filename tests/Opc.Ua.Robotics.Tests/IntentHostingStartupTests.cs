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
                    .CreateRobotIntentBuildContext(cancellationToken);
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
