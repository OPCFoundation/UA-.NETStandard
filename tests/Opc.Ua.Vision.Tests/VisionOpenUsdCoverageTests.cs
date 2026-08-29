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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;
using Opc.Ua.Vision.OpenUsd.Rendering;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Coverage tests for <see cref="OpenUsdSceneCameraCaptureProvider"/>
    /// paths that are not exercised by <see cref="SceneCameraCaptureProviderTests"/>
    /// and for the internal <see cref="DeviceSelector"/> guard clauses.
    /// </summary>
    /// <remarks>
    /// Three families of paths in this provider are host-topology-dependent
    /// and therefore cannot be honestly covered by a unit test on this host:
    /// <list type="bullet">
    /// <item><description>The <c>else</c> branch of the constructor at
    /// <c>OpenUsdSceneCameraCaptureProvider.cs</c> lines 96-106 (device
    /// probe failed → <c>m_device = null</c>) is only reachable on hosts
    /// where every backend probe fails; on this Windows host D3D12 always
    /// succeeds. The complementary <see cref="OpenUsdSceneCameraCaptureProvider"/>
    /// method <c>NoBackend</c> at lines 366-378 is only reached from that
    /// branch, so it inherits the same restriction. See
    /// <see cref="SceneCameraCaptureProviderTests.CaptureAsyncReportsNoRenderingBackendWhenDeviceProbeFailedAndNeverReturnsSuccessWithEmptyImage"/>
    /// for the CI-side invariants.</description></item>
    /// <item><description>The full success path of <c>CaptureCore</c>
    /// (rendering RGBA, PNG-encoding, returning <c>Succeeded</c>) requires
    /// a well-formed USD stage and OpenUSD plugin tree, and belongs in
    /// integration tests.</description></item>
    /// <item><description>Every reachable path inside <c>CaptureCore</c>
    /// (<c>StageOpenFailed</c>, <c>CameraResolveFailed</c>, <c>RenderFailed</c>,
    /// <c>BlankFrame</c>, <c>EncodingFailed</c>) is unreachable from managed
    /// test code today: <c>UsdStage.Open</c> tears the test host down inside
    /// native code for both an existing malformed <c>.usda</c> file and a
    /// syntactically valid minimal <c>.usda</c> stage. This is a second
    /// instance of the same "answers instead of refusing" pattern the
    /// <c>ValidateRequest</c> comment already calls out for missing files —
    /// filed as a defect below rather than covered here.</description></item>
    /// <item><description>Similarly, <c>DeviceSelector.FormatException</c>
    /// is only invoked when a backend probe throws; on this host every probe
    /// succeeds, so the helper is unreachable from a unit test.</description></item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class VisionOpenUsdCoverageTests
    {
        [Test]
        public void ConstructorReturnsNullPluginPathWhenConfiguredPluginPathDoesNotExist()
        {
            string nonExistent = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "not-a-real-plugin-dir-" + Guid.NewGuid().ToString("N"));
            var options = new OpenUsdSceneCaptureOptions { PluginPath = nonExistent };

            using var provider = new OpenUsdSceneCameraCaptureProvider(options, telemetry: null);

            Assert.That(provider.PluginPath, Is.Null,
                "A configured PluginPath that does not exist must resolve to null so " +
                "the provider falls back to the auto-discovery path.");
        }

        [Test]
        public void ConstructorResolvesConfiguredPluginPathWhenDirectoryExists()
        {
            string tempPluginDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "temp-plugin-dir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPluginDir);
            try
            {
                var options = new OpenUsdSceneCaptureOptions { PluginPath = tempPluginDir };

                using var provider = new OpenUsdSceneCameraCaptureProvider(options, telemetry: null);

                Assert.That(provider.PluginPath, Is.EqualTo(tempPluginDir),
                    "A configured PluginPath that exists on disk must be returned verbatim.");
            }
            finally
            {
                try
                {
                    Directory.Delete(tempPluginDir, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [Test]
        public async Task CaptureAsyncRefusesLocalStagePathThatNamesNoReadableFile()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            string missingPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "does-not-exist-" + Guid.NewGuid().ToString("N") + ".usda");
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = missingPath,
                Width = 64,
                Height = 64,
                Format = SceneCameraImageFormat.Png
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest),
                    "A missing local file must be refused in managed code so the native " +
                    "resolver never sees it; otherwise UsdStage.Open can tear the process down.");
                Assert.That(result.Reason, Is.Not.Null);
                Assert.That(result.Reason, Does.Contain("does not name a readable file"));
                Assert.That(result.Image.IsNull, Is.True);
            });
        }

        [Test]
        public async Task CaptureAsyncRefusesUnknownImageFormat()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 64,
                Height = 64,
                Format = (SceneCameraImageFormat)999
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
                Assert.That(result.Reason, Is.Not.Null);
                Assert.That(result.Reason, Does.Contain("not supported"));
                Assert.That(result.Image.IsNull, Is.True);
            });
        }

        [Test]
        public async Task CaptureResultEchoesRequestSuppliedTimestampOnFailurePaths()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            var ts = new DateTime(2024, 6, 15, 12, 34, 56, DateTimeKind.Utc);
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = string.Empty,
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png,
                TimestampUtc = ts
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.TimestampUtc, Is.EqualTo(ts),
                "A caller-supplied TimestampUtc must be echoed back on failure results " +
                "so the caller can correlate the failure with the request it made.");
        }

        [Test]
        public async Task CaptureResultCarriesBackendDescriptorOnEveryOutcome()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = string.Empty,
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Backend, Is.Not.Null);
                Assert.That(result.Backend, Is.SameAs(provider.Backend),
                    "Every result must carry the provider's Backend descriptor unchanged so " +
                    "the caller can correlate a failure with the graphics backend it used.");
            });
        }

        [Test]
        public void SceneCameraCaptureRequestExposesEveryInitOnlyPropertyForCallers()
        {
            var ts = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:cam",
                PrimPath = "/World/Camera",
                Width = 640,
                Height = 480,
                TimeCode = 42.5,
                Format = SceneCameraImageFormat.Png,
                TimestampUtc = ts
            };

            Assert.Multiple(() =>
            {
                Assert.That(request.StageIdentifier, Is.EqualTo("urn:test:cam"));
                Assert.That(request.PrimPath, Is.EqualTo("/World/Camera"));
                Assert.That(request.Width, Is.EqualTo(640));
                Assert.That(request.Height, Is.EqualTo(480));
                Assert.That(request.TimeCode, Is.EqualTo(42.5));
                Assert.That(request.Format, Is.EqualTo(SceneCameraImageFormat.Png));
                Assert.That(request.TimestampUtc, Is.EqualTo(ts));
            });
        }

        [Test]
        public void ConstructorSucceedsWithPreferSoftwareOptionAndProducesBackendDescriptor()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider(
                new OpenUsdSceneCaptureOptions { PreferSoftware = true },
                telemetry: null);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Backend, Is.Not.Null);
                Assert.That(provider.Backend.Name, Is.Not.Empty,
                    "PreferSoftware=true changes the D3D12 probe order on Windows but must " +
                    "still resolve to a named backend descriptor.");
            });
        }

        [Test]
        public void ConstructorSucceedsWithAllowSoftwareFallbackFalseAndProducesBackendDescriptor()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider(
                new OpenUsdSceneCaptureOptions { AllowSoftwareFallback = false },
                telemetry: null);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Backend, Is.Not.Null);
                Assert.That(provider.Backend.Name, Is.Not.Empty,
                    "AllowSoftwareFallback=false drops the WARP probe on Windows but must " +
                    "still resolve to a named backend descriptor when hardware D3D12 is available.");
            });
        }

        [Test]
        public void DeviceSelectorTrySelectDeviceThrowsArgumentNullExceptionForNullOptions()
        {
            Assert.That(() =>
                    DeviceSelector.TrySelectDevice(
                        null!,
                        NullLogger.Instance,
                        out _,
                        out _),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DeviceSelectorTrySelectDeviceThrowsArgumentNullExceptionForNullLogger()
        {
            Assert.That(() =>
                    DeviceSelector.TrySelectDevice(
                        new OpenUsdSceneCaptureOptions(),
                        null!,
                        out _,
                        out _),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DeviceSelectorTrySelectDevicePopulatesBackendDescriptorWhenAnyProbeSucceeds()
        {
            bool selected = DeviceSelector.TrySelectDevice(
                new OpenUsdSceneCaptureOptions(),
                NullLogger.Instance,
                out SelectedSilkDevice device,
                out string reason);

            if (!selected)
            {
                Assert.That(reason, Is.Not.Empty,
                    "TrySelectDevice must fill the aggregate reason when every probe fails, " +
                    "so the caller can surface it in a diagnostic.");
                return;
            }
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(device.Device, Is.Not.Null);
                    Assert.That(device.Backend, Is.Not.Null);
                    Assert.That(device.Backend.IsAvailable, Is.True);
                    Assert.That(device.Backend.Name, Is.Not.Empty);
                    Assert.That(reason, Is.Empty,
                        "aggregateReason must be empty on the success path so the caller " +
                        "does not log a stale unavailable message.");
                });
            }
            finally
            {
                device.Device.Dispose();
            }
        }

        [Test]
        public async Task CaptureAsyncPropagatesCancellationRequestedBeforeCall()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png
            };

            OperationCanceledException? thrown = null;
            try
            {
                await provider.CaptureAsync(request, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                thrown = ex;
            }
            Assert.That(thrown, Is.Not.Null,
                "A pre-cancelled token must cancel the capture before touching the render pipeline; " +
                "the check runs before ValidateRequest so the stage identifier value is irrelevant.");
        }

        [Test]
        public async Task DisposedProviderDisposeIsIdempotentAndFurtherCapturesRejected()
        {
            var provider = new OpenUsdSceneCameraCaptureProvider();
            provider.Dispose();
            Assert.DoesNotThrow(provider.Dispose,
                "Dispose must be idempotent - a defensive host may call it twice.");

            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = "urn:test:stage",
                Width = 32,
                Height = 32,
                Format = SceneCameraImageFormat.Png
            };

            ObjectDisposedException? thrown = null;
            try
            {
                await provider.CaptureAsync(request, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                thrown = ex;
            }
            Assert.That(thrown, Is.Not.Null,
                "A capture request after Dispose must be refused with ObjectDisposedException, " +
                "not swallowed and answered as a failure.");
        }
    }
}
