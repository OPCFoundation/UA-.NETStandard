/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies executor resolution for multiple Robot Intent controllers.
    /// </summary>
    [TestFixture]
    public class IntentControllerExecutorResolutionTests
    {
        [Test]
        public async Task SharedDiExecutorReceivesControllerIdentityForTwoControllers()
        {
            var executor = new SharedIdentityExecutor();
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(static _ => { })
                .AddRobotIntent();
            services.AddSingleton<IIntentExecutor>(executor);
            using ServiceProvider provider = services.BuildServiceProvider();
            await using var fixture = new RobotIntentFixture(provider);
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext();

            (IIntentControllerBuilder left, IIntentControllerBuilder right) =
                await AddTwoControllersAsync(context).ConfigureAwait(false);
            fixture.Manager.StartIntentControllerHosts();
            SubmitAccepted(context, left, "left-intent");
            SubmitAccepted(context, right, "right-intent");

            await WaitAsync(() => executor.Records.Count >= 2).ConfigureAwait(false);
            ExecutionRecord[] records = [.. executor.Records];

            Assert.Multiple(() =>
            {
                Assert.That(records.Select(static record => record.ControllerName), Does.Contain("LeftArm"));
                Assert.That(records.Select(static record => record.ControllerName), Does.Contain("RightArm"));
                Assert.That(records.Single(record => record.IntentId == "left-intent").ControllerId,
                    Is.EqualTo(left.State.NodeId));
                Assert.That(records.Single(record => record.IntentId == "right-intent").ControllerId,
                    Is.EqualTo(right.State.NodeId));
                Assert.That(records.All(static record => !record.ControllerId.IsNull), Is.True);
            });
        }

        [Test]
        public async Task PerControllerDiExecutorsDriveTwoControllersIndependently()
        {
            var leftExecutor = new NamedExecutor();
            var rightExecutor = new NamedExecutor();
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(static _ => { })
                .AddRobotIntent()
                .AddRobotIntentExecutor("LeftArm", leftExecutor)
                .AddRobotIntentExecutor("RightArm", rightExecutor);
            using ServiceProvider provider = services.BuildServiceProvider();
            await using var fixture = new RobotIntentFixture(provider);
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext();

            (IIntentControllerBuilder left, IIntentControllerBuilder right) =
                await AddTwoControllersAsync(context).ConfigureAwait(false);
            fixture.Manager.StartIntentControllerHosts();
            SubmitAccepted(context, left, "left-only");
            SubmitAccepted(context, right, "right-only");

            await WaitAsync(() => leftExecutor.IntentIds.Contains("left-only") &&
                rightExecutor.IntentIds.Contains("right-only")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(leftExecutor.IntentIds.ToArray(), Is.EqualTo(s_stringValues1));
                Assert.That(rightExecutor.IntentIds.ToArray(), Is.EqualTo(s_stringValues2));
            });
        }

        [Test]
        public async Task MissingExecutorStillFailsAtBuildTime()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(static _ => { })
                .AddRobotIntent();
            using ServiceProvider provider = services.BuildServiceProvider();
            await using var fixture = new RobotIntentFixture(provider);
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext();

            Exception? error = null;
            try
            {
                await context.AddIntentControllerAsync(
                    "NoExecutor",
                    static controller => controller.Accepts<WaitIntentDataType>()).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                error = ex;
            }

            Assert.That(error, Is.TypeOf<InvalidOperationException>()
                .With.Message.Contains("No Robot Intent executor is registered"));
        }

        private static async ValueTask<(IIntentControllerBuilder Left, IIntentControllerBuilder Right)>
            AddTwoControllersAsync(IRobotIntentBuildContext context)
        {
            IIntentControllerBuilder left = await context.AddIntentControllerAsync(
                "LeftArm",
                static controller => controller.Accepts<WaitIntentDataType>()).ConfigureAwait(false);
            IIntentControllerBuilder right = await context.AddIntentControllerAsync(
                "RightArm",
                static controller => controller.Accepts<WaitIntentDataType>()).ConfigureAwait(false);
            return (left, right);
        }

        private static void SubmitAccepted(
            IRobotIntentBuildContext context,
            IIntentControllerBuilder controller,
            string intentId)
        {
            var sessionId = new NodeId(intentId + "-session", 2);
            Assert.That(controller.Host.RequestControl(context.Context, sessionId, out _), Is.True);
            IntentAdmission admission = controller.Host.SubmitIntent(context.Context, sessionId, new WaitIntentDataType
            {
                IntentId = intentId,
                Duration = 1.0
            });
            Assert.That(admission.Accepted, Is.True);
        }

        private static async Task WaitAsync(Func<bool> condition)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("timed out waiting for the expected condition");
        }

        private readonly record struct ExecutionRecord(string IntentId, NodeId ControllerId, string ControllerName);

        private sealed class SharedIdentityExecutor : IIntentExecutor
        {
            public ConcurrentQueue<ExecutionRecord> Records { get; } = new();

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                Records.Enqueue(new ExecutionRecord(
                    execution.IntentId,
                    execution.ControllerId,
                    execution.ControllerName));
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private sealed class NamedExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> IntentIds { get; } = new();

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                IntentIds.Enqueue(execution.IntentId);
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
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

        private static readonly string[] s_stringValues1 = ["left-only"];
        private static readonly string[] s_stringValues2 = ["right-only"];
    }
}
