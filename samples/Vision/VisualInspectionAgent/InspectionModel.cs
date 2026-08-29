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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Vision;

namespace Vision.VisualInspectionAgent
{
    internal sealed class FixtureImageAnalyzer
    {
        public ArrayOf<MeasuredCharacteristic> Measure(byte[] png)
        {
            ArgumentNullException.ThrowIfNull(png);

            (byte[] rgb, int width, int height) = PngDecoder.Decode(png);
            if (width != ImageWidth || height != ImageHeight)
            {
                throw new InvalidDataException(FormattableString.Invariant(
                    $"Expected {ImageWidth}x{ImageHeight}, got {width}x{height}."));
            }

            int borePixels = CountDarkRunThrough(rgb, width, BoreCenterY, BoreCenterX);
            PixelRun slot = FindDarkRunRightOf(rgb, width, SlotCenterY, minX: 400);
            return new[]
            {
                new MeasuredCharacteristic(
                "BoreDiameter",
                borePixels / ScalePixelsPerMillimetre,
                PixelPitchMillimetres,
                ConfidenceFromUncertainty(PixelPitchMillimetres)),
                new MeasuredCharacteristic(
                "SlotWidth",
                slot.Length / ScalePixelsPerMillimetre,
                PixelPitchMillimetres,
                ConfidenceFromUncertainty(PixelPitchMillimetres)),
                new MeasuredCharacteristic(
                "EdgeOffset",
                (BracketRightX - slot.StartX) / ScalePixelsPerMillimetre,
                PixelPitchMillimetres,
                ConfidenceFromUncertainty(PixelPitchMillimetres))
            }.ToArrayOf();
        }

        private static int CountDarkRunThrough(byte[] rgb, int width, int y, int centerX)
        {
            int start = centerX;
            while (start > 0 && IsDark(rgb, width, start - 1, y))
            {
                start--;
            }
            int end = centerX;
            while (end + 1 < width && IsDark(rgb, width, end + 1, y))
            {
                end++;
            }
            return end - start + 1;
        }

        private static PixelRun FindDarkRunRightOf(byte[] rgb, int width, int y, int minX)
        {
            int bestStart = -1;
            int bestLength = 0;
            int x = minX;
            while (x < BracketRightX)
            {
                while (x < BracketRightX && !IsDark(rgb, width, x, y))
                {
                    x++;
                }
                int start = x;
                while (x < BracketRightX && IsDark(rgb, width, x, y))
                {
                    x++;
                }
                int length = x - start;
                if (length > bestLength)
                {
                    bestStart = start;
                    bestLength = length;
                }
            }
            if (bestStart < 0)
            {
                throw new InvalidDataException("Could not find the slot in the fixture image.");
            }
            return new PixelRun(bestStart, bestLength);
        }

        private static bool IsDark(byte[] rgb, int width, int x, int y)
        {
            int offset = ((y * width) + x) * 3;
            return rgb[offset] < 64 && rgb[offset + 1] < 64 && rgb[offset + 2] < 64;
        }

        private static double ConfidenceFromUncertainty(double uncertainty)
        {
            return Math.Clamp(1.0 - uncertainty, 0.0, 1.0);
        }

        private const int ImageWidth = 800;
        private const int ImageHeight = 600;
        private const int BoreCenterX = 290;
        private const int BoreCenterY = 300;
        private const int SlotCenterY = 300;
        private const int BracketRightX = 650;
        private const double ScalePixelsPerMillimetre = 10.0;
        private const double PixelPitchMillimetres = 0.10;

        private readonly record struct PixelRun(int StartX, int Length);
    }

