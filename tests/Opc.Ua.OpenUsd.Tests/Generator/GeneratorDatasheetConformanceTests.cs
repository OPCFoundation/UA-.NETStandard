/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using Generators;
using GeneratorModel = Opc.Ua.Generators;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Tests.Generator
{
    /// <summary>
    /// Asserts that the simulation model and the published datasheet agree.
    /// </summary>
    /// <remarks>
    /// The point of the sample's design is that load fraction is the only
    /// independent variable, so every other published quantity is a function of it.
    /// These tests hold that claim to account: if a curve constant is edited without
    /// updating <c>DATASHEET.md</c>, or a derived quantity is turned into an
    /// independent one, the identities below stop holding.
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorDatasheetConformanceTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>
        /// Rated speed follows from the pole count and output frequency.
        /// </summary>
        [Test]
        public void RatedSpeedFollowsFromPolesAndFrequency()
        {
            Assert.That(GeneratorDatasheet.Engine.RatedSpeedRpm, Is.EqualTo(1500.0).Within(Tolerance));
        }

        /// <summary>
        /// Rated current is derived from the rating rather than tabulated.
        /// </summary>
        [Test]
        public void RatedCurrentFollowsFromApparentPowerAndVoltage()
        {
            double expected = GeneratorDatasheet.Ratings.PrimeApparentPowerVoltAmperes
                / (Math.Sqrt(3.0) * GeneratorDatasheet.Electrical.RatedLineVoltage);
            Assert.That(
                GeneratorDatasheet.Electrical.RatedCurrentAmperes,
                Is.EqualTo(expected).Within(Tolerance));
            Assert.That(GeneratorDatasheet.Electrical.RatedCurrentAmperes, Is.EqualTo(721.7).Within(0.1));
        }

        /// <summary>
        /// The fuel curve reproduces the datasheet's published consumption table.
        /// </summary>
        [TestCase(0.50, 53.7)]
        [TestCase(0.75, 78.7)]
        [TestCase(1.00, 103.7)]
        public void FuelCurveReproducesThePublishedTable(double load, double expectedLitresPerHour)
        {
            Assert.That(
                GeneratorDatasheet.Curves.FuelLitresPerHour(load),
                Is.EqualTo(expectedLitresPerHour).Within(0.05));
        }

        /// <summary>
        /// Efficiency reconciles with the published power and fuel rate.
        /// </summary>
        /// <remarks>
        /// This is the identity the whole model rests on:
        /// <c>eta = P / (Vdot * rho * LHV)</c>. Because both P and Vdot are
        /// functions of the same load fraction, it cannot drift.
        /// </remarks>
        [TestCase(0.10)]
        [TestCase(0.25)]
        [TestCase(0.50)]
        [TestCase(0.75)]
        [TestCase(1.00)]
        [TestCase(1.10)]
        public void EfficiencyReconcilesWithPowerAndFuelRate(double load)
        {
            double outputKilowatts = load * GeneratorDatasheet.Ratings.PrimePowerWatts / 1000.0;
            double inputKilowatts = GeneratorDatasheet.Curves.FuelLitresPerHour(load)
                * GeneratorDatasheet.Fuel.EnergyPerLitreKilowattHours;

            Assert.That(
                GeneratorDatasheet.Curves.EfficiencyPercent(load),
                Is.EqualTo(100.0 * outputKilowatts / inputKilowatts).Within(Tolerance));
        }

        /// <summary>
        /// Efficiency stays physical across the whole simulated range.
        /// </summary>
        /// <remarks>
        /// A curve fit that produces a negative or above-unity efficiency somewhere
        /// in its range is not a model, it is a coincidence that happens to look
        /// right at the duty point.
        /// </remarks>
        [Test]
        public void EfficiencyStaysPhysicalAndRisesWithLoad()
        {
            double previous = 0.0;
            for (double load = 0.05; load <= 1.10; load += 0.05)
            {
                double efficiency = GeneratorDatasheet.Curves.EfficiencyPercent(load);
                Assert.Multiple(() =>
                {
                    Assert.That(efficiency, Is.GreaterThan(0.0), $"at {load:P0}");
                    Assert.That(efficiency, Is.LessThan(100.0), $"at {load:P0}");
                });
                Assert.That(efficiency, Is.GreaterThan(previous), $"efficiency fell at {load:P0}");
                previous = efficiency;
            }
        }

        /// <summary>
        /// A no-load set consumes fuel but produces no efficiency figure.
        /// </summary>
        /// <remarks>
        /// Guards the divide-by-zero the identity would otherwise hit at zero load.
        /// </remarks>
        [Test]
        public void EfficiencyIsZeroWhenTheSetProducesNoPower()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GeneratorDatasheet.Curves.EfficiencyPercent(0.0), Is.Zero);
                Assert.That(GeneratorDatasheet.Curves.EfficiencyPercent(-0.1), Is.Zero);
                Assert.That(
                    GeneratorDatasheet.Curves.FuelLitresPerHour(0.0),
                    Is.EqualTo(GeneratorDatasheet.Curves.FuelNoLoadLitresPerHour).Within(Tolerance));
            });
        }

        /// <summary>
        /// Thermal curves stay inside the published engineering ranges.
        /// </summary>
        [Test]
        public void ThermalCurvesStayWithinTheirRanges()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    GeneratorDatasheet.Curves.CoolantCelsius(0.0),
                    Is.EqualTo(GeneratorDatasheet.Engine.ThermostatCelsius).Within(Tolerance));
                Assert.That(
                    GeneratorDatasheet.Curves.CoolantCelsius(1.0),
                    Is.EqualTo(95.0).Within(Tolerance));
                Assert.That(
                    GeneratorDatasheet.Curves.CoolantCelsius(1.0),
                    Is.LessThan(GeneratorDatasheet.TripPoints.HighCoolantCelsius),
                    "A set at full load must not sit on its own shutdown trip.");
                Assert.That(
                    GeneratorDatasheet.Curves.ExhaustCelsius(1.0),
                    Is.LessThan(GeneratorDatasheet.Ranges.ExhaustMaxCelsius));
            });
        }

        /// <summary>
        /// Trip points sit outside the normal operating envelope.
        /// </summary>
        /// <remarks>
        /// A protection that trips during normal running is not a protection.
        /// </remarks>
        [Test]
        public void TripPointsSitOutsideNormalOperation()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    GeneratorDatasheet.TripPoints.OverspeedRpm,
                    Is.GreaterThan(GeneratorDatasheet.Engine.RatedSpeedRpm));
                Assert.That(
                    GeneratorDatasheet.TripPoints.LowOilPressureBar,
                    Is.LessThan(GeneratorDatasheet.Engine.RatedOilPressureBar));
                Assert.That(GeneratorDatasheet.TripPoints.OverloadFraction, Is.GreaterThan(1.0));
            });
        }

        /// <summary>
        /// The datasheet document and the constants carry the same figures.
        /// </summary>
        /// <remarks>
        /// The document is the published artefact and the constants are what the
        /// server actually serves; a sample whose datasheet describes a different
        /// machine from the one it simulates is worse than no datasheet.
        /// </remarks>
        [Test]
        public void DatasheetDocumentAgreesWithTheConstants()
        {
            string path = FindDatasheet();
            string text = File.ReadAllText(path);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(GeneratorDatasheet.Identity.Manufacturer));
                Assert.That(text, Does.Contain(GeneratorDatasheet.Identity.Model));
                Assert.That(text, Does.Contain(GeneratorDatasheet.Identity.ProductCode));
                Assert.That(text, Does.Contain("400 kW"), "prime power");
                Assert.That(text, Does.Contain("440 kW"), "standby power");
                Assert.That(text, Does.Contain("721.7 A"), "rated current");
                Assert.That(text, Does.Contain("1500 min"), "rated speed");
                Assert.That(text, Does.Contain("12.5 L"), "displacement");
                Assert.That(text, Does.Contain("9.868 kWh/L"), "fuel energy density");
                Assert.That(text, Does.Contain("3.67 + 100.00"), "fuel curve");
            });
        }

        /// <summary>
        /// Locates DATASHEET.md by walking up from the test output directory.
        /// </summary>
        /// <returns>The full path to the datasheet.</returns>
        private static string FindDatasheet()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(
                    dir.FullName, "samples", "OpenUsd", "GeneratorServer", "DATASHEET.md");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            Assert.Fail("DATASHEET.md could not be located from the test output directory.");
            return string.Empty;
        }
    }
}
