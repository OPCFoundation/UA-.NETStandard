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
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Exercises <see cref="OpenUsdSceneCameraCaptureProvider"/> along the
    /// paths that must hold on any host: argument validation, request
    /// validation, no-graphics-device fallback, and disposal semantics.
    /// The provider probes a graphics device during construction and CI
    /// typically has none — the tests treat both outcomes (device found,
    /// no device found) as legal and only assert on the invariants that
    /// hold regardless (never throw on the no-device path, populate
    /// <see cref="SceneCameraCaptureResult.Backend"/>, refuse malformed
    /// requests deterministically, refuse to serve any frame once
    /// disposed).
    /// </summary>
    [TestFixture]
    public sealed class SceneCameraCaptureProviderTests
    {
        [Test]
        public void ConstructorThrowsArgumentNullExceptionForNullOptions()
        {
            Assert.That(
                () => new OpenUsdSceneCameraCaptureProvider(null!, telemetry: null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorSucceedsWithDefaultOptionsAndProducesBackendDescriptor()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.Backend, Is.Not.Null);
                Assert.That(provider.Backend.Name, Is.Not.Null);
                Assert.That(provider.Backend.Name, Is.Not.Empty);
            });
        }

        [Test]
        public async Task CaptureAsyncRejectsRequestWithEmptyStageIdentifier()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = string.Empty,
                Width = 320,
                Height = 240,
                Format = SceneCameraImageFormat.Png
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
                Assert.That(result.Image.IsNull, Is.True);
                Assert.That(result.Reason, Is.Not.Null);
            });
        }

        [Test]
        public async Task CaptureAsyncRejectsRequestWithZeroWidthOrHeight()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();

            SceneCameraCaptureResult zeroWidth = await provider.CaptureAsync(
                new SceneCameraCaptureRequest
                {
                    StageIdentifier = "urn:test:stage",
                    Width = 0,
                    Height = 240,
                    Format = SceneCameraImageFormat.Png
                }, CancellationToken.None).ConfigureAwait(false);
            SceneCameraCaptureResult zeroHeight = await provider.CaptureAsync(
                new SceneCameraCaptureRequest
                {
                    StageIdentifier = "urn:test:stage",
                    Width = 320,
                    Height = 0,
                    Format = SceneCameraImageFormat.Png
                }, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(zeroWidth.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
                Assert.That(zeroHeight.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
            });
        }

        [Test]
        public async Task CaptureAsyncRejectsFrameExceedingConfiguredMaximum()
        {
            var options = new OpenUsdSceneCaptureOptions { MaxFrameWidth = 64, MaxFrameHeight = 64 };
            using var provider = new OpenUsdSceneCameraCaptureProvider(options, telemetry: null);
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 128,
                Height = 128,
                Format = SceneCameraImageFormat.Png
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
        }

        [Test]
        public void CaptureAsyncThrowsArgumentNullExceptionForNullRequest()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();

            Assert.That(
                async () => await provider.CaptureAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CaptureAsyncPropagatesCancellationBeforeAnyRenderingWork()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 32,
                Height = 32
            };

            Assert.That(
                async () => await provider.CaptureAsync(request, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task CaptureAsyncReportsNoRenderingBackendWhenDeviceProbeFailedAndNeverReturnsSuccessWithEmptyImage()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();

            if (provider.Backend.IsAvailable)
            {
                Assert.Ignore(
                    "A rendering backend is available on this host; the NoRenderingBackend "
                    + "path only reproduces on hosts without a graphics device (typical CI). "
                    + "This test asserts the CI-side invariants only.");
                return;
            }
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:no-backend",
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.NoRenderingBackend),
                    "With no graphics device the provider must never touch native rendering code — the NoRenderingBackend path is the entire contract.");
                Assert.That(result.Image.IsNull, Is.True,
                    "NoRenderingBackend must never return an image, even an empty one, because a caller could misread that as 'no draws' rather than 'no backend'.");
                Assert.That(result.Reason, Is.Not.Null);
                Assert.That(result.Backend, Is.Not.Null);
            });
        }

        [Test]
        public void DisposedProviderRejectsFurtherCaptureRequests()
        {
            var provider = new OpenUsdSceneCameraCaptureProvider();
            provider.Dispose();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png
            };

            Assert.That(
                async () => await provider.CaptureAsync(request, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var provider = new OpenUsdSceneCameraCaptureProvider();

            Assert.DoesNotThrow(() =>
            {
                provider.Dispose();
                provider.Dispose();
            });
        }
    }
}
