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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportSessionTests
    {
        [Test]
        public async Task RestoringAnOpenUsdDocumentNeverCreatesGpuAssetsOrSubscriptionsAsync()
        {
            var test = new OpenUsdViewportTestContext();
            var host = new ObserveTestHost();
            await using (host.ConfigureAwait(false))
            {
                var document = new CompanionPlugin(
                    host.Host, providers: [test.Provider], viewportFactory: test.Factory);
                await using (document.ConfigureAwait(false))
                {
                    JsonElement state = JsonSerializer.SerializeToElement(
                        new CompanionDocumentState(1, "openusd", null),
                        CompanionJsonContext.Default.CompanionDocumentState);
                    await document.RestoreStateAsync(state).ConfigureAwait(false);
                    Assert.That(document.ShowViewport, Is.True);
                    Assert.That(document.HasViewport, Is.False);
                    Assert.That(document.ViewportSurface, Is.Null);
                    Assert.That(document.RenderedVisionFrame, Is.Null);
                    Assert.That(test.SurfaceCreations, Is.Zero);
                    Assert.That(test.AssetReads, Is.Zero);
                    Assert.That(test.Cache.Acquisitions, Is.Zero);
                    Assert.That(test.StreamStarted.Task.IsCompleted, Is.False);
                    Assert.That(document.CaptureState().GetRawText(), Does.Not.Contain("rendering"));
                }
            }
        }

        [Test]
        public async Task ExplicitStartOwnsOneSurfaceAndPickingOnlySelectsTheRetainedTargetAsync()
        {
            var test = new OpenUsdViewportTestContext();
            OpenUsdViewportSession session = test.CreateSession();
            Assert.That(test.SurfaceCreations, Is.Zero);
            await using (session.ConfigureAwait(false))
            {
                await session.StartAsync(test.Workflow.Context, test.Workflow.Target, "/World/Camera", default)
                    .ConfigureAwait(false);
                await test.StreamStarted.Task.ConfigureAwait(false);
                Assert.That(session.IsStarted, Is.True);
                Assert.That(test.Surface.Starts, Is.EqualTo(1));
                Assert.That(test.AssetReads, Is.EqualTo(1));
                Assert.That(test.Cache.Acquisitions, Is.EqualTo(1));
                RenderPickResult picked = await session.PickAsync(1, 0, CancellationToken.None).ConfigureAwait(false);
                Assert.That(picked.Status, Is.EqualTo(RenderPickStatus.Hit));
                Assert.That(picked.PrimPath, Is.EqualTo("/World/Cell"));
                test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
            }
            Assert.That(test.Surface.Disposals, Is.EqualTo(1));
            Assert.That(test.StreamDisposals, Is.EqualTo(1));
            Assert.That(test.Cache.Disposals, Is.EqualTo(1));
            Assert.That(test.Workflow.ReaderDisposals, Is.EqualTo(1));
            Assert.That(session.Statistics.ActiveFrames, Is.Zero);
            Assert.That(session.Statistics.ActiveGenerations, Is.Zero);
            await session.DisposeAsync().ConfigureAwait(false);
            Assert.That(test.Surface.Disposals, Is.EqualTo(1));
        }

        [Test]
        public async Task ClosingDrainsAnInFlightRenderedVisionReadbackBeforeDisposingTheSurfaceAsync()
        {
            var test = new OpenUsdViewportTestContext();
            var release = new TaskCompletionSource<RetainedSceneFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            test.Surface.Capture = _ => new ValueTask<RetainedSceneFrame>(release.Task);
            OpenUsdViewportSession session = test.CreateSession();
            await session.StartAsync(test.Workflow.Context, test.Workflow.Target, "/World/Camera", default)
                .ConfigureAwait(false);
            Task<SceneCameraCaptureResult> capture = session.CaptureAsync(default).AsTask();
            await test.Surface.CaptureStarted.Task.ConfigureAwait(false);
            Task closing = session.DisposeAsync().AsTask();
            Assert.That(closing.IsCompleted, Is.False);
            Assert.That(test.Surface.Disposals, Is.Zero);
            Assert.That(test.Cache.Disposals, Is.Zero);
            release.TrySetResult(OpenUsdViewportTestContext.TestSurface.Frame());
            Assert.That(() => capture, Throws.InstanceOf<OperationCanceledException>());
            await closing.ConfigureAwait(false);
            Assert.That(test.Surface.Disposals, Is.EqualTo(1));
            Assert.That(test.Cache.Disposals, Is.EqualTo(1));
            Assert.That(test.StreamDisposals, Is.EqualTo(1));
        }

        [Test]
        public async Task ReboundPrimarySessionCannotCaptureOrPickFromTheOldStageAsync()
        {
            var test = new OpenUsdViewportTestContext();
            OpenUsdViewportSession session = test.CreateSession();
            await using (session.ConfigureAwait(false))
            {
                await session.StartAsync(test.Workflow.Context, test.Workflow.Target, null, default)
                    .ConfigureAwait(false);
                test.Workflow.SessionId = new NodeId("rebound", 1);
                Assert.That(async () => await session.CaptureAsync(default).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(async () => await session.PickAsync(0, 0, default).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(test.Surface.Captures, Is.Zero);
            }
            test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task NativeFailureIsExplicitAndStopsFurtherRenderedVisionRequestsAsync()
        {
            var test = new OpenUsdViewportTestContext();
            OpenUsdViewportSession session = test.CreateSession();
            await using (session.ConfigureAwait(false))
            {
                await session.StartAsync(test.Workflow.Context, test.Workflow.Target, null, default)
                    .ConfigureAwait(false);
                var failure = new NotSupportedException("GPU interop unavailable");
                test.Surface.Fail(failure);
                Assert.That(session.Failure, Is.SameAs(failure));
                Assert.That(async () => await session.CaptureAsync(default).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(test.Surface.Captures, Is.Zero);
            }
            Assert.That(test.Surface.Disposals, Is.EqualTo(1));
        }

        [Test]
        public async Task UnexpectedSourceCancellationFailsStartupAndStillReleasesSceneAndCacheAsync()
        {
            var test = new OpenUsdViewportTestContext();
            test.Workflow.ReadValue = static (_, _) => Task.FromException<DataValue>(
                new OperationCanceledException("The source canceled independently of the document."));
            OpenUsdViewportSession session = test.CreateSession();
            await using (session.ConfigureAwait(false))
            {
                Assert.That(() => session.StartAsync(test.Workflow.Context, test.Workflow.Target, null, default),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(session.Failure, Is.TypeOf<OperationCanceledException>());
                Assert.That(session.IsStarted, Is.False);
                Assert.That(test.Surface.Disposals, Is.EqualTo(1));
                Assert.That(test.Cache.Disposals, Is.EqualTo(1));
                Assert.That(test.Workflow.ReaderDisposals, Is.EqualTo(1));
                test.Workflow.Server.VerifyNoMutationOrSessionOwnership();
            }
        }
    }
}
