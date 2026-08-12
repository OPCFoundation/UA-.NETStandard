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
using Opc.Ua;
using Opc.Ua.Vision;

namespace Vision.VisualInspectionCell
{
    /// <summary>
    /// Applies the inspection recipe to measured evidence.
    /// </summary>
    internal sealed class InspectionVerdictPolicy
    {
        public InspectionVerdictPolicy(InspectionRecipe recipe)
        {
            m_recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        }

        public InspectionAnalysis Judge(string fixtureName, IReadOnlyList<MeasuredCharacteristic> measurements)
        {
            if (measurements == null)
            {
                throw new ArgumentNullException(nameof(measurements));
            }

            var characteristics = new List<VisionCharacteristicDataType>(m_recipe.Characteristics.Count);
            VisionResultEvaluationEnum verdict = VisionResultEvaluationEnum.Ok;
            foreach (InspectionCharacteristicRecipe recipe in m_recipe.Characteristics)
            {
                MeasuredCharacteristic? measurement = FindMeasurement(measurements, recipe.CharacteristicId);
                VisionResultEvaluationEnum characteristicVerdict = measurement == null
                    ? VisionResultEvaluationEnum.NotDecidable
                    : JudgeCharacteristic(recipe, measurement);
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
                    Actual = measurement?.Actual ?? 0.0,
                    Deviation = measurement == null ? 0.0 : measurement.Actual - recipe.Nominal,
                    LowerTolerance = recipe.LowerTolerance,
                    UpperTolerance = recipe.UpperTolerance,
                    Uncertainty = measurement?.Uncertainty ?? 0.0,
                    Unit = InspectionRecipe.Millimetre,
                    Status = ToToleranceStatus(characteristicVerdict)
                });
            }

            return new InspectionAnalysis(fixtureName, characteristics.ToArrayOf(), verdict);
        }

        public VisionResultEvaluationEnum JudgeCharacteristics(
            ArrayOf<VisionCharacteristicDataType> characteristics)
        {
            var measurements = new List<MeasuredCharacteristic>(characteristics.Count);
            for (int ii = 0; ii < characteristics.Count; ii++)
            {
                VisionCharacteristicDataType characteristic = characteristics[ii];
                measurements.Add(new MeasuredCharacteristic(
                    characteristic.CharacteristicId,
                    characteristic.Actual,
                    characteristic.Uncertainty));
            }
            return Judge("submitted-characteristics", measurements).Verdict;
        }

        private static VisionResultEvaluationEnum JudgeCharacteristic(
            InspectionCharacteristicRecipe recipe,
            MeasuredCharacteristic measurement)
        {
            long intervalLow = ToMicrometres(measurement.Actual - measurement.Uncertainty);
            long intervalHigh = ToMicrometres(measurement.Actual + measurement.Uncertainty);
            long toleranceLow = ToMicrometres(recipe.Nominal - recipe.LowerTolerance);
            long toleranceHigh = ToMicrometres(recipe.Nominal + recipe.UpperTolerance);
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

        private static MeasuredCharacteristic? FindMeasurement(
            IReadOnlyList<MeasuredCharacteristic> measurements,
            string characteristicId)
        {
            for (int ii = 0; ii < measurements.Count; ii++)
            {
                MeasuredCharacteristic measurement = measurements[ii];
                if (string.Equals(measurement.CharacteristicId, characteristicId, StringComparison.Ordinal))
                {
                    return measurement;
                }
            }

            return null;
        }

        private static long ToMicrometres(double value)
        {
            return (long)Math.Round(value * 1000.0, MidpointRounding.AwayFromZero);
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

        private readonly InspectionRecipe m_recipe;
    }
}
