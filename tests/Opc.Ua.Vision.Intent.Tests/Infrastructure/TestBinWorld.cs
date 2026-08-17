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
using System.Threading;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Live world state for the test cell — the shared object the
    /// ground-truth detector reads and the deterministic executor
    /// mutates when the robot picks or places a part.
    /// </summary>
    internal sealed class TestBinWorld
    {
        public TestBinWorld()
        {
            IReadOnlyList<TestPart> catalog = TestPartsCatalog.Parts;
            m_parts = new PartRuntime[catalog.Count];
            for (int ii = 0; ii < catalog.Count; ii++)
            {
                m_parts[ii] = new PartRuntime(catalog[ii]);
            }
        }

        /// <summary>
        /// Returns a lock-free snapshot of every part's current state.
        /// </summary>
        public IReadOnlyList<TestPartSnapshot> Snapshot()
        {
            lock (m_lock)
            {
                var copy = new TestPartSnapshot[m_parts.Length];
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    PartRuntime runtime = m_parts[ii];
                    copy[ii] = new TestPartSnapshot(
                        runtime.Part,
                        runtime.WorldX,
                        runtime.WorldY,
                        runtime.WorldZ,
                        runtime.Location);
                }
                return copy;
            }
        }

        /// <summary>
        /// Marks the named part as <see cref="TestPartLocation.Held"/>.
        /// </summary>
        public bool MarkHeld(string classLabel)
        {
            if (classLabel == null)
            {
                throw new ArgumentNullException(nameof(classLabel));
            }
            lock (m_lock)
            {
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    if (string.Equals(m_parts[ii].Part.ClassLabel, classLabel, StringComparison.Ordinal))
                    {
                        m_parts[ii].Location = TestPartLocation.Held;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Marks the named part as <see cref="TestPartLocation.Placed"/>
        /// at the placement position — after this call the detector
        /// no longer emits a detection for the part because the
        /// snapshot's Location leaves <see cref="TestPartLocation.InBin"/>.
        /// </summary>
        public bool MarkPlaced(string classLabel, double worldX, double worldY, double worldZ)
        {
            if (classLabel == null)
            {
                throw new ArgumentNullException(nameof(classLabel));
            }
            lock (m_lock)
            {
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    if (string.Equals(m_parts[ii].Part.ClassLabel, classLabel, StringComparison.Ordinal))
                    {
                        m_parts[ii].Location = TestPartLocation.Placed;
                        m_parts[ii].WorldX = worldX;
                        m_parts[ii].WorldY = worldY;
                        m_parts[ii].WorldZ = worldZ;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Resets every part to its authored InBin state.
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    PartRuntime runtime = m_parts[ii];
                    runtime.WorldX = runtime.Part.InitialWorldPosition[0];
                    runtime.WorldY = runtime.Part.InitialWorldPosition[1];
                    runtime.WorldZ = runtime.Part.InitialWorldPosition[2];
                    runtime.Location = TestPartLocation.InBin;
                }
            }
        }

        private sealed class PartRuntime
        {
            public PartRuntime(TestPart part)
            {
                Part = part;
                WorldX = part.InitialWorldPosition[0];
                WorldY = part.InitialWorldPosition[1];
                WorldZ = part.InitialWorldPosition[2];
                Location = TestPartLocation.InBin;
            }

            public TestPart Part { get; }

            public double WorldX { get; set; }

            public double WorldY { get; set; }

            public double WorldZ { get; set; }

            public TestPartLocation Location { get; set; }
        }

        private readonly PartRuntime[] m_parts;
        private readonly Lock m_lock = new();
    }
}
