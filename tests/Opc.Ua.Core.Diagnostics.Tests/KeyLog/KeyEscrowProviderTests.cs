// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Verifies the key escrow provider extension point and default disk
    /// provider behavior.
    /// </summary>
    [TestFixture]
    public sealed class KeyEscrowProviderTests : TempDirectoryFixture
    {
        /// <summary>
        /// Verifies the disk provider creates a non-null per-session
        /// escrow handle.
        /// </summary>
        [Test]
        public async Task DiskKeyEscrowProviderBeginsSessionReturnsNonNullHandle()
        {
            await using var provider = new DiskKeyEscrowProvider();

            IKeyEscrowSession session = await provider.BeginSessionAsync(
                "session-1",
                TempDirectory,
                CancellationToken.None).ConfigureAwait(false);

            await using (session.ConfigureAwait(false))
            {
                Assert.That(session, Is.Not.Null);
            }
        }

        /// <summary>
        /// Verifies key material escrowed by the disk provider is readable
        /// from the JSON key-log artifact.
        /// </summary>
        [Test]
        public async Task DiskKeyEscrowProviderEscrowedKeyMaterialAppearsInKeylog()
        {
            ChannelKeyMaterial expected = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = Path.Combine(TempDirectory, DiskKeyEscrowProvider.KeyLogJsonFileName);
            await using var provider = new DiskKeyEscrowProvider();
            IKeyEscrowSession session = await provider.BeginSessionAsync(
                "session-1",
                TempDirectory,
                CancellationToken.None).ConfigureAwait(false);

            await using (session.ConfigureAwait(false))
            {
                await session.EscrowAsync(expected, CancellationToken.None).ConfigureAwait(false);
                await session.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            byte[] sessionKey = SessionKeyManager.LoadKey(path);
            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader(path, sessionKey).ReadAllAsync(CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            PcapTestHelpers.AssertMaterialEqual(records[0], expected, includeJsonOnlyFields: true);
        }

        /// <summary>
        /// Verifies a consumer-registered escrow provider wins over the
        /// default disk provider registered by AddPcap.
        /// </summary>
        [Test]
        public async Task CustomKeyEscrowProviderReplacesDiskWriter()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            var provider = new CountingKeyEscrowProvider();
            var services = new ServiceCollection();
            services.AddSingleton<IKeyEscrowProvider>(provider);
            services.AddPcap(options => options.BaseFolder = TempDirectory);
            services.AddSingleton<ICaptureSourceFactory>(new StubSourceFactory(material));
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            await using CaptureSessionManager manager = serviceProvider.GetRequiredService<CaptureSessionManager>();

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { Source = CaptureSourceKind.InProcessClient },
                CancellationToken.None).ConfigureAwait(false);

            await manager.StopAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

            Assert.That(provider.BeginSessionCount, Is.EqualTo(1));
            Assert.That(provider.EscrowCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies disposing a disk escrow session flushes pending key
        /// material into the key-log artifact.
        /// </summary>
        [Test]
        public async Task IKeyEscrowSessionDisposeAsyncFlushesWriter()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = Path.Combine(TempDirectory, DiskKeyEscrowProvider.KeyLogJsonFileName);
            await using var provider = new DiskKeyEscrowProvider();
            IKeyEscrowSession session = await provider.BeginSessionAsync(
                "session-1",
                TempDirectory,
                CancellationToken.None).ConfigureAwait(false);

            await session.EscrowAsync(material, CancellationToken.None).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);

            byte[] raw = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);

            Assert.That(raw, Is.Not.Empty);
        }

        private sealed class CountingKeyEscrowProvider : IKeyEscrowProvider
        {
            public int BeginSessionCount => Volatile.Read(ref m_beginSessionCount);

            public int EscrowCount => Volatile.Read(ref m_escrowCount);

            public ValueTask<IKeyEscrowSession> BeginSessionAsync(
                string sessionId,
                string sessionFolder,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_beginSessionCount);
                return new ValueTask<IKeyEscrowSession>(new CountingSession(this));
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }

            private int m_beginSessionCount;
            private int m_escrowCount;

            private sealed class CountingSession : IKeyEscrowSession
            {
                public CountingSession(CountingKeyEscrowProvider provider)
                {
                    m_provider = provider;
                }

                public ValueTask EscrowAsync(ChannelKeyMaterial material, CancellationToken ct)
                {
                    Interlocked.Increment(ref m_provider.m_escrowCount);
                    return ValueTask.CompletedTask;
                }

                public ValueTask FlushAsync(CancellationToken ct)
                {
                    return ValueTask.CompletedTask;
                }

                public ValueTask DisposeAsync()
                {
                    return ValueTask.CompletedTask;
                }

                private readonly CountingKeyEscrowProvider m_provider;
            }
        }

        private sealed class StubSourceFactory : ICaptureSourceFactory
        {
            public StubSourceFactory(ChannelKeyMaterial material)
            {
                m_material = material;
            }

            public ICaptureSource Create(
                CaptureSourceKind kind,
                string sessionFolder,
                ILoggerFactory loggerFactory)
            {
                return new StubSource(m_material);
            }

            private readonly ChannelKeyMaterial m_material;
        }

        private sealed class StubSource : ICaptureSource
        {
            public StubSource(ChannelKeyMaterial material)
            {
                m_material = material;
            }

            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();

            public long FrameCount => 0;

            public long ByteCount => 0;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                return ValueTask.CompletedTask;
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

            public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield return m_material;
            }

            public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }

            private readonly ChannelKeyMaterial m_material;
        }
    }
}
