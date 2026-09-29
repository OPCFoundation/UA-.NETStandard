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
using NUnit.Framework;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Covers how the client reads the predefined OPC 40001-3 §7.2 job
    /// parameters out of the ISA-95 payloads.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryJobParameterClientTests
    {
        [Test]
        public void EveryPredefinedJobOrderParameterIsRead()
        {
            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = "J-1",
                JobOrderParameters = new[]
                {
                    Parameter("JobName", Variant.From(Texts("Batch"))),
                    Parameter("OrderNumbers", Variant.From(Strings("ERP-1", "ERP-2"))),
                    Parameter("Customers", Variant.From(Strings("Acme"))),
                    Parameter("CustomerOrderNumbers", Variant.From(Strings("C-9"))),
                    Parameter("JobExecutionMode", Variant.From((int)JobExecutionMode.TestMode)),
                    Parameter("ReasonForStateChange", Variant.From(new LocalizedText("en", "Operator"))),
                    Parameter("RunsPlanned", Variant.From(4u)),
                    Parameter("PlannedProductionTime", Variant.From(1_000.0)),
                    Parameter("PlannedSetupTime", Variant.From(200.0)),
                    Parameter("PlannedTimePerRun", Variant.From(250.0)),
                    Parameter("PlannedQuantityPerRun", Variant.From(10.0)),
                    Parameter("PlannedOrderQuantity", Variant.From(40.0)),
                    Parameter("Overproduction", Variant.From(false)),
                    Parameter("PlannedDuration", Variant.From(1_500.0f)),
                    Parameter("JobAnnotation", Variant.From(Texts("Use die 7"))),
                    Parameter("VendorSpecific", Variant.From("kept"))
                }.ToArrayOf()
            };

            MachineryJobOrderParameters planned = MachineryJobOrderParameters.FromJobOrder(order);

            Assert.That(planned.JobName[0].Text, Is.EqualTo("Batch"));
            Assert.That(planned.OrderNumbers.Count, Is.EqualTo(2));
            Assert.That(planned.Customers[0], Is.EqualTo("Acme"));
            Assert.That(planned.CustomerOrderNumbers[0], Is.EqualTo("C-9"));
            Assert.That(planned.JobExecutionMode, Is.EqualTo(JobExecutionMode.TestMode));
            Assert.That(planned.ReasonForStateChange.Text, Is.EqualTo("Operator"));
            Assert.That(planned.RunsPlanned, Is.EqualTo(4u));
            Assert.That(planned.PlannedProductionTime, Is.EqualTo(1_000.0));
            Assert.That(planned.PlannedSetupTime, Is.EqualTo(200.0));
            Assert.That(planned.PlannedTimePerRun, Is.EqualTo(250.0));
            Assert.That(planned.PlannedQuantityPerRun, Is.EqualTo(10.0));
            Assert.That(planned.PlannedOrderQuantity, Is.EqualTo(40.0));
            Assert.That(
                planned.Overproduction,
                Is.False,
                "A top-level Overproduction is read when the sub-parameter is absent.");
            Assert.That(
                planned.PlannedDuration,
                Is.EqualTo(1_500.0),
                "A Duration sent as Float is still read.");
            Assert.That(planned.JobAnnotation[0].Text, Is.EqualTo("Use die 7"));
            Assert.That(planned.Parameters.Count, Is.EqualTo(16));
        }

        [Test]
        public void TheSubParameterOverproductionWinsAndMistypedValuesAreNotCarried()
        {
            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = "J-2",
                JobOrderParameters = new[]
                {
                    new V2.ISA95ParameterDataType
                    {
                        ID = "PlannedOrderQuantity",
                        Value = Variant.From(12.0),
                        Subparameters = new[]
                        {
                            Parameter("Overproduction", Variant.From(true))
                        }.ToArrayOf()
                    },
                    Parameter("Overproduction", Variant.From(false)),
                    Parameter("RunsPlanned", Variant.From("four")),
                    Parameter("RunsPlanned", Variant.From(8u)),
                    Parameter("JobExecutionMode", Variant.From(new EnumValue(2))),
                    Parameter("JobName", Variant.From("not an array")),
                    null!,
                    new V2.ISA95ParameterDataType { ID = null, Value = Variant.From(1) }
                }.ToArrayOf()
            };

            MachineryJobOrderParameters planned = MachineryJobOrderParameters.FromJobOrder(order);

            Assert.That(planned.Overproduction, Is.True);
            Assert.That(
                planned.RunsPlanned,
                Is.Null,
                "The first entry of a repeated ID is the one read, and it carries the wrong type.");
            Assert.That(planned.JobExecutionMode, Is.EqualTo(JobExecutionMode.ProductionMode));
            Assert.That(planned.JobName.Count, Is.Zero);
            Assert.That(planned.PlannedDuration, Is.Null);
            Assert.That(planned.ReasonForStateChange.IsNull, Is.True);
        }

        [Test]
        public void EveryPredefinedJobResponseParameterIsRead()
        {
            var bom = new BOMInformationDataType();
            var performance = new OutputPerformanceInfoDataType();
            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "R-1",
                JobOrderID = "J-1",
                JobResponseData = new[]
                {
                    Parameter("JobName", Variant.From(Texts("Batch"))),
                    Parameter("OrderNumbers", Variant.From(Strings("ERP-1"))),
                    Parameter("Customers", Variant.From(Strings("Acme"))),
                    Parameter("CustomerOrderNumbers", Variant.From(Strings("C-9"))),
                    Parameter("JobExecutionMode", Variant.From((int)JobExecutionMode.SimulationMode)),
                    Parameter("ReasonForStateChange", Variant.From(new LocalizedText("en", "Done"))),
                    Parameter("RunsCompleted", Variant.From(3u)),
                    Parameter("RunsStarted", Variant.From(4u)),
                    Parameter("ActualQuantityCurrentRun", Variant.From(5.0)),
                    Parameter("ActualUnitBusyTime", Variant.From(600.0)),
                    Parameter("ActualUnitSetupTime", Variant.From(60.0)),
                    Parameter("ActualUnitDelayTime", Variant.From(6.0)),
                    Parameter("ActualProductionTime", Variant.From(534.0)),
                    Parameter("ProducedQuantity", Variant.From(35.0)),
                    Parameter("EstimatedRemainingTime", Variant.From(120.0)),
                    Parameter("JobResult", Variant.From((int)JobResult.Unsuccessful)),
                    Parameter("GoodQuantity", Variant.From(33.0)),
                    Parameter("AsBuiltBOM", Variant.FromStructure(new[] { bom }.ToArrayOf())),
                    Parameter(
                        "OutputPerformanceInfo",
                        Variant.FromStructure(new[] { performance }.ToArrayOf()))
                }.ToArrayOf()
            };

            MachineryJobResponseParameters actual =
                MachineryJobResponseParameters.FromJobResponse(response);

            Assert.That(actual.JobName[0].Text, Is.EqualTo("Batch"));
            Assert.That(actual.OrderNumbers[0], Is.EqualTo("ERP-1"));
            Assert.That(actual.Customers[0], Is.EqualTo("Acme"));
            Assert.That(actual.CustomerOrderNumbers[0], Is.EqualTo("C-9"));
            Assert.That(actual.JobExecutionMode, Is.EqualTo(JobExecutionMode.SimulationMode));
            Assert.That(actual.ReasonForStateChange.Text, Is.EqualTo("Done"));
            Assert.That(actual.RunsCompleted, Is.EqualTo(3u));
            Assert.That(actual.RunsStarted, Is.EqualTo(4u));
            Assert.That(actual.ActualQuantityCurrentRun, Is.EqualTo(5.0));
            Assert.That(actual.ActualUnitBusyTime, Is.EqualTo(600.0));
            Assert.That(actual.ActualUnitSetupTime, Is.EqualTo(60.0));
            Assert.That(actual.ActualUnitDelayTime, Is.EqualTo(6.0));
            Assert.That(actual.ActualProductionTime, Is.EqualTo(534.0));
            Assert.That(actual.ProducedQuantity, Is.EqualTo(35.0));
            Assert.That(actual.EstimatedRemainingTime, Is.EqualTo(120.0));
            Assert.That(actual.JobResult, Is.EqualTo(JobResult.Unsuccessful));
            Assert.That(actual.GoodQuantity, Is.EqualTo(33.0));
            Assert.That(actual.AsBuiltBOM.Count, Is.EqualTo(1));
            Assert.That(actual.OutputPerformanceInfo.Count, Is.EqualTo(1));
            Assert.That(actual.Parameters.Count, Is.EqualTo(19));
        }

        [Test]
        public void AnEmptyResponseReadsAsNothingCarried()
        {
            MachineryJobResponseParameters actual = MachineryJobResponseParameters.FromJobResponse(
                new V2.ISA95JobResponseDataType { JobResponseID = "R-0", JobOrderID = "J-0" });

            Assert.That(actual.RunsCompleted, Is.Null);
            Assert.That(actual.JobResult, Is.Null);
            Assert.That(actual.AsBuiltBOM.Count, Is.Zero);
            Assert.That(actual.OutputPerformanceInfo.Count, Is.Zero);
            Assert.That(actual.JobName.Count, Is.Zero);
        }

        [Test]
        public void NullPayloadsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(
                () => MachineryJobOrderParameters.FromJobOrder(null!));
            Assert.Throws<ArgumentNullException>(
                () => MachineryJobResponseParameters.FromJobResponse(null!));
        }

        private static V2.ISA95ParameterDataType Parameter(string id, Variant value)
        {
            return new V2.ISA95ParameterDataType { ID = id, Value = value };
        }

        private static ArrayOf<LocalizedText> Texts(params string[] texts)
        {
            var result = new LocalizedText[texts.Length];
            for (int ii = 0; ii < texts.Length; ii++)
            {
                result[ii] = new LocalizedText("en", texts[ii]);
            }
            return result.ToArrayOf();
        }

        private static ArrayOf<string> Strings(params string[] values)
        {
            return values.ToArrayOf();
        }
    }
}
