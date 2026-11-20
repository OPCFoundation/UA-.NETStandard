// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Vision.VisualInspectionCell
{
    internal enum VisualInspectionInferenceLocation
    {
        OnServer,
        EdgeOffServer
    }

    internal sealed class VisualInspectionCellOptions
    {
        public VisualInspectionInferenceLocation InferenceLocation { get; init; }
            = VisualInspectionInferenceLocation.OnServer;

        public static bool TryParseLocation(string? value, out VisualInspectionInferenceLocation location)
        {
            if (Enum.TryParse(value, ignoreCase: true, out location))
            {
                return true;
            }

            location = VisualInspectionInferenceLocation.OnServer;
            return false;
        }
    }
}
