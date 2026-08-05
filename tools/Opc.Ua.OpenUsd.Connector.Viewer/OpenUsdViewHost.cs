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
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Opc.Ua.OpenUsd.Client;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Viewer;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    internal readonly record struct RendererPickPixel(
        int X,
        int Y,
        int Width,
        int Height);

    internal interface IOpenUsdPickBackend
    {
        ValueTask<RenderPickResult> PickAsync(
            RenderPickRequest request,
            CancellationToken cancellationToken);
    }

    internal enum RendererPickOutcome
    {
        Completed,
        Fallback
    }

    internal enum RendererPickFailureFallback
    {
        None,
        CommandPrim
    }

    internal readonly record struct PickModeDecision(
        bool ProbeRenderer,
        bool WatchCommandPrim);

    internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }

        public static readonly ReferenceIdentityComparer Instance = new();
    }

    internal static class PickModeSelection
    {
        public static PickModeDecision Select(
            UsdViewPickMode mode,
            bool hasCallback,
            bool rendererStarted)
        {
            if (!hasCallback)
            {
                return new PickModeDecision(false, false);
            }

            bool probeRenderer = mode != UsdViewPickMode.CommandPrim;
            bool watchCommandPrim = mode != UsdViewPickMode.Renderer && !rendererStarted;
            return new PickModeDecision(probeRenderer, watchCommandPrim);
        }
    }

    internal static class RendererPickLogic
    {
        public static RendererPickPixel ToPhysicalPixel(
            double positionX,
            double positionY,
            double boundsWidth,
            double boundsHeight,
            double renderScaling)
        {
            double scale = renderScaling > 0 ? renderScaling : 1.0;
            int width = Math.Max(1, (int)Math.Round(boundsWidth * scale));
            int height = Math.Max(1, (int)Math.Round(boundsHeight * scale));
            int x = Math.Clamp((int)Math.Round(positionX * scale), 0, width - 1);
            int y = Math.Clamp((int)Math.Round(positionY * scale), 0, height - 1);
            return new RendererPickPixel(x, y, width, height);
        }

        public static RenderPickRequest CreateRequest(RendererPickPixel pixel, ulong stateRevision)
        {
            return new RenderPickRequest(
                pixel.X,
                pixel.Y,
                new ViewportDimensions(pixel.Width, pixel.Height),
                stateRevision,
                null,
                RenderPickTarget.Primitive,
                RenderPickOptions.None);
        }

        public static async ValueTask<RendererPickOutcome> PickAsync(
            IOpenUsdPickBackend backend,
            RenderPickRequest request,
            Func<string, CancellationToken, Task> primPicked,
            CancellationToken cancellationToken)
        {
            RenderPickResult result = await backend.PickAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (result.Status == RenderPickStatus.Stale)
            {
                request = new RenderPickRequest(
                    request.X,
                    request.Y,
                    request.Viewport,
                    result.StateRevision,
                    result.SceneRevision,
                    RenderPickTarget.Primitive,
                    RenderPickOptions.None);
                result = await backend.PickAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (result.Status == RenderPickStatus.Unsupported)
            {
                return RendererPickOutcome.Fallback;
            }

            if (result.Status == RenderPickStatus.Hit &&
                !string.IsNullOrWhiteSpace(result.PrimPath))
            {
                await primPicked(result.PrimPath, cancellationToken).ConfigureAwait(false);
            }

            return RendererPickOutcome.Completed;
        }
    }

    internal static class RendererPickDispatch
    {
        public static async Task<RendererPickFailureFallback> PickFromPointerAsync(
            Func<RenderPickRequest> createRequest,
            Func<RenderPickRequest, CancellationToken, ValueTask<RendererPickOutcome>> pickAsync,
            RendererPickFallbackController fallbackController,
            CancellationToken cancellationToken)
        {
            try
            {
                RenderPickRequest request = createRequest();
                RendererPickOutcome outcome = await Task.Run(
                    () => pickAsync(request, cancellationToken).AsTask(),
                    cancellationToken).ConfigureAwait(false);
                if (outcome == RendererPickOutcome.Fallback)
                {
                    return fallbackController.DisableRenderer(null);
                }
            }
            catch (OperationCanceledException)
            {
                // The viewport closed or the caller cancelled.
            }
#pragma warning disable CA1031 // Picking is best-effort and must not affect the render loop.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return fallbackController.DisableRenderer(exception);
            }

            return RendererPickFailureFallback.None;
        }
    }

    internal sealed class RendererPickFallbackController
    {
        public RendererPickFallbackController(
            UsdViewPickMode mode,
            bool hasCallback,
            Action<Exception?> reportRendererFailure,
            Action startCommandPrimWatcher)
        {
            m_mode = mode;
            m_hasCallback = hasCallback;
            m_reportRendererFailure = reportRendererFailure;
            m_startCommandPrimWatcher = startCommandPrimWatcher;
        }

        public void MarkCommandPrimWatcherStarted()
        {
            using (m_lock.EnterScope())
            {
                m_commandPrimWatcherStarted = true;
            }
        }

        public RendererPickFailureFallback DisableRenderer(Exception? exception)
        {
            Action? startCommandPrimWatcher = null;
            bool reportFailure = false;
            using (m_lock.EnterScope())
            {
                if (!m_rendererFailureReported)
                {
                    m_rendererFailureReported = true;
                    reportFailure = true;
                }

                if (m_mode == UsdViewPickMode.Auto &&
                    m_hasCallback &&
                    !m_commandPrimWatcherStarted)
                {
                    m_commandPrimWatcherStarted = true;
                    startCommandPrimWatcher = m_startCommandPrimWatcher;
                }
            }

            if (reportFailure)
            {
                m_reportRendererFailure(exception);
            }

            startCommandPrimWatcher?.Invoke();
            return startCommandPrimWatcher is null
                ? RendererPickFailureFallback.None
                : RendererPickFailureFallback.CommandPrim;
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly UsdViewPickMode m_mode;
        private readonly bool m_hasCallback;
        private readonly Action<Exception?> m_reportRendererFailure;
        private readonly Action m_startCommandPrimWatcher;
        private bool m_rendererFailureReported;
        private bool m_commandPrimWatcherStarted;
    }

    internal static class RendererPickBackendDiscovery
    {
        public static IOpenUsdPickBackend? TryFindPickBackend(object root)
        {
            try
            {
                return TryFindPickBackend(root, includeProperties: false) ??
                    TryFindPickBackend(root, includeProperties: true);
            }
#pragma warning disable CA1031 // Reflection probes must never affect the render loop.
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        internal sealed class RenderPickingBackendAdapter : IOpenUsdPickBackend
        {
            public RenderPickingBackendAdapter(IRenderPickingBackend backend)
            {
                m_backend = backend;
            }

            public ValueTask<RenderPickResult> PickAsync(
                RenderPickRequest request,
                CancellationToken cancellationToken)
            {
                return m_backend.PickAsync(request, cancellationToken);
            }

            private readonly IRenderPickingBackend m_backend;
        }

        internal sealed class ReflectedPickBackend : IOpenUsdPickBackend
        {
            public ReflectedPickBackend(object backend, MethodInfo pickMethod)
            {
                m_backend = backend;
                m_pickMethod = pickMethod;
            }

            public ValueTask<RenderPickResult> PickAsync(
                RenderPickRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_pickMethod.Invoke(m_backend, [request]) is RenderPickResult result)
                {
                    return ValueTask.FromResult(result);
                }
                return ValueTask.FromResult(RenderPickResult.Unsupported(
                    in request, request.RequestedStateRevision, request.RequestedSceneRevision));
            }

            private readonly object m_backend;
            private readonly MethodInfo m_pickMethod;
        }

        private static IOpenUsdPickBackend? TryFindPickBackend(object root, bool includeProperties)
        {
            const int maxVisited = 32;
            var visited = new HashSet<object>(ReferenceIdentityComparer.Instance);
            var queue = new Queue<object>();
            queue.Enqueue(root);
            while (queue.Count > 0 && visited.Count < maxVisited)
            {
                object current = queue.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (current is IRenderPickingBackend pickingBackend)
                {
                    return new RenderPickingBackendAdapter(pickingBackend);
                }

                Type currentType = current.GetType();
                MethodInfo? pickMethod = currentType.GetMethod(
                    "Pick",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    [typeof(RenderPickRequest)],
                    modifiers: null);
                if (pickMethod is not null &&
                    pickMethod.ReturnType == typeof(RenderPickResult))
                {
                    return new ReflectedPickBackend(current, pickMethod);
                }

                if (!ReferenceEquals(current, root) && !IsOpenUsdType(currentType))
                {
                    continue;
                }

                EnqueueMembers(current, currentType, visited, queue, includeProperties);
            }
            return null;
        }

        private static void EnqueueMembers(
            object current,
            Type currentType,
            HashSet<object> visited,
            Queue<object> queue,
            bool includeProperties)
        {
            const BindingFlags flags = BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance;
            foreach (FieldInfo field in currentType.GetFields(flags))
            {
                EnqueueValue(TryGetValue(() => field.GetValue(current)), visited, queue);
            }
            if (!includeProperties)
            {
                return;
            }

            foreach (PropertyInfo property in currentType.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length == 0)
                {
                    EnqueueValue(TryGetValue(() => property.GetValue(current)), visited, queue);
                }
            }
        }

        private static object? TryGetValue(Func<object?> getValue)
        {
            try
            {
                return getValue();
            }
#pragma warning disable CA1031 // Reflection probes ignore inaccessible or throwing members.
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        private static void EnqueueValue(
            object? value,
            HashSet<object> visited,
            Queue<object> queue)
        {
            if (value is null || visited.Contains(value) || !IsOpenUsdType(value.GetType()))
            {
                return;
            }
            queue.Enqueue(value);
        }

        private static bool IsOpenUsdType(Type type)
        {
            string? ns = type.Namespace;
            return ns is not null && ns.StartsWith("OpenUsd", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Renders the connector's composed stage with the OpenUSD Avalonia viewer and hands
    /// the connector a sink that authors into that very stage, so subscribed OPC UA values
    /// animate the picture on screen instead of a file nobody reloads.
    /// </summary>
    /// <remarks>
    /// The connector discovers this type by name through
    /// <see cref="IUsdViewHost"/> and never references the rendering stack, so the
    /// connector package stays free of Avalonia and the native OpenUSD payload.
    /// </remarks>
    [ExcludeFromCodeCoverage(
        Justification = "Shell only: opens Avalonia windows and binds native OpenUSD payloads; " +
            "renderer pick dispatch and fallback decisions live in tested seams outside this class.")]
    public sealed class OpenUsdViewHost : IUsdViewHost
    {
        /// <inheritdoc/>
        [ExcludeFromCodeCoverage(
            Justification = "This shell opens Avalonia windows and loads the native OpenUSD renderer; " +
                "renderer pick dispatch and fallback decisions are covered by unit tests.")]
        public void RunViewport(
            UsdViewOptions options,
            Func<IUsdSink, CancellationToken, Task> sessionAsync,
            CancellationToken cancellationToken)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (sessionAsync is null)
            {
                throw new ArgumentNullException(nameof(sessionAsync));
            }
            if (string.IsNullOrWhiteSpace(options.StagePath))
            {
                throw new ArgumentException(
                    "A stage path is required to open the viewport.", nameof(options));
            }

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            UsdStageSink? sink = null;
            Task? session = null;
            Task? pickWatcher = null;
            var rendererPick = new RendererPickAttachmentHolder();
            ILogger logger = options.Telemetry.CreateLogger<OpenUsdViewHost>();

            var hostOptions = new ViewerHostOptions
            {
                StagePath = Path.GetFullPath(options.StagePath),
                PluginPath = options.PluginPath ?? ResolvePluginPath(),
                Renderer = options.Renderer,
                Title = options.Title ?? "OPC UA - OpenUSD Connector",
                StageCameraPath = options.CameraPath,
                ShutdownToken = lifetime.Token,
                StageReadyAsync = (stageSession, stageToken) =>
                {
                    sink = new UsdStageSink(stageSession.Scheduler, ReportSinkFailure);
                    var pickWatcherLock = new System.Threading.Lock();
                    RendererPickFallbackController? fallbackController = null;
                    void StartCommandPrimWatcher()
                    {
                        using (pickWatcherLock.EnterScope())
                        {
                            if (pickWatcher is not null)
                            {
                                return;
                            }

                            pickWatcher = Task.Run(
                                () => WatchCommandPrimAsync(
                                    stageSession.Scheduler,
                                    options.CommandPrimPath,
                                    options.PrimPicked!,
                                    logger,
                                    lifetime.Token,
                                    stageToken),
                                CancellationToken.None);
                        }
                    }

                    fallbackController = new RendererPickFallbackController(
                        options.PickMode,
                        options.PrimPicked is not null,
                        exception => ReportRendererPickFailure(logger, exception),
                        StartCommandPrimWatcher);
                    bool rendererPickStarted = false;
                    PickModeDecision pickDecision = PickModeSelection.Select(
                        options.PickMode, options.PrimPicked is not null, rendererStarted: false);
                    if (pickDecision.ProbeRenderer && options.PrimPicked is not null)
                    {
                        rendererPick.Attachment = TryAttachRendererPick(
                            options.PrimPicked, fallbackController, logger, lifetime.Token, stageToken);
                        rendererPickStarted = rendererPick.Attachment is not null;
                    }
                    pickDecision = PickModeSelection.Select(
                        options.PickMode, options.PrimPicked is not null, rendererPickStarted);
                    if (pickDecision.WatchCommandPrim && options.PrimPicked is not null)
                    {
                        fallbackController.MarkCommandPrimWatcherStarted();
                        StartCommandPrimWatcher();
                    }
                    // The viewport owns the UI thread, so the OPC UA pipeline runs on the
                    // thread pool. The stage token fires when the document closes, which
                    // is what stops the session.
                    session = Task.Run(
                        () => RunSessionAsync(sessionAsync, sink, lifetime.Token, stageToken),
                        CancellationToken.None);
                    return Task.CompletedTask;
                }
            };

            try
            {
                ViewerEntryPoint.Run(hostOptions);
            }
            finally
            {
                lifetime.Cancel();
                rendererPick.Attachment?.Dispose();
                DrainSession(session);
                ObservePickWatcher(pickWatcher);
                DisposeSink(sink);
            }
        }

        [ExcludeFromCodeCoverage(
            Justification = "Walks Avalonia desktop lifetime and visual tree on the UI thread; " +
                "backend discovery and fallback decisions are covered by unit tests.")]
        private static RendererPickAttachment? TryAttachRendererPick(
            Func<string, CancellationToken, Task> primPicked,
            RendererPickFallbackController fallbackController,
            ILogger logger,
            CancellationToken hostToken,
            CancellationToken stageToken)
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                    desktop.MainWindow is not { } window)
                {
                    return null;
                }

                StormViewportControl? viewport = FindVisualDescendant<StormViewportControl>(window);
                if (viewport is null)
                {
                    return null;
                }

                IOpenUsdPickBackend? backend = RendererPickBackendDiscovery.TryFindPickBackend(viewport);
                if (backend is null)
                {
                    return null;
                }

                var context = new RendererPickContext(
                    viewport, backend, primPicked, fallbackController, hostToken, stageToken);
                viewport.PointerPressed += context.OnPointerPressed;
                return new RendererPickAttachment(viewport, context);
            }
#pragma warning disable CA1031 // Renderer pick discovery is opportunistic; command prim fallback handles failure.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.RendererPickAttachFailed(exception);
                return null;
            }
        }

        private static T? FindVisualDescendant<T>(Visual root)
            where T : Visual
        {
            foreach (Visual visual in root.GetVisualDescendants())
            {
                if (visual is T match)
                {
                    return match;
                }
            }
            return null;
        }

        private static async Task WatchCommandPrimAsync(
            UsdStageScheduler scheduler,
            string commandPrimPath,
            Func<string, CancellationToken, Task> primPicked,
            ILogger logger,
            CancellationToken hostToken,
            CancellationToken stageToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                hostToken, stageToken);
            try
            {
                var state = new CommandPickState();
                await ReadAndRaiseCommandPrimAsync(
                    scheduler, commandPrimPath, primPicked, state, emitInitialTarget: false,
                    linked.Token).ConfigureAwait(false);

                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                while (await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false))
                {
                    await ReadAndRaiseCommandPrimAsync(
                        scheduler, commandPrimPath, primPicked, state, emitInitialTarget: true,
                        linked.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The viewport closed or the caller cancelled.
            }
#pragma warning disable CA1031 // Pick callbacks are best-effort and must not tear the viewport down.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.CommandPrimPickWatcherFailed(exception);
            }
        }

        private static async Task ReadAndRaiseCommandPrimAsync(
            UsdStageScheduler scheduler,
            string commandPrimPath,
            Func<string, CancellationToken, Task> primPicked,
            CommandPickState state,
            bool emitInitialTarget,
            CancellationToken cancellationToken)
        {
            string? target = await scheduler.InvokeAsync(
                stage => ReadCommandTarget(stage, commandPrimPath),
                cancellationToken).ConfigureAwait(false);
            if (!UsdViewPickCommand.TryUpdatePickedPrim(
                target, ref state.LastTarget, emitInitialTarget, out string pickedPrimPath))
            {
                return;
            }

            await primPicked(pickedPrimPath, cancellationToken).ConfigureAwait(false);
        }

        private static string? ReadCommandTarget(UsdStage stage, string commandPrimPath)
        {
            if (string.IsNullOrWhiteSpace(commandPrimPath) ||
                commandPrimPath[0] != '/')
            {
                return null;
            }

            UsdPrim prim = stage.GetPrim(commandPrimPath);
            if (!prim.Exists())
            {
                return null;
            }

            string[] targets = prim.GetRelationshipTargets("targetPrim");
            if (targets.Length > 0)
            {
                return targets[0];
            }

            return TryReadTargetAttribute(prim, readString: true) ??
                TryReadTargetAttribute(prim, readString: false);
        }

        private static string? TryReadTargetAttribute(UsdPrim prim, bool readString)
        {
            try
            {
                return readString ? prim.GetString("targetPrim") : prim.GetToken("targetPrim");
            }
#pragma warning disable CA1031 // Missing or differently-typed fallback attributes are ignored.
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        private static void ObservePickWatcher(Task? pickWatcher)
        {
            if (pickWatcher is null || !pickWatcher.IsCompleted)
            {
                return;
            }

            _ = pickWatcher.Exception;
        }

        private sealed class CommandPickState
        {
            public string? LastTarget;
        }

        [ExcludeFromCodeCoverage(
            Justification = "Binds Avalonia pointer events to the tested renderer pick dispatch seam.")]
        private sealed class RendererPickContext
        {
            public RendererPickContext(
                StormViewportControl viewport,
                IOpenUsdPickBackend backend,
                Func<string, CancellationToken, Task> primPicked,
                RendererPickFallbackController fallbackController,
                CancellationToken hostToken,
                CancellationToken stageToken)
            {
                m_viewport = viewport;
                m_backend = backend;
                m_primPicked = primPicked;
                m_fallbackController = fallbackController;
                m_hostToken = hostToken;
                m_stageToken = stageToken;
            }

            public async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
            {
                try
                {
                    if (m_hostToken.IsCancellationRequested || m_stageToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await RendererPickDispatch.PickFromPointerAsync(
                        () => CreatePickRequest(e),
                        (request, cancellationToken) => RendererPickLogic.PickAsync(
                            m_backend, request, m_primPicked, cancellationToken),
                        m_fallbackController,
                        m_hostToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The viewport closed or the caller cancelled.
                }
            }

            [ExcludeFromCodeCoverage(
                Justification = "Reads Avalonia UI-thread-affine pointer and viewport properties; " +
                    "the pure request construction logic is covered separately.")]
            private RenderPickRequest CreatePickRequest(PointerPressedEventArgs e)
            {
                Point position = e.GetPosition(m_viewport);
                double scale = TopLevel.GetTopLevel(m_viewport)?.RenderScaling ?? 1.0;
                RendererPickPixel pixel = RendererPickLogic.ToPhysicalPixel(
                    position.X, position.Y, m_viewport.Bounds.Width, m_viewport.Bounds.Height, scale);
                ulong stateRevision = TryGetCurrentRenderState(m_viewport, out StageRenderState state)
                    ? state.Revision
                    : 0;
                return RendererPickLogic.CreateRequest(pixel, stateRevision);
            }

            private static bool TryGetCurrentRenderState(
                StormViewportControl viewport,
                out StageRenderState state)
            {
                try
                {
                    PropertyInfo? property = viewport.GetType().GetProperty(
                        "CurrentRenderState",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property?.GetValue(viewport) is StageRenderState renderState)
                    {
                        state = renderState;
                        return true;
                    }
                }
#pragma warning disable CA1031 // A missing state only makes the pick more likely to return stale.
                catch (Exception)
#pragma warning restore CA1031
                {
                    // The viewport does not expose a render state; fall back to the default.
                    state = StageRenderState.Default;
                    return false;
                }

                state = StageRenderState.Default;
                return false;
            }

            private readonly StormViewportControl m_viewport;
            private readonly IOpenUsdPickBackend m_backend;
            private readonly Func<string, CancellationToken, Task> m_primPicked;
            private readonly RendererPickFallbackController m_fallbackController;
            private readonly CancellationToken m_hostToken;
            private readonly CancellationToken m_stageToken;
        }

        private sealed class RendererPickAttachment : IDisposable
        {
            public RendererPickAttachment(
                StormViewportControl viewport,
                RendererPickContext context)
            {
                m_viewport = viewport;
                m_context = context;
            }

            public void Dispose()
            {
                m_viewport.PointerPressed -= m_context.OnPointerPressed;
            }

            private readonly StormViewportControl m_viewport;
            private readonly RendererPickContext m_context;
        }

        private sealed class RendererPickAttachmentHolder
        {
            public RendererPickAttachment? Attachment;
        }

        private static async Task RunSessionAsync(
            Func<IUsdSink, CancellationToken, Task> sessionAsync,
            IUsdSink sink,
            CancellationToken hostToken,
            CancellationToken stageToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                hostToken, stageToken);
            try
            {
                await sessionAsync(sink, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The window closed or the caller cancelled; both are ordinary shutdowns.
            }
#pragma warning disable CA1031 // A failing session must not tear the viewport down.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine($"The OPC UA session ended with an error: {exception.Message}");
            }
        }

        /// <summary>
        /// Waits briefly for the session to unwind so its final values reach the stage,
        /// without letting a stuck session block process exit.
        /// </summary>
        private static void DrainSession(Task? session)
        {
            if (session is null)
            {
                return;
            }
            try
            {
                session.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Already reported by RunSessionAsync.
            }
        }

        private static void DisposeSink(UsdStageSink? sink)
        {
            if (sink is null)
            {
                return;
            }
            try
            {
                sink.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // The stage is going away regardless.
            }
        }

        private static void ReportSinkFailure(Exception exception)
        {
            Console.Error.WriteLine($"A stage update failed: {exception.Message}");
        }

        private static void ReportRendererPickFailure(ILogger logger, Exception? exception)
        {
            if (exception is null)
            {
                logger.RendererPickUnsupported();
                return;
            }

            logger.RendererPickFailed(exception);
        }

        /// <summary>
        /// Locates the staged USD plugin tree next to the running application, which is
        /// how both the viewer staging script and the runtime packages lay it out.
        /// </summary>
        private static string? ResolvePluginPath()
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, "plugin", "usd");
            return Directory.Exists(candidate) ? candidate : null;
        }
    }

    internal static partial class OpenUsdViewHostLog
    {
        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 0, Level = LogLevel.Warning,
            Message = "Renderer viewport pick failed; CommandPrim fallback will be enabled " +
                "when Auto pick mode allows it.")]
        public static partial void RendererPickFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 1, Level = LogLevel.Warning,
            Message = "Renderer viewport pick is unsupported; CommandPrim fallback will be enabled " +
                "when Auto pick mode allows it.")]
        public static partial void RendererPickUnsupported(this ILogger logger);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 2, Level = LogLevel.Warning,
            Message = "Renderer pick attachment failed; CommandPrim fallback will be used when pick mode allows it.")]
        public static partial void RendererPickAttachFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 3, Level = LogLevel.Warning,
            Message = "The viewport CommandPrim pick watcher ended with an error.")]
        public static partial void CommandPrimPickWatcherFailed(this ILogger logger, Exception exception);
    }

    internal static class OpenUsdViewerEventIds
    {
        public const int ViewHost = 0;
    }
}
