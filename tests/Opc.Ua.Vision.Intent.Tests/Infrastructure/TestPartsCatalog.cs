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
