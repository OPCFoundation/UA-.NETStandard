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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Nodes
{
    [TestFixture]
    [Category("NodeSource")]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class NodeBehaviorLifecycleIntegrationTests
    {
        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(NodeBehaviorLifecycleIntegrationTests),
                Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<ReferenceServer>(
                telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_server?.Dispose();
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public async Task CreationFailureUnwindsActivatedAndUnactivatedLeasesAsync()
        {
            var failure = new InvalidOperationException("factory creation failed");
            var recorder = new NodeBehaviorTestRecorder();
            var source = new NodeBehaviorTestSource(
                recorder,
                includeSibling: false,
                createFailure: (node, factory) =>
                    node == "Parent" && factory == "derived"
                        ? failure
                        : null);
            int registrationsBefore =
                m_server.NodeManagerLifecycle.Registrations.Count;

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(source)
                        .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(failure));
                Assert.That(
                    recorder.GetEvents(),
                    Is.EqualTo(s_creationFailureEvents));
                Assert.That(
                    recorder.GetLeases().Single(lease =>
                        lease.NodeName == "Parent" &&
                        lease.FactoryName == "base").ActivateCount,
                    Is.Zero);
                Assert.That(
                    recorder.GetLeases().All(lease => lease.DisposeCount == 1),
                    Is.True);
                Assert.That(
                    m_server.NodeManagerLifecycle.Registrations.Count,
                    Is.EqualTo(registrationsBefore));
            });
            NodeState visible = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(source.ParentId)
                .ConfigureAwait(false);
            Assert.That(visible, Is.Null);
        }

        [TestCase(ActivationFailureKind.Exception)]
        [TestCase(ActivationFailureKind.Cancellation)]
        public void ActivationFailureUnwindsOnceDuringHostRollback(
            ActivationFailureKind failureKind)
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("activation failed");
            var recorder = new NodeBehaviorTestRecorder();
            var source = new NodeBehaviorTestSource(
                recorder,
                includeSibling: false,
                leaseOptions: (node, factory) =>
                    node == "Parent" && factory == "derived"
                        ? new NodeBehaviorTestLeaseOptions
                        {
                            ActivationException = failureKind ==
                                ActivationFailureKind.Exception
                                    ? failure
                                    : null,
                            CancelActivation = failureKind ==
                                ActivationFailureKind.Cancellation
                                    ? cancellation.Cancel
                                    : null
                        }
                        : null);
            int registrationsBefore =
                m_server.NodeManagerLifecycle.Registrations.Count;

            if (failureKind == ActivationFailureKind.Exception)
            {
                InvalidOperationException exception =
                    Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await m_server.NodeManagerLifecycle
                            .AddNodeSourceAsync(source)
                            .ConfigureAwait(false));
                Assert.That(exception, Is.SameAs(failure));
            }
            else
            {
                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(
                            source,
                            callerContext: null,
                            cancellation.Token)
                        .ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    recorder.GetEvents(),
                    Is.EqualTo(s_activationFailureEvents));
                Assert.That(
                    recorder.GetLeases().All(lease =>
                        lease.ActivateCount <= 1 &&
                        lease.DeactivateCount <= 1 &&
                        lease.DisposeCount == 1),
                    Is.True);
                Assert.That(
                    recorder.GetLeases().Single(lease =>
                        lease.NodeName == "Parent" &&
                        lease.FactoryName == "derived").DeactivateCount,
                    Is.Zero);
                Assert.That(
                    m_server.NodeManagerLifecycle.Registrations.Count,
                    Is.EqualTo(registrationsBefore));
            });
        }

        [Test]
        public async Task RuntimeImportedTypedNodeBehaviorCleansUpOnRemovalAsync()
        {
            var recorder = new NodeBehaviorTestRecorder();
            var source = new ImportedNodeBehaviorTestSource(recorder);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(source)
                .ConfigureAwait(false);
            NodeBehaviorTestLease lease = recorder.GetLeases().Single();
            NodeState visible = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(source.NodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(source.Node, Is.TypeOf<ImportedBehaviorObjectState>());
                Assert.That(visible, Is.SameAs(source.Node));
                Assert.That(lease.Context.Node, Is.SameAs(source.Node));
                Assert.That(lease.IsActive, Is.True);
                Assert.That(lease.ActivateCount, Is.EqualTo(1));
            });

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, callerContext: null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(lease.IsActive, Is.False);
                Assert.That(lease.DeactivateCount, Is.EqualTo(1));
                Assert.That(lease.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task RuntimeImportedTypedNodeBehaviorRollsBackActivationFailureAsync()
        {
            var failure = new InvalidOperationException("imported behavior activation failed");
            var recorder = new NodeBehaviorTestRecorder();
            var source = new ImportedNodeBehaviorTestSource(
                recorder,
                (_, _) => new NodeBehaviorTestLeaseOptions
                {
                    ActivationException = failure
                });
            int registrationsBefore =
                m_server.NodeManagerLifecycle.Registrations.Count;

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddNodeSourceAsync(source)
                        .ConfigureAwait(false));
            NodeBehaviorTestLease lease = recorder.GetLeases().Single();
            NodeState visible = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(source.NodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(failure));
                Assert.That(lease.Context.Node, Is.SameAs(source.Node));
                Assert.That(lease.ActivateCount, Is.EqualTo(1));
                Assert.That(lease.DeactivateCount, Is.Zero);
                Assert.That(lease.DisposeCount, Is.EqualTo(1));
                Assert.That(visible, Is.Null);
                Assert.That(
                    m_server.NodeManagerLifecycle.Registrations.Count,
                    Is.EqualTo(registrationsBefore));
            });
        }

        [TestCase(ReloadKind.Reload)]
        [TestCase(ReloadKind.Shadow)]
        public async Task ReloadActivatesReplacementBeforeVisibilityAsync(
            ReloadKind reloadKind)
        {
            var recorder = new NodeBehaviorTestRecorder();
            var initial = new NodeBehaviorTestSource(
                recorder,
                includeChild: false,
                includeSibling: false);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddNodeSourceAsync(initial)
                .ConfigureAwait(false);
            NodeBehaviorTestLease oldDerived = recorder.GetLeases().Single(lease =>
                ReferenceEquals(lease.Context.Source, initial) &&
                lease.FactoryName == "derived");
            NodeBehaviorContext oldContext = oldDerived.Context;
            bool oldWasActiveDuringReplacement = false;
            bool replacementWasVisibleDuringActivation = false;

            var replacement = new NodeBehaviorTestSource(
                recorder,
                includeChild: false,
                includeSibling: false,
                leaseOptions: (node, factory) =>
                    node == "Parent" && factory == "derived"
                        ? new NodeBehaviorTestLeaseOptions
                        {
                            OnActivateAsync = async (context, ct) =>
                            {
                                oldWasActiveDuringReplacement = oldDerived.IsActive;
                                NodeState visible = await m_server.CurrentInstance.NodeManager
                                    .FindNodeInAddressSpaceAsync(
                                        context.Node.NodeId,
                                        ct)
                                    .ConfigureAwait(false);
                                replacementWasVisibleDuringActivation =
                                    ReferenceEquals(visible, context.Node);
                            }
                        }
                        : null);

            registration = reloadKind == ReloadKind.Reload
                ? await m_server.NodeManagerLifecycle
                    .ReloadNodeSourceAsync(registration, replacement)
                    .ConfigureAwait(false)
                : await m_server.NodeManagerLifecycle
                    .ShadowReloadNodeSourceAsync(registration, replacement)
                    .ConfigureAwait(false);
            NodeBehaviorTestLease replacementDerived =
                recorder.GetLeases().Single(lease =>
                    ReferenceEquals(lease.Context.Source, replacement) &&
                    lease.FactoryName == "derived");
            NodeState committed = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(replacement.ParentId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(registration.Generation, Is.EqualTo(2));
                Assert.That(oldWasActiveDuringReplacement, Is.True);
                Assert.That(replacementWasVisibleDuringActivation, Is.False);
                Assert.That(replacementDerived.IsActive, Is.True);
                Assert.That(committed, Is.SameAs(replacement.Parent));
                Assert.That(
                    replacementDerived.Context.Generation.SourceId,
                    Is.EqualTo(oldContext.Generation.SourceId));
                Assert.That(
                    replacementDerived.Context.Generation.Generation,
                    Is.EqualTo(2));
            });

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, callerContext: null)
                .ConfigureAwait(false);

            string[] events = recorder.GetEvents();
            int replacementActivation = Array.LastIndexOf(
                events,
                "activate:Parent:derived");
            int oldDeactivation = Array.IndexOf(
                events,
                "deactivate:Parent:derived");
            Assert.Multiple(() =>
            {
                Assert.That(replacementActivation, Is.GreaterThanOrEqualTo(0));
                Assert.That(oldDeactivation, Is.GreaterThan(replacementActivation));
                Assert.That(
                    recorder.GetLeases().All(lease =>
                        !lease.IsActive &&
                        lease.DeactivateCount == 1 &&
                        lease.DisposeCount == 1),
                    Is.True);
            });
        }

        public enum ActivationFailureKind
        {
            Exception,
            Cancellation
        }

        public enum ReloadKind
        {
            Reload,
            Shadow
        }

        private static readonly string[] s_creationFailureEvents =
        [
            "create:Child:base",
            "create:Child:derived",
            "activate:Child:base",
            "activate:Child:derived",
            "create:Parent:base",
            "create:Parent:derived",
            "deactivate:Child:derived",
            "deactivate:Child:base",
            "dispose:Parent:base",
            "dispose:Child:derived",
            "dispose:Child:base"
        ];

        private static readonly string[] s_activationFailureEvents =
        [
            "create:Child:base",
            "create:Child:derived",
            "activate:Child:base",
            "activate:Child:derived",
            "create:Parent:base",
            "create:Parent:derived",
            "activate:Parent:base",
            "activate:Parent:derived",
            "deactivate:Parent:base",
            "deactivate:Child:derived",
            "deactivate:Child:base",
            "dispose:Parent:derived",
            "dispose:Parent:base",
            "dispose:Child:derived",
            "dispose:Child:base"
        ];
    }
}
