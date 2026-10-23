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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Opc.Ua;
using Opc.Ua.OpenUsd.Connector.Viewer;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;
using UaLens.Workspace;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Inert composition root for one document-owned viewport and rendered Vision adapter.
    /// </summary>
    internal sealed class OpenUsdViewportFactory
    {
        /// <summary>
        /// Composes injectable source, cache and surface factories without starting native work.
        /// </summary>
        public OpenUsdViewportFactory(
            IOpenUsdViewportSourceFactory? source = null,
            IOpenUsdViewportAssetCache? assets = null,
            Func<ITelemetryContext, IOpenUsdViewportSurface>? createSurface = null)
        {
            m_source = source ?? new OpenUsdCompanionProvider();
            m_assets = assets ?? new OpenUsdViewportAssetCache();
            m_createSurface = createSurface ?? (static telemetry => new OpenUsdViewportSurface(telemetry));
        }

        /// <summary>
        /// Creates one inert document lifetime.
        /// </summary>
        public OpenUsdViewportSession Create(ITelemetryContext telemetry)
        {
            return new OpenUsdViewportSession(m_source, m_assets, m_createSurface, telemetry);
        }

        private readonly IOpenUsdViewportSourceFactory m_source;
        private readonly IOpenUsdViewportAssetCache m_assets;
        private readonly Func<ITelemetryContext, IOpenUsdViewportSurface> m_createSurface;
    }

    /// <summary>
    /// Cancels and drains source, native readbacks and picks before releasing the scene/cache.
    /// UI entry points keep the application's existing dispatcher; no connection is created or disposed.
    /// </summary>
    internal sealed class OpenUsdViewportSession : IAsyncDisposable
    {
        /// <summary>
        /// Creates a lifetime that borrows its primary-session source and owns only preview resources.
        /// </summary>
        public OpenUsdViewportSession(
            IOpenUsdViewportSourceFactory sourceFactory,
            IOpenUsdViewportAssetCache cache,
            Func<ITelemetryContext, IOpenUsdViewportSurface> createSurface,
            ITelemetryContext telemetry)
        {
            m_sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
            m_cache = cache ?? throw new ArgumentNullException(nameof(cache));
            m_createSurface = createSurface ?? throw new ArgumentNullException(nameof(createSurface));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Gets the live surface, or null before start and after disposal.
        /// </summary>
        public Control? Surface => m_surface?.View;

        /// <summary>
        /// Gets the captured stage's logical identity.
        /// </summary>
        public string StageIdentifier => m_source?.RootIdentifier ?? string.Empty;

        /// <summary>
        /// Gets the active stage camera path.
        /// </summary>
        public string CameraPath => m_surface?.CameraPath ?? string.Empty;

        /// <summary>
        /// Gets the number of telemetry values handed to the stage sink.
        /// </summary>
        public long AppliedValues => m_source?.AppliedValues ?? 0;

        /// <summary>
        /// Gets live or final native resource evidence.
        /// </summary>
        public UsdViewportStatistics Statistics => m_surface?.Statistics ?? m_finalStatistics;

        /// <summary>
        /// Gets the first native or source failure.
        /// </summary>
        public Exception? Failure => Volatile.Read(ref m_failure);

        /// <summary>
        /// Gets whether a current stage has rendered its first frame.
        /// </summary>
        public bool IsStarted { get; private set; }

        /// <summary>
        /// Reports surface attachment and removal on the owning dispatcher.
        /// </summary>
        public event EventHandler? SurfaceChanged;

        /// <summary>
        /// Reports the first source or renderer failure.
        /// </summary>
        public event EventHandler<Exception>? Failed;

        /// <summary>
        /// Explicitly captures assets, opens the renderer and starts the bounded live source.
        /// </summary>
        public async Task StartAsync(
            CompanionContext context, CompanionTarget target, string? cameraPath, CancellationToken cancellationToken)
        {
            if (m_startCalled)
            {
                throw new InvalidOperationException("A viewport lifetime may only be started once.");
            }
            m_startCalled = true;
            try
            {
                await m_operations.ExecuteAsync(async token =>
                {
                    m_source = await m_sourceFactory.OpenViewportAsync(context, target, token).ConfigureAwait(true);
                    m_source.RequireCurrent();
                    m_assets = await m_cache.AcquireAsync(
                        m_source.RootIdentifier, m_source.PrimPath, m_source.Assets, token).ConfigureAwait(true);
                    token.ThrowIfCancellationRequested();
                    m_source.RequireCurrent();
                    m_surface = m_createSurface(m_telemetry) ??
                        throw new InvalidOperationException("No embedded viewport surface was configured.");
                    m_surface.Failed += OnNativeFailed;
                    await m_surface.StartAsync(m_assets.StagePath, m_source.RootIdentifier, cameraPath, token)
                        .ConfigureAwait(true);
                    m_capture = new RetainedSceneCameraCaptureProvider(m_surface, m_telemetry);
                    SurfaceChanged?.Invoke(this, EventArgs.Empty);
                    m_live = RunLiveAsync(m_lifetime.Token);
                    using var warmup = CancellationTokenSource.CreateLinkedTokenSource(token);
                    warmup.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        _ = await m_surface.WaitForFirstFrameAsync(warmup.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "No OpenUSD geometry was presented within 30 seconds. " + m_surface.Status);
                    }
                    token.ThrowIfCancellationRequested();
                    if (Failure is { } failure)
                    {
                        throw new InvalidOperationException("The viewport failed during startup.", failure);
                    }
                    m_lifetime.Token.ThrowIfCancellationRequested();
                    m_source.RequireCurrent();
                    IsStarted = true;
                }, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (Failure is not null && !cancellationToken.IsCancellationRequested)
            {
                await DisposeAsync().ConfigureAwait(true);
                throw new InvalidOperationException("The viewport source failed during startup.", Failure);
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(true);
                throw;
            }
        }

        /// <summary>
        /// Changes camera within the retained stage while tracking cancellation and ownership.
        /// </summary>
        public ValueTask SetCameraAsync(string? path, CancellationToken cancellationToken)
        {
            return m_operations.ExecuteAsync(async token =>
            {
                RequireStarted();
                await m_surface!.SetCameraAsync(path, token).ConfigureAwait(false);
                m_lifetime.Token.ThrowIfCancellationRequested();
                m_source!.RequireCurrent();
            }, cancellationToken);
        }

        /// <summary>
        /// Selects a prim without dispatching server commands.
        /// </summary>
        public ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken)
        {
            return m_operations.ExecuteAsync(async token =>
            {
                RequireStarted();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                RenderPickResult result = await m_surface!.PickAsync(x, y, deadline.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                m_lifetime.Token.ThrowIfCancellationRequested();
                m_source!.RequireCurrent();
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Captures one bounded image through the existing Vision provider contract.
        /// </summary>
        public ValueTask<SceneCameraCaptureResult> CaptureAsync(CancellationToken cancellationToken)
        {
            return m_operations.ExecuteAsync(async token =>
            {
                RequireStarted();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                UsdViewportStatistics statistics = m_surface!.Statistics;
                SceneCameraCaptureResult result = await m_capture!.CaptureAsync(new SceneCameraCaptureRequest
                {
                    StageIdentifier = m_source!.RootIdentifier,
                    PrimPath = m_surface.CameraPath,
                    Width = statistics.Width,
                    Height = statistics.Height
                }, deadline.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                m_lifetime.Token.ThrowIfCancellationRequested();
                m_source.RequireCurrent();
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Immediately cancels live source work; disposal additionally drains requests and frees resources.
        /// </summary>
        public void Cancel()
        {
            if (!m_lifetime.IsCancellationRequested)
            {
                m_lifetime.Cancel();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(m_disposal ??= DisposeCoreAsync());
        }

        private async Task RunLiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await m_source!.RunAsync(m_surface!.Sink, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The owning document drains the subscription before disposing the stage.
            }
            catch (Exception error) when (
                error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                ReportFailure(error);
            }
        }

        private void RequireStarted()
        {
            if (!IsStarted || m_lifetime.IsCancellationRequested || Failure is not null)
            {
                throw new InvalidOperationException("Start a current, healthy viewport before using it.");
            }
            m_source!.RequireCurrent();
        }

        private void OnNativeFailed(object? sender, Exception error) => ReportFailure(error);

        private void ReportFailure(Exception error)
        {
            if (Interlocked.CompareExchange(ref m_failure, error, null) is null)
            {
                m_lifetime.Cancel();
                Failed?.Invoke(this, error);
            }
        }

        private async Task DisposeCoreAsync()
        {
            IsStarted = false;
            await m_lifetime.CancelAsync().ConfigureAwait(true);
            await m_operations.DisposeAsync().ConfigureAwait(true);
            try
            {
                try
                {
                    await m_live.ConfigureAwait(true);
                }
                finally
                {
                    if (m_surface is not null)
                    {
                        m_surface.Failed -= OnNativeFailed;
                        await m_surface.DisposeAsync().ConfigureAwait(true);
                        m_finalStatistics = m_surface.Statistics;
                    }
                }
            }
            finally
            {
                try
                {
                    if (m_source is not null)
                    {
                        await m_source.DisposeAsync().ConfigureAwait(true);
                    }
                }
                finally
                {
                    if (m_assets is not null)
                    {
                        await m_assets.DisposeAsync().ConfigureAwait(true);
                    }
                    m_surface = null;
                    m_capture = null;
                    m_lifetime.Dispose();
                    SurfaceChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private readonly IOpenUsdViewportSourceFactory m_sourceFactory;
        private readonly IOpenUsdViewportAssetCache m_cache;
        private readonly Func<ITelemetryContext, IOpenUsdViewportSurface> m_createSurface;
        private readonly ITelemetryContext m_telemetry;
        private readonly OwnedOperationScope m_operations = new("The viewport document stopped.");
        private readonly CancellationTokenSource m_lifetime = new();
        private IOpenUsdViewportSurface? m_surface;
        private IOpenUsdViewportAssets? m_assets;
        private OpenUsdViewportSource? m_source;
        private RetainedSceneCameraCaptureProvider? m_capture;
        private UsdViewportStatistics m_finalStatistics = new(0, 0, 0, 0, 0, 0, 0, 0);
        private Task m_live = Task.CompletedTask;
        private Task? m_disposal;
        private Exception? m_failure;
        private bool m_startCalled;
    }
}