    internal sealed class InspectionVerdictPolicy
    {
        public InspectionDecision Judge(string fixtureName, ArrayOf<MeasuredCharacteristic> measurements)
        {
            if (measurements.Count == 0)
            {
                throw new ArgumentException("Measurements are required.", nameof(measurements));
            }

            var characteristics = new List<VisionCharacteristicDataType>(measurements.Count);
            VisionResultEvaluationEnum verdict = VisionResultEvaluationEnum.Ok;
            for (int ii = 0; ii < measurements.Count; ii++)
            {
                MeasuredCharacteristic measurement = measurements[ii];
                InspectionCharacteristicRecipe recipe = RecipeFor(measurement.CharacteristicId);
                VisionResultEvaluationEnum characteristicVerdict = JudgeCharacteristic(recipe, measurement);
                if (characteristicVerdict == VisionResultEvaluationEnum.NotOk)
                {
                    verdict = VisionResultEvaluationEnum.NotOk;
                }
                else if (characteristicVerdict == VisionResultEvaluationEnum.NotDecidable &&
                    verdict != VisionResultEvaluationEnum.NotOk)
                {
                    verdict = VisionResultEvaluationEnum.NotDecidable;
                }

                characteristics.Add(new VisionCharacteristicDataType
                {
                    CharacteristicId = recipe.CharacteristicId,
                    Name = recipe.Name,
                    Nominal = recipe.Nominal,
                    Actual = measurement.Actual,
                    Deviation = measurement.Actual - recipe.Nominal,
                    LowerTolerance = recipe.LowerTolerance,
                    UpperTolerance = recipe.UpperTolerance,
                    Uncertainty = measurement.Uncertainty,
                    Unit = Millimetre,
                    Status = ToToleranceStatus(characteristicVerdict)
                });
            }
            return new InspectionDecision(fixtureName, characteristics.ToArrayOf(), verdict);
        }

        private static InspectionCharacteristicRecipe RecipeFor(string characteristicId)
        {
            foreach (InspectionCharacteristicRecipe characteristic in s_characteristics)
            {
                if (string.Equals(characteristic.CharacteristicId, characteristicId, StringComparison.Ordinal))
                {
                    return characteristic;
                }
            }
            throw new KeyNotFoundException(characteristicId);
        }

        private static VisionResultEvaluationEnum JudgeCharacteristic(
            InspectionCharacteristicRecipe recipe,
            MeasuredCharacteristic measurement)
        {
            double intervalLow = measurement.Actual - measurement.Uncertainty;
            double intervalHigh = measurement.Actual + measurement.Uncertainty;
            double toleranceLow = recipe.Nominal - recipe.LowerTolerance;
            double toleranceHigh = recipe.Nominal + recipe.UpperTolerance;
            if (intervalLow >= toleranceLow && intervalHigh <= toleranceHigh)
            {
                return VisionResultEvaluationEnum.Ok;
            }
            if (intervalHigh < toleranceLow || intervalLow > toleranceHigh)
            {
                return VisionResultEvaluationEnum.NotOk;
            }
            return VisionResultEvaluationEnum.NotDecidable;
        }

        private static VisionToleranceStatusEnum ToToleranceStatus(VisionResultEvaluationEnum verdict)
        {
            return verdict switch
            {
                VisionResultEvaluationEnum.Ok => VisionToleranceStatusEnum.InTolerance,
                VisionResultEvaluationEnum.NotOk => VisionToleranceStatusEnum.OutOfTolerance,
                _ => VisionToleranceStatusEnum.Indeterminate
            };
        }

        private static EUInformation Millimetre { get; } =
            new("mm", "millimetre", "http://www.opcfoundation.org/UA/units/un/cefact");

        private static readonly InspectionCharacteristicRecipe[] s_characteristics =
        [
            new("BoreDiameter", "Bore diameter", 12.00, 0.20, 0.20),
            new("SlotWidth", "Slot width", 8.00, 0.15, 0.15),
            new("EdgeOffset", "Edge offset", 20.00, 0.25, 0.25)
        ];
    }

    internal sealed class ScriptedOperatorPolicy
    {
        public async Task<OperatorDisposition> GetDispositionAsync(
            VisualInspectionAgentMode mode,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (mode == VisualInspectionAgentMode.Human)
            {
                await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
                return OperatorDisposition.Stop;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            return OperatorDisposition.AcceptAsNotOk;
        }
    }

    internal sealed record InspectionCharacteristicRecipe(
        string CharacteristicId,
        string Name,
        double Nominal,
        double LowerTolerance,
        double UpperTolerance);

    internal sealed record MeasuredCharacteristic(
        string CharacteristicId,
        double Actual,
        double Uncertainty,
        double Confidence);

    internal sealed record InspectionDecision(
        string FixtureName,
        ArrayOf<VisionCharacteristicDataType> Characteristics,
        VisionResultEvaluationEnum Evaluation);

    internal enum OperatorDisposition
    {
        AcceptAsOk,

        AcceptAsNotOk,

        Reinspect,

        Stop
    }
}
