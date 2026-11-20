/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class DefaultCaptureSourceFactoryTests : TempDirectoryFixture
    {
        [Test]
        public void CtorRejectsNullRegistry()
        {
            Assert.That(
                () => new DefaultCaptureSourceFactory(registry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("registry"));
        }

        [Test]
        public async Task CreateInProcessClientReturnsInProcessClientCaptureSource()
        {
            var registry = new ChannelCaptureRegistry();
            var factory = new DefaultCaptureSourceFactory(registry);

            ICaptureSource source = factory.Create(
                CaptureSourceKind.InProcessClient,
                TempDirectory,
                NullLoggerFactory.Instance);
            try
            {
                Assert.That(source, Is.Not.Null);
                Assert.That(source, Is.TypeOf<InProcessClientCaptureSource>());
            }
            finally
            {
                if (source is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateInProcessServerReturnsInProcessServerCaptureSource()
        {
            var registry = new ChannelCaptureRegistry();
            var factory = new DefaultCaptureSourceFactory(registry);

            ICaptureSource source = factory.Create(
                CaptureSourceKind.InProcessServer,
                TempDirectory,
                NullLoggerFactory.Instance);
            try
            {
                Assert.That(source, Is.Not.Null);
                Assert.That(source, Is.TypeOf<InProcessServerCaptureSource>());
            }
            finally
            {
                if (source is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateReplayReturnsReplayCaptureSource()
        {
            var registry = new ChannelCaptureRegistry();
            var factory = new DefaultCaptureSourceFactory(registry);

            ICaptureSource source = factory.Create(
                CaptureSourceKind.Replay,
                TempDirectory,
                NullLoggerFactory.Instance);
            try
            {
                Assert.That(source, Is.Not.Null);
                Assert.That(source, Is.TypeOf<ReplayCaptureSource>());
            }
            finally
            {
                if (source is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public void CreateUnsupportedKindThrowsDiagnostics()
        {
            var registry = new ChannelCaptureRegistry();
            var factory = new DefaultCaptureSourceFactory(registry);

            // 99 is intentionally not a defined CaptureSourceKind value.
            const CaptureSourceKind invalid = (CaptureSourceKind)99;

            Assert.That(
                () => factory.Create(
                    invalid,
                    TempDirectory,
                    NullLoggerFactory.Instance),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Unsupported capture source kind"));
        }
    }
}
