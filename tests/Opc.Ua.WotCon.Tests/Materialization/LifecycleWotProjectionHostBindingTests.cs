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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
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
            ArrayOf<NodeManagerBatchChange> captured = default;
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), cancellation.Token))
                .Callback<ArrayOf<NodeManagerBatchChange>, CancellationToken>((changes, _) => captured = changes)
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
            Assert.That(captured[0].Current, Is.Null);
            Assert.That(captured[0].Factory, Is.InstanceOf<RuntimeNodeSetNodeManagerFactory>());
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
            ArrayOf<NodeManagerBatchChange> captured = default;
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.SetupGet(owner => owner.SupportsPreparedPublication).Returns(true);
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), cancellation.Token))
                .Callback<ArrayOf<NodeManagerBatchChange>, CancellationToken>((changes, _) => captured = changes)
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
            Assert.That(captured[0].Current, Is.Null);
            Assert.That(captured[0].Factory, Is.Not.Null);
            Assert.That(captured[0].TransitionMode, Is.EqualTo(NodeManagerMonitoredItemTransitionMode.Migrate));
            lifecycle.VerifyAll();
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
            var invocation = new Mock<INodeManagerPublication>(MockBehavior.Strict);
            invocation.SetupGet(owner => owner.IsCurrent).Returns(true);
            invocation.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<INodeManagerPublicationCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellation.Token))
                .Returns(new ValueTask<INodeManagerPublication>(invocation.Object));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.SetupGet(owner => owner.SupportsPublicationIsolation).Returns(true);
            lifecycle.Setup(owner => owner.CapturePublication()).Returns(capture.Object);
            var host = new LifecycleWotProjectionHost(lifecycle.Object);
            IOperationContext? context = hasContext ? Mock.Of<IOperationContext>() : null;

            await using (IWotProjectionPublication publication = await host.CapturePublication()
                .BeginAsync(context, cancellation.Token).ConfigureAwait(false))
            {
                Assert.That(publication.IsCurrent, Is.True);
            }

            lifecycle.VerifyAll();
            capture.Verify(owner => owner.BeginAsync(cancellation.Token), Times.Once);
            invocation.Verify(owner => owner.DisposeAsync(), Times.Once);
            invocation.VerifyAll();
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
