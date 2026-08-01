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
using NUnit.Framework;
using Robotics;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="CellChoreographer"/>.
    /// </summary>
    /// <remarks>
    /// The sample previously ran the two robots on independent figure-eight paths whose
    /// platforms cleared by 1.2 m while the arms reach 1.61 m, so the arms swept through
    /// each other. These tests run the coordinated cycle for several minutes of simulated
    /// time and assert the separation and reservation invariants on every step, which is the
    /// regression guard for that.
    /// </remarks>
    [TestFixture]
    [Category("Robotics")]
    [Parallelizable]
    public class CellChoreographyTests
    {
        private const double Step = 0.05;
        private const int LongRunSteps = 12000; // ten minutes of simulated time

        [Test]
        public void RobotsNeverOverlap()
        {
            var cell = new CellChoreographer();
            double worstClearance = double.MaxValue;

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                RobotAgent a = cell.Robots[0];
                RobotAgent b = cell.Robots[1];
                bool overlap = CellChoreographer.FootprintsOverlap(a, b);
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                worstClearance = Math.Min(worstClearance, Math.Sqrt((dx * dx) + (dy * dy)));

                Assert.That(overlap, Is.False,
                    $"Step {i}: {a.Id} at ({a.X:N2},{a.Y:N2}) stowed={a.ArmStowed} and " +
                    $"{b.Id} at ({b.X:N2},{b.Y:N2}) stowed={b.ArmStowed} overlap.");
            }

            Assert.That(worstClearance, Is.GreaterThan(0.0));
        }

        /// <summary>
        /// The arms are the part that used to collide, so their reach is checked directly
        /// rather than inferred from the platform separation.
        /// </summary>
        [Test]
        public void ToolCentrePointsNeverMeet()
        {
            var cell = new CellChoreographer();

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                (double ax, double ay, _) = CellChoreographer
                    .ToolCentrePointOf(cell.Robots[0]).Origin;
                (double bx, double by, _) = CellChoreographer
                    .ToolCentrePointOf(cell.Robots[1]).Origin;
                double distance = Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));

                Assert.That(distance, Is.GreaterThan(0.30),
                    $"Step {i}: the two tool centre points came within {distance:N3} m.");
            }
        }

        [Test]
        public void ACorridorLaneIsNeverSharedByBothRobots()
        {
            var cell = new CellChoreographer();

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                var zones = new List<CellZone>();
                foreach (RobotAgent robot in cell.Robots)
                {
                    zones.Add(CellChoreographer.ZoneAt(robot.X, robot.Y));
                }
                bool shared = zones[0] == zones[1] && zones[0] != CellZone.None;

                Assert.That(shared, Is.False,
                    $"Step {i}: both robots occupied {zones[0]}.");
            }
        }

        /// <summary>
        /// An arm may only leave its transport envelope inside an end zone, because that is
        /// the reservation that guarantees nothing else is there.
        /// </summary>
        [Test]
        public void ArmsOnlyDeployInsideAnEndZone()
        {
            var cell = new CellChoreographer();

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                foreach (RobotAgent robot in cell.Robots)
                {
                    if (robot.ArmStowed)
                    {
                        continue;
                    }
                    CellZone zone = CellChoreographer.ZoneAt(robot.X, robot.Y);
                    bool inEndZone = zone is CellZone.EndZoneA or CellZone.EndZoneB;
                    Assert.That(
                        inEndZone,
                        Is.True,
                        $"Step {i}: {robot.Id} deployed its arm in {zone}.");
                }
            }
        }

        /// <summary>
        /// Every part must be in exactly one place at all times - resting in a slot, carried
        /// by one robot, or on the floor after a dropped grip.
        /// </summary>
        [Test]
        public void PartsAreConserved()
        {
            var cell = new CellChoreographer();
            int expected = cell.Parts.Count;

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                var carriers = new List<string>();
                foreach (CellPart part in cell.Parts)
                {
                    int places = 0;
                    if (part.CarriedBy != null)
                    {
                        places++;
                        carriers.Add(part.CarriedBy);
                    }
                    if (part.IsResting)
                    {
                        places++;
                    }
                    Assert.That(places, Is.EqualTo(1),
                        $"Step {i}: {part.Id} is in {places} places.");
                }
                Assert.That(cell.Parts, Has.Count.EqualTo(expected));
                Assert.That(carriers, Is.Unique, $"Step {i}: a part is carried twice.");
            }
        }

        /// <summary>
        /// A carried part has to sit exactly on the tool centre point, which is the whole
        /// reason the pose is computed from forward kinematics rather than from the script.
        /// </summary>
        [Test]
        public void ACarriedPartTracksTheToolCentrePoint()
        {
            var cell = new CellChoreographer();
            int carriedSamples = 0;

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                foreach (RobotAgent robot in cell.Robots)
                {
                    if (robot.HoldingPart == null)
                    {
                        continue;
                    }
                    CellPart part = FindPart(cell, robot.HoldingPart);
                    (double x, double y, double z) = CellChoreographer
                        .ToolCentrePointOf(robot).Origin;
                    Assert.That(part.X, Is.EqualTo(x).Within(1e-9));
                    Assert.That(part.Y, Is.EqualTo(y).Within(1e-9));
                    Assert.That(part.Z, Is.EqualTo(z).Within(1e-9));
                    carriedSamples++;
                }
            }

            Assert.That(carriedSamples, Is.GreaterThan(0),
                "The run never carried a part, so the assertion proved nothing.");
        }

        /// <summary>
        /// The interlock must not deadlock: a part staged on the western station has to
        /// reach the eastern one and come back.
        /// </summary>
        [Test]
        public void PartsAreHandedOverBetweenBothStations()
        {
            var cell = new CellChoreographer();
            var startedOnA = new HashSet<string>(StringComparer.Ordinal);
            foreach (CellPart part in cell.Parts)
            {
                if (part.Station == CellStation.TableA)
                {
                    _ = startedOnA.Add(part.Id);
                }
            }
            var deliveredEast = new HashSet<string>(StringComparer.Ordinal);
            var returnedToA = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
                foreach (CellPart part in cell.Parts)
                {
                    if (!part.IsResting || part.OnFloor)
                    {
                        continue;
                    }
                    if (part.Station == CellStation.TableB)
                    {
                        if (startedOnA.Contains(part.Id))
                        {
                            _ = deliveredEast.Add(part.Id);
                        }
                    }
                    else if (deliveredEast.Contains(part.Id))
                    {
                        _ = returnedToA.Add(part.Id);
                    }
                }
            }

            Assert.That(deliveredEast, Is.Not.Empty,
                "R1 never delivered a western part to the eastern station.");
            Assert.That(returnedToA, Is.Not.Empty,
                "R2 never returned a delivered part to the western station.");
            Assert.That(cell.Kpis.PartsMoved, Is.GreaterThan(1));
        }

        [Test]
        public void KpisAccumulateOverTheRun()
        {
            var cell = new CellChoreographer();

            for (int i = 0; i < LongRunSteps; i++)
            {
                cell.Advance(Step);
            }

            Assert.That(cell.Kpis.PartsMoved, Is.GreaterThan(0));
            Assert.That(cell.Kpis.AverageCycleSeconds, Is.GreaterThan(0.0));
            Assert.That(cell.Kpis.Utilisation, Is.GreaterThan(0.0));
            Assert.That(cell.Kpis.Utilisation, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void EmergencyStopHaltsEveryRobot()
        {
            var cell = new CellChoreographer();
            for (int i = 0; i < 400; i++)
            {
                cell.Advance(Step);
            }

            cell.EmergencyStop = true;
            for (int i = 0; i < 40; i++)
            {
                cell.Advance(Step);
            }

            double[] x = [cell.Robots[0].X, cell.Robots[1].X];
            double[] y = [cell.Robots[0].Y, cell.Robots[1].Y];
            for (int i = 0; i < 40; i++)
            {
                cell.Advance(Step);
            }

            for (int r = 0; r < cell.Robots.Count; r++)
            {
                Assert.That(cell.Robots[r].Speed, Is.Zero);
                Assert.That(cell.Robots[r].X, Is.EqualTo(x[r]).Within(1e-12));
                Assert.That(cell.Robots[r].Y, Is.EqualTo(y[r]).Within(1e-12));
            }
        }

        /// <summary>
        /// A dropped part must be recovered rather than abandoned, otherwise the cell would
        /// slowly lose its stock.
        /// </summary>
        [Test]
        public void DroppedPartsAreRecovered()
        {
            var cell = new CellChoreographer(seed: 7, faultProbability: 1.0);
            bool sawDrop = false;

            for (int i = 0; i < 4000; i++)
            {
                cell.Advance(Step);
                foreach (CellPart part in cell.Parts)
                {
                    sawDrop |= part.OnFloor;
                }
            }

            Assert.That(sawDrop, Is.True, "The forced fault never dropped a part.");
            Assert.That(cell.Kpis.FaultCount, Is.GreaterThan(0));
            Assert.That(cell.Parts, Has.Count.EqualTo(3));
        }

        [Test]
        public void BatteryDrainsAndRecharges()
        {
            var cell = new CellChoreographer();
            double lowest = 100.0;
            bool charged = false;

            for (int i = 0; i < LongRunSteps * 2; i++)
            {
                cell.Advance(Step);
                foreach (RobotAgent robot in cell.Robots)
                {
                    lowest = Math.Min(lowest, robot.BatteryLevel);
                    charged |= robot.State == RobotCycleState.Charging;
                }
            }

            Assert.That(lowest, Is.LessThan(100.0), "The battery never drained.");
            Assert.That(charged, Is.True, "No robot ever docked to charge.");
        }

        private static CellPart FindPart(CellChoreographer cell, string id)
        {
            foreach (CellPart part in cell.Parts)
            {
                if (string.Equals(part.Id, id, StringComparison.Ordinal))
                {
                    return part;
                }
            }
            throw new InvalidOperationException($"Part {id} is missing.");
        }
    }
}
