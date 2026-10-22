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
 * WHETHER IN CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
 * IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Avalonia.Media;
using NUnit.Framework;

namespace UaLens.Tests.Themes
{
    [TestFixture]
    public sealed class PaletteEvaluationTests
    {
        [TestCase("Normal", "#FF0000")]
        [TestCase("Protanopia", "#6D5F00")]
        [TestCase("Deuteranopia", "#A39000")]
        [TestCase("Tritanopia", "#FF000F")]
        public void SeverityOneSimulationMatchesRedReferenceAndNeutralEndpoints(string modelName, string red)
        {
            ColorVisionModel model = Enum.Parse<ColorVisionModel>(modelName);
            Assert.That(PaletteEvaluation.Simulate(Colors.Red, model), Is.EqualTo(Color.Parse(red)));
            Assert.That(PaletteEvaluation.Simulate(Colors.Black, model), Is.EqualTo(Colors.Black));
            Assert.That(PaletteEvaluation.Simulate(Colors.White, model), Is.EqualTo(Colors.White));
        }

        [Test]
        public void ContrastUsesLinearSrgbAndIsSymmetric()
        {
            Assert.That(PaletteEvaluation.Contrast(Colors.Black, Colors.White), Is.EqualTo(21).Within(1e-12));
            Assert.That(PaletteEvaluation.Contrast(Colors.White, Colors.Black), Is.EqualTo(21).Within(1e-12));
            Assert.That(PaletteEvaluation.Contrast(Colors.Red, Colors.Red), Is.EqualTo(1));
            Assert.That(PaletteEvaluation.Contrast(Color.Parse("#808080"), Colors.White),
                Is.EqualTo(3.949439648).Within(1e-8));
            Assert.That(PaletteEvaluation.LinearRgbDistance(Colors.Red, Colors.Red), Is.Zero);
            Assert.That(PaletteEvaluation.LinearRgbDistance(Colors.Black, Colors.White),
                Is.EqualTo(Math.Sqrt(3)).Within(1e-12));
        }
    }
}
