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
using Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// The predefined OPC 40001-3 parameters of a job order, read from its
    /// ISA-95 <c>JobOrderParameters</c>.
    /// </summary>
    /// <remarks>
    /// OPC 40001-3 §7.2 Table 1 predefines these key-value pairs and marks
    /// which may appear in a job order ("In"). Every one is optional, so
    /// <see langword="null"/> — or an empty array — means the order does not
    /// carry it, and a value of the wrong type counts as not carried. Anything
    /// the table does not predefine stays reachable through
    /// <see cref="Parameters"/>, together with the descriptions and
    /// engineering units the typed properties leave out.
    /// </remarks>
    public sealed record MachineryJobOrderParameters
    {
        /// <summary>
        /// Human-readable name of the job, possibly in several languages.
        /// </summary>
        public ArrayOf<LocalizedText> JobName { get; init; }

        /// <summary>
        /// The company-internal ERP orders the job order belongs to.
        /// </summary>
        public ArrayOf<string> OrderNumbers { get; init; }

        /// <summary>
        /// The customers of the job order.
        /// </summary>
        public ArrayOf<string> Customers { get; init; }

        /// <summary>
        /// The customer orders the job order belongs to.
        /// </summary>
        public ArrayOf<string> CustomerOrderNumbers { get; init; }

        /// <summary>
        /// The execution mode of the machine for the job order.
        /// </summary>
        public JobExecutionMode? JobExecutionMode { get; init; }

        /// <summary>
        /// Why the last state change happened.
        /// </summary>
        public LocalizedText? ReasonForStateChange { get; init; }

        /// <summary>
        /// The number of runs to perform; zero when the machine cannot know it,
        /// as in continuous production.
        /// </summary>
        public uint? RunsPlanned { get; init; }

        /// <summary>
        /// The planned pure production time, in milliseconds.
        /// </summary>
        public double? PlannedProductionTime { get; init; }

        /// <summary>
        /// The planned setup time before production starts, in milliseconds.
        /// </summary>
        public double? PlannedSetupTime { get; init; }

        /// <summary>
        /// The planned time for one run, in milliseconds.
        /// </summary>
        public double? PlannedTimePerRun { get; init; }

        /// <summary>
        /// The planned quantity one run produces.
        /// </summary>
        public double? PlannedQuantityPerRun { get; init; }

        /// <summary>
        /// The planned quantity of the whole order (lot size).
        /// </summary>
        public double? PlannedOrderQuantity { get; init; }

        /// <summary>
        /// Whether the machine continues past the nominal output. Table 1 makes
        /// it a sub-parameter of <c>PlannedOrderQuantity</c>; a top-level entry
        /// is accepted as well.
        /// </summary>
        public bool? Overproduction { get; init; }

        /// <summary>
        /// The planned duration of the order including setup and delays, in
        /// milliseconds.
        /// </summary>
        public double? PlannedDuration { get; init; }

        /// <summary>
        /// Operator-facing notes on the equipment the order needs.
        /// </summary>
        public ArrayOf<LocalizedText> JobAnnotation { get; init; }

        /// <summary>
        /// Every parameter the job order carries, predefined or not.
        /// </summary>
        public ArrayOf<V2.ISA95ParameterDataType> Parameters { get; init; }

        /// <summary>
        /// Reads the predefined parameters of a job order.
        /// </summary>
        /// <param name="jobOrder">The job order.</param>
        public static MachineryJobOrderParameters FromJobOrder(V2.ISA95JobOrderDataType jobOrder)
        {
            if (jobOrder == null)
            {
                throw new ArgumentNullException(nameof(jobOrder));
            }
            var parameters = new MachineryJobParameterLookup(jobOrder.JobOrderParameters);
            V2.ISA95ParameterDataType? orderQuantity = parameters.Find("PlannedOrderQuantity");
            return new MachineryJobOrderParameters
            {
                JobName = parameters.Texts("JobName"),
                OrderNumbers = parameters.Strings("OrderNumbers"),
                Customers = parameters.Strings("Customers"),
                CustomerOrderNumbers = parameters.Strings("CustomerOrderNumbers"),
                JobExecutionMode = (Jobs.JobExecutionMode?)parameters.Enumeration("JobExecutionMode"),
                ReasonForStateChange = parameters.Text("ReasonForStateChange"),
                RunsPlanned = parameters.UInt32("RunsPlanned"),
                PlannedProductionTime = parameters.Double("PlannedProductionTime"),
                PlannedSetupTime = parameters.Double("PlannedSetupTime"),
                PlannedTimePerRun = parameters.Double("PlannedTimePerRun"),
                PlannedQuantityPerRun = parameters.Double("PlannedQuantityPerRun"),
                PlannedOrderQuantity = parameters.Double("PlannedOrderQuantity"),
                Overproduction = new MachineryJobParameterLookup(
                    orderQuantity?.Subparameters ?? default).Boolean("Overproduction") ??
                    parameters.Boolean("Overproduction"),
                PlannedDuration = parameters.Double("PlannedDuration"),
                JobAnnotation = parameters.Texts("JobAnnotation"),
                Parameters = jobOrder.JobOrderParameters
            };
        }
    }

    /// <summary>
    /// The predefined OPC 40001-3 parameters of a job response, read from its
    /// ISA-95 <c>JobResponseData</c>.
    /// </summary>
    /// <remarks>
    /// OPC 40001-3 §7.2 Table 1 predefines these key-value pairs and marks
    /// which may appear in a job response ("Out"). Every one is optional, so
    /// <see langword="null"/> — or an empty array — means the response does not
    /// carry it. <see cref="Parameters"/> keeps everything as received.
    /// </remarks>
    public sealed record MachineryJobResponseParameters
    {
        /// <summary>
        /// Human-readable name of the job, possibly in several languages.
        /// </summary>
        public ArrayOf<LocalizedText> JobName { get; init; }

        /// <summary>
        /// The company-internal ERP orders the output belongs to.
        /// </summary>
        public ArrayOf<string> OrderNumbers { get; init; }

        /// <summary>
        /// The customers of the job order.
        /// </summary>
        public ArrayOf<string> Customers { get; init; }

        /// <summary>
        /// The customer orders the job order belongs to.
        /// </summary>
        public ArrayOf<string> CustomerOrderNumbers { get; init; }

        /// <summary>
        /// The execution mode the machine ran the job order in.
        /// </summary>
        public JobExecutionMode? JobExecutionMode { get; init; }

        /// <summary>
        /// Why the last state change happened.
        /// </summary>
        public LocalizedText? ReasonForStateChange { get; init; }

        /// <summary>
        /// How many runs have completed.
        /// </summary>
        public uint? RunsCompleted { get; init; }

        /// <summary>
        /// How many of the planned runs have started.
        /// </summary>
        public uint? RunsStarted { get; init; }

        /// <summary>
        /// The quantity produced in the current run.
        /// </summary>
        public double? ActualQuantityCurrentRun { get; init; }

        /// <summary>
        /// The time since production of the order started, in milliseconds.
        /// </summary>
        public double? ActualUnitBusyTime { get; init; }

        /// <summary>
        /// The time spent on setup, in milliseconds.
        /// </summary>
        public double? ActualUnitSetupTime { get; init; }

        /// <summary>
        /// The time lost to delays, in milliseconds.
        /// </summary>
        public double? ActualUnitDelayTime { get; init; }

        /// <summary>
        /// The value-adding production time, in milliseconds.
        /// </summary>
        public double? ActualProductionTime { get; init; }

        /// <summary>
        /// The produced quantity, good, scrap and rework together.
        /// </summary>
        public double? ProducedQuantity { get; init; }

        /// <summary>
        /// The estimated time left to complete the order, in milliseconds.
        /// </summary>
        public double? EstimatedRemainingTime { get; init; }

        /// <summary>
        /// The high-level result of executing the order.
        /// </summary>
        public JobResult? JobResult { get; init; }

        /// <summary>
        /// The produced quantity that meets the quality requirements.
        /// </summary>
        public double? GoodQuantity { get; init; }

        /// <summary>
        /// The bill of material of the produced output.
        /// </summary>
        public ArrayOf<BOMInformationDataType> AsBuiltBOM { get; init; }

        /// <summary>
        /// Performance information of the executed order.
        /// </summary>
        public ArrayOf<OutputPerformanceInfoDataType> OutputPerformanceInfo { get; init; }

        /// <summary>
        /// Every parameter the response carries, predefined or not.
        /// </summary>
        public ArrayOf<V2.ISA95ParameterDataType> Parameters { get; init; }

        /// <summary>
        /// Reads the predefined parameters of a job response.
        /// </summary>
        /// <param name="response">The job response.</param>
        public static MachineryJobResponseParameters FromJobResponse(
            V2.ISA95JobResponseDataType response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            var parameters = new MachineryJobParameterLookup(response.JobResponseData);
            return new MachineryJobResponseParameters
            {
                JobName = parameters.Texts("JobName"),
                OrderNumbers = parameters.Strings("OrderNumbers"),
                Customers = parameters.Strings("Customers"),
                CustomerOrderNumbers = parameters.Strings("CustomerOrderNumbers"),
                JobExecutionMode = (Jobs.JobExecutionMode?)parameters.Enumeration("JobExecutionMode"),
                ReasonForStateChange = parameters.Text("ReasonForStateChange"),
                RunsCompleted = parameters.UInt32("RunsCompleted"),
                RunsStarted = parameters.UInt32("RunsStarted"),
                ActualQuantityCurrentRun = parameters.Double("ActualQuantityCurrentRun"),
                ActualUnitBusyTime = parameters.Double("ActualUnitBusyTime"),
                ActualUnitSetupTime = parameters.Double("ActualUnitSetupTime"),
                ActualUnitDelayTime = parameters.Double("ActualUnitDelayTime"),
                ActualProductionTime = parameters.Double("ActualProductionTime"),
                ProducedQuantity = parameters.Double("ProducedQuantity"),
                EstimatedRemainingTime = parameters.Double("EstimatedRemainingTime"),
                JobResult = (Jobs.JobResult?)parameters.Enumeration("JobResult"),
                GoodQuantity = parameters.Double("GoodQuantity"),
                AsBuiltBOM = parameters.Structures<BOMInformationDataType>("AsBuiltBOM"),
                OutputPerformanceInfo = parameters
                    .Structures<OutputPerformanceInfoDataType>("OutputPerformanceInfo"),
                Parameters = response.JobResponseData
            };
        }
    }

    /// <summary>
    /// Looks predefined parameters up by their <c>ID</c> and reads them with
    /// the type OPC 40001-3 declares.
    /// </summary>
    internal readonly struct MachineryJobParameterLookup
    {
        public MachineryJobParameterLookup(ArrayOf<V2.ISA95ParameterDataType> parameters)
        {
            m_byId = new Dictionary<string, V2.ISA95ParameterDataType>(StringComparer.Ordinal);
            for (int ii = 0; ii < parameters.Count; ii++)
            {
                V2.ISA95ParameterDataType? parameter = parameters[ii];
                // The first entry wins: an ID that repeats is the sender's
                // mistake, and the earliest one is what a reader meets first.
                if (parameter?.ID != null && !m_byId.ContainsKey(parameter.ID))
                {
                    m_byId[parameter.ID] = parameter;
                }
            }
        }

        public V2.ISA95ParameterDataType? Find(string id)
        {
            return m_byId.TryGetValue(id, out V2.ISA95ParameterDataType? parameter)
                ? parameter
                : null;
        }

        public double? Double(string id)
        {
            // Duration is a Double on the wire; a sender that used Float is
            // still read.
            Variant value = ValueOf(id);
            if (value.TryGetValue(out double number))
            {
                return number;
            }
            return value.TryGetValue(out float single) ? single : null;
        }

        public uint? UInt32(string id)
        {
            return ValueOf(id).TryGetValue(out uint number) ? number : null;
        }

        public bool? Boolean(string id)
        {
            return ValueOf(id).TryGetValue(out bool flag) ? flag : null;
        }

        public LocalizedText? Text(string id)
        {
            return ValueOf(id).TryGetValue(out LocalizedText text) ? text : null;
        }

        public ArrayOf<LocalizedText> Texts(string id)
        {
            return ValueOf(id).TryGetValue(out ArrayOf<LocalizedText> texts)
                ? texts
                : ArrayOf<LocalizedText>.Empty;
        }

        public ArrayOf<string> Strings(string id)
        {
            return ValueOf(id).TryGetValue(out ArrayOf<string> texts)
                ? texts
                : ArrayOf<string>.Empty;
        }

        /// <summary>
        /// Reads an enumeration the series defines. Enumerations travel as
        /// Int32 on the wire; the caller casts to its enumeration type.
        /// </summary>
        public int? Enumeration(string id)
        {
            Variant value = ValueOf(id);
            if (value.TryGetValue(out int number))
            {
                return number;
            }
            return value.TryGetValue(out EnumValue enumValue) ? enumValue.Value : null;
        }

        public ArrayOf<T> Structures<T>(string id) where T : IEncodeable
        {
            return ValueOf(id).TryGetStructure(out ArrayOf<T> structures)
                ? structures
                : ArrayOf<T>.Empty;
        }

        private Variant ValueOf(string id)
        {
            return m_byId != null && m_byId.TryGetValue(id, out V2.ISA95ParameterDataType? parameter)
                ? parameter.Value
                : Variant.Null;
        }

        private readonly Dictionary<string, V2.ISA95ParameterDataType> m_byId;
    }
}
