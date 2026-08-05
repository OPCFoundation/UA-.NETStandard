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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using OpenUsd.Rendering;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdViewHostTests
    {
        [Test]
        public void PointerConversionScalesRoundsAndClamps()
        {
            RendererPickPixel pixel = RendererPickLogic.ToPhysicalPixel(
                positionX: 100.4,
                positionY: -2.0,
                boundsWidth: 50.0,
                boundsHeight: 25.0,
                renderScaling: 2.0);

            Assert.That(pixel.Width, Is.EqualTo(100));
            Assert.That(pixel.Height, Is.EqualTo(50));
            Assert.That(pixel.X, Is.EqualTo(99));
            Assert.That(pixel.Y, Is.Zero);
        }

        [Test]
        public void PointerConversionKeepsDegenerateViewportPickable()
        {
            RendererPickPixel pixel = RendererPickLogic.ToPhysicalPixel(
                positionX: 8.0,
                positionY: 9.0,
                boundsWidth: 0.0,
                boundsHeight: -4.0,
                renderScaling: 0.0);

            Assert.That(pixel.Width, Is.EqualTo(1));
            Assert.That(pixel.Height, Is.EqualTo(1));
            Assert.That(pixel.X, Is.Zero);
            Assert.That(pixel.Y, Is.Zero);
        }

        [Test]
        public void CreateRequestUsesPixelViewportAndStateRevision()
        {
            RenderPickRequest request = RendererPickLogic.CreateRequest(
                new RendererPickPixel(3, 4, 101, 102), stateRevision: 17);

            Assert.That(request.X, Is.EqualTo(3));
            Assert.That(request.Y, Is.EqualTo(4));
            Assert.That(request.Viewport.Width, Is.EqualTo(101));
            Assert.That(request.Viewport.Height, Is.EqualTo(102));
            Assert.That(request.RequestedStateRevision, Is.EqualTo(17));
            Assert.That(request.RequestedSceneRevision, Is.Null);
            Assert.That(request.Target, Is.EqualTo(RenderPickTarget.Primitive));
            Assert.That(request.Flags, Is.EqualTo(RenderPickOptions.None));
        }

        [Test]
        public async Task StalePickRetriesOnceWithReturnedRevisionsThenRaisesHit()
        {
            RenderPickRequest initial = RendererPickLogic.CreateRequest(
                new RendererPickPixel(1, 2, 10, 20), stateRevision: 5);
            var backend = new SequencePickBackend(
                static request => RenderPickResult.Stale(in request, 7, 11),
                static request => Hit(request, "/World/Robot"));
            string? picked = null;

            RendererPickOutcome outcome = await RendererPickLogic.PickAsync(
                backend,
                initial,
                (primPath, _) =>
                {
                    picked = primPath;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.That(outcome, Is.EqualTo(RendererPickOutcome.Completed));
            Assert.That(picked, Is.EqualTo("/World/Robot"));
            Assert.That(backend.Requests, Has.Count.EqualTo(2));
            Assert.That(backend.Requests[1].RequestedStateRevision, Is.EqualTo(7));
            Assert.That(backend.Requests[1].RequestedSceneRevision, Is.EqualTo(11));
        }

        [Test]
        public async Task MissDoesNotRaiseCallback()
        {
            RenderPickRequest request = RendererPickLogic.CreateRequest(
                new RendererPickPixel(1, 2, 10, 20), stateRevision: 5);
            var backend = new SequencePickBackend(
                static current => RenderPickResult.Miss(in current, 5, null));
            int callbackCount = 0;

            RendererPickOutcome miss = await RendererPickLogic.PickAsync(
                backend,
                request,
                (_, _) =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);
            Assert.That(miss, Is.EqualTo(RendererPickOutcome.Completed));
            Assert.That(callbackCount, Is.Zero);
        }

        [Test]
        public async Task UnsupportedPickRequestsFallback()
        {
            RenderPickRequest request = RendererPickLogic.CreateRequest(
                new RendererPickPixel(1, 2, 10, 20), stateRevision: 5);
            var backend = new SequencePickBackend(
                static current => RenderPickResult.Unsupported(in current, 5, null));

            RendererPickOutcome outcome = await RendererPickLogic.PickAsync(
                backend,
                request,
                static (_, _) => throw new InvalidOperationException("Callback should not run."),
                CancellationToken.None);

            Assert.That(outcome, Is.EqualTo(RendererPickOutcome.Fallback));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RendererPickDispatchCreatesRequestBeforeHopAndRaisesHit()
        {
            int callerThread = Environment.CurrentManagedThreadId;
            var backend = new SequencePickBackend(static request => Hit(request, "/World/RobotTargets/TargetA"));
            bool createdOnCallerThread = false;
            string? picked = null;
            int reports = 0;
            int fallbackStarts = 0;
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Auto,
                hasCallback: true,
                _ => reports++,
                () => fallbackStarts++);

            RendererPickFailureFallback outcome = await RendererPickDispatch.PickFromPointerAsync(
                () =>
                {
                    createdOnCallerThread = Environment.CurrentManagedThreadId == callerThread;
                    if (!createdOnCallerThread)
                    {
                        throw new InvalidOperationException("UI-affine state was read off the caller thread.");
                    }
                    return MakeRequest();
                },
                (request, cancellationToken) => RendererPickLogic.PickAsync(
                    backend,
                    request,
                    (primPath, _) =>
                    {
                        picked = primPath;
                        return Task.CompletedTask;
                    },
                    cancellationToken),
                fallback,
                CancellationToken.None);

            Assert.That(outcome, Is.EqualTo(RendererPickFailureFallback.None));
            Assert.That(createdOnCallerThread, Is.True);
            Assert.That(picked, Is.EqualTo("/World/RobotTargets/TargetA"));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].X, Is.EqualTo(1));
            Assert.That(reports, Is.Zero);
            Assert.That(fallbackStarts, Is.Zero);
        }

        [Test]
        public async Task RendererPickFailureInAutoReportsOnceAndStartsCommandFallback()
        {
            int reports = 0;
            int fallbackStarts = 0;
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Auto,
                hasCallback: true,
                exception =>
                {
                    Assert.That(exception, Is.TypeOf<InvalidOperationException>());
                    reports++;
                },
                () => fallbackStarts++);

            RendererPickFailureFallback first = await RendererPickDispatch.PickFromPointerAsync(
                MakeRequest,
                static (_, _) => throw new InvalidOperationException("Renderer pick failed."),
                fallback,
                CancellationToken.None);
            RendererPickFailureFallback second = await RendererPickDispatch.PickFromPointerAsync(
                MakeRequest,
                static (_, _) => throw new InvalidOperationException("Renderer pick failed again."),
                fallback,
                CancellationToken.None);

            Assert.That(first, Is.EqualTo(RendererPickFailureFallback.CommandPrim));
            Assert.That(second, Is.EqualTo(RendererPickFailureFallback.None));
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(fallbackStarts, Is.EqualTo(1));
        }

        [Test]
        public async Task RendererPickFailureInRendererModeDoesNotStartCommandFallback()
        {
            int reports = 0;
            int fallbackStarts = 0;
            var fallback = new RendererPickFallbackController(
                UsdViewPickMode.Renderer,
                hasCallback: true,
                _ => reports++,
                () => fallbackStarts++);

            RendererPickFailureFallback outcome = await RendererPickDispatch.PickFromPointerAsync(
                MakeRequest,
                static (_, _) => throw new InvalidOperationException("Renderer pick failed."),
                fallback,
                CancellationToken.None);

            Assert.That(outcome, Is.EqualTo(RendererPickFailureFallback.None));
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(fallbackStarts, Is.Zero);
        }

        [Test]
        public void PickModeSelectionMatchesFallbackContract()
        {
            Assert.That(PickModeSelection.Select(UsdViewPickMode.Auto, hasCallback: false, rendererStarted: false),
                Is.EqualTo(new PickModeDecision(false, false)));
            Assert.That(PickModeSelection.Select(UsdViewPickMode.Auto, hasCallback: true, rendererStarted: false),
                Is.EqualTo(new PickModeDecision(true, true)));
            Assert.That(PickModeSelection.Select(UsdViewPickMode.Auto, hasCallback: true, rendererStarted: true),
                Is.EqualTo(new PickModeDecision(true, false)));
            Assert.That(PickModeSelection.Select(UsdViewPickMode.Renderer, hasCallback: true, rendererStarted: false),
                Is.EqualTo(new PickModeDecision(true, false)));
            Assert.That(PickModeSelection.Select(UsdViewPickMode.CommandPrim, hasCallback: true, rendererStarted: false),
                Is.EqualTo(new PickModeDecision(false, true)));

            var decision = new PickModeDecision(ProbeRenderer: true, WatchCommandPrim: false);
            Assert.That(decision.ProbeRenderer, Is.True);
            Assert.That(decision.WatchCommandPrim, Is.False);
        }

        [Test]
        public void ReferenceIdentityComparerUsesObjectIdentity()
        {
            object first = new();
            object second = new();
            object alias = first;

            Assert.That(ReferenceIdentityComparer.Instance.Equals(first, alias), Is.True);
            Assert.That(ReferenceIdentityComparer.Instance.Equals(first, second), Is.False);
            Assert.That(ReferenceIdentityComparer.Instance.GetHashCode(first),
                Is.EqualTo(ReferenceIdentityComparer.Instance.GetHashCode(alias)));
        }

        [Test]
        public async Task BackendDiscoveryPrefersFieldsBeforeProperties()
        {
            var root = new global::OpenUsd.TestDoubles.RootWithFieldAndProperty();

            IOpenUsdPickBackend? backend = RendererPickBackendDiscovery.TryFindPickBackend(root);

            Assert.That(backend, Is.Not.Null);
            RenderPickResult result = await backend!.PickAsync(MakeRequest(), CancellationToken.None);
            Assert.That(result.PrimPath, Is.EqualTo("/World/Field"));
        }

        [Test]
        public async Task BackendDiscoveryUsesPropertiesAfterFieldPass()
        {
            var root = new global::OpenUsd.TestDoubles.RootWithPropertyBackend();

            IOpenUsdPickBackend? backend = RendererPickBackendDiscovery.TryFindPickBackend(root);

            Assert.That(backend, Is.Not.Null);
            RenderPickResult result = await backend!.PickAsync(MakeRequest(), CancellationToken.None);
            Assert.That(result.PrimPath, Is.EqualTo("/World/Property"));
        }

        [Test]
        public async Task BackendDiscoveryAdaptsDirectRenderPickingBackend()
        {
            var root = new global::OpenUsd.TestDoubles.DirectPickBackend();

            IOpenUsdPickBackend? backend = RendererPickBackendDiscovery.TryFindPickBackend(root);

            Assert.That(backend, Is.Not.Null);
            RenderPickResult result = await backend!.PickAsync(MakeRequest(), CancellationToken.None);
            Assert.That(result.PrimPath, Is.EqualTo("/World/Direct"));
        }

        [Test]
        public void BackendDiscoveryStopsAtVisitCapAndIgnoresThrowingMembers()
        {
            var root = global::OpenUsd.TestDoubles.RootWithLongChain.Create(length: 35);

            IOpenUsdPickBackend? backend = RendererPickBackendDiscovery.TryFindPickBackend(root);

            Assert.That(backend, Is.Null);
            Assert.That(RendererPickBackendDiscovery.TryFindPickBackend(
                new global::OpenUsd.TestDoubles.RootWithThrowingProperty()), Is.Null);
        }

        private static RenderPickRequest MakeRequest()
        {
            return RendererPickLogic.CreateRequest(new RendererPickPixel(1, 2, 10, 20), 5);
        }

        private static RenderPickResult Hit(RenderPickRequest request, string primPath)
        {
            var item = new SelectionItem(primPath, null!, null, null);
            return RenderPickResult.Hit(
                in request,
                request.RequestedStateRevision,
                request.RequestedSceneRevision,
                in item,
                null,
                null,
                null,
                null,
                null);
        }

        private sealed class SequencePickBackend : IOpenUsdPickBackend
        {
            private readonly Queue<Func<RenderPickRequest, RenderPickResult>> m_results = new();

            public SequencePickBackend(params Func<RenderPickRequest, RenderPickResult>[] results)
            {
                foreach (Func<RenderPickRequest, RenderPickResult> result in results)
                {
                    m_results.Enqueue(result);
                }
            }

            public List<RenderPickRequest> Requests { get; } = [];

            public ValueTask<RenderPickResult> PickAsync(
                RenderPickRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(request);
                return ValueTask.FromResult(m_results.Dequeue()(request));
            }
        }
    }
}

