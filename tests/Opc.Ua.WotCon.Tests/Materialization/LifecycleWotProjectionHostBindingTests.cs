// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Verifies that <see cref="LifecycleWotProjectionHost"/> installs
    /// <see cref="RuntimeNodeSetOptions.ConfigureAsync"/> so each runtime
    /// NodeSet generation owns its own binding runtime, built from the
    /// injected <see cref="IWotProjectionBindingRuntimeFactory"/> and the
    /// document's prepared binding plans.
    /// </summary>
    [TestFixture]
    public sealed class LifecycleWotProjectionHostBindingTests
    {
        /// <summary>
        /// Verifies immediate host entry points prepare canonical additions and preserve pre-commit failures.
        /// </summary>
        [TestCase("Add")]
        [TestCase("Shadow")]
        [TestCase("Immediate")]
        public async Task DirectProjectionUsesCanonicalPreparationAndPreservesFailureAsync(string operation)
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("candidate rejected");
            ArrayOf<LifecycleChange> captured = default;
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            Mock<ILifecycleWriter> writer = SetupPublication(lifecycle, cancellation.Token);
            writer.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), cancellation.Token))
                .Callback<ArrayOf<LifecycleChange>, CancellationToken>((changes, _) => captured = changes)
                .Throws(failure);
            var host = new LifecycleWotProjectionHost(lifecycle.Object);
            var document = new WotProjectionDocument("direct",
            [
                new WotProjectionSource("direct", ["urn:wot:direct"], Encoding.UTF8.GetBytes("""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>urn:wot:direct</Uri></NamespaceUris>
                    </UANodeSet>
                    """))
            ]);
            var missing = new WotProjectionHandle("direct", 0, null, [], 0);

            Exception? actual = operation switch
            {
                "Add" => await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await host.AddAsync(document, cancellation.Token).ConfigureAwait(false))
                    .ConfigureAwait(false),
                "Shadow" => await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await host.ShadowReloadAsync(missing, document, cancellation.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await host.ImmediateReloadAsync(missing, document, cancellation.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false)
            };

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Slot, Is.Null);
            Assert.That(captured[0].Factory, Is.Not.Null);
            writer.VerifyAll();
            lifecycle.VerifyAll();
            lifecycle.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies standalone preparation uses the canonical context-free transaction and preserves failures.
        /// </summary>
        /// <param name="hasContext">Whether the WoT compatibility entry point receives a request context.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StandalonePreparationUsesContextFreeLifecycleTransactionAsync(bool hasContext)
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("Candidate preparation failed.");
            ArrayOf<LifecycleChange> captured = default;
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            Mock<ILifecycleWriter> writer = SetupPublication(lifecycle, cancellation.Token);
            writer.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), cancellation.Token))
                .Callback<ArrayOf<LifecycleChange>, CancellationToken>((changes, _) => captured = changes)
                .Throws(failure);
            var host = new LifecycleWotProjectionHost(lifecycle.Object);
            var document = new WotProjectionDocument("context-free-preparation",
            [
                new WotProjectionSource("context-free-source", ["urn:wot:context-free"], Encoding.UTF8.GetBytes("""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>urn:wot:context-free</Uri></NamespaceUris>
                    </UANodeSet>
                    """))
            ]);
            IOperationContext? context = hasContext ? Mock.Of<IOperationContext>() : null;

            await Assert.ThatAsync(async () => await host.PrepareAsync(
                [WotProjectionChange.Add(document)], null, context, cancellation.Token).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message))
                .ConfigureAwait(false);

            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Slot, Is.Null);
            Assert.That(captured[0].Factory, Is.Not.Null);
            Assert.That(captured[0].TransitionMode, Is.EqualTo(NodeManagerMonitoredItemTransitionMode.Migrate));
            lifecycle.VerifyAll();
            writer.VerifyAll();
        }

        /// <summary>
        /// Verifies captured WoT invocations use the context-free lifecycle admission and retain disposal.
        /// </summary>
        /// <param name="hasContext">Whether the WoT compatibility entry point receives a request context.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedPublicationUsesContextFreeLifecycleAdmissionAsync(bool hasContext)
        {
            using var cancellation = new CancellationTokenSource();
            var invocation = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            invocation.SetupGet(owner => owner.IsCurrent).Returns(true);
            invocation.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellation.Token))
                .Returns(new ValueTask<ILifecycleWriter>(invocation.Object));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            var host = new LifecycleWotProjectionHost(lifecycle.Object);
            IOperationContext? context = hasContext ? Mock.Of<IOperationContext>() : null;

            Assert.That(host.SupportedAtomicities.Span.ToArray(), Is.EqualTo(new[]
            {
                WoTAtomicityEnum.PerResource,
                WoTAtomicityEnum.PerGroup,
                WoTAtomicityEnum.PerClosure,
                WoTAtomicityEnum.PerRegistry
            }));
            await using (IWotProjectionPublication publication = await host.CapturePublication()
                .BeginAsync(context, cancellation.Token).ConfigureAwait(false))
            {
                Assert.That(publication.IsCurrent, Is.True);
            }

            lifecycle.VerifyAll();
            lifecycle.Verify(owner => owner.CaptureChanges(), Times.Once);
            lifecycle.VerifyNoOtherCalls();
            capture.Verify(owner => owner.BeginAsync(cancellation.Token), Times.Once);
            invocation.Verify(owner => owner.DisposeAsync(), Times.Once);
            invocation.VerifyAll();
        }

        /// <summary>
        /// Preserves a provider's capture rejection instead of manufacturing an isolated writer.
        /// </summary>
        [Test]
        public void PublicationCaptureRequiresLifecycleIsolation()
        {
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            var failure = new NotSupportedException("The provider cannot capture changes.");
            lifecycle.Setup(owner => owner.CaptureChanges()).Throws(failure);
            var host = new LifecycleWotProjectionHost(lifecycle.Object);

            NotSupportedException? actual = Assert.Throws<NotSupportedException>(() => host.CapturePublication());
            Assert.That(actual, Is.SameAs(failure));
            lifecycle.Verify(owner => owner.CaptureChanges(), Times.Once);
            lifecycle.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Slot-based publication must not reinterpret a retained WoT handle as the newer generation.
        /// </summary>
        [TestCase("Shadow")]
        [TestCase("Immediate")]
        [TestCase("Remove")]
        [TestCase("Prepared")]
        [Category("Integration")]
        public async Task StaleProjectionHandleCannotMutateNewerSlotGenerationAsync(string operation)
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            var document = new WotProjectionDocument("stale",
            [
                new WotProjectionSource("stale", ["urn:wot:stale-handle"], Encoding.UTF8.GetBytes("""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>urn:wot:stale-handle</Uri></NamespaceUris>
                      <UAObject NodeId="ns=1;i=1" BrowseName="1:Root"><DisplayName>Root</DisplayName></UAObject>
                    </UANodeSet>
                    """))
            ]);
            WotProjectionHandle first = await runtime.Host.AddAsync(document).ConfigureAwait(false);
            WotProjectionHandle second = await runtime.Host.ShadowReloadAsync(first, document).ConfigureAwait(false);
            long revision = runtime.Lifecycle.Revision;
            await Assert.ThatAsync(async () =>
            {
                switch (operation)
                {
                    case "Shadow":
                        await runtime.Host.ShadowReloadAsync(first, document).ConfigureAwait(false);
                        break;
                    case "Immediate":
                        await runtime.Host.ImmediateReloadAsync(first, document).ConfigureAwait(false);
                        break;
                    case "Remove":
                        await runtime.Host.RemoveAsync(first).ConfigureAwait(false);
                        break;
                    default:
                        await using (await runtime.Host.PrepareAsync(
                            [WotProjectionChange.Replace(first, document)], null, null, CancellationToken.None)
                            .ConfigureAwait(false))
                        {
                        }
                        break;
                }
            }, Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(runtime.Lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(runtime.Lifecycle.Registrations.Contains(value => value.Generation == second.Generation &&
                value.Id == second.Registration!.Id), Is.True);
            await runtime.Host.RemoveAsync(second).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies observed publications retain private source namespace mappings through commit or abort.
        /// </summary>
        /// <param name="invocation">Whether preparation uses an explicit publication invocation.</param>
        /// <param name="commit">Whether the prepared source is committed.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        public async Task ObservedPublicationPreservesPrivateSourceRootMappingAsync(bool invocation, bool commit)
        {
            const string SourceUri = "urn:wot:observed-private-source";
            byte[] xml = Encoding.UTF8.GetBytes($"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>{SourceUri}</Uri></NamespaceUris>
                  <Models>
                    <Model ModelUri="{SourceUri}" Version="1.0.0" PublicationDate="2026-01-01T00:00:00Z" />
                  </Models>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:SourceRoot">
                    <DisplayName>SourceRoot</DisplayName>
                    <References><Reference ReferenceType="i=40">i=58</Reference></References>
                  </UAObject>
                </UANodeSet>
                """);
            var document = new WotProjectionDocument(
                "private-source", [new WotProjectionSource("private-source", [SourceUri], xml)]);
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            int published = 0;
            IWotInvocationProjectionHost host = runtime.Observe(_ => published++);
            string[] namespacesBefore = runtime.Namespaces.ToArray();
            NodeId root = NodeId.Null;
            IWotProjectionPublication? publication = null;
            try
            {
                if (invocation)
                {
                    publication = await host.CapturePublication().BeginAsync(null, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await using IWotPreparedProjectionPublication prepared = publication is not null
                    ? await publication.PrepareAsync([WotProjectionChange.Add(document)]).ConfigureAwait(false)
                    : await host.PrepareAsync(
                        [WotProjectionChange.Add(document)], null, null, CancellationToken.None).ConfigureAwait(false);
                Assert.That(prepared, Is.InstanceOf<IWotPreparedProjectionNamespaceSource>());
                var source = (IWotPreparedProjectionNamespaceSource)prepared;
                var namespaces = new NamespaceTable(source.CapturedNamespaceUris.ToArray()!);
                root = ExpandedNodeId.ToNodeId(new ExpandedNodeId(1, SourceUri), namespaces);
                Assert.That(root.IsNull, Is.False);
                Assert.That(root.NamespaceIndex, Is.EqualTo(namespacesBefore.Length));
                Assert.That(
                    ExpandedNodeId.ToNodeId(new ExpandedNodeId(1, "urn:wot:unprepared-source"), namespaces).IsNull,
                    Is.True);
                Assert.That(runtime.Namespaces.ToArray(), Is.EqualTo(namespacesBefore));
                Assert.That(
                    await runtime.Server.NodeManager.FindNodeInAddressSpaceAsync(root).ConfigureAwait(false), Is.Null);
                Assert.That(published, Is.Zero);
                if (commit)
                {
                    await prepared.CommitAsync(_ => default, () => { }).ConfigureAwait(false);
                }
            }
            finally
            {
                if (publication is not null)
                {
                    await publication.DisposeAsync().ConfigureAwait(false);
                }
            }
            Assert.That(published, Is.EqualTo(commit ? 1 : 0));
            NodeState? liveRoot = await runtime.Server.NodeManager.FindNodeInAddressSpaceAsync(root)
                .ConfigureAwait(false);
            if (commit)
            {
                Assert.That(runtime.Namespaces.GetIndex(SourceUri), Is.EqualTo(root.NamespaceIndex));
                Assert.That(liveRoot, Is.Not.Null);
                Assert.That(liveRoot!.BrowseName, Is.EqualTo(new QualifiedName("SourceRoot", root.NamespaceIndex)));
            }
            else
            {
                Assert.That(runtime.Namespaces.ToArray(), Is.EqualTo(namespacesBefore));
                Assert.That(liveRoot, Is.Null);
            }
        }

        [Test]
        public async Task ConfigureAsyncIsInstalledAndForwardsBindingPlansToTheRuntimeFactory()
        {
            var runtimeFactory = new RecordingProjectionBindingRuntimeFactory();
            var host = new LifecycleWotProjectionHost(Mock.Of<INodeManagerLifecycle>(), runtimeFactory);

            var harness = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: harness.ScalarNodeIdText));
            ArrayOf<WotBindingPlan> plans = new[]
            {
                WotProjectionBindingRuntimeTestHarness.Plan(form)
            }.ToArrayOf();
            var document = new WotProjectionDocument(
                "closure-a", [], plans);

            RuntimeNodeSetOptions options = InvokeBuildOptions(host, document);

            Assert.That(options.ConfigureAsync, Is.Not.Null,
                "The host must install ConfigureAsync when a runtime factory is supplied.");

            IAsyncDisposable? result = await options.ConfigureAsync!(
                harness.Builder, CancellationToken.None).ConfigureAwait(false);

            Assert.That(runtimeFactory.LastBuilder, Is.SameAs(harness.Builder));
            Assert.That(runtimeFactory.LastBindingPlans.Count, Is.EqualTo(1));
            Assert.That(runtimeFactory.LastBindingPlans[0], Is.SameAs(plans[0]));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ConfigureAsyncIsNotInstalledWhenNoRuntimeFactoryIsSupplied()
        {
            var host = new LifecycleWotProjectionHost(Mock.Of<INodeManagerLifecycle>());
            var document = new WotProjectionDocument("closure-a", []);

            RuntimeNodeSetOptions options = InvokeBuildOptions(host, document);

            Assert.That(options.ConfigureAsync, Is.Null,
                "Without a runtime factory the host must preserve the pre-existing (data-only) behavior.");
        }

        /// <summary>
        /// Supplies the provider boundary behind the consumer's captured change-set writer.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider mock.</param>
        /// <param name="cancellationToken">The expected admission token.</param>
        /// <returns>The provider invocation used by the writer.</returns>
        private static Mock<ILifecycleWriter> SetupPublication(
            Mock<INodeManagerLifecycle> lifecycle, CancellationToken cancellationToken)
        {
            var publication = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            publication.SetupGet(owner => owner.IsCurrent).Returns(true);
            publication.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellationToken)).ReturnsAsync(publication.Object);
            lifecycle.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            return publication;
        }

        private static RuntimeNodeSetOptions InvokeBuildOptions(
            LifecycleWotProjectionHost host, WotProjectionDocument document)
        {
            MethodInfo method = typeof(LifecycleWotProjectionHost).GetMethod(
                "BuildOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (RuntimeNodeSetOptions)method.Invoke(host, [document])!;
        }

        private sealed class RecordingProjectionBindingRuntimeFactory : IWotProjectionBindingRuntimeFactory
        {
            public INodeManagerBuilder? LastBuilder { get; private set; }

            public ArrayOf<WotBindingPlan> LastBindingPlans { get; private set; }

            public ValueTask<IAsyncDisposable?> CreateAsync(
                INodeManagerBuilder builder,
                ArrayOf<WotBindingPlan> bindingPlans,
                CancellationToken cancellationToken = default)
            {
                LastBuilder = builder;
                LastBindingPlans = bindingPlans;
                return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null);
            }
        }
    }
}
