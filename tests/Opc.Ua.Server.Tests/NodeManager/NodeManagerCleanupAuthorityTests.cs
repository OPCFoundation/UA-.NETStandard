/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises the one-time host binding without starting a server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerCleanupAuthorityTests
    {
        /// <summary>
        /// Invalid constructor inputs do not reserve a partial authority binding.
        /// </summary>
        [Test]
        public async Task MissingAdaptersDoNotBindAuthorityAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            INodeManagerCleanupAuthorization authorization = Mock.Of<INodeManagerCleanupAuthorization>();
            INodeManagerCleanupVerifier verifier = Mock.Of<INodeManagerCleanupVerifier>();
            INodeManagerCleanupAudit audit = Mock.Of<INodeManagerCleanupAudit>();

            Assert.That(
                () => new NodeManagerCleanupRecovery(server, null!, verifier, audit),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("authorization"));
            Assert.That(
                () => new NodeManagerCleanupRecovery(server, authorization, null!, audit),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("verifier"));
            Assert.That(
                () => new NodeManagerCleanupRecovery(server, authorization, verifier, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("audit"));
            Assert.That(
                () => new NodeManagerCleanupRecovery(server, authorization, verifier, audit),
                Throws.Nothing);
        }

        /// <summary>
        /// Neither a second service nor a lifecycle alias can replace any bound adapter.
        /// </summary>
        [Test]
        public async Task BoundAdapterIdentitiesCannotBeReplacedAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            var authorization = new Mock<INodeManagerCleanupAuthorization>(MockBehavior.Strict);
            authorization.Setup(policy => policy.AuthorizeAsync(
                    NodeManagerCleanupOperation.Discover, default, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerCleanupAuthorization>(
                    new NodeManagerCleanupAuthorization(true, "test-operator")));
            var audit = new Mock<INodeManagerCleanupAudit>();
            INodeManagerCleanupVerifier verifier = Mock.Of<INodeManagerCleanupVerifier>();
            var recovery = new NodeManagerCleanupRecovery(server, authorization.Object, verifier, audit.Object);

            var alias = (NodeManagerLifecycle)server.NodeManagerLifecycle;
            Assert.That(alias, Is.SameAs(server.NodeManagerLifecycle));
            Assert.That(
                () => alias.BindCleanupAuthority(new NodeManagerCleanupAuthority(
                    Mock.Of<INodeManagerCleanupAuthorization>(), verifier, audit.Object)),
                Throws.InvalidOperationException);
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server, Mock.Of<INodeManagerCleanupAuthorization>(), verifier, audit.Object),
                Throws.InvalidOperationException);
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server, authorization.Object, Mock.Of<INodeManagerCleanupVerifier>(), audit.Object),
                Throws.InvalidOperationException);
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server, authorization.Object, verifier, Mock.Of<INodeManagerCleanupAudit>()),
                Throws.InvalidOperationException);

            server.SealStartupBindings();
            Assert.That(
                () => new NodeManagerCleanupRecovery(server, authorization.Object, verifier, audit.Object),
                Throws.Nothing);
            ArrayOf<RetainedNodeManagerCleanupFailure> failures = await recovery.DiscoverAsync().ConfigureAwait(false);
            Assert.That(failures.IsNull, Is.False);
            Assert.That(failures.IsEmpty, Is.True);
            audit.Verify(value => value.RecordAsync(
                It.Is<NodeManagerCleanupAuditRecord>(record =>
                    record.Operation == NodeManagerCleanupOperation.Discover &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Requested &&
                    record.AuditUserId == "test-operator"),
                It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(value => value.RecordAsync(
                It.Is<NodeManagerCleanupAuditRecord>(record =>
                    record.Operation == NodeManagerCleanupOperation.Discover &&
                    record.Outcome == NodeManagerCleanupAuditOutcome.Succeeded),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Both startup entry points seal an unconfigured server before invoking subclass callbacks.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StartupSealsUnconfiguredAuthorityBeforeCallbackAsync(bool serviceHostOverload)
        {
            await using var server = new StartupProbeServer(NUnitTelemetryContext.Create());
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());

            Exception? failure = await Assert.CatchAsync(async () =>
            {
                if (serviceHostOverload)
                {
                    _ = await server.StartAsync(configuration, default, []).ConfigureAwait(false);
                }
                else
                {
                    await server.StartAsync(configuration).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(server.StartupFailure));
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server,
                    Mock.Of<INodeManagerCleanupAuthorization>(),
                    Mock.Of<INodeManagerCleanupVerifier>(),
                    Mock.Of<INodeManagerCleanupAudit>()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Hosted attachment binds the same adapters as the direct path before publishing the owner.
        /// </summary>
        [Test]
        public async Task HostedAttachmentUsesCanonicalBindingAndRejectsConflictsAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            INodeManagerCleanupAuthorization authorization = Mock.Of<INodeManagerCleanupAuthorization>();
            INodeManagerCleanupVerifier verifier = Mock.Of<INodeManagerCleanupVerifier>();
            INodeManagerCleanupAudit audit = Mock.Of<INodeManagerCleanupAudit>();
            _ = new NodeManagerCleanupRecovery(server, authorization, verifier, audit);

            var conflicting = new HostedNodeManagerLifecycle(new NodeManagerCleanupAuthority(
                Mock.Of<INodeManagerCleanupAuthorization>(), verifier, audit));
            Assert.That(() => conflicting.Attach(server.NodeManagerLifecycle), Throws.InvalidOperationException);
            Assert.That(conflicting.GetRecoveryOwners(), Is.Empty);
            Assert.That(() => _ = conflicting.Registrations, Throws.InvalidOperationException);

            var hosted = new HostedNodeManagerLifecycle(new NodeManagerCleanupAuthority(
                authorization, verifier, audit));
            hosted.Attach(server.NodeManagerLifecycle);
            server.SealStartupBindings();
            Assert.That(hosted.GetRecoveryOwners(), Has.Length.EqualTo(1));
            Assert.That(hosted.GetRecoveryOwners()[0], Is.SameAs(server.NodeManagerLifecycle));
            Assert.That(
                () => new NodeManagerCleanupRecovery(server, authorization, verifier, audit),
                Throws.Nothing);
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server, authorization, verifier, Mock.Of<INodeManagerCleanupAudit>()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// An unconfigured hosted server cannot install permissive adapters after attachment.
        /// </summary>
        [Test]
        public async Task UnconfiguredHostedAuthorityCannotBeReplacedAsync()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(server.NodeManagerLifecycle);
            server.SealStartupBindings();
            var recovery = new NodeManagerCleanupRecovery(
                hosted.GetRecoveryOwners, hosted.ResumeCleanupAsync, hosted.CleanupAuthority);

            await Assert.ThatAsync(
                async () => await recovery.DiscoverAsync().ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(
                () => new NodeManagerCleanupRecovery(
                    server,
                    Mock.Of<INodeManagerCleanupAuthorization>(),
                    Mock.Of<INodeManagerCleanupVerifier>(),
                    Mock.Of<INodeManagerCleanupAudit>()),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Exits startup at the first overridable callback without opening a server.
        /// </summary>
        /// <param name="telemetry">The test telemetry context.</param>
        private sealed class StartupProbeServer(ITelemetryContext telemetry) : StandardServer(telemetry)
        {
            /// <summary>
            /// Gets the exact exception raised by the callback.
            /// </summary>
            internal InvalidOperationException StartupFailure { get; } = new("Startup callback reached.");

            /// <inheritdoc/>
            protected override ValueTask OnServerStartingAsync(
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                throw StartupFailure;
            }
        }
    }
}
