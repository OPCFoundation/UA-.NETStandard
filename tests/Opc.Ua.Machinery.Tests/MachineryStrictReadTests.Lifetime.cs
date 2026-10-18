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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Client;
using DiNames = Opc.Ua.Di.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Lifetime values and both mandatory bounds accept the model's scalar Number types.
        /// </summary>
        [TestCase(false, "Float")]
        [TestCase(false, "Int32")]
        [TestCase(false, "UInt64")]
        [TestCase(true, "Float")]
        [TestCase(true, "Int32")]
        [TestCase(true, "UInt64")]
        public async Task LifetimeFieldsAcceptScalarNumbers(bool equipment, string kind)
        {
            Variant value = kind switch
            {
                "Float" => Variant.From(5.0f),
                "Int32" => Variant.From(5),
                _ => Variant.From(5ul)
            };
            (ReadHarness harness, _) = CreateLifetimeHarness(equipment, value);

            MachineryLifetimeVariable result = await ReadLifetimeAsync(harness, equipment).ConfigureAwait(false);

            Assert.That(result.Remaining, Is.EqualTo(5.0));
            Assert.That(result.StartValue, Is.EqualTo(5.0));
            Assert.That(result.LimitValue, Is.EqualTo(5.0));
            Assert.That(result.Values.Count, Is.GreaterThanOrEqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(result.Values[i].Value.WrappedValue, Is.EqualTo(value));
            }
        }

        /// <summary>
        /// Missing mandatory bounds fail strict reads while the legacy constructor remains tolerant.
        /// </summary>
        [TestCase(false, "StartValue")]
        [TestCase(false, "LimitValue")]
        [TestCase(true, "StartValue")]
        [TestCase(true, "LimitValue")]
        public async Task LifetimeBoundsAreMandatoryOnlyInStrictReads(bool equipment, string missing)
        {
            (ReadHarness harness, NodeId source) = CreateLifetimeHarness(equipment, Variant.From(5.0), missing);
            MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await ReadLifetimeAsync(harness, equipment).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(error.ReadValue!.StartingNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.BrowsePath.Count, Is.EqualTo(1));
            Assert.That(error.ReadValue.BrowsePath[0].Name, Is.EqualTo(missing));
            Assert.That(error.ReadValue.IsAbsent, Is.True);

            MachineryLifetimeVariable legacy = await ReadLifetimeAsync(harness, equipment, strict: false)
                .ConfigureAwait(false);
            Assert.That(missing == "StartValue" ? legacy.StartValue : legacy.LimitValue, Is.Null);
        }

        /// <summary>
        /// A published Good-null mandatory bound is present and remains distinguishable from absence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PresentNullLifetimeBoundsRetainEvidence(bool equipment)
        {
            (ReadHarness harness, _) = CreateLifetimeHarness(equipment, Variant.Null);
            MachineryLifetimeVariable result = await ReadLifetimeAsync(harness, equipment).ConfigureAwait(false);
            Assert.That(result.Remaining, Is.Null);
            Assert.That(result.StartValue, Is.Null);
            Assert.That(result.LimitValue, Is.Null);
            Assert.That(result.Values.Count, Is.GreaterThanOrEqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(result.Values[i].IsAbsent, Is.False);
            }
        }

        /// <summary>
        /// Scalar and one-dimensional Number warnings retain their exact evidence without forced Double arrays.
        /// </summary>
        [TestCase("Double")]
        [TestCase("Float")]
        [TestCase("Int32")]
        [TestCase("FloatArray")]
        [TestCase("Int32Array")]
        public async Task WarningValuesAcceptModeledScalarAndNumericArrays(string kind)
        {
            Variant warning = kind switch
            {
                "Double" => Variant.From(2.0),
                "Float" => Variant.From(2.0f),
                "Int32" => Variant.From(2),
                "FloatArray" => Variant.From((ArrayOf<float>)[2.0f, 1.0f]),
                _ => Variant.From((ArrayOf<int>)[2, 1])
            };
            (ReadHarness harness, NodeId source) = CreateLifetimeHarness(false, Variant.From(5.0));
            QualifiedName name = Di(harness, DiNames.WarningValues);
            DataValue original = TimedValue(warning, StatusCodes.Good);
            harness.Add(source, [name], original);

            MachineryLifetimeVariable result = await ReadLifetimeAsync(harness, false).ConfigureAwait(false);

            Assert.That(result.WarningValues.Count, Is.Zero);
            Assert.That(Field(result.Values, name).Value, Is.EqualTo(original));
            Assert.That(Field(result.Values, name).IsAbsent, Is.False);
        }

        /// <summary>
        /// Non-Double warning arrays still obey the configured decoded-array limit.
        /// </summary>
        [Test]
        public void FloatWarningArraysCannotBypassTheReadLimit()
        {
            (ReadHarness harness, NodeId source) = CreateLifetimeHarness(false, Variant.From(5.0));
            harness.Add(source, [Di(harness, DiNames.WarningValues)],
                new DataValue(Variant.From((ArrayOf<float>)[3f, 2f, 1f])));
            MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await ReadLifetimeAsync(harness, false,
                    options: new MachineryClientOptions { MaximumReadArrayItems = 2 }).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(error.ReadValue!.BrowsePath.Count, Is.EqualTo(1));
            Assert.That(error.ReadValue.BrowsePath[0].Name, Is.EqualTo(DiNames.WarningValues));
        }

        /// <summary>
        /// Number compatibility does not admit StatusCode payloads or multidimensional warning matrices.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WarningValuesRejectWrongNumericRankAndStatusCode(bool matrix)
        {
            (ReadHarness harness, NodeId source) = CreateLifetimeHarness(false, Variant.From(5.0));
            Variant value = matrix
                ? Variant.From(new double[2, 2].ToMatrixOf())
                : Variant.From((StatusCode)StatusCodes.Good);
            harness.Add(source, [Di(harness, DiNames.WarningValues)], new DataValue(value));

            MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await ReadLifetimeAsync(harness, false).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(error.ReadValue!.Value.WrappedValue, Is.EqualTo(value));
        }

        private static (ReadHarness Harness, NodeId Source) CreateLifetimeHarness(
            bool equipment, Variant value, string? missing = null)
        {
            var harness = new ReadHarness();
            NodeId folder = harness.Add(harness.Machine,
                [Machinery(harness, equipment ? BrowseNames.MachineryEquipment : BrowseNames.LifetimeCounters)]);
            ReferenceDescription entry = Reference(harness, 1, equipment ? NodeClass.Object : NodeClass.Variable);
            NodeId source;
            if (equipment)
            {
                NodeId item = harness.Add(folder, [entry.BrowseName]);
                entry.NodeId = item;
                harness.Add(item, [Machinery(harness, BrowseNames.MachineryEquipmentTypeId)],
                    new DataValue(Variant.From("urn:equipment:test")));
                source = harness.Add(item, [Machinery(harness, BrowseNames.EquipmentLife)], new DataValue(value));
            }
            else
            {
                source = harness.Add(folder, [entry.BrowseName], new DataValue(value));
                entry.NodeId = source;
            }
            foreach (string field in new[] { DiNames.StartValue, DiNames.LimitValue })
            {
                if (field != missing)
                {
                    harness.Add(source, [Di(harness, field)], new DataValue(value));
                }
            }
            harness.Browse = _ => Page([entry]);
            return (harness, source);
        }

        private static async Task<MachineryLifetimeVariable> ReadLifetimeAsync(
            ReadHarness harness, bool equipment, bool strict = true, MachineryClientOptions? options = null)
        {
            MachineryClient client = harness.Client(options, strict);
            var values = new List<MachineryLifetimeVariable>();
            if (equipment)
            {
                await foreach (MachineryEquipmentItem item in client.EnumerateEquipmentAsync(harness.Machine))
                {
                    values.Add(item.EquipmentLife!);
                }
            }
            else
            {
                await foreach (MachineryLifetimeVariable item in client.ReadLifetimeCountersAsync(harness.Machine))
                {
                    values.Add(item);
                }
            }
            Assert.That(values, Has.Count.EqualTo(1));
            return values[0];
        }
    }
}
