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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that <see cref="ConfigurationNodeManager"/> deterministically
    /// drains or cancels the deferred post-<c>ApplyChanges</c> work
    /// (OPC 10000-12 §7.10.9) during server shutdown / disposal so it never
    /// runs against listeners or managers that are about to be torn down.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class ConfigurationNodeManagerShutdownTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DeleteAddressSpaceAsyncCancelsDeferredApplyGracePeriodAsync()
        {
            // A long grace period is configured so that, if shutdown failed to
            // cancel it, DeleteAddressSpaceAsync would block for the full grace.
            // The isolated manager shares the running server's serverInternal
            // but owns its own (empty) address space, so tearing it down does
            // not disturb the shared fixture server.
            IServerInternal serverInternal = m_server.CurrentInstance;
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            using var manager = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator,
                pendingKeyStore: null)
            {
                ApplyChangesGracePeriod = TimeSpan.FromSeconds(30)
            };

            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var certificateType = new NodeId(Guid.NewGuid(), 1);
            using Certificate rotated = CertificateBuilder
                .Create("CN=ShutdownGrace " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            coordinator.Stage(sessionId, new PushConfigurationOperation
            {
                AffectedCertificateType = certificateType,
                CommitAsync = _ =>
                {
                    InvokeRegisterPendingRotation(manager, certificateType, rotated);
                    return Task.CompletedTask;
                }
            });

            ServiceResult result = await InvokeApplyChangesAsync(manager, sessionId).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);

            var stopwatch = Stopwatch.StartNew();
            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
            stopwatch.Stop();

            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(10)),
                "shutdown must cancel the 30s grace period rather than waiting it out");

            // The manager's `using` scope disposes it at method end; the
            // deferred task is already drained (asserted below), so CA2025's
            // "a task may use a disposed instance" cannot actually occur here.
#pragma warning disable CA2025
            Task pending = GetPendingApplyChangesTask(manager);
