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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class CaptureSessionManagerTests : TempDirectoryFixture
    {
        [Test]
        public void CtorRejectsNullSourceFactory()
        {
            Assert.That(
                () => new CaptureSessionManager(sourceFactory: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("sourceFactory"));
        }

        [Test]
        public void CtorWithExplicitBaseFolderRejectsWhitespacePath()
        {
            var factory = new StubSourceFactory();

            Assert.That(
                () => new CaptureSessionManager(factory, baseFolder: "   "),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("baseFolder"));
        }

        [Test]
        public async Task StartAsyncCreatesAndStartsSession()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { Source = CaptureSourceKind.InProcessClient },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session, Is.Not.Null);
            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Running));
            Assert.That(session.SourceKind, Is.EqualTo(CaptureSourceKind.InProcessClient));
            Assert.That(factory.Created, Has.Count.EqualTo(1));
            Assert.That(factory.Created[0].StartCount, Is.EqualTo(1));
        }

        [Test]
        public void StartAsyncRejectsNullRequest()
        {
            var factory = new StubSourceFactory();
            using var managerScope = new ManagerScope(factory, TempDirectory);

            Assert.That(
                async () => await managerScope.Manager.StartAsync(
                    request: null!,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public async Task StartAsyncDisposesSourceWhenStartThrows()
        {
            var factory = new StubSourceFactory(throwOnStart: true);
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            Assert.That(
                async () => await manager.StartAsync(
                    new StartCaptureRequest(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(factory.ThrowingCreated, Has.Count.EqualTo(1));
            Assert.That(factory.ThrowingCreated[0].DisposeCount, Is.GreaterThanOrEqualTo(1),
                "A failed StartAsync must dispose the source.");
            Assert.That(manager.List(), Is.Empty,
                "A failed Start must not leave a session in the registry.");
        }

        [Test]
        public async Task GetByIdReturnsStartedSession()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);
            CaptureSession created = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            CaptureSession resolved = manager.Get(created.Id);

            Assert.That(resolved, Is.SameAs(created));
        }

        [Test]
        public async Task GetUnknownIdThrowsDiagnosticsException()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            Assert.That(
                () => manager.Get("does-not-exist"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("does-not-exist"));
        }

        [Test]
        public void GetRejectsWhitespaceId()
        {
            var factory = new StubSourceFactory();
            using var scope = new ManagerScope(factory, TempDirectory);

            Assert.That(
                () => scope.Manager.Get(string.Empty),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("sessionId"));
        }

        [Test]
        public async Task ListReflectsActiveSessions()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);
            CaptureSession first = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);
            CaptureSession second = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            IReadOnlyList<CaptureSession> all = manager.List();

            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all, Contains.Item(first));
            Assert.That(all, Contains.Item(second));
        }

        [Test]
        public async Task StopAsyncTransitionsSessionToCompleted()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);
            CaptureSession started = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            CaptureSession stopped = await manager.StopAsync(started.Id, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(stopped, Is.SameAs(started));
            Assert.That(stopped.State, Is.EqualTo(CaptureSessionState.Completed));
            // Session must still be retained in the registry after stop.
            Assert.That(manager.List(), Contains.Item(stopped));
        }

        [Test]
        public async Task RemoveAsyncStopsAndRemovesSession()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);
            CaptureSession started = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            await manager.RemoveAsync(started.Id, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(manager.List(), Does.Not.Contain(started));
            Assert.That(
                () => manager.Get(started.Id),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        [Test]
        public async Task RemoveAsyncUnknownIdIsNoOp()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            Assert.DoesNotThrowAsync(async () =>
                await manager.RemoveAsync("missing", CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task StartAsyncEnforcesMaxActiveSessionsCap()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);
            var sessions = new List<CaptureSession>();
            for (int i = 0; i < manager.MaxActiveSessions; i++)
            {
                sessions.Add(await manager.StartAsync(
                    new StartCaptureRequest(),
                    CancellationToken.None).ConfigureAwait(false));
            }

            Assert.That(
                async () => await manager.StartAsync(
                    new StartCaptureRequest(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("max " + manager.MaxActiveSessions));

            // Cleanup
            foreach (CaptureSession session in sessions)
            {
                await manager.RemoveAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncDisposesEveryTrackedSession()
        {
            var factory = new StubSourceFactory();
            var manager = new CaptureSessionManager(factory, TempDirectory);
            CaptureSession a = await manager.StartAsync(new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);
            CaptureSession b = await manager.StartAsync(new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(a.State, Is.EqualTo(CaptureSessionState.Disposed));
            Assert.That(b.State, Is.EqualTo(CaptureSessionState.Disposed));
        }

        [Test]
        public async Task StartAsyncAfterDisposeThrowsObjectDisposed()
        {
            var factory = new StubSourceFactory();
            var manager = new CaptureSessionManager(factory, TempDirectory);
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await manager.StartAsync(
                    new StartCaptureRequest(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task SessionFolderFromRequestOverridesBaseFolder()
        {
            var factory = new StubSourceFactory();
            string customFolder = Path.Combine(TempDirectory, "custom-session");
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { SessionFolder = customFolder },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.SessionFolder, Is.EqualTo(customFolder));
            Assert.That(Directory.Exists(customFolder), Is.True,
                "Manager must create the requested folder.");
        }

        [Test]
        public async Task SessionFolderDefaultsToSubfolderOfBaseWhenRequestIsEmpty()
        {
            var factory = new StubSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.SessionFolder, Does.StartWith(TempDirectory));
            Assert.That(session.SessionFolder, Does.EndWith(session.Id));
        }

        // ----- helpers -----

        // Wraps a manager so it can be disposed synchronously inside a sync test
        // body that uses async lambdas (a tiny convenience for null-argument tests).
        private sealed class ManagerScope : IDisposable
        {
            public CaptureSessionManager Manager { get; }
            public ManagerScope(ICaptureSourceFactory factory, string baseFolder)
            {
                Manager = new CaptureSessionManager(factory, baseFolder);
            }
            public void Dispose()
            {
                Manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private sealed class StubSourceFactory : ICaptureSourceFactory
        {
            private readonly bool m_throwOnStart;

            public StubSourceFactory(bool throwOnStart = false)
            {
                m_throwOnStart = throwOnStart;
            }

            public List<InMemoryCaptureSource> Created { get; } = new();

            public List<ThrowingStartSource> ThrowingCreated { get; } = new();

            public ICaptureSource Create(
                CaptureSourceKind kind,
                string sessionFolder,
                ILoggerFactory loggerFactory)
            {
                if (m_throwOnStart)
                {
                    var thrower = new ThrowingStartSource();
                    ThrowingCreated.Add(thrower);
                    return thrower;
                }
                var concrete = new InMemoryCaptureSource();
                Created.Add(concrete);
                return concrete;
            }
        }

        private sealed class ThrowingStartSource : ICaptureSource
        {
            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();
            public long FrameCount => 0;
            public long ByteCount => 0;
            public int DisposeCount { get; private set; }

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                throw new InvalidOperationException("start-failed");
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
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
                DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
