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

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Vision;

namespace Vision.VisualInspectionCell
{
    internal sealed record InspectionCharacteristicRecipe(
        string CharacteristicId,
        string Name,
        double Nominal,
        double LowerTolerance,
        double UpperTolerance);

    internal sealed class InspectionRecipe
    {
        public const string RecipeId = "machined-bracket-mm-v1";
        public const string PartId = "machined-bracket";

        public IReadOnlyList<InspectionCharacteristicRecipe> Characteristics { get; } =
        [
            new("BoreDiameter", "Bore diameter", 12.00, 0.20, 0.20),
            new("SlotWidth", "Slot width", 8.00, 0.15, 0.15),
            new("EdgeOffset", "Edge offset", 20.00, 0.25, 0.25)
        ];

        public static EUInformation Millimetre { get; } =
            new("mm", "millimetre", "http://www.opcfoundation.org/UA/units/un/cefact");

        public InspectionCharacteristicRecipe this[string characteristicId]
        {
            get
            {
                foreach (InspectionCharacteristicRecipe characteristic in Characteristics)
                {
                    if (string.Equals(characteristic.CharacteristicId, characteristicId, System.StringComparison.Ordinal))
                    {
                        return characteristic;
                    }
                }

                throw new KeyNotFoundException(characteristicId);
            }
        }
    }

    internal sealed record MeasuredCharacteristic(string CharacteristicId, double Actual, double Uncertainty);

    internal sealed record InspectionAnalysis(
        string FixtureName,
        ArrayOf<VisionCharacteristicDataType> Characteristics,
        VisionResultEvaluationEnum Verdict);
}
