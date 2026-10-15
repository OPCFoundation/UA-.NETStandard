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
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Exercises native camera capture with the bundled resolver and imaging
    /// manifests registered, as they are in an initialized OpenUSD application.
    /// </summary>
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("OpenUsd")]
    [NonParallelizable]
    public sealed class OpenUsdNativeCaptureTests
    {
        /// <summary>
        /// Registers the packaged runtime and creates two distinct owned scenes.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_root = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "TestResults", nameof(OpenUsdNativeCaptureTests),
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(m_root);
            string core = Path.Combine(AppContext.BaseDirectory, "usd", "plugInfo.json");
            string imaging = Path.Combine(AppContext.BaseDirectory, "plugin", "usd", "plugInfo.json");
            Assert.That(File.Exists(core), Is.True, "The bundled core resolver manifest is required.");
            Assert.That(File.Exists(imaging), Is.True, "The bundled imaging manifest is required.");
            _ = UsdPluginRegistry.Register(core);
            _ = UsdPluginRegistry.Register(imaging);
            await WriteStageAsync("red.usda", "(0.9, 0.1, 0.1)").ConfigureAwait(false);
            await WriteStageAsync("blue.usda", "(0.1, 0.1, 0.9)").ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes only the two owned scenes after their native handles are released.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            File.Delete(Path.Combine(m_root, "red.usda"));
            File.Delete(Path.Combine(m_root, "blue.usda"));
            Directory.Delete(m_root, recursive: false);
        }

        /// <summary>
        /// Captures retain the same scene, then replace it rather than returning stale pixels.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureAsyncRetainsRepeatedSceneAndReplacesItWhenStageChanges(bool namedCamera)
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider(
                new OpenUsdSceneCaptureOptions { PreferSoftware = true }, telemetry: null);
            Assert.That(provider.Backend.IsAvailable, Is.True, provider.Backend.UnavailableReason);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            string? camera = namedCamera ? "/World/Camera" : null;
            SceneCameraCaptureRequest firstRequest = CreateRequest("red.usda", camera, timestamp);

            SceneCameraCaptureResult first = await provider.CaptureAsync(firstRequest, deadline.Token)
                .ConfigureAwait(false);
            AssertFrame(first, provider.Backend, timestamp);
            SceneCameraCaptureResult repeated = await provider.CaptureAsync(firstRequest, deadline.Token)
                .ConfigureAwait(false);
            AssertFrame(repeated, provider.Backend, timestamp);
            SceneCameraCaptureResult changed = await provider.CaptureAsync(
                CreateRequest("blue.usda", camera, timestamp.AddSeconds(1)), deadline.Token).ConfigureAwait(false);
            AssertFrame(changed, provider.Backend, timestamp.AddSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(repeated.Image, Is.EqualTo(first.Image),
                    "A retained scene must not become an empty frame on its second capture.");
                Assert.That(changed.Image, Is.Not.EqualTo(first.Image),
                    "Changing the stage must replace the retained scene and its rendered color.");
            });
            provider.Dispose();
            Assert.That(
                async () => await provider.CaptureAsync(firstRequest, deadline.Token).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// A missing camera produces an explicit failure without poisoning the next valid capture.
        /// </summary>
        [Test]
        public async Task CaptureAsyncRecoversAfterAnUnknownCameraIsRejected()
        {
            using var provider = new OpenUsdSceneCameraCaptureProvider(
                new OpenUsdSceneCaptureOptions { PreferSoftware = true }, telemetry: null);
            Assert.That(provider.Backend.IsAvailable, Is.True, provider.Backend.UnavailableReason);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timestamp = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

            SceneCameraCaptureResult rejected = await provider.CaptureAsync(
                CreateRequest("red.usda", "/World/MissingCamera", timestamp), deadline.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(rejected.Status, Is.EqualTo(SceneCameraCaptureStatus.CameraResolveFailed), rejected.Reason);
                Assert.That(rejected.Reason, Does.Contain("/World/MissingCamera"));
                Assert.That(rejected.Image.IsNull, Is.True);
                Assert.That(rejected.TimestampUtc, Is.EqualTo(timestamp));
                Assert.That(rejected.Backend, Is.SameAs(provider.Backend));
            });
            SceneCameraCaptureResult recovered = await provider.CaptureAsync(
                CreateRequest("red.usda", "/World/Camera", timestamp), deadline.Token).ConfigureAwait(false);
            AssertFrame(recovered, provider.Backend, timestamp);
        }

        private static void AssertFrame(
            SceneCameraCaptureResult result, SceneCameraCaptureBackend backend, DateTime timestamp)
        {
            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded), result.Reason);
            Assert.That(result.Image.Span.Length, Is.GreaterThan(33));
            Assert.Multiple(() =>
            {
                Assert.That(result.Reason, Is.Null);
                Assert.That(result.Backend, Is.SameAs(backend));
                Assert.That(result.TimestampUtc, Is.EqualTo(timestamp));
                Assert.That(result.Width, Is.EqualTo(64));
                Assert.That(result.Height, Is.EqualTo(48));
                Assert.That(result.Format, Is.EqualTo(SceneCameraImageFormat.Png));
                Assert.That(result.Image.Span[..8].ToArray(),
                    Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
                Assert.That(BinaryPrimitives.ReadUInt32BigEndian(result.Image.Span.Slice(16, 4)), Is.EqualTo(64u));
                Assert.That(BinaryPrimitives.ReadUInt32BigEndian(result.Image.Span.Slice(20, 4)), Is.EqualTo(48u));
            });
        }

        private SceneCameraCaptureRequest CreateRequest(string name, string? camera, DateTime timestamp)
        {
            return new SceneCameraCaptureRequest
            {
                StageIdentifier = Path.Combine(m_root, name),
                PrimPath = camera,
                Width = 64,
                Height = 48,
                TimestampUtc = timestamp
            };
        }

        private async Task WriteStageAsync(string name, string color)
        {
            string stage = $$"""
                #usda 1.0
                (
                    defaultPrim = "World"
                    upAxis = "Y"
                )
                def Xform "World"
                {
                    def Cube "Block"
                    {
                        double size = 2
                        color3f[] primvars:displayColor = [{{color}}]
                    }
                    def Camera "Camera"
                    {
                        float focalLength = 35
                        double3 xformOp:translate = (0, 0, 8)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(m_root, name), stage).ConfigureAwait(false);
        }

        private string m_root = string.Empty;
    }
}
