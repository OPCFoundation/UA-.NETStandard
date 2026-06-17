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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Audit;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Audit
{
    [TestFixture]
    public sealed class LoggerPcapAuditSinkTests : TempDirectoryFixture
    {
        [Test]
        public async Task OnEventAsyncLogsAtWarning()
        {
            var logger = new RecordingLogger<LoggerPcapAuditSink>();
            var sink = new LoggerPcapAuditSink(logger);

            await sink.OnEventAsync(
                CreateEvent(PcapAuditEventKind.DumpKeys, sessionId: "warning-session"),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(logger.Entries[0].Message, Does.Contain("DumpKeys"));
        }

        [Test]
        public async Task OnEventAsyncRateLimitsFrameCapturedEvents()
        {
            var logger = new RecordingLogger<LoggerPcapAuditSink>();
            var sink = new LoggerPcapAuditSink(logger);
            string sessionId = Guid.NewGuid().ToString("N");
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;

            for (int ii = 0; ii < 100; ii++)
            {
                await sink.OnEventAsync(
                    CreateEvent(PcapAuditEventKind.FrameCaptured, sessionId, timestamp),
                    CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task OnEventAsyncDoesNotRateLimitOtherKinds()
        {
            var logger = new RecordingLogger<LoggerPcapAuditSink>();
            var sink = new LoggerPcapAuditSink(logger);

            for (int ii = 0; ii < 5; ii++)
            {
                await sink.OnEventAsync(
                    CreateEvent(
                        PcapAuditEventKind.DumpKeys,
                        sessionId: "dump-session" + ii.ToString(CultureInfo.InvariantCulture)),
                    CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(logger.Entries, Has.Count.EqualTo(5));
        }

        [Test]
        public async Task OnEventAsyncSupportsParallelEmission()
        {
            var logger = new RecordingLogger<LoggerPcapAuditSink>();
            var sink = new LoggerPcapAuditSink(logger);

            Task[] tasks = [.. Enumerable.Range(0, 10).Select(ii => Task.Run(async () => await sink.OnEventAsync(
                    CreateEvent(
                        PcapAuditEventKind.DumpKeys,
                        sessionId: "parallel-session-" + ii.ToString(CultureInfo.InvariantCulture)),
                    CancellationToken.None).ConfigureAwait(false)))];

            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(logger.Entries, Has.Count.EqualTo(10));
        }

        [Test]
        public async Task CaptureSessionManagerEmitsStartCaptureAuditEvent()
        {
            var auditSink = new RecordingAuditSink();
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(
                factory,
                TempDirectory,
                loggerFactory: null,
                maxActiveSessions: null,
                auditSink: auditSink);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { Source = CaptureSourceKind.InProcessClient },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(auditSink.Events, Has.Count.EqualTo(1));
            Assert.That(auditSink.Events[0].Kind, Is.EqualTo(PcapAuditEventKind.StartCapture));
            Assert.That(auditSink.Events[0].SessionId, Is.EqualTo(session.Id));
            Assert.That(auditSink.Events[0].ResourcePath, Is.EqualTo(session.SessionFolder));
        }

        private static PcapAuditEvent CreateEvent(
            PcapAuditEventKind kind,
            string? sessionId,
            DateTimeOffset? timestamp = null)
        {
            return new PcapAuditEvent(
                kind,
                timestamp ?? DateTimeOffset.UtcNow,
                sessionId,
                resourcePath: "resource",
                remoteEndpoint: "endpoint",
                properties: null);
        }

        private sealed class RecordingAuditSink : IPcapAuditSink
        {
            public List<PcapAuditEvent> Events { get; } = [];

            public ValueTask OnEventAsync(PcapAuditEvent auditEvent, CancellationToken cancellationToken)
            {
                Events.Add(auditEvent);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class StubSourceFactory : ICaptureSourceFactory
        {
            public ICaptureSource Create(
                CaptureSourceKind kind,
                string sessionFolder,
                ILoggerFactory loggerFactory)
            {
                return new InMemoryCaptureSource();
            }
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            private readonly ConcurrentQueue<LogEntry> m_entries = new();

            public IReadOnlyList<LogEntry> Entries => [.. m_entries];

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
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
                m_entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
            }
        }

        private sealed record LogEntry(LogLevel Level, string Message);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
