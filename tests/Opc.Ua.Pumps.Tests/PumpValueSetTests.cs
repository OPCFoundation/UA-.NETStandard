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

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// The value set is what makes the open OPC 40223 groups readable without
    /// a property per variable, so its lookup and coercion behaviour is the
    /// contract every group accessor rests on.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpValueSetTests
    {
        private static PumpValue Value(string name, Variant value, StatusCode? status = null)
        {
            return new PumpValue
            {
                NodeId = new NodeId(1, 1),
                Name = name,
                Value = value,
                StatusCode = status ?? StatusCodes.Good
            };
        }

        [Test]
        public void AbsentNamesReadAsNullRatherThanThrowing()
        {
            var set = new PumpValueSet(new NodeId(1, 1), []);

            Assert.Multiple(() =>
            {
                Assert.That(set.IsEmpty, Is.True);
                Assert.That(set[BrowseNames.MassFlow], Is.Null);
                Assert.That(set.GetDouble(BrowseNames.MassFlow), Is.Null);
                Assert.That(set.GetBoolean(BrowseNames.Cavitation), Is.Null);
                Assert.That(set.GetString(BrowseNames.Fluid), Is.Null);
                Assert.That(set.Contains(BrowseNames.MassFlow), Is.False);
            });
        }

        [Test]
        public void ValuesAreFoundByTheirGeneratedBrowseName()
        {
            var set = new PumpValueSet(
                new NodeId(1, 1),
                [
                    Value(BrowseNames.MassFlow, Variant.From(12.5)),
                    Value(BrowseNames.Cavitation, Variant.From(true)),
                    Value(BrowseNames.NumberOfStarts, Variant.From(42u)),
                    Value(BrowseNames.Fluid, Variant.From("water"))
                ]);

            Assert.Multiple(() =>
            {
                Assert.That(set, Has.Count.EqualTo(4));
                Assert.That(
                    set.Names.ToArray(),
                    Is.EquivalentTo(new[]
                    {
                        BrowseNames.MassFlow,
                        BrowseNames.Cavitation,
                        BrowseNames.NumberOfStarts,
                        BrowseNames.Fluid
                    }));
                Assert.That(set.GetDouble(BrowseNames.MassFlow), Is.EqualTo(12.5));
                Assert.That(set.GetBoolean(BrowseNames.Cavitation), Is.True);
                Assert.That(set.GetUInt32(BrowseNames.NumberOfStarts), Is.EqualTo(42u));
                Assert.That(set.GetString(BrowseNames.Fluid), Is.EqualTo("water"));
            });
        }

        [Test]
        public void ABadStatusReadsAsNoValue()
        {
            // A server that publishes a variable it cannot currently measure
            // reports a bad status. Handing the stale number back as if it
            // were a reading is the failure mode worth guarding.
            var set = new PumpValueSet(
                new NodeId(1, 1),
                [Value(BrowseNames.MassFlow, Variant.From(12.5), StatusCodes.BadNoData)]);

            Assert.Multiple(() =>
            {
                Assert.That(set.Contains(BrowseNames.MassFlow), Is.True);
                Assert.That(set.GetDouble(BrowseNames.MassFlow), Is.Null);
            });
        }

        [Test]
        public void IntegerReadingsCoerceToDouble()
        {
            // OPC 40223 types most readings Double, but a server is free to
            // publish a narrower type, and a caller asking for a flow rate
            // should not have to care which.
            var set = new PumpValueSet(
                new NodeId(1, 1),
                [
                    Value(BrowseNames.MassFlow, Variant.From(12.0f)),
                    Value(BrowseNames.Speed, Variant.From(1500))
                ]);

            Assert.Multiple(() =>
            {
                Assert.That(set.GetDouble(BrowseNames.MassFlow), Is.EqualTo(12.0));
                Assert.That(set.GetDouble(BrowseNames.Speed), Is.EqualTo(1500.0));
            });
        }

        [Test]
        public void EngineeringUnitsTravelWithTheReading()
        {
            var value = new PumpValue
            {
                NodeId = new NodeId(1, 1),
                Name = BrowseNames.DifferentialPressure,
                Value = Variant.From(350_000.0),
                StatusCode = StatusCodes.Good,
                EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("Pa") },
                EuRange = new Range { Low = 0, High = 1_000_000 }
            };
            var set = new PumpValueSet(new NodeId(1, 1), [value]);

            PumpValue? read = set[BrowseNames.DifferentialPressure];
            Assert.That(read, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(read!.AsDouble(), Is.EqualTo(350_000.0));
                Assert.That(read.EngineeringUnits!.DisplayName.Text, Is.EqualTo("Pa"));
                Assert.That(read.EuRange!.High, Is.EqualTo(1_000_000));
            });
        }

        [Test]
        public void SupervisionReportsOnlyTheSignalsThatAreRaised()
        {
            var status = new PumpSupervisionStatus
            {
                NodeId = new NodeId(1, 1),
                ProcessFluid = new PumpValueSet(
                    new NodeId(2, 1),
                    [
                        Value(BrowseNames.Cavitation, Variant.From(true)),
                        Value(BrowseNames.Dry, Variant.From(false))
                    ]),
                PumpOperation = new PumpValueSet(
                    new NodeId(3, 1),
                    [
                        Value(BrowseNames.MotorOverheat, Variant.From(false)),
                        Value(BrowseNames.Leakage, Variant.From(true))
                    ])
            };

            var raised = new List<string>();
            foreach (KeyValuePair<string, PumpValue> signal in status.ActiveSignals)
            {
                raised.Add(signal.Key + "/" + signal.Value.Name);
            }

            Assert.Multiple(() =>
            {
                Assert.That(status.HasActiveSignals, Is.True);
                Assert.That(raised, Has.Count.EqualTo(2));
                Assert.That(
                    raised,
                    Does.Contain(BrowseNames.SupervisionProcessFluid + "/" +
                        BrowseNames.Cavitation));
                Assert.That(
                    raised,
                    Does.Contain(BrowseNames.SupervisionPumpOperation + "/" +
                        BrowseNames.Leakage));
            });
        }

        [Test]
        public void AHealthyPumpRaisesNoSupervisionSignals()
        {
            var status = new PumpSupervisionStatus
            {
                NodeId = new NodeId(1, 1),
                ProcessFluid = new PumpValueSet(
                    new NodeId(2, 1),
                    [Value(BrowseNames.Cavitation, Variant.From(false))])
            };

            Assert.Multiple(() =>
            {
                Assert.That(status.HasActiveSignals, Is.False);
                Assert.That(status.ActiveSignals, Is.Empty);
                Assert.That(status.Groups, Is.Not.Empty);
            });
        }
    }
}
