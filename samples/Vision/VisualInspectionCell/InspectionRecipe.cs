// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Vision.Perception;

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
