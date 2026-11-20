// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// The location of a part in the test cell — mirrors the concept
    /// from the bin-picking sample without any dependency on it.
    /// </summary>
    internal enum TestPartLocation
    {
        InBin = 0,
        Held = 1,
        Placed = 2
    }

    /// <summary>
    /// Immutable authored description of a part.
    /// </summary>
    internal sealed record TestPart(
        string ClassLabel,
        uint ClassId,
        double[] InitialWorldPosition,
        double[] Size);

    /// <summary>
    /// Immutable snapshot of a part's current runtime state.
    /// </summary>
    internal sealed record TestPartSnapshot(
        TestPart Part,
        double WorldX,
        double WorldY,
        double WorldZ,
        TestPartLocation Location);

    /// <summary>
    /// Two-part deterministic catalog. Positions are metres in the
    /// world frame. Two parts are enough to prove the loop end-to-end
    /// without inflating the harness footprint.
    /// </summary>
    internal static class TestPartsCatalog
    {
        public static IReadOnlyList<TestPart> Parts => s_parts;

        public static TestPart? TryGet(string classLabel)
        {
            if (classLabel == null)
            {
                return null;
            }
            for (int ii = 0; ii < s_parts.Length; ii++)
            {
                if (string.Equals(s_parts[ii].ClassLabel, classLabel, StringComparison.Ordinal))
                {
                    return s_parts[ii];
                }
            }
            return null;
        }

        private static readonly TestPart[] s_parts =
        [
            new TestPart(
                ClassLabel: "TestCube",
                ClassId: 1u,
                InitialWorldPosition: [0.700, 0.100, 0.600],
                Size: [0.04, 0.04, 0.04]),
            new TestPart(
                ClassLabel: "TestCylinder",
                ClassId: 2u,
                InitialWorldPosition: [0.650, -0.050, 0.605],
                Size: [0.04, 0.04, 0.03])
        ];
    }
}
