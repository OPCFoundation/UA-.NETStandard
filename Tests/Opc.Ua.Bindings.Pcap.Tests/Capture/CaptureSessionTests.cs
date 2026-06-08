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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class CaptureSessionTests
    {
        [Test]
        public void CtorRejectsEmptyId()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest();

            Assert.That(
                () => new CaptureSession(
                    id: string.Empty,
                    sourceKind: CaptureSourceKind.InProcessClient,
                    source: source,
                    sessionFolder: "x",
                    request: request),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("id"));
        }

        [Test]
        public void CtorRejectsNullSource()
        {
            var request = new StartCaptureRequest();

            Assert.That(
                () => new CaptureSession(
                    id: "s1",
                    sourceKind: CaptureSourceKind.InProcessClient,
                    source: null!,
                    sessionFolder: "x",
                    request: request),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("source"));
        }

        [Test]
        public void CtorRejectsWhitespaceSessionFolder()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest();

            Assert.That(
                () => new CaptureSession(
                    id: "s1",
                    sourceKind: CaptureSourceKind.InProcessClient,
                    source: source,
                    sessionFolder: "   ",
                    request: request),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("sessionFolder"));
        }

        [Test]
        public void CtorRejectsNullRequest()
        {
            var source = new InMemoryCaptureSource();

            Assert.That(
                () => new CaptureSession(
                    id: "s1",
                    sourceKind: CaptureSourceKind.InProcessClient,
                    source: source,
                    sessionFolder: "x",
                    request: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public void NewSessionStartsInStartingState()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                request);

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Starting));
            Assert.That(session.StartedAt, Is.Null);
            Assert.That(session.StoppedAt, Is.Null);
            Assert.That(session.Error, Is.Null);
        }

        [Test]
        public async Task StartAsyncTransitionsToRunningAndStampsStartedAt()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                request);

            DateTimeOffset before = DateTimeOffset.UtcNow;
            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            DateTimeOffset after = DateTimeOffset.UtcNow;

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Running));
            Assert.That(source.StartCount, Is.EqualTo(1));
            Assert.That(session.StartedAt, Is.Not.Null);
            Assert.That(session.StartedAt!.Value, Is.GreaterThanOrEqualTo(before));
            Assert.That(session.StartedAt.Value, Is.LessThanOrEqualTo(after));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StartAsyncWhenAlreadyRunningIsIdempotent()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                request);

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);

            // Source must only be started once even though StartAsync was
            // called twice; the second call is a no-op.
            Assert.That(source.StartCount, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Running));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StartAsyncFromCompletedStateThrowsDiagnostics()
        {
            var source = new InMemoryCaptureSource();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Completed));

            Assert.That(
                async () => await session.StartAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("cannot be started from state"));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StopAsyncTransitionsToCompletedAndStampsStoppedAt()
        {
            var source = new InMemoryCaptureSource();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            DateTimeOffset before = DateTimeOffset.UtcNow;
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            DateTimeOffset after = DateTimeOffset.UtcNow;

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Completed));
            Assert.That(source.StopCount, Is.EqualTo(1));
            Assert.That(session.StoppedAt, Is.Not.Null);
            Assert.That(session.StoppedAt!.Value, Is.GreaterThanOrEqualTo(before));
            Assert.That(session.StoppedAt.Value, Is.LessThanOrEqualTo(after));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StopAsyncIsIdempotentAfterCompletion()
        {
            var source = new InMemoryCaptureSource();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);

            // The second StopAsync must short-circuit; the underlying source
            // must NOT see a second Stop call.
            Assert.That(source.StopCount, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Completed));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StartAsyncCapturesSourceFailureIntoFailedState()
        {
            var source = new ThrowingCaptureSource(startMessage: "boom-start");
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            Assert.That(
                async () => await session.StartAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("boom-start"));

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Failed));
            Assert.That(session.Error, Is.EqualTo("boom-start"));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StopAsyncCapturesSourceFailureIntoFailedState()
        {
            var source = new ThrowingCaptureSource(stopMessage: "boom-stop");
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                async () => await session.StopAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("boom-stop"));

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Failed));
            Assert.That(session.Error, Is.EqualTo("boom-stop"));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ToInfoProjectsAllPublicFields()
        {
            var source = new InMemoryCaptureSource(
                pcapFilePath: "/tmp/example.pcap",
                keyLogFilePath: "/tmp/example.uakeys.json");
            var request = new StartCaptureRequest { Source = CaptureSourceKind.Replay };
            var session = new CaptureSession(
                "session-42",
                CaptureSourceKind.Replay,
                source,
                "/tmp/session-42",
                request);

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);

            CaptureSessionInfo info = session.ToInfo();

            Assert.That(info.SessionId, Is.EqualTo("session-42"));
            Assert.That(info.Source, Is.EqualTo(CaptureSourceKind.Replay));
            Assert.That(info.State, Is.EqualTo(CaptureSessionState.Completed));
            Assert.That(info.SessionFolder, Is.EqualTo("/tmp/session-42"));
            Assert.That(info.PcapFilePath, Is.EqualTo("/tmp/example.pcap"));
            Assert.That(info.KeyLogFilePath, Is.EqualTo("/tmp/example.uakeys.json"));
            Assert.That(info.Error, Is.Null);
            Assert.That(info.StartedAt, Is.Not.Null);
            Assert.That(info.StoppedAt, Is.Not.Null);
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAsyncTransitionsToDisposedStateAndStopsRunningSource()
        {
            var source = new InMemoryCaptureSource();
            var session = new CaptureSession(
                "s1",
                CaptureSourceKind.InProcessClient,
                source,
                "x",
                new StartCaptureRequest());

            await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);

            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Disposed));
            Assert.That(source.StopCount, Is.EqualTo(1),
                "DisposeAsync from Running must stop the source.");
            Assert.That(source.DisposeCount, Is.GreaterThanOrEqualTo(1),
                "DisposeAsync must dispose the source.");
        }

        [Test]
        public async Task ConstructorReadOnlyPropertiesAreAvailableImmediately()
        {
            var source = new InMemoryCaptureSource();
            var request = new StartCaptureRequest { Source = CaptureSourceKind.Replay };
            var session = new CaptureSession(
                "abc",
                CaptureSourceKind.InProcessServer,
                source,
                "/folder",
                request);

            Assert.That(session.Id, Is.EqualTo("abc"));
            Assert.That(session.SourceKind, Is.EqualTo(CaptureSourceKind.InProcessServer));
            Assert.That(session.Source, Is.SameAs(source));
            Assert.That(session.SessionFolder, Is.EqualTo("/folder"));
            Assert.That(session.Request, Is.SameAs(request));
            await session.DisposeAsync().ConfigureAwait(false);
        }

        // -------- helpers --------

        private sealed class ThrowingCaptureSource : ICaptureSource
        {
            private readonly string? m_startMessage;
            private readonly string? m_stopMessage;

            public ThrowingCaptureSource(string? startMessage = null, string? stopMessage = null)
            {
                m_startMessage = startMessage;
                m_stopMessage = stopMessage;
            }

            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();
            public long FrameCount => 0;
            public long ByteCount => 0;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                if (m_startMessage is not null)
                {
                    throw new InvalidOperationException(m_startMessage);
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
                if (m_stopMessage is not null)
                {
                    throw new InvalidOperationException(m_stopMessage);
                }
                return ValueTask.CompletedTask;
            }

            public string? GetRawPcapFilePath()
            {
                return null;
            }

            public string? GetKeyLogFilePath()
            {
                return null;
            }

#pragma warning disable CS1998
            public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                yield break;
            }

            public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken ct)
            {
                yield break;
            }
#pragma warning restore CS1998

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
