// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.ECM.Server;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Pins mode construction, physical quantity validation and reserved identifier boundaries.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmEnergySavingModeTests
    {
        /// <summary>
        /// Each constructor argument retains its distinct value and unit domain.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRetainsEveryCharacteristic(bool dynamicData)
        {
            var mode = new EcmEnergySavingMode(
                7, "Standby", 11000, 2000, 3000, 40000, 5000, 6f, 0.7f, 0.8f, dynamicData);

            Assert.That(mode.Id, Is.EqualTo(7));
            Assert.That(mode.Name, Is.EqualTo("Standby"));
            Assert.That(mode.TimeMinPause, Is.EqualTo(11000));
            Assert.That(mode.TimeToPause, Is.EqualTo(2000));
            Assert.That(mode.TimeMinLengthOfStay, Is.EqualTo(3000));
            Assert.That(mode.TimeMaxLengthOfStay, Is.EqualTo(40000));
            Assert.That(mode.RegularTimeToOperate, Is.EqualTo(5000));
            Assert.That(mode.ModePowerConsumption, Is.EqualTo(6f));
            Assert.That(mode.EnergyConsumptionToPause, Is.EqualTo(0.7f));
            Assert.That(mode.EnergyConsumptionToOperate, Is.EqualTo(0.8f));
            Assert.That(mode.DynamicData, Is.EqualTo(dynamicData));
        }

        /// <summary>
        /// Reserved state identifiers cannot be used for configured energy-saving modes.
        /// </summary>
        [TestCase(0)]
        [TestCase(0xF0)]
        [TestCase(0xFE)]
        [TestCase(0xFF)]
        public void ReservedIdentifiersAreRejected(byte id)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EcmEnergySavingMode(id, "Pause", 0, 0, 0, 0, 0, 0, 0, 0))!;

            Assert.That(error.ParamName, Is.EqualTo("id"));
        }

        /// <summary>
        /// Mode identifiers immediately adjacent to reserved identifiers remain available.
        /// </summary>
        [TestCase(1)]
        [TestCase(0xEF)]
        [TestCase(0xF1)]
        [TestCase(0xFD)]
        public void NonreservedIdentifiersAndZeroQuantitiesAreAccepted(byte id)
        {
            var mode = new EcmEnergySavingMode(id, "Pause", 0, 0, 0, 0, 0, 0, 0, 0);

            Assert.That(mode.Id, Is.EqualTo(id));
            Assert.That(mode.TimeMinPause, Is.Zero);
            Assert.That(mode.TimeToPause, Is.Zero);
            Assert.That(mode.TimeMinLengthOfStay, Is.Zero);
            Assert.That(mode.TimeMaxLengthOfStay, Is.Zero);
            Assert.That(mode.RegularTimeToOperate, Is.Zero);
            Assert.That(mode.ModePowerConsumption, Is.Zero);
            Assert.That(mode.EnergyConsumptionToPause, Is.Zero);
            Assert.That(mode.EnergyConsumptionToOperate, Is.Zero);
        }

        /// <summary>
        /// Names must supply a nonempty BrowseName.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void MissingNamesAreRejected(string? name)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                new EcmEnergySavingMode(1, name!, 0, 0, 0, 0, 0, 0, 0, 0))!;

            Assert.That(error.ParamName, Is.EqualTo("name"));
        }

        /// <summary>
        /// Every physical quantity rejects negative and nonfinite values independently.
        /// </summary>
        [Test]
        [Combinatorial]
        public void InvalidPhysicalQuantitiesIdentifyTheirArgument(
            [Values("timeMinPause", "timeToPause", "timeMinLengthOfStay", "timeMaxLengthOfStay",
                "regularTimeToOperate", "modePowerConsumption", "energyConsumptionToPause",
                "energyConsumptionToOperate")] string parameter,
            [Values(-1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity)] double value)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EcmEnergySavingMode(
                    1,
                    "Pause",
                    parameter == "timeMinPause" ? value : 0,
                    parameter == "timeToPause" ? value : 0,
                    parameter == "timeMinLengthOfStay" ? value : 0,
                    parameter == "timeMaxLengthOfStay" ? value : 0,
                    parameter == "regularTimeToOperate" ? value : 0,
                    parameter == "modePowerConsumption" ? (float)value : 0,
                    parameter == "energyConsumptionToPause" ? (float)value : 0,
                    parameter == "energyConsumptionToOperate" ? (float)value : 0))!;

            Assert.That(error.ParamName, Is.EqualTo(parameter));
        }

        /// <summary>
        /// The maximum stay accepts equality but rejects a value immediately below the minimum.
        /// </summary>
        [TestCase(9.999, false)]
        [TestCase(10, true)]
        [TestCase(10.001, true)]
        public void MaximumStayIncludesTheMinimumBoundary(double maximum, bool accepted)
        {
            if (!accepted)
            {
                ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new EcmEnergySavingMode(1, "Pause", 0, 0, 10, maximum, 0, 0, 0, 0))!;
                Assert.That(error.ParamName, Is.EqualTo("timeMaxLengthOfStay"));
                return;
            }
            var mode = new EcmEnergySavingMode(1, "Pause", 0, 0, 10, maximum, 0, 0, 0, 0);

            Assert.That(mode.TimeMinLengthOfStay, Is.EqualTo(10));
            Assert.That(mode.TimeMaxLengthOfStay, Is.EqualTo(maximum));
        }
    }
}
