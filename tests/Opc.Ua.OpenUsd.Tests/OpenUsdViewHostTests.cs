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

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using OpenUsd.Rendering;
using OpenUsd.Viewer;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdViewHostTests
    {
        [Test]
        public void RendererPickCallbackIsWiredToViewerHostOptions()
        {
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Renderer,
                hasCallback: true,
                static _ => Assert.Fail("Renderer mode should not start command fallback."),
                static () => Assert.Fail("Renderer mode should not start command fallback."));
            var options = new UsdViewOptions
            {
                PrimPicked = static (_, _) => Task.CompletedTask,
                PickMode = UsdViewPickMode.Renderer
            };
            var hostOptions = new ViewerHostOptions
            {
                PrimPicked = ViewerPickCallback.CreateHandler(
                    options,
                    () => fallback,
                    NullLogger.Instance,
                    CancellationToken.None),
                PickTarget = RenderPickTarget.Primitive
            };

            Assert.That(hostOptions.PrimPicked, Is.Not.Null);
            Assert.That(hostOptions.PickTarget, Is.EqualTo(RenderPickTarget.Primitive));
        }

        [Test]
        public async Task RendererHitRaisesPrimPickedWithPath()
        {
            string? picked = null;
            ViewerPickEventArgs pick = CreatePickArgs(Hit("/World/RobotTargets/Bin"));

            await ViewerPickCallback.HandleAsync(
                pick,
                (primPath, _) =>
                {
                    picked = primPath;
                    return Task.CompletedTask;
                },
                fallbackController: null,
                NullLogger.Instance,
                CancellationToken.None,
                CancellationToken.None);

            Assert.That(picked, Is.EqualTo("/World/RobotTargets/Bin"));
        }

        [Test]
        public async Task RendererMissDoesNotRaisePrimPicked()
        {
            int callbackCount = 0;
            ViewerPickEventArgs pick = CreatePickArgs(RenderPickResult.Miss(in s_request, 5, null));

            await ViewerPickCallback.HandleAsync(
                pick,
                (_, _) =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                },
                fallbackController: null,
                NullLogger.Instance,
                CancellationToken.None,
                CancellationToken.None);

            Assert.That(callbackCount, Is.Zero);
        }

        [Test]
        public async Task AutoPrefersRendererAndFallsBackOnlyWhenRendererIsUnsupported()
        {
            int reports = 0;
            int fallbackStarts = 0;
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Auto,
                hasCallback: true,
                _ => reports++,
                () => fallbackStarts++);
            PickModeDecision decision = PickModeSelection.Select(UsdViewPickMode.Auto, hasCallback: true);

            Assert.That(decision, Is.EqualTo(new PickModeDecision(UseRenderer: true, WatchCommandPrim: false)));

            await ViewerPickCallback.HandleAsync(
                CreatePickArgs(RenderPickResult.Unsupported(in s_request, 5, null)),
                static (_, _) => throw new InvalidOperationException("Unsupported picks must not invoke the handler."),
                fallback,
                NullLogger.Instance,
                CancellationToken.None,
                CancellationToken.None);

            Assert.That(reports, Is.EqualTo(1));
            Assert.That(fallbackStarts, Is.EqualTo(1));
        }

        [Test]
        public void CommandPrimModeStartsOnlyTheCommandPrimWatcher()
        {
            PickModeDecision decision = PickModeSelection.Select(UsdViewPickMode.CommandPrim, hasCallback: true);

            Assert.That(decision.UseRenderer, Is.False);
            Assert.That(decision.WatchCommandPrim, Is.True);
        }

        [Test]
        public async Task ThrowingPrimPickedHandlerDoesNotEscapeViewerCallback()
        {
            ViewerPickEventArgs pick = CreatePickArgs(Hit("/World/RobotTargets/Fixture"));

            await ViewerPickCallback.HandleAsync(
                pick,
                static (_, _) => throw new InvalidOperationException("OPC UA call failed."),
                fallbackController: null,
                NullLogger.Instance,
                CancellationToken.None,
                CancellationToken.None);

            Assert.Pass();
        }

        [Test]
        public async Task BlockingPrimPickedHandlerRemainsAsynchronous()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ViewerPickEventArgs pick = CreatePickArgs(Hit("/World/RobotTargets/Inspect"));

            Task callback = ViewerPickCallback.HandleAsync(
                pick,
                (_, _) => release.Task,
                fallbackController: null,
                NullLogger.Instance,
                CancellationToken.None,
                CancellationToken.None);

            Assert.That(callback.IsCompleted, Is.False);

            release.SetResult();
            await callback;
        }

        [Test]
        public void RendererModeDoesNotStartCommandPrimFallbackOnUnsupportedPick()
        {
            int fallbackStarts = 0;
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Renderer,
                hasCallback: true,
                static _ => { },
                () => fallbackStarts++);

            RendererPickFailureFallback outcome = fallback.DisableRenderer(null);

            Assert.That(outcome, Is.EqualTo(RendererPickFailureFallback.None));
            Assert.That(fallbackStarts, Is.Zero);
        }

        private static ViewerPickEventArgs CreatePickArgs(RenderPickResult result)
        {
            return (ViewerPickEventArgs)Activator.CreateInstance(
                typeof(ViewerPickEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [result],
                culture: null)!;
        }

        private static RenderPickResult Hit(string primPath)
        {
            var item = new SelectionItem(primPath, null!, null, null);
            return RenderPickResult.Hit(
                in s_request,
                s_request.RequestedStateRevision,
                s_request.RequestedSceneRevision,
                in item,
                null,
                null,
                null,
                null,
                null);
        }

        private static readonly RenderPickRequest s_request = new(
            1,
            2,
            new ViewportDimensions(10, 20),
            5,
            null,
            RenderPickTarget.Primitive,
            RenderPickOptions.None);
    }
}