#pragma warning restore CA2025
            Assert.That(pending.IsCompleted, Is.True, "the deferred apply must be drained after DeleteAddressSpaceAsync");
        }

        [Test]
        public async Task DisposeSignalsCancellationOfPendingDeferredApplyAsync()
        {
            // Dispose must never block on async work, but it still signals the
            // pending deferred apply to stop (covering the direct-construction
            // path where DeleteAddressSpaceAsync is not invoked). The task then
            // completes without waiting the full grace period.
            IServerInternal serverInternal = m_server.CurrentInstance;
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            var manager = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator,
                pendingKeyStore: null)
            {
                ApplyChangesGracePeriod = TimeSpan.FromSeconds(30)
            };

            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var certificateType = new NodeId(Guid.NewGuid(), 1);
            using Certificate rotated = CertificateBuilder
                .Create("CN=ShutdownDispose " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            coordinator.Stage(sessionId, new PushConfigurationOperation
            {
                AffectedCertificateType = certificateType,
                CommitAsync = _ =>
                {
                    InvokeRegisterPendingRotation(manager, certificateType, rotated);
                    return Task.CompletedTask;
                }
            });

            Task pending;
            try
            {
                ServiceResult result = await InvokeApplyChangesAsync(manager, sessionId).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                // Dispose (below) intentionally races the still-in-flight
                // deferred apply: this test verifies Dispose only signals
                // cancellation and never blocks, with the task completing
                // afterwards. CA2025 (a task may use a disposed manager) is
                // therefore expected and intentional here.
#pragma warning disable CA2025
                pending = GetPendingApplyChangesTask(manager);
#pragma warning restore CA2025
                Assert.That(pending.IsCompleted, Is.False, "the deferred apply is still in its grace period");
            }
            finally
            {
                manager.Dispose();
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(pending.IsCompleted, Is.True);
        }

        [Test]
        public async Task DeleteAddressSpaceAsyncDrainsInProgressDeferredEffectsAsync()
        {
            // A blocking §7.10.9 effect handler simulates deferred effects that
            // are already running when shutdown begins. DeleteAddressSpaceAsync
            // must wait for them to finish (drain) rather than returning while
            // they still run against a manager/server being torn down. Uses a
            // dedicated server that this test alone tears down.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                IServerInternal serverInternal = server.CurrentInstance;
                var configManager = (ConfigurationNodeManager)serverInternal.ConfigurationNodeManager!;
                NodeState? node = await serverInternal.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = (ServerConfigurationState)node!;

                var handler = new BlockingEffectHandler();
                ReplaceEffectHandler(configManager, handler);
                configManager.ApplyChangesGracePeriod = TimeSpan.Zero;

                var sessionId = new NodeId(Guid.NewGuid(), 1);
                IPushConfigurationTransactionCoordinator coordinator = GetCoordinator(configManager);
                coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList =
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                    CommitAsync = _ => Task.CompletedTask
                });

                ServiceResult applyResult = await configNode.ApplyChanges!.OnCallMethod2Async!(
                    CreateAdminContextForSession(sessionId),
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    ArrayOf<Variant>.Empty,
                    new List<Variant>(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(applyResult), Is.True);

                using (var enteredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await handler.Entered.WaitAsync(enteredTimeout.Token).ConfigureAwait(false);
                }

                Task delete = configManager.DeleteAddressSpaceAsync(CancellationToken.None).AsTask();
                Assert.That(
                    delete.IsCompleted,
                    Is.False,
                    "shutdown must wait for the in-progress deferred effect to drain");

                handler.Release();

                using (var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await delete.WaitAsync(drainTimeout.Token).ConfigureAwait(false);
                }

                Assert.That(handler.InvocationCount, Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ServiceResult> InvokeApplyChangesAsync(
            ConfigurationNodeManager manager,
            NodeId sessionId)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "ApplyChangesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method ApplyChangesAsync not found.");

            var valueTask = (ValueTask<ServiceResult>)method.Invoke(
                manager,
                [
                    CreateAdminContextForSession(sessionId),
                    null,
                    NodeId.Null,
                    ArrayOf<Variant>.Empty,
                    new List<Variant>(),
                    CancellationToken.None
                ])!;
            return await valueTask.ConfigureAwait(false);
        }

        private static void InvokeRegisterPendingRotation(
            ConfigurationNodeManager manager,
            NodeId certificateType,
            Certificate oldCertificateWithKey)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "RegisterPendingRotation",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method RegisterPendingRotation not found.");
            method.Invoke(manager, [certificateType, oldCertificateWithKey]);
        }

        private static Task GetPendingApplyChangesTask(ConfigurationNodeManager manager)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_pendingApplyChangesTask",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_pendingApplyChangesTask not found.");
            return (Task)field.GetValue(manager)!;
        }

        private static IPushConfigurationTransactionCoordinator GetCoordinator(ConfigurationNodeManager manager)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_coordinator", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_coordinator not found.");
            return (IPushConfigurationTransactionCoordinator)field.GetValue(manager)!;
        }

        private static void ReplaceEffectHandler(
            ConfigurationNodeManager manager,
            IPushConfigurationTrustListEffectHandler handler)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_trustListEffectHandler", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_trustListEffectHandler not found.");
            field.SetValue(manager, handler);
        }

        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds)
                .Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private sealed class BlockingEffectHandler : IPushConfigurationTrustListEffectHandler
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Entered => m_entered.Task;

            public int InvocationCount { get; private set; }

            public void Release()
            {
                m_release.TrySetResult(true);
            }

            public async ValueTask ApplyAsync(
                PushConfigurationTrustListEffectContext context,
                CancellationToken cancellationToken = default)
            {
                InvocationCount++;
                m_entered.TrySetResult(true);
                await m_release.Task.ConfigureAwait(false);
            }
        }
    }
}
