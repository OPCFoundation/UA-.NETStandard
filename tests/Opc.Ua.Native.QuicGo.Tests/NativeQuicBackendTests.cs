// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Tests the production managed API against the independently built native image.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class NativeQuicBackendTests
    {
        /// <summary>
        /// Selects only the explicit test artifact; missing native prerequisites fail, not skip.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [OneTimeSetUp]
        public async Task InitializeAsync()
        {
            string path = Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_PATH") ??
                throw new InvalidOperationException(
                    "Set OPCUA_QUIC_NATIVE_PATH to the reviewed production native image.");
            using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
            m_library = new NativeQuicLibrary(path, new ByteString(hash));
        }

        /// <summary>
        /// Refuses a wrong image digest before loading any Go runtime.
        /// </summary>
        [Test]
        [Order(0)]
        public void WrongAssetHashRefusedBeforeLoading()
        {
            var backend = new NativeQuicBackend(new NativeQuicLibrary(
                m_library.AbsolutePath, new ByteString(new byte[32])));
            Assert.ThrowsAsync<InvalidDataException>(async () => await backend.GetCountsAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Runs the same real-socket TAP scenario as NativeAOT.
        /// </summary>
        [TestCase(false, false, 91UL)]
        [TestCase(true, false, 91UL)]
        [TestCase(false, true, 91UL)]
        [TestCase(false, false, 0x52e4a40fa8dbUL)]
        [Order(1)]
        public async Task GenericNativeScenarioUsesRealSocketsAsync(
            bool passControlToken, bool cancelLifetimeFirst, ulong lateErrorCode)
        {
            var backend = new NativeQuicBackend(m_library);
            int checks = await NativeQuicLiveScenario.RunAsync(backend, passControlToken: passControlToken,
                cancelLifetimeFirst: cancelLifetimeFirst, lateErrorCode: lateErrorCode)
                .ConfigureAwait(false);
            Assert.That(checks, Is.EqualTo(22));
            Assert.That(await backend.GetCountsAsync().ConfigureAwait(false), Is.Default);
        }

        /// <summary>
        /// DI resolves the same injectable singleton and never changes the selected native image.
        /// </summary>
        [Test]
        [Order(2)]
        public async Task OptionalDependencyInjectionAndSingleImageLifetimeAsync()
        {
            var services = new ServiceCollection();
            services.AddNativeQuicGo(m_library);
            services.AddNativeQuicGo(m_library);
            await using ServiceProvider provider = services.BuildServiceProvider();
            INativeQuicBackend first = provider.GetRequiredService<INativeQuicBackend>();
            INativeQuicBackend second = provider.GetRequiredService<INativeQuicBackend>();
            Assert.That(first, Is.SameAs(second));
            Assert.That(await first.GetCountsAsync().ConfigureAwait(false), Is.Default);
            var other = new NativeQuicBackend(new NativeQuicLibrary(
                m_library.AbsolutePath + ".another", m_library.Sha256));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await other.GetCountsAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Resolves the packaged RID filename and verifies the emitted hash sidecar without PATH probing.
        /// </summary>
        [Test]
        [Order(3)]
        public async Task PackagedAssetSidecarSelectsTheReviewedImageAsync()
        {
            NativeQuicLibrary selected = await NativeQuicLibrary.FromDirectoryAsync(
                Path.GetDirectoryName(m_library.AbsolutePath)!).ConfigureAwait(false);
            Assert.That(selected.AbsolutePath, Is.EqualTo(m_library.AbsolutePath));
            Assert.That(selected.Sha256, Is.EqualTo(m_library.Sha256));
            var backend = new NativeQuicBackend(selected);
            Assert.That(await backend.GetCountsAsync().ConfigureAwait(false), Is.Default);
        }

        /// <summary>
        /// Already-cancelled managed calls never allocate a native job.
        /// </summary>
        [Test]
        public async Task PrecancelledOperationDoesNotAllocateAsync()
        {
            var backend = new NativeQuicBackend(m_library);
            using var source = new CancellationTokenSource();
            await source.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await backend.GetCountsAsync(source.Token).ConfigureAwait(false));
            Assert.That(await backend.GetCountsAsync().ConfigureAwait(false), Is.Default);
        }

        /// <summary>
        /// A requested or disposed dynamic control cannot leave an unobserved native operation.
        /// </summary>
        [Test]
        public async Task PrecancelledOrDisposedDynamicControlDoesNotAllocateAsync()
        {
            var backend = new NativeQuicBackend(m_library);
            using var control = new NativeQuicCancellation();
            var request = new NativeQuicRequest(TimeSpan.FromSeconds(1), capacity: 1)
            {
                Cancellation = control
            };
            await control.CancelAsync(51).ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await backend.StreamAsync(default, NativeQuicStreamOperation.Read, request).ConfigureAwait(false));
            control.Dispose();
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await backend.StreamAsync(default, NativeQuicStreamOperation.Read, request).ConfigureAwait(false));
            Assert.That(await backend.GetCountsAsync().ConfigureAwait(false), Is.Default);
        }

        /// <summary>
        /// Arbitrary handle values cannot cross the native type/ownership registry.
        /// </summary>
        [Test]
        public async Task InvalidHandleProducesExplicitAdmissionFailureAsync()
        {
            var backend = new NativeQuicBackend(m_library);
            NativeQuicCompletion result = await backend.StreamAsync(new NativeQuicStreamHandle(ulong.MaxValue),
                NativeQuicStreamOperation.Read, new NativeQuicRequest(TimeSpan.FromSeconds(1), capacity: 1))
                .ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(NativeQuicStatus.Invalid));
            Assert.That(result.BytesTransferred, Is.Zero);
            Assert.That(result.Stream.Value, Is.Zero);
            Assert.That(await backend.GetCountsAsync().ConfigureAwait(false), Is.Default);
        }

        private NativeQuicLibrary m_library = null!;
    }

    /// <summary>
    /// Structural policy/material boundary tests, without generating production credentials.
    /// </summary>
    [TestFixture]
    public sealed class NativeQuicOptionsTests
    {
        /// <summary>
        /// A native library cannot be selected using ambient PATH/current-directory resolution.
        /// </summary>
        [Test]
        public void RelativeLibraryPathIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new NativeQuicLibrary("opcua_quic_go.dll", default));
        }

        /// <summary>
        /// An independently verified SHA256 digest is required.
        /// </summary>
        [TestCase(0)]
        [TestCase(31)]
        [TestCase(33)]
        public void InvalidLibraryDigestLengthIsRejected(int length)
        {
            Assert.Throws<ArgumentException>(() =>
                new NativeQuicLibrary(Path.GetFullPath("native.dll"), new ByteString(new byte[length])));
        }

        /// <summary>
        /// Private material has finite bounds and disposal prevents subsequent export to native code.
        /// </summary>
        [Test]
        public void PrivateKeyIsCopiedAndClearedOnDisposal()
        {
            byte[] input = [1, 2, 3, 4];
            var key = new NativeQuicPrivateKey(input);
            input.AsSpan().Clear();
            byte[] copy = key.Copy();
            Assert.That(copy, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            CryptographicOperations.ZeroMemory(copy);
            key.Dispose();
            key.Dispose();
            Assert.Throws<ObjectDisposedException>(() => key.Copy());
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeQuicPrivateKey([]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeQuicPrivateKey(new byte[16385]));
        }

        /// <summary>
        /// URI-only or key-only policy is never accepted.
        /// </summary>
        [Test]
        public void PeerPolicyRequiresAbsoluteUriAndNonzeroSpki()
        {
            Assert.Throws<ArgumentException>(() => new NativeQuicPeer("urn:test", default));
            Assert.Throws<ArgumentException>(() => new NativeQuicPeer("urn:test", new ByteString(new byte[32])));
            byte[] pin = new byte[32];
            pin[0] = 1;
            Assert.Throws<ArgumentException>(() => new NativeQuicPeer("relative", new ByteString(pin)));
            var peer = new NativeQuicPeer("urn:test", new ByteString(pin));
            pin[0] = 2;
            Assert.That(peer.SpkiSha256[0], Is.EqualTo(1));
        }

        /// <summary>
        /// Request bounds include timeout, payload, read capacity and application codes.
        /// </summary>
        [Test]
        public void RequestBoundsAndAdjacentValidLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeQuicRequest(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeQuicRequest(TimeSpan.FromMilliseconds(60001)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicRequest(TimeSpan.FromSeconds(1), new ByteString(new byte[65537])));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicRequest(TimeSpan.FromSeconds(1), capacity: 65537));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicRequest(TimeSpan.FromSeconds(1), errorCode: 1UL << 62));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicRequest(TimeSpan.FromSeconds(1), cancellationErrorCode: 1UL << 62));
            var smallest = new NativeQuicRequest(TimeSpan.FromMilliseconds(1));
            var largest = new NativeQuicRequest(TimeSpan.FromMilliseconds(60000),
                new ByteString(new byte[65536]), capacity: 65536, errorCode: (1UL << 62) - 1);
            Assert.That(smallest.Timeout.TotalMilliseconds, Is.EqualTo(1));
            Assert.That(largest.Capacity, Is.EqualTo(65536));
            Assert.That(largest.Payload.Length, Is.EqualTo(65536));
            Assert.That(largest.ErrorCode, Is.EqualTo((1UL << 62) - 1));
        }

        /// <summary>
        /// Empty/oversized trust and empty/oversized listener policies fail before native admission.
        /// </summary>
        [Test]
        public void CredentialsAndListenerPolicyAreMandatoryAndBounded()
        {
            using var pki = new TestCredentials();
            using var key = new NativeQuicPrivateKey([1]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicCredentials(default, default, key, default, "urn:test"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicCredentials(pki.Client.CertificateDer, default, key, default, "urn:test"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicOptions("127.0.0.1:0", "h3", pki.Server, default));
            var peers = new NativeQuicPeer[33];
            Array.Fill(peers, pki.ClientPeer);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicOptions("127.0.0.1:0", "h3", pki.Server, new ArrayOf<NativeQuicPeer>(peers.AsMemory())));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NativeQuicOptions(new string('a', 513), "h3", pki.Server, [pki.ClientPeer]));
        }

        /// <summary>
        /// Malformed evidence cannot be presented as authenticated transport facts.
        /// </summary>
        [TestCase(0)]
        [TestCase(904)]
        [TestCase(919)]
        [TestCase(921)]
        public void MalformedPeerEvidenceLayoutIsRejected(int size)
        {
            Assert.Throws<InvalidDataException>(() => new NativeQuicPeerEvidence(new ByteString(new byte[size])));
        }
    }
}
