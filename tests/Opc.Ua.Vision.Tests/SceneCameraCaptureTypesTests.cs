/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Locks the shape of the value types the OpenUsd provider hands
    /// callers: a default <see cref="SceneCameraCaptureBackend"/> that
    /// safely signals "no rendering backend" without needing a null
    /// check, and result/request records that default to something the
    /// caller can reason about.
    /// </summary>
    [TestFixture]
    public sealed class SceneCameraCaptureTypesTests
    {
        [Test]
        public void SceneCameraCaptureBackendNoneIsAvailabilityFalseAndCarriesReason()
        {
            SceneCameraCaptureBackend none = SceneCameraCaptureBackend.None;

            Assert.Multiple(() =>
            {
                Assert.That(none.Name, Is.EqualTo("None"));
                Assert.That(none.IsAvailable, Is.False);
                Assert.That(none.IsSoftware, Is.False);
                Assert.That(none.UnavailableReason, Is.Not.Null);
            });
        }

        [Test]
        public void SceneCameraCaptureResultDefaultsBackendToNoneSentinel()
        {
            var result = new SceneCameraCaptureResult();

            Assert.That(result.Backend, Is.SameAs(SceneCameraCaptureBackend.None));
        }

        [Test]
        public void SceneCameraCaptureResultDefaultsImageToNullByteString()
        {
            var result = new SceneCameraCaptureResult();

            Assert.That(result.Image.IsNull, Is.True,
                "A default result must not accidentally look like an empty successful frame.");
        }

        [Test]
        public void SceneCameraCaptureRequestDefaultsToPngFormat()
        {
            var request = new SceneCameraCaptureRequest();

            Assert.That(request.Format, Is.EqualTo(SceneCameraImageFormat.Png));
        }

        [Test]
        public void SceneCameraCaptureRequestDefaultsStageIdentifierToEmptyStringNotNull()
        {
            var request = new SceneCameraCaptureRequest();

            Assert.That(request.StageIdentifier, Is.EqualTo(string.Empty),
                "A null StageIdentifier would defeat the InvalidRequest guard on the provider.");
        }

        [Test]
        public void OpenUsdSceneCaptureOptionsHasSensibleMaximumFrameSize()
        {
            var options = new OpenUsdSceneCaptureOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.MaxFrameWidth, Is.GreaterThan(0));
                Assert.That(options.MaxFrameHeight, Is.GreaterThan(0));
            });
        }
    }
}