namespace OpenUsd.TestDoubles
{
    internal sealed class ReflectedBackend
    {
        private readonly string m_primPath;

        public ReflectedBackend(string primPath)
        {
            m_primPath = primPath;
        }

        public RenderPickResult Pick(RenderPickRequest request)
        {
            var item = new SelectionItem(m_primPath, null!, null, null);
            return RenderPickResult.Hit(
                in request,
                request.RequestedStateRevision,
                request.RequestedSceneRevision,
                in item,
                null,
                null,
                null,
                null,
                null);
        }
    }

    internal sealed class DirectPickBackend : IRenderPickingBackend
    {
        public ValueTask<RenderPickResult> PickAsync(
            RenderPickRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new SelectionItem("/World/Direct", null!, null, null);
            return ValueTask.FromResult(RenderPickResult.Hit(
                in request,
                request.RequestedStateRevision,
                request.RequestedSceneRevision,
                in item,
                null,
                null,
                null,
                null,
                null));
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1823:Avoid unused private fields",
        Justification = "The field is intentionally discovered by the viewer's reflection probe.")]
    internal sealed class RootWithFieldAndProperty
    {
        private readonly ReflectedBackend m_fieldBackend = new("/World/Field");

        public ReflectedBackend PropertyBackend { get; } = new("/World/Property");
    }

    internal sealed class RootWithPropertyBackend
    {
        public ReflectedBackend PropertyBackend { get; } = new("/World/Property");
    }

    internal sealed class RootWithThrowingProperty
    {
        public ReflectedBackend PropertyBackend => throw new InvalidOperationException("No backend today.");
    }

    internal sealed class RootWithLongChain
    {
        public RootWithLongChain? Next;

        public ReflectedBackend? Backend;

        public static RootWithLongChain Create(int length)
        {
            var root = new RootWithLongChain();
            RootWithLongChain current = root;
            for (int i = 0; i < length; i++)
            {
                current.Next = new RootWithLongChain();
                current = current.Next;
            }
            current.Backend = new ReflectedBackend("/World/TooFar");
            return root;
        }
    }
}
