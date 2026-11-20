// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
