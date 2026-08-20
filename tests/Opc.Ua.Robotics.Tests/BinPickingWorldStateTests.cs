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
using NUnit.Framework;
using Vision.BinPickingCell;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Covers the bin-picking cell's world model, whose location label decides whether the
    /// camera still reports a part. The label has to follow the part's coordinates: when it
    /// followed the last operation instead, a part the robot had returned to the bin stayed
    /// invisible to the detector for good, and a stack-then-return cycle could only run once.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public sealed class BinPickingWorldStateTests
    {
        [Test]
        public void EveryPartStartsInTheBin()
        {
            var state = new BinPickingWorldState();

            IReadOnlyList<BinPickingPartSnapshot> parts = state.Snapshot();

            Assert.That(parts, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (BinPickingPartSnapshot part in parts)
                {
                    Assert.That(part.Location, Is.EqualTo(BinPickingPartLocation.InBin),
                        part.Part.ClassLabel + " starts in the bin.");
                    Assert.That(BinPickingPartsCatalog.IsInsideBin(part.WorldX, part.WorldY), Is.True,
                        part.Part.ClassLabel + " starts inside the bin footprint.");
                }
            });
        }

        [Test]
        public void PickedPartIsHeldAndNoLongerReported()
        {
            var state = new BinPickingWorldState();

            bool marked = state.MarkHeld(SampleLabel, 0.30, 0.0, 1.00);

            Assert.That(marked, Is.True);
            Assert.That(Find(state, SampleLabel).Location, Is.EqualTo(BinPickingPartLocation.Held),
                "A part in the gripper must not be reported as sitting in the bin.");
        }

        [Test]
        public void PartPlacedOnTheFixtureIsNotInTheBin()
        {
            var state = new BinPickingWorldState();
            _ = state.MarkHeld(SampleLabel, 0.30, 0.0, 1.00);

            _ = state.MarkPlaced(SampleLabel, FixtureX, FixtureY, 0.85);

            Assert.That(Find(state, SampleLabel).Location, Is.EqualTo(BinPickingPartLocation.Placed));
        }

        [Test]
        public void PartReturnedToTheBinIsInTheBinAgain()
        {
            var state = new BinPickingWorldState();
            _ = state.MarkHeld(SampleLabel, 0.30, 0.0, 1.00);
            _ = state.MarkPlaced(SampleLabel, FixtureX, FixtureY, 0.85);

            _ = state.MarkPlaced(SampleLabel, BinPickingPartsCatalog.BinCentreX, BinPickingPartsCatalog.BinCentreY, 0.85);

            Assert.That(Find(state, SampleLabel).Location, Is.EqualTo(BinPickingPartLocation.InBin),
                "A part put back in the bin has to become visible to the camera again, or a "
                + "stack-then-return cycle can only ever run once.");
        }

        [Test]
        public void PartReturnedToItsOwnStartingSpotIsInTheBin()
        {
            var state = new BinPickingWorldState();
            BinPickingPart part = BinPickingPartsCatalog.Parts[0];
            _ = state.MarkHeld(part.ClassLabel, 0.30, 0.0, 1.00);

            _ = state.MarkPlaced(
                part.ClassLabel,
                part.InitialWorldPosition[0],
                part.InitialWorldPosition[1],
                part.InitialWorldPosition[2]);

            Assert.That(Find(state, part.ClassLabel).Location, Is.EqualTo(BinPickingPartLocation.InBin));
        }

        [Test]
        public void BinFootprintCoversEveryAuthoredPartPositionAndExcludesTheFixture()
        {
            Assert.Multiple(() =>
            {
                foreach (BinPickingPart part in BinPickingPartsCatalog.Parts)
                {
                    Assert.That(
                        BinPickingPartsCatalog.IsInsideBin(part.InitialWorldPosition[0], part.InitialWorldPosition[1]),
                        Is.True,
                        part.ClassLabel + " is authored inside the bin.");
                }
                Assert.That(BinPickingPartsCatalog.IsInsideBin(FixtureX, FixtureY), Is.False,
                    "The fixture stands well clear of the bin, so a part stacked there is not in the bin.");
            });
        }

        [Test]
        public void ResetPutsEveryPartBackWhereItStarted()
        {
            var state = new BinPickingWorldState();
            _ = state.MarkHeld(SampleLabel, 0.30, 0.0, 1.00);
            _ = state.MarkPlaced(SampleLabel, FixtureX, FixtureY, 0.85);

            state.Reset();

            Assert.Multiple(() =>
            {
                foreach (BinPickingPartSnapshot snapshot in state.Snapshot())
                {
                    Assert.That(snapshot.WorldX, Is.EqualTo(snapshot.Part.InitialWorldPosition[0]).Within(1e-9));
                    Assert.That(snapshot.WorldY, Is.EqualTo(snapshot.Part.InitialWorldPosition[1]).Within(1e-9));
                    Assert.That(snapshot.WorldZ, Is.EqualTo(snapshot.Part.InitialWorldPosition[2]).Within(1e-9));
                    Assert.That(snapshot.Location, Is.EqualTo(BinPickingPartLocation.InBin));
                }
            });
        }

        [Test]
        public void UnknownClassLabelIsRejected()
        {
            var state = new BinPickingWorldState();

            Assert.Multiple(() =>
            {
                Assert.That(state.MarkHeld("NoSuchPart", 0.0, 0.0, 0.0), Is.False);
                Assert.That(state.MarkPlaced("NoSuchPart", 0.0, 0.0, 0.0), Is.False);
            });
        }

        private static BinPickingPartSnapshot Find(BinPickingWorldState state, string classLabel)
        {
            foreach (BinPickingPartSnapshot snapshot in state.Snapshot())
            {
                if (snapshot.Part.ClassLabel == classLabel)
                {
                    return snapshot;
                }
            }
            Assert.Fail("The catalogue has no part called " + classLabel + ".");
            return null!;
        }

        // The fixture the parts get stacked on, from Assets/Cell.usda.
        private const double FixtureX = BinPickingCellGeometry.FixtureCentreX;
        private const double FixtureY = 0.0;
        private const string SampleLabel = "RedCube";
    }
}
