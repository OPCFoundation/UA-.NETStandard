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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.OpenUsd.Client;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Viewer;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    internal enum RendererPickFailureFallback
    {
        None,
        CommandPrim
    }

    internal readonly record struct PickModeDecision(
        bool UseRenderer,
        bool WatchCommandPrim);

    internal static class PickModeSelection
    {
        public static PickModeDecision Select(
            UsdViewPickMode mode,
            bool hasCallback)
        {
            if (!hasCallback)
            {
                return new PickModeDecision(false, false);
            }

            return mode switch
            {
                UsdViewPickMode.CommandPrim => new PickModeDecision(false, true),
                _ => new PickModeDecision(true, false)
            };
        }
    }

    internal static class ViewerPickCallback
    {
        public static Func<ViewerPickEventArgs, CancellationToken, Task>? CreateHandler(
            UsdViewOptions options,
            Func<RendererPickFallbackController?> getFallbackController,
            ILogger logger,
            CancellationToken hostToken)
        {
            PickModeDecision decision = PickModeSelection.Select(
                options.PickMode,
                options.PrimPicked is not null);
            if (!decision.UseRenderer || options.PrimPicked is null)
            {
                return null;
            }

            return (pick, pickToken) => HandleAsync(
                pick,
                options.PrimPicked,
                getFallbackController(),
                logger,
                hostToken,
                pickToken);
        }

        public static async Task HandleAsync(
            ViewerPickEventArgs pick,
            Func<string, CancellationToken, Task> primPicked,
            RendererPickFallbackController? fallbackController,
            ILogger logger,
            CancellationToken hostToken,
            CancellationToken pickToken)
        {
            if (pick.Status == RenderPickStatus.Unsupported)
            {
                fallbackController?.DisableRenderer(null);
                return;
            }

            if (pick.Status != RenderPickStatus.Hit || string.IsNullOrWhiteSpace(pick.PrimPath))
            {
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, pickToken);
            try
            {
                await primPicked(pick.PrimPath, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The viewport closed or the caller cancelled.
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                logger.PrimPickedCallbackFailed(exception);
            }
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
            ILogger logger = options.Telemetry.CreateLogger<OpenUsdViewHost>();
            RendererPickFallbackController? fallbackController = null;

            var hostOptions = new ViewerHostOptions
            {
                StagePath = Path.GetFullPath(options.StagePath),
                PluginPath = options.PluginPath ?? ResolvePluginPath(),
                Renderer = options.Renderer,
                Title = options.Title ?? "OPC UA - OpenUSD Connector",
                StageCameraPath = options.CameraPath,
                ShutdownToken = lifetime.Token,
                PickTarget = RenderPickTarget.Primitive,
                PrimPicked = ViewerPickCallback.CreateHandler(
                    options,
                    () => fallbackController,
                    logger,
                    lifetime.Token),
                StageReadyAsync = (stageSession, stageToken) =>
                {
                    sink = new UsdStageSink(stageSession.Scheduler, ReportSinkFailure);
                    var pickWatcherLock = new System.Threading.Lock();
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
                    PickModeDecision pickDecision = PickModeSelection.Select(
                        options.PickMode, options.PrimPicked is not null);
                    if (pickDecision.UseRenderer &&
                        options.PickMode == UsdViewPickMode.Auto &&
                        stageSession.PickingBackend is null)
                    {
                        fallbackController.DisableRenderer(null);
                    }
                    else if (pickDecision.WatchCommandPrim && options.PrimPicked is not null)
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
                DrainSession(session);
                ObservePickWatcher(pickWatcher);
                DisposeSink(sink);
            }
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
            Message = "Renderer viewport pick failed; CommandPrim fallback will be enabled when pick mode allows it.")]
        public static partial void RendererPickFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 1, Level = LogLevel.Warning,
            Message = "Renderer viewport pick is unsupported; CommandPrim fallback will be enabled when pick mode allows it.")]
        public static partial void RendererPickUnsupported(this ILogger logger);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 2, Level = LogLevel.Warning,
            Message = "The viewport PrimPicked callback failed.")]
        public static partial void PrimPickedCallbackFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 3, Level = LogLevel.Warning,
            Message = "The viewport CommandPrim pick watcher ended with an error.")]
        public static partial void CommandPrimPickWatcherFailed(this ILogger logger, Exception exception);
    }

    internal static class OpenUsdViewerEventIds
    {
        public const int ViewHost = 0;
    }
}
