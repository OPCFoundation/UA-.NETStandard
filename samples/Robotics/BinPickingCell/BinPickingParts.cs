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

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Where a part currently lives in the cell.
    /// </summary>
    internal enum BinPickingPartLocation
    {
        /// <summary>
        /// The part is in the bin at its authored position.
        /// </summary>
        InBin = 0,

        /// <summary>
        /// The part is currently held by the gripper.
        /// </summary>
        Held = 1,

        /// <summary>
        /// The part has been placed on the fixture (or somewhere else
        /// outside the bin).
        /// </summary>
        Placed = 2
    }

    /// <summary>
    /// Immutable description of a part in the cell. Values mirror the
    /// authored transforms in <c>Assets/Cell.usda</c>: same class labels,
    /// same initial world positions, same colours and same axis-aligned
    /// bounding-box sizes. The detector reads this catalog rather than
    /// re-parsing the USD file so the ground truth is deterministic and
    /// available even when the OpenUSD render backend is not.
    /// </summary>
    /// <param name="ClassLabel">
    /// Human-readable class name (matches <c>Cell.usda</c> prim name).
    /// </param>
    /// <param name="ClassId">
    /// Small integer id, monotonic and stable across restarts. A client
    /// can key on the id when the label is not convenient.
    /// </param>
    /// <param name="Shape">
    /// Coarse shape hint: <c>cube</c>, <c>cylinder</c>, <c>sphere</c>,
    /// <c>slab</c> or <c>brick</c>. Not part of the OPC UA payload but
    /// used to compute the 3-D size vector below.
    /// </param>
    /// <param name="Colour">
    /// Approximate authored displayColor (RGB in [0,1]). Kept for the
    /// demo's traceability — a client that reads the rendered frame can
    /// cross-reference this against the mean colour of the reported
    /// bounding box.
    /// </param>
    /// <param name="InitialWorldPosition">
    /// Position (metres) in the <c>world</c> frame, matching the
    /// authored USD translate.
    /// </param>
    /// <param name="RotationZDegrees">
    /// Rotation about the world Z axis, degrees, matching the authored
    /// <c>xformOp:rotateZ</c>.
    /// </param>
    /// <param name="Size">
    /// Axis-aligned size (width, depth, height) in metres before Z
    /// rotation — the extents the detector reports as
    /// <c>BoundingBox3D.Size</c>.
    /// </param>
    internal sealed record BinPickingPart(
        string ClassLabel,
        uint ClassId,
        string Shape,
        double[] Colour,
        double[] InitialWorldPosition,
        double RotationZDegrees,
        double[] Size);

    /// <summary>
    /// Mutable per-part runtime state. Copied out under a lock into a
    /// snapshot so the detector can iterate lock-free.
    /// </summary>
    internal sealed class BinPickingPartRuntime
    {
        public BinPickingPartRuntime(BinPickingPart part)
        {
            Part = part ?? throw new ArgumentNullException(nameof(part));
            WorldX = part.InitialWorldPosition[0];
            WorldY = part.InitialWorldPosition[1];
            WorldZ = part.InitialWorldPosition[2];
            RotationZDegrees = part.RotationZDegrees;
            Location = BinPickingPartLocation.InBin;
        }

        public BinPickingPart Part { get; }

        public double WorldX { get; set; }

        public double WorldY { get; set; }

        public double WorldZ { get; set; }

        public double RotationZDegrees { get; set; }

        public BinPickingPartLocation Location { get; set; }
    }

    /// <summary>
    /// Immutable snapshot of one part's live state, safe to hand out to
    /// the detector without any lock.
    /// </summary>
    internal sealed record BinPickingPartSnapshot(
        BinPickingPart Part,
        double WorldX,
        double WorldY,
        double WorldZ,
        double RotationZDegrees,
        BinPickingPartLocation Location);

    /// <summary>
    /// Aggregate live state for the five parts in the cell. Registered
    /// as a DI singleton and mutated from either the arm-executor's
    /// pick/place events or from the demo hosted service; the detector
    /// reads a lock-free snapshot on every inference tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class deliberately does not depend on the OPC UA server
    /// address space or on the OpenUSD stage: this is where the demo
    /// declares "what parts exist and where they are right now". The
    /// values are seeded from <see cref="BinPickingPartsCatalog"/>
    /// which mirrors the authored transforms in <c>Assets/Cell.usda</c>.
    /// </para>
    /// <para>
    /// Mutation uses <see cref="System.Threading.Lock"/> because these
    /// operations are short and synchronous; the detector never blocks
    /// on the lock — it copies the state into an
    /// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> and
    /// walks that copy.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed class BinPickingWorldState
    {
        public BinPickingWorldState()
        {
            IReadOnlyList<BinPickingPart> catalog = BinPickingPartsCatalog.Parts;
            m_parts = new BinPickingPartRuntime[catalog.Count];
            for (int ii = 0; ii < catalog.Count; ii++)
            {
                m_parts[ii] = new BinPickingPartRuntime(catalog[ii]);
            }
        }

        /// <summary>
        /// Returns a lock-free snapshot of every part's current state.
        /// </summary>
        public IReadOnlyList<BinPickingPartSnapshot> Snapshot()
        {
            lock (m_lock)
            {
                var copy = new BinPickingPartSnapshot[m_parts.Length];
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    BinPickingPartRuntime runtime = m_parts[ii];
                    copy[ii] = new BinPickingPartSnapshot(
                        runtime.Part,
                        runtime.WorldX,
                        runtime.WorldY,
                        runtime.WorldZ,
                        runtime.RotationZDegrees,
                        runtime.Location);
                }
                return copy;
            }
        }

        /// <summary>
        /// Marks the part with <paramref name="classLabel"/> as
        /// <see cref="BinPickingPartLocation.Held"/> and moves its
        /// world position to <paramref name="worldX"/>,
        /// <paramref name="worldY"/>, <paramref name="worldZ"/>.
        /// </summary>
        /// <returns>
        /// <c>true</c> when the class was recognised.
        /// </returns>
        public bool MarkHeld(string classLabel, double worldX, double worldY, double worldZ)
        {
            if (classLabel == null)
            {
                throw new ArgumentNullException(nameof(classLabel));
            }
            lock (m_lock)
            {
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    BinPickingPartRuntime runtime = m_parts[ii];
                    if (string.Equals(runtime.Part.ClassLabel, classLabel, StringComparison.Ordinal))
                    {
                        runtime.Location = BinPickingPartLocation.Held;
                        runtime.WorldX = worldX;
                        runtime.WorldY = worldY;
                        runtime.WorldZ = worldZ;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Marks the part with <paramref name="classLabel"/> as released at
        /// <paramref name="worldX"/>, <paramref name="worldY"/>,
        /// <paramref name="worldZ"/>, and records whether that spot is inside the bin.
        /// </summary>
        /// <remarks>
        /// The location label follows the coordinates rather than the operation: a part
        /// put back inside the bin's footprint is <see cref="BinPickingPartLocation.InBin"/>
        /// again. It used to become <see cref="BinPickingPartLocation.Placed"/> whatever
        /// the coordinates said, and since the detector only reports parts that are InBin,
        /// a part the robot had returned to the bin stayed invisible to the camera - the
        /// world model claiming one thing while its own coordinates said another.
        /// </remarks>
        /// <returns>
        /// <c>true</c> when the class was recognised.
        /// </returns>
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
                    BinPickingPartRuntime runtime = m_parts[ii];
                    if (string.Equals(runtime.Part.ClassLabel, classLabel, StringComparison.Ordinal))
                    {
                        runtime.Location = BinPickingPartsCatalog.IsInsideBin(worldX, worldY)
                            ? BinPickingPartLocation.InBin
                            : BinPickingPartLocation.Placed;
                        runtime.WorldX = worldX;
                        runtime.WorldY = worldY;
                        runtime.WorldZ = worldZ;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Resets every part to its authored position and marks it as
        /// <see cref="BinPickingPartLocation.InBin"/>. Used by the proof
        /// service so a second run of the demo starts from a known
        /// state.
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < m_parts.Length; ii++)
                {
                    BinPickingPartRuntime runtime = m_parts[ii];
                    runtime.WorldX = runtime.Part.InitialWorldPosition[0];
                    runtime.WorldY = runtime.Part.InitialWorldPosition[1];
                    runtime.WorldZ = runtime.Part.InitialWorldPosition[2];
                    runtime.RotationZDegrees = runtime.Part.RotationZDegrees;
                    runtime.Location = BinPickingPartLocation.InBin;
                }
            }
        }

        private readonly BinPickingPartRuntime[] m_parts;
        private readonly Lock m_lock = new();
    }

    /// <summary>
    /// Static catalog seeded from <c>Assets/Cell.usda</c>. The numbers
    /// mirror the authored transforms exactly; changing one without the
    /// other silently splits the ground truth from the rendered image.
    /// </summary>
    internal static class BinPickingPartsCatalog
    {
        /// <summary>
        /// Returns the five parts of the reference bin, in the order
        /// they are declared in the USD stage.
        /// </summary>
        public static IReadOnlyList<BinPickingPart> Parts => s_parts;

        /// <summary>
        /// Gets whether a world position is inside the bin's footprint.
        /// </summary>
        /// <remarks>
        /// The bin is where parts are picked from and returned to, so "is it in the bin"
        /// is what decides whether the camera should still be reporting a part. Keeping
        /// the footprint here, next to the authored part positions, keeps one answer to
        /// where the bin is: the cell's Bin Location is built from these same numbers.
        /// </remarks>
        public static bool IsInsideBin(double worldX, double worldY)
        {
            return Math.Abs(worldX - BinCentreX) <= BinHalfExtent
                && Math.Abs(worldY - BinCentreY) <= BinHalfExtent;
        }

        /// <summary>
        /// Looks up a part by its class label. Returns <c>null</c> when
        /// the label is unknown — used by the demo hosted service to
        /// cross-check a composed pose against the authored world
        /// position.
        /// </summary>
        public static BinPickingPart? TryGet(string classLabel)
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

        /// <summary>
        /// Centre of the bin in the world frame, matching <c>Assets/Cell.usda</c>.
        /// </summary>
        public const double BinCentreX = 0.38;

        /// <summary>
        /// Centre of the bin in the world frame, matching <c>Assets/Cell.usda</c>.
        /// </summary>
        public const double BinCentreY = 0.0;

        /// <summary>
        /// Half the bin's inner span; a part within this of the centre is in the bin.
        /// </summary>
        public const double BinHalfExtent = 0.12;

        private static readonly BinPickingPart[] s_parts =        [
            new BinPickingPart(
                ClassLabel: "RedCube",
                ClassId: 1u,
                Shape: "cube",
                Colour: [0.90, 0.15, 0.15],
                InitialWorldPosition: [0.3300, -0.0800, 0.8500],
                RotationZDegrees: 20.0,
                Size: [0.0400, 0.0400, 0.0400]),
            new BinPickingPart(
                ClassLabel: "GreenCylinder",
                ClassId: 2u,
                Shape: "cylinder",
                Colour: [0.15, 0.85, 0.20],
                InitialWorldPosition: [0.4200, 0.0500, 0.8500],
                RotationZDegrees: 0.0,
                Size: [0.0400, 0.0400, 0.0300]),
            new BinPickingPart(
                ClassLabel: "BlueSphere",
                ClassId: 3u,
                Shape: "sphere",
                Colour: [0.15, 0.30, 0.95],
                InitialWorldPosition: [0.4600, -0.0500, 0.8500],
                RotationZDegrees: 0.0,
                Size: [0.0480, 0.0480, 0.0480]),
            new BinPickingPart(
                ClassLabel: "YellowSlab",
                ClassId: 4u,
                Shape: "slab",
                Colour: [0.95, 0.85, 0.15],
                InitialWorldPosition: [0.3400, 0.0600, 0.8390],
                RotationZDegrees: -15.0,
                Size: [0.0640, 0.0320, 0.0180]),
            new BinPickingPart(
                ClassLabel: "OrangeBrick",
                ClassId: 5u,
                Shape: "brick",
                Colour: [0.95, 0.45, 0.10],
                InitialWorldPosition: [0.4200, -0.0100, 0.8420],
                RotationZDegrees: 40.0,
                Size: [0.0500, 0.0280, 0.0240])
        ];
    }
}
