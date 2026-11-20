/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportSourceTests
    {
        [Test]
        public async Task LiveSourceReusesConnectorConversionAndDrainsItsSubscriptionAsync()
        {
            var test = new OpenUsdViewportTestContext();
            foreach (OpenUsdIntentProfile intent in new[]
            {
                OpenUsdIntentProfile.UsdToUaCommand,
                OpenUsdIntentProfile.UaHistoryToUsd,
                OpenUsdIntentProfile.UaAlarmToUsd
            })
            {
                test.Workflow.Representation.Bindings.Add(new OpenUsdConnector.BindingInfo { Intent = intent });
            }
            OpenUsdViewportSource source = await test.OpenSourceAsync().ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var sink = new OpenUsdViewportTestContext.TestSink();
                using var cancellation = new CancellationTokenSource();
                Task live = source.RunAsync(sink, cancellation.Token);
                try
                {
                    Variant initial = await sink.Values.Reader.ReadAsync().ConfigureAwait(false);
                    Assert.That(initial.TryGetValue(out double first), Is.True);
                    Assert.That(first, Is.EqualTo(7), "The shared connector applies scale 2 and offset 1.");
                    await test.StreamStarted.Task.ConfigureAwait(false);
                    await test.Queue.Writer.WriteAsync(new OpenUsdWorkflowChange(
                        test.Workflow.Binding.SourceNodeId, OpenUsdWorkflowTestContext.Value(Variant.From(5d))))
                        .ConfigureAwait(false);
                    Variant updated = await sink.Values.Reader.ReadAsync().ConfigureAwait(false);
                    Assert.That(updated.TryGetValue(out double second), Is.True);
                    Assert.That(second, Is.EqualTo(11));
                    Assert.That(source.AppliedValues, Is.EqualTo(2));
                    Assert.That(test.AssetReads, Is.EqualTo(1), "Updates do not export or reopen the asset closure.");
                }
                finally
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    Assert.That(async () => await live.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>());
                }
                Assert.That(test.StreamDisposals, Is.EqualTo(1));
                test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
            }
            Assert.That(test.Workflow.ReaderDisposals, Is.EqualTo(1));
        }

        [Test]
        public async Task SessionGenerationChangesRejectTheNextLiveValueBeforeAuthoringAsync()
        {
            var test = new OpenUsdViewportTestContext();
            OpenUsdViewportSource source = await test.OpenSourceAsync().ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var sink = new OpenUsdViewportTestContext.TestSink();
                Task live = source.RunAsync(sink, CancellationToken.None);
                _ = await sink.Values.Reader.ReadAsync().ConfigureAwait(false);
                await test.StreamStarted.Task.ConfigureAwait(false);
                test.Workflow.SessionId = new NodeId("replacement", 1);
                await test.Queue.Writer.WriteAsync(new OpenUsdWorkflowChange(
                    test.Workflow.Binding.SourceNodeId, OpenUsdWorkflowTestContext.Value(Variant.From(9d))))
                    .ConfigureAwait(false);
                Assert.That(async () => await live.ConfigureAwait(false), Throws.InvalidOperationException);
                Assert.That(sink.Writes, Is.EqualTo(1));
                Assert.That(test.StreamDisposals, Is.EqualTo(1));
                test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
            }
        }

        [Test]
        public void DigestFailurePreventsAnyCacheOrNativeSurfaceCreation()
        {
            var test = new OpenUsdViewportTestContext();
            test.Workflow.Representation.RootLayerDigest = ByteString.From(new byte[32]);
            Assert.That(() => test.OpenSourceAsync(),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(test.Cache.Acquisitions, Is.Zero);
            Assert.That(test.SurfaceCreations, Is.Zero);
            Assert.That(test.Workflow.ReaderDisposals, Is.EqualTo(1));
        }

        [TestCase("faceVertexCounts")]
        [TestCase("points")]
        [TestCase("inputs:file")]
        [TestCase("info:id")]
        public void LiveTopologyAndAssetBindingsAreRejectedBeforeAssetReads(string property)
        {
            var test = new OpenUsdViewportTestContext();
            test.Workflow.Binding.PropertyName = property;
            Assert.That(() => test.OpenSourceAsync(),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(test.AssetReads, Is.Zero);
            Assert.That(test.Workflow.ReaderDisposals, Is.EqualTo(1));
        }

        [Test]
        public async Task MetadataChangesStopTheOwnedStreamWithoutApplyingAReplacementBindingAsync()
        {
            var test = new OpenUsdViewportTestContext();
            OpenUsdViewportSource source = await test.OpenSourceAsync().ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var sink = new OpenUsdViewportTestContext.TestSink();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                Task live = source.RunAsync(sink, cancellation.Token);
                _ = await sink.Values.Reader.ReadAsync(cancellation.Token).ConfigureAwait(false);
                test.Workflow.Binding.PropertyName = "replacement";
                Assert.That(() => live, Throws.TypeOf<InvalidOperationException>());
                Assert.That(sink.Writes, Is.EqualTo(1));
                Assert.That(test.StreamDisposals, Is.EqualTo(1));
                test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
            }
        }
    }
}
