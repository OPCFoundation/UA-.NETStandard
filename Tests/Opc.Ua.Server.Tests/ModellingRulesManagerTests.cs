/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

            Assert.DoesNotThrow(() => manager.Dispose());
            Assert.DoesNotThrow(() => manager.Dispose());
        }
    }
}
