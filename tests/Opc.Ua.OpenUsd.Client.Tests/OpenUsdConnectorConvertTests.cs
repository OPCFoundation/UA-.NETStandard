/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OpenUsdConnector.Convert"/>: the pure mapping from a
    /// source <see cref="Variant"/> to the USD-side value for each render-target kind.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorConvertTests
    {
        private static OpenUsdConnector.BindingInfo Binding(
            OpenUsdRenderTargetKind kind, double scale = 1.0, double offset = 0.0)
        {
            return new() { Kind = kind, Scale = scale, Offset = offset };
        }

        [TestCase(OpenUsdRenderTargetKind.Translation)]
        [TestCase(OpenUsdRenderTargetKind.Rotation)]
        [TestCase(OpenUsdRenderTargetKind.Scale)]
        [TestCase(OpenUsdRenderTargetKind.Opacity)]
        [TestCase(OpenUsdRenderTargetKind.Custom)]
        public void ScalarKindsApplyScaleAndOffset(OpenUsdRenderTargetKind kind)
        {
            OpenUsdConnector.BindingInfo b = Binding(kind, scale: 2.0, offset: 1.0);

            Variant result = OpenUsdConnector.Convert(b, new Variant(3.0));

            Assert.That(result.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(7.0).Within(1e-9));
        }

        [Test]
        public void IntegerSourceIsCoercedToDouble()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 1.0, offset: 0.0);

            Variant result = OpenUsdConnector.Convert(b, new Variant(5));

            Assert.That(result.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(5.0).Within(1e-9));
        }

        [TestCase(20.0, 0f, 0f, 1f)]
        [TestCase(100.0, 1f, 0f, 0f)]
        [TestCase(-50.0, 0f, 0f, 1f)]
        [TestCase(500.0, 1f, 0f, 0f)]
        public void DisplayColorMapsAndClamps(double raw, float r, float g, float bl)
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.DisplayColor);

            Variant result = OpenUsdConnector.Convert(b, new Variant(raw));

            Assert.That(result.TryGetValue(out ArrayOf<float> colour), Is.True);
            Assert.That(colour.Count, Is.EqualTo(3));
            Assert.That(colour[0], Is.EqualTo(r).Within(1e-4));
            Assert.That(colour[1], Is.EqualTo(g).Within(1e-4));
            Assert.That(colour[2], Is.EqualTo(bl).Within(1e-4));
        }

        [Test]
        public void EmissiveColorMapsPressure()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.EmissiveColor);

            Variant hot = OpenUsdConnector.Convert(b, new Variant(6.0));
            Assert.That(hot.TryGetValue(out ArrayOf<float> hotColour), Is.True);
            Assert.That(hotColour[0], Is.EqualTo(0.1f).Within(1e-4));
            Assert.That(hotColour[1], Is.EqualTo(1f).Within(1e-4));
            Assert.That(hotColour[2], Is.EqualTo(0.2f).Within(1e-4));

            Variant cold = OpenUsdConnector.Convert(b, new Variant(0.0));
            Assert.That(cold.TryGetValue(out ArrayOf<float> coldColour), Is.True);
            Assert.That(coldColour[0], Is.EqualTo(0f).Within(1e-4));
            Assert.That(coldColour[1], Is.EqualTo(0f).Within(1e-4));
            Assert.That(coldColour[2], Is.EqualTo(0f).Within(1e-4));
        }

        [TestCase(1.0, "inherited")]
        [TestCase(0.0, "invisible")]
        public void VisibilityMapsToToken(double raw, string expected)
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Visibility);

            Variant result = OpenUsdConnector.Convert(b, new Variant(raw));

            Assert.That(result.TryGetValue(out string token), Is.True);
            Assert.That(token, Is.EqualTo(expected));
        }

        [Test]
        public void NullSourceReturnsNullVariant()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);

            Variant result = OpenUsdConnector.Convert(b, default);

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ThreeDCartesianCoordinatesTranslationReturnsDoubleArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);
            var coordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(coordinates)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector.Count, Is.EqualTo(3));
            Assert.That(vector[0], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(2.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void ThreeDCartesianCoordinatesTranslationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 2.0, offset: 1.0);
            var coordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(coordinates)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(3.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(7.0).Within(1e-9));
        }

        [Test]
        public void ThreeDOrientationRotationReturnsDoubleArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            var orientation = new ThreeDOrientation { A = 0.1, B = 0.2, C = 0.3 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(orientation)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector.Count, Is.EqualTo(3));
            Assert.That(vector[0], Is.EqualTo(0.1).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(0.2).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(0.3).Within(1e-9));
        }

        [Test]
        public void ThreeDOrientationRotationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation, scale: 10.0, offset: 1.0);
            var orientation = new ThreeDOrientation { A = 1.0, B = 2.0, C = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(orientation)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(11.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(21.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(31.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameTranslationReturnsCartesianCoordinatesArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 4.0, Y = 5.0, Z = 6.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(4.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(6.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameRotationReturnsOrientationArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 0.0, Y = 0.0, Z = 0.0 },
                Orientation = new ThreeDOrientation { A = 7.0, B = 8.0, C = 9.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(7.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(8.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(9.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameTranslationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 0.5, offset: -1.0);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 2.0, Y = 4.0, Z = 6.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.Zero);
            Assert.That(vector[1], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(2.0).Within(1e-9));
        }
    }
}
