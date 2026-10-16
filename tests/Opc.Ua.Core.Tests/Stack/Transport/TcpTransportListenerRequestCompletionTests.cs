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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Loopback tests of how the <see cref="TcpTransportListener"/> completes a request: the
    /// response is sent for a request that completes synchronously or later, and a request
    /// whose processing fails is logged and answered with a service fault.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TcpTransportListenerRequestCompletionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_unobserved = new ConcurrentQueue<Exception>();
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        [TearDown]
        public void TearDown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            Assert.That(m_unobserved, Is.Empty, "A request completion left an unobserved exception.");
        }

        /// <summary>
        /// Concurrent requests that complete synchronously, through a task, or later through a
        /// pooled value task source on another thread each get their own response, also while
        /// the listener reuses its pending-request continuations.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task EveryRequestGetsItsOwnResponseAcrossCompletionKindsAsync()
        {
            const int kChannels = 4;
            const int kRequestsPerChannel = 300;
            var callback = new ScriptedCallback(request =>
            {
                uint handle = request.RequestHeader.RequestHandle;
                IServiceResponse response = Respond(request);
                switch (handle % 3)
                {
                    case 0:
                        return new ValueTask<IServiceResponse>(response);
                    case 1:
                        return new ValueTask<IServiceResponse>(Task.Run(async () =>
                        {
                            await Task.Yield();
                            return response;
                        }));
                    default:
                        var source = new ManualResetValueTaskSource<IServiceResponse>();
                        _ = Task.Run(async () =>
                        {
                            await Task.Yield();
                            source.SetResult(response);
                        });
                        return source.Task;
                }
            });
            await using Loopback loopback = await Loopback.StartAsync(callback).ConfigureAwait(false);

            var clients = new UaSCUaBinaryTransportChannel[kChannels];
            try
            {
                for (int ii = 0; ii < kChannels; ii++)
                {
                    clients[ii] = await loopback.ConnectAsync().ConfigureAwait(false);
                }
                var requests = new List<Task<(uint Sent, uint Received)>>();
                for (int ii = 0; ii < kChannels * kRequestsPerChannel; ii++)
                {
                    UaSCUaBinaryTransportChannel client = clients[ii % kChannels];
                    uint handle = (uint)ii + 1;
                    requests.Add(Task.Run(async () =>
                    {
                        IServiceResponse response = await client.SendRequestAsync(
                            CreateReadRequest(handle), CancellationToken.None).ConfigureAwait(false);
                        return (handle, response.ResponseHeader.RequestHandle);
                    }));
                }
                (uint Sent, uint Received)[] results = await Task.WhenAll(requests).ConfigureAwait(false);

                Assert.That(results.Where(r => r.Sent != r.Received), Is.Empty);
                Assert.That(callback.RequestCount, Is.EqualTo(kChannels * kRequestsPerChannel));
                Assert.That(loopback.Logs.Where(l => l.Level >= LogLevel.Error), Is.Empty);
            }
            finally
            {
                foreach (UaSCUaBinaryTransportChannel client in clients)
                {
                    client?.Dispose();
                }
            }
        }

        /// <summary>
        /// Data-channel response notifications belong to one request even when channel evidence is cached.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(30000)]
        public async Task DataChannelResponseCallbacksRemainRequestScopedAsync(bool completeLater)
        {
            var contexts = new ConcurrentQueue<SecureChannelContext>();
            var notified = new ConcurrentQueue<uint>();
            var pending = new ConcurrentDictionary<uint, TaskCompletionSource<IServiceResponse>>();
            var callback = new Mock<ITransportListenerCallback>();
            callback.Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(),
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((SecureChannelContext context, IServiceRequest request, CancellationToken _) =>
                {
                    if (request is not OpenDataChannelRequest)
                    {
                        return new ValueTask<IServiceResponse>(Respond(request));
                    }

                    uint handle = request.RequestHeader.RequestHandle;
                    contexts.Enqueue(context);
                    context.ResponseDispatched += () => notified.Enqueue(handle);
                    IServiceResponse response = new OpenDataChannelResponse
                    {
                        ResponseHeader = new ResponseHeader { RequestHandle = handle }
                    };
                    if (!completeLater)
                    {
                        return new ValueTask<IServiceResponse>(response);
                    }

                    var completion = new TaskCompletionSource<IServiceResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    pending[handle] = completion;
                    return new ValueTask<IServiceResponse>(completion.Task);
                });

            await using Loopback loopback = await Loopback.StartAsync(callback.Object).ConfigureAwait(false);
            using UaSCUaBinaryTransportChannel client = await loopback.ConnectAsync().ConfigureAwait(false);
            var first = new OpenDataChannelRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1, TimeoutHint = 10000 }
            };
            var second = new OpenDataChannelRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 2, TimeoutHint = 10000 }
            };
            Task<IServiceResponse> responseA = client.SendRequestAsync(first, CancellationToken.None).AsTask();
            Task<IServiceResponse> responseB = client.SendRequestAsync(second, CancellationToken.None).AsTask();
            if (completeLater)
            {
                Assert.That(await WaitForAsync(() => pending.Count == 2).ConfigureAwait(false), Is.True);
                pending[2].SetResult(new OpenDataChannelResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = 2 }
                });
                await responseB.ConfigureAwait(false);
                Assert.That(await WaitForAsync(() => notified.Count == 1).ConfigureAwait(false), Is.True);
                Assert.That(notified, Is.EqualTo(new uint[] { 2 }));
                pending[1].SetResult(new OpenDataChannelResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = 1 }
                });
            }

            await Task.WhenAll(responseA, responseB).ConfigureAwait(false);
            await client.SendRequestAsync(CreateReadRequest(3), CancellationToken.None).ConfigureAwait(false);
            Assert.That(await WaitForAsync(() => notified.Count == 2).ConfigureAwait(false), Is.True);
            Assert.That(contexts, Has.Count.EqualTo(2));
            Assert.That(contexts.First(), Is.Not.SameAs(contexts.Last()));
            Assert.That(notified, Is.EquivalentTo(new uint[] { 1, 2 }));
            Assert.That(loopback.Logs.Where(log => log.Level >= LogLevel.Error), Is.Empty);
        }

        /// <summary>
        /// A request whose processing fails, synchronously, through a faulted task or later
        /// through a value task source, is logged as an error and answered with a service fault,
        /// and the listener keeps serving the channel.
        /// </summary>
        [TestCase(FailureKind.SynchronousThrow)]
        [TestCase(FailureKind.FaultedTask)]
        [TestCase(FailureKind.LaterFault)]
        [CancelAfter(30000)]
        public async Task ProcessingFailureIsLoggedAndAnsweredWithServiceFaultAsync(FailureKind kind)
        {
            var callback = new ScriptedCallback(request =>
            {
                if (request.RequestHeader.RequestHandle != 1)
                {
                    return new ValueTask<IServiceResponse>(Respond(request));
                }
                var error = new ServiceResultException(StatusCodes.BadNodeIdUnknown, "Injected failure.");
                switch (kind)
                {
                    case FailureKind.SynchronousThrow:
                        throw error;
                    case FailureKind.FaultedTask:
                        return new ValueTask<IServiceResponse>(Task.FromException<IServiceResponse>(error));
                    default:
                        var source = new ManualResetValueTaskSource<IServiceResponse>();
                        _ = Task.Run(async () =>
                        {
                            await Task.Yield();
                            source.SetException(error);
                        });
                        return source.Task;
                }
            });
            await using Loopback loopback = await Loopback.StartAsync(callback).ConfigureAwait(false);
            using UaSCUaBinaryTransportChannel client = await loopback.ConnectAsync().ConfigureAwait(false);

            IServiceResponse fault = await client.SendRequestAsync(CreateReadRequest(1), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(fault, Is.InstanceOf<ServiceFault>());
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(loopback.Logs.Count(l => l.Level == LogLevel.Error &&
                l.EventId == CoreEventIds.TcpTransportListener + 22),
                Is.EqualTo(1));

            IServiceResponse next = await client.SendRequestAsync(CreateReadRequest(2), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(next.ResponseHeader.RequestHandle, Is.EqualTo(2));
        }

        /// <summary>
        /// A callback's value task source that throws when the listener reads its state,
        /// registers the continuation or reads the result fails that request like a processing
        /// failure: logged, answered with a service fault, and the channel keeps working.
        /// </summary>
        [TestCase(ThrowingSourceMode.GetStatus)]
        [TestCase(ThrowingSourceMode.OnCompleted)]
        [TestCase(ThrowingSourceMode.GetResult)]
        [CancelAfter(30000)]
        public async Task ThrowingValueTaskSourceIsAnsweredWithServiceFaultAsync(ThrowingSourceMode mode)
        {
            var callback = new ScriptedCallback(request => request.RequestHeader.RequestHandle == 1
                ? new ValueTask<IServiceResponse>(new ThrowingSource(mode), 0)
                : new ValueTask<IServiceResponse>(Respond(request)));
            await using Loopback loopback = await Loopback.StartAsync(callback).ConfigureAwait(false);
            using UaSCUaBinaryTransportChannel client = await loopback.ConnectAsync().ConfigureAwait(false);

            IServiceResponse fault = await client.SendRequestAsync(CreateReadRequest(1), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fault, Is.InstanceOf<ServiceFault>());
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(loopback.Logs.Count(l => l.Level == LogLevel.Error &&
                l.EventId == CoreEventIds.TcpTransportListener + 22), Is.EqualTo(1));
            IServiceResponse next = await client.SendRequestAsync(CreateReadRequest(2), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(next.ResponseHeader.RequestHandle, Is.EqualTo(2));
        }

        /// <summary>
        /// A cancelled request is answered with the fault the endpoint creates for it.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task CancelledProcessingIsAnsweredWithServiceFaultAsync()
        {
            var callback = new ScriptedCallback(received =>
            {
                var source = new ManualResetValueTaskSource<IServiceResponse>();
                _ = Task.Run(async () =>
                {
                    await Task.Yield();
                    source.SetException(new OperationCanceledException());
                });
                return source.Task;
            });
            await using Loopback loopback = await Loopback.StartAsync(callback).ConfigureAwait(false);
            using UaSCUaBinaryTransportChannel client = await loopback.ConnectAsync().ConfigureAwait(false);
            ReadRequest request = CreateReadRequest(1);
            StatusCode expected = EndpointBase.CreateFault(
                loopback.Telemetry.CreateLogger<TcpTransportListenerRequestCompletionTests>(),
                request, new OperationCanceledException()).ResponseHeader.ServiceResult;

            IServiceResponse fault = await client.SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fault, Is.InstanceOf<ServiceFault>());
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(expected));
        }

        /// <summary>
        /// A response that completes after the client closed its channel cannot be sent: the
        /// listener logs that quietly, sends no fault, and keeps serving other channels.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ResponseAfterChannelClosedIsDroppedQuietlyAsync()
        {
            var processing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new ManualResetValueTaskSource<IServiceResponse>();
            var callback = new ScriptedCallback(request =>
            {
                if (request.RequestHeader.RequestHandle != 1)
                {
                    return new ValueTask<IServiceResponse>(Respond(request));
                }
                processing.TrySetResult(true);
                return source.Task;
            });
            await using Loopback loopback = await Loopback.StartAsync(callback).ConfigureAwait(false);
            UaSCUaBinaryTransportChannel closing = await loopback.ConnectAsync().ConfigureAwait(false);
            Task<IServiceResponse> abandoned = closing.SendRequestAsync(CreateReadRequest(1), CancellationToken.None)
                .AsTask();
            await processing.Task.ConfigureAwait(false);
            await closing.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            closing.Dispose();
            Assert.CatchAsync(async () => await abandoned.ConfigureAwait(false));

            source.SetResult(Respond(CreateReadRequest(1)));

            Assert.That(await WaitForAsync(() => loopback.Logs.Any(l =>
                l.EventId == CoreEventIds.TcpTransportListener + 21))
                .ConfigureAwait(false), Is.True, "The dropped response was not logged.");
            Assert.That(loopback.Logs.Where(l => l.Level >= LogLevel.Error), Is.Empty);
            using UaSCUaBinaryTransportChannel other = await loopback.ConnectAsync().ConfigureAwait(false);
            IServiceResponse response = await other.SendRequestAsync(CreateReadRequest(2), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(2));
        }

        public enum FailureKind
        {
            SynchronousThrow,
            FaultedTask,
            LaterFault
        }

        public enum ThrowingSourceMode
        {
            GetStatus,
            OnCompleted,
            GetResult
        }

        /// <summary>
        /// A value task source that fails at one step of being awaited.
        /// </summary>
        private sealed class ThrowingSource : IValueTaskSource<IServiceResponse>
        {
            public ThrowingSource(ThrowingSourceMode mode)
            {
                m_mode = mode;
            }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                return m_mode switch
                {
                    ThrowingSourceMode.GetStatus => throw Failure(),
                    ThrowingSourceMode.OnCompleted => ValueTaskSourceStatus.Pending,
                    _ => ValueTaskSourceStatus.Succeeded
                };
            }

            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                throw Failure();
            }

            public IServiceResponse GetResult(short token)
            {
                throw Failure();
            }

            private static ServiceResultException Failure()
            {
                return new ServiceResultException(StatusCodes.BadNotSupported, "Injected source failure.");
            }

            private readonly ThrowingSourceMode m_mode;
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            m_unobserved.Enqueue(e.Exception);
        }

        private static ReadResponse Respond(IServiceRequest request)
        {
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };
        }

        private static ReadRequest CreateReadRequest(uint handle)
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = handle, TimeoutHint = 10000 },
                NodesToRead = [new ReadValueId { NodeId = new NodeId(1001, 1), AttributeId = Attributes.Value }]
            };
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            return condition();
        }

        private ConcurrentQueue<Exception> m_unobserved = null!;

        /// <summary>
        /// A listener on a free loopback port with a log that the tests read.
        /// </summary>
        private sealed class Loopback : IAsyncDisposable
        {
            private Loopback(DefaultTelemetry telemetry, CapturingLoggerProvider logs, Uri endpointUrl)
            {
                Telemetry = telemetry;
                m_logs = logs;
                m_endpointUrl = endpointUrl;
                m_listener = new TcpTransportListener(telemetry);
            }

            public ITelemetryContext Telemetry { get; }

            public IReadOnlyCollection<LogEntry> Logs => [.. m_logs.Entries];

            public static async Task<Loopback> StartAsync(ITransportListenerCallback callback)
            {
                var logs = new CapturingLoggerProvider();
                var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                    builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
                var loopback = new Loopback(
                    telemetry, logs, new Uri($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}"));
                var certificateRegistry = new Mock<ICertificateRegistry>();
                certificateRegistry
                    .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                    .Returns((CertificateEntry)null!);
                await loopback.m_listener.OpenAsync(
                    loopback.m_endpointUrl,
                    new TransportListenerSettings
                    {
                        Descriptions = [loopback.Endpoint],
                        Configuration = CreateConfiguration(),
                        ServerCertificates = certificateRegistry.Object,
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create(),
                        MaxChannelCount = 10
                    },
                    callback,
                    CancellationToken.None).ConfigureAwait(false);
                return loopback;
            }

            public async Task<UaSCUaBinaryTransportChannel> ConnectAsync()
            {
                var channel = new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(Telemetry), Telemetry)
                {
                    OperationTimeout = 10000
                };
                try
                {
                    await channel.OpenAsync(
                        m_endpointUrl,
                        new TransportChannelSettings
                        {
                            Description = Endpoint,
                            Configuration = CreateConfiguration(),
                            NamespaceUris = new NamespaceTable(),
                            Factory = EncodeableFactory.Create()
                        },
                        CancellationToken.None).ConfigureAwait(false);
                    return channel;
                }
                catch
                {
                    channel.Dispose();
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                await m_listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_listener.DisposeAsync().ConfigureAwait(false);
                ((IDisposable)Telemetry).Dispose();
            }

            private EndpointDescription Endpoint => new()
            {
                EndpointUrl = m_endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };

            private static EndpointConfiguration CreateConfiguration()
            {
                var configuration = EndpointConfiguration.Create();
                configuration.OperationTimeout = 10000;
                configuration.ChannelLifetime = 60000;
                configuration.SecurityTokenLifetime = 60000;
                return configuration;
            }

            private static int GetFreeTcpPort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally
                {
                    listener.Stop();
                }
            }

            private readonly CapturingLoggerProvider m_logs;
            private readonly Uri m_endpointUrl;
            private readonly TcpTransportListener m_listener;
        }

        private readonly record struct LogEntry(LogLevel Level, int EventId, string Message);

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public ConcurrentQueue<LogEntry> Entries { get; } = new();

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Entries);
            }

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                public CapturingLogger(ConcurrentQueue<LogEntry> entries)
                {
                    m_entries = entries;
                }

                public IDisposable BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null!;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    m_entries.Enqueue(new LogEntry(logLevel, eventId.Id, formatter(state, exception)));
                }

                private readonly ConcurrentQueue<LogEntry> m_entries;
            }
        }

        /// <summary>
        /// Answers each request with the result of a script.
        /// </summary>
        private sealed class ScriptedCallback : ITransportListenerCallback
        {
            public ScriptedCallback(Func<IServiceRequest, ValueTask<IServiceResponse>> script)
            {
                m_script = script;
            }

            public int RequestCount => Volatile.Read(ref m_requestCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                return m_script(request);
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }

            private readonly Func<IServiceRequest, ValueTask<IServiceResponse>> m_script;
            private int m_requestCount;
        }
    }
}
