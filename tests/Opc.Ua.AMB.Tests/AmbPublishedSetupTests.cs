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
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Exercises interrupted published authoring through the real lifecycle readiness and retirement paths.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AmbPublishedSetupTests
    {
        /// <summary>
        /// A later root failure leaves earlier roots owned, resumable and releasable without replaying setup.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task PublishedSetupResumesAfterInterruptedRootRegistrationAsync(bool cancel, bool stop)
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                int callbacks = 0;
                INodeManagerBuilder? setupBuilder = null;
                var first = new InterruptedRoot("First");
                var second = new InterruptedRoot("Second");
                Exception failure = cancel
                    ? new OperationCanceledException(new CancellationToken(canceled: true))
                    : new InvalidOperationException("Injected second-root registration failure.");
                var resource = new Mock<IAsyncDisposable>(MockBehavior.Strict);
                resource.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
                var services = new ServiceCollection();
                services.AddSingleton(new AmbSetupConfigurator(context =>
                {
                    callbacks++;
                    setupBuilder = context.Builder;
                    ushort ns = context.Manager.InstanceNamespaceIndex;
                    first.BrowseName = new QualifiedName("First", ns);
                    second.BrowseName = new QualifiedName("Second", ns);
                    context.Builder.Add(first);
                    context.Builder.Add(second);
                    second.Failure = failure;
                    return default;
                }));
                using ServiceProvider provider = services.BuildServiceProvider();
                var assets = new AssetManagement();
                await using IPreparedNodeManagerBatch prepared = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new AmbNodeManagerFactory(assets, provider))])
                    .ConfigureAwait(false);
                NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                AmbNodeManager manager = assets.Manager!;

                Assert.Multiple(() =>
                {
                    Assert.That(committed.Issues.Count, Is.EqualTo(1));
                    Assert.That(committed.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.Readiness));
                    Assert.That(committed.Issues[0].CurrentFailure, Is.SameAs(failure));
                    Assert.That(manager.FindPredefinedNode<NodeState>(first.NodeId), Is.SameAs(first),
                        "The first root was indexed before registration of the second root failed.");
                    Assert.That(callbacks, Is.EqualTo(1));
                });
                ServiceResultException? authoringException = Assert.Throws<ServiceResultException>(
                    () => setupBuilder!.AddFolder("TooLate"));
                Assert.That(authoringException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.DoesNotThrow(() => setupBuilder!.Node("First"),
                    "A failed registration has closed authoring but has not sealed configuration.");
                int firstRootVisits = first.Visits;
                manager.RegisterNodeResource(first, resource.Object);

                if (!stop)
                {
                    await committed.RetryReconciliationAsync().ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(committed.Issues.ToArray(), Is.Empty);
                        Assert.That(manager.FindPredefinedNode<NodeState>(second.NodeId), Is.SameAs(second));
                        Assert.That(first.Visits, Is.EqualTo(firstRootVisits),
                            "Retry must not register the completed first root again.");
                        Assert.That(callbacks, Is.EqualTo(1));
                    });
                    ServiceResultException? sealedException = Assert.Throws<ServiceResultException>(
                        () => setupBuilder!.Node("First"));
                    Assert.That(sealedException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                }
                await using IPreparedNodeManagerBatch removal = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(committed.Registrations[0])]).ConfigureAwait(false);
                NodeManagerBatchResult removed = await removal.CommitAsync(_ => default).ConfigureAwait(false);
                await removed.Retirement.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(removed.Issues.ToArray(), Is.Empty);
                    Assert.That(assets.Manager, Is.Null);
                    Assert.That(callbacks, Is.EqualTo(1), "Stopping must not retry configuration.");
                });
                resource.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A partially applied external-reference group is retryable without replaying completed groups.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedSetupResumesAfterInterruptedReferencePublicationAsync(bool stop)
        {
            var fixture = new ServerFixture<ReferenceFaultServer>(telemetry => new ReferenceFaultServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            ReferenceFaultServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                int callbacks = 0;
                var services = new ServiceCollection();
                services.AddSingleton(new AmbSetupConfigurator(context =>
                {
                    callbacks++;
                    server.Master.FirstTarget = context.Builder.AddFolder("First").Node.NodeId;
                    server.Master.SecondTarget = context.Builder.AddFolder(
                        "Second", Ua.ObjectIds.Locations).Node.NodeId;
                    return default;
                }));
                using ServiceProvider provider = services.BuildServiceProvider();
                var assets = new AssetManagement();
                await using IPreparedNodeManagerBatch prepared = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new AmbNodeManagerFactory(assets, provider))])
                    .ConfigureAwait(false);
                NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(committed.Issues.Count, Is.EqualTo(1));
                    Assert.That(committed.Issues[0].CurrentFailure, Is.SameAs(server.Master.Failure));
                    Assert.That(server.Master.FirstCalls, Is.EqualTo(1));
                    Assert.That(server.Master.SecondCalls, Is.EqualTo(1));
                });

                if (!stop)
                {
                    await committed.RetryReconciliationAsync().ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(committed.Issues.ToArray(), Is.Empty);
                        Assert.That(server.Master.FirstCalls,
                            Is.EqualTo(server.Master.FailedTarget == server.Master.FirstTarget ? 2 : 1));
                        Assert.That(server.Master.SecondCalls,
                            Is.EqualTo(server.Master.FailedTarget == server.Master.SecondTarget ? 2 : 1));
                        Assert.That(callbacks, Is.EqualTo(1));
                    });
                    AssertExternalReferenceCount(server, 1);
                }
                await using IPreparedNodeManagerBatch removal = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(committed.Registrations[0])]).ConfigureAwait(false);
                NodeManagerBatchResult removed = await removal.CommitAsync(_ => default).ConfigureAwait(false);
                await removed.Retirement.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(assets.Manager, Is.Null);
                Assert.That(callbacks, Is.EqualTo(1));
                AssertExternalReferenceCount(server, 0);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks actual serving references for duplicates after retry and residue after retirement.
        /// </summary>
        private static void AssertExternalReferenceCount(ReferenceFaultServer server, int expected)
        {
            var core = (CoreNodeManager)server.Master.CoreNodeManager!;
            foreach (NodeId parent in new[] { Ua.ObjectIds.ObjectsFolder, Ua.ObjectIds.Locations })
            {
                var references = new List<IReference>();
                core.FindPredefinedNode<NodeState>(parent)!
                    .GetReferences(server.CurrentInstance.DefaultSystemContext, references);
                NodeId target = parent == Ua.ObjectIds.ObjectsFolder
                    ? server.Master.FirstTarget
                    : server.Master.SecondTarget;
                Assert.That(references.Count(reference => reference.TargetId == target), Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Fails one traversal only after the configure callback has handed off the staged roots.
        /// </summary>
        private sealed class InterruptedRoot : BaseObjectState
        {
            public InterruptedRoot(string name)
                : base(parent: null)
            {
                DisplayName = new LocalizedText(name);
            }

            public Exception? Failure { get; set; }

            public int Visits { get; private set; }

            public override void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
            {
                Visits++;
                if (Failure is { } failure)
                {
                    Failure = null;
                    throw failure;
                }
                base.GetChildren(context, children);
            }
        }

        /// <summary>
        /// Installs reference fault injection without replacing the owning lifecycle.
        /// </summary>
        private sealed class ReferenceFaultServer : StandardServer
        {
            public ReferenceFaultServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public ReferenceFaultMaster Master { get; private set; } = null!;

            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Master = new ReferenceFaultMaster(server, configuration);
                return new ValueTask<IMasterNodeManager>(Master);
            }
        }

        /// <summary>
        /// Throws after applying the second watched group, as a reference owner can after partial progress.
        /// </summary>
        private sealed class ReferenceFaultMaster : MasterNodeManager
        {
            public ReferenceFaultMaster(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, dynamicNamespaceUri: null)
            {
            }

            public NodeId FirstTarget { get; set; }

            public NodeId SecondTarget { get; set; }

            public int FirstCalls { get; private set; }

            public int SecondCalls { get; private set; }

            public NodeId FailedTarget { get; private set; }

            public InvalidOperationException Failure { get; } = new("Injected reference publication failure.");

            public override async ValueTask AddReferencesAsync(
                NodeId sourceId,
                IList<IReference> references,
                CancellationToken cancellationToken = default)
            {
                await base.AddReferencesAsync(sourceId, references, cancellationToken).ConfigureAwait(false);
                if (!FirstTarget.IsNull && references.Any(reference => reference.TargetId == FirstTarget))
                {
                    FirstCalls++;
                }
                if (!SecondTarget.IsNull && references.Any(reference => reference.TargetId == SecondTarget))
                {
                    SecondCalls++;
                }
                if (FailedTarget.IsNull && FirstCalls == 1 && SecondCalls == 1)
                {
                    FailedTarget = references.Any(reference => reference.TargetId == FirstTarget)
                        ? FirstTarget
                        : SecondTarget;
                    throw Failure;
                }
            }
        }
    }
}
