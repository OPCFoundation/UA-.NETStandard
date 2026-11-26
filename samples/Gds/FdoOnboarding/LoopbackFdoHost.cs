// Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Transport;
using Fdo.Transport.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FdoOnboarding
{
    /// <summary>
    /// Runs one FDO role at a time on an HTTP listener bound only to IPv4 loopback.
    /// </summary>
    internal sealed class LoopbackFdoHost : IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership of the listener.
        /// </summary>
        private LoopbackFdoHost(WebApplication application)
        {
            m_application = application;
        }

        /// <summary>
        /// Gets the actual dynamically assigned endpoint.
        /// </summary>
        public Uri Address => new(m_application.Urls.Single());

        /// <summary>
        /// Starts the listener without HTTP logging of session authorization headers.
        /// </summary>
        public static async Task<LoopbackFdoHost> StartAsync(int maxBytes, CancellationToken ct)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Limits.MaxRequestBodySize = maxBytes;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            });
            WebApplication application = builder.Build();
            var host = new LoopbackFdoHost(application);
            application.Run(host.HandleAsync);
            try
            {
                await application.StartAsync(ct).ConfigureAwait(false);
                return host;
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Exposes one role binding until both the client and the server finish the exchange.
        /// </summary>
        public async Task<T> RunAsync<T>(
            FdoHttpServerSession binding, Func<CancellationToken, ValueTask<T>> client, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var phase = new Phase(binding, timeout.Token);
            if (Interlocked.CompareExchange(ref m_phase, phase, null) != null)
            {
                throw new InvalidOperationException("Another FDO role is active.");
            }
            try
            {
                T result = await client(timeout.Token).ConfigureAwait(false);
                await phase.Completed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                return result;
            }
            catch (FdoTransportException) when (phase.Completed.Task.IsFaulted)
            {
                await phase.Completed.Task.ConfigureAwait(false);
                throw;
            }
            finally
            {
                Volatile.Write(ref m_phase, null);
                await timeout.CancelAsync().ConfigureAwait(false);
                await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_application.DisposeAsync().ConfigureAwait(false);
            m_gate.Dispose();
        }

        /// <summary>
        /// Lets the library validate FDO framing, authorization and protocol messages.
        /// </summary>
        private async Task HandleAsync(HttpContext context)
        {
            await m_gate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
            try
            {
                Phase? phase = Volatile.Read(ref m_phase);
                if (phase == null || phase.Binding.IsComplete)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    context.RequestAborted, phase.CancellationToken);
                try
                {
                    FdoHttpServerResponse response = await phase.Binding.ProcessAsync(
                        new FdoHttpServerRequest
                        {
                            Method = context.Request.Method,
                            Target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ??
                                throw new InvalidDataException("The listener did not provide the raw request target."),
                            AuthorizationValues = context.Request.Headers.Authorization
                                .Select(value => value!).ToArray(),
                            ContentTypeValues = context.Request.Headers.ContentType.Select(value => value!).ToArray(),
                            ContentLength = context.Request.ContentLength,
                            Body = context.Request.Body
                        }, requestTimeout.Token).ConfigureAwait(false);
                    context.Response.StatusCode = response.StatusCode;
                    context.Response.Headers.Authorization = response.Authorization;
                    ReadOnlyMemory<byte> body = response.EncodedBody;
                    context.Response.ContentLength = body.Length;
                    if (!body.IsEmpty)
                    {
                        context.Response.ContentType = "application/cbor";
                        context.Response.Headers["Message-Type"] =
                            response.MessageType.ToString(CultureInfo.InvariantCulture);
                        await context.Response.Body.WriteAsync(body, requestTimeout.Token).ConfigureAwait(false);
                    }
                    await context.Response.CompleteAsync().ConfigureAwait(false);
                    if (response.IsTerminal)
                    {
                        return;
                    }
                    phase.Binding.ConfirmResponseSent();
                    if (phase.Binding.IsComplete)
                    {
                        phase.Completed.TrySetResult();
                    }
                }
                catch (Exception ex)
                {
                    phase.Completed.TrySetException(ex);
                    context.Abort();
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// The single in-flight HTTP exchange, including the final response handoff.
        /// </summary>
        private sealed record Phase(FdoHttpServerSession Binding, CancellationToken CancellationToken)
        {
            /// <summary>
            /// Gets the signal for the final response handoff or terminal processing failure.
            /// </summary>
            public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>The loopback web application.</summary>
        private readonly WebApplication m_application;
        /// <summary>Serializes processing and handoff before the next request can enter the role.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
        /// <summary>The active role, if the demo is currently running an exchange.</summary>
        private Phase? m_phase;
    }
}
