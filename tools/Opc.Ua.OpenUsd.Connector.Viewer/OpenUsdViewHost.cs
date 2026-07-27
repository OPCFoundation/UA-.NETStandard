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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.OpenUsd.Client;
using OpenUsd.Viewer;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
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
    public sealed class OpenUsdViewHost : IUsdViewHost
    {
        /// <inheritdoc/>
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
                DisposeSink(sink);
            }
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

        private static void ReportSinkFailure(Exception exception) =>
            Console.Error.WriteLine($"A stage update failed: {exception.Message}");

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
}
