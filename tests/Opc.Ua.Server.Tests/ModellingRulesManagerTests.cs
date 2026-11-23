// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="ModellingRulesManager"/>.
    /// </summary>
    [TestFixture]
    [Category("ModellingRulesManager")]
    [Parallelizable(ParallelScope.All)]
    public class ModellingRulesManagerTests
    {
        private static Mock<IServerInternal> CreateServerMock(
            out Mock<IDiagnosticsNodeManager> diagnosticsNodeManager)
        {
            diagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            diagnosticsNodeManager
                .Setup(m => m.AddModellingRuleAsync(
                    It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.DiagnosticsNodeManager).Returns(diagnosticsNodeManager.Object);
            return server;
        }

        [Test]
        public void IsSupportedWithNullNodeIdReturnsFalse()
        {
            Mock<IServerInternal> server = CreateServerMock(out _);
            using var manager = new ModellingRulesManager(server.Object);

            Assert.That(manager.IsSupported(NodeId.Null), Is.False);
        }

        [Test]
        public void IsSupportedForUnregisteredRuleReturnsFalse()
        {
            Mock<IServerInternal> server = CreateServerMock(out _);
            using var manager = new ModellingRulesManager(server.Object);

            Assert.That(manager.IsSupported(new NodeId(1234, 1)), Is.False);
        }

        [Test]
        public async Task RegisterModellingRuleAsyncMakesRuleSupportedAsync()
        {
            Mock<IServerInternal> server = CreateServerMock(
                out Mock<IDiagnosticsNodeManager> diagnostics);
            using var manager = new ModellingRulesManager(server.Object);
            var ruleId = new NodeId(42, 1);

            await manager.RegisterModellingRuleAsync(ruleId, "MyRule").ConfigureAwait(false);

            Assert.That(manager.IsSupported(ruleId), Is.True);
            diagnostics.Verify(
                m => m.AddModellingRuleAsync(ruleId, "MyRule", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task UnregisterModellingRuleRemovesSupportAsync()
        {
            Mock<IServerInternal> server = CreateServerMock(out _);
            using var manager = new ModellingRulesManager(server.Object);
            var ruleId = new NodeId(7, 1);
            await manager.RegisterModellingRuleAsync(ruleId, "Rule").ConfigureAwait(false);
            Assert.That(manager.IsSupported(ruleId), Is.True);

            manager.UnregisterModellingRule(ruleId);

            Assert.That(manager.IsSupported(ruleId), Is.False);
        }

        [Test]
        public void UnregisterUnknownModellingRuleDoesNotThrow()
        {
            Mock<IServerInternal> server = CreateServerMock(out _);
            using var manager = new ModellingRulesManager(server.Object);

            Assert.DoesNotThrow(() => manager.UnregisterModellingRule(new NodeId(99, 1)));
        }

        [Test]
        public void DisposeIsSafe()
        {
            Mock<IServerInternal> server = CreateServerMock(out _);
            var manager = new ModellingRulesManager(server.Object);

            Assert.DoesNotThrow(manager.Dispose);
            Assert.DoesNotThrow(manager.Dispose);
        }
    }
}
