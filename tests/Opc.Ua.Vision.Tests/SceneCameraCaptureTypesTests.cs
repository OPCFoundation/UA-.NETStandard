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
