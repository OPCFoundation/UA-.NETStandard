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
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Server.Jobs
{
    /// <summary>
    /// Where OPC 40001-3 allows a predefined job parameter to appear.
    /// </summary>
    public enum MachineryJobParameterScope
    {
        /// <summary>Only in a job order's <c>JobOrderParameters</c>.</summary>
        JobOrder,

        /// <summary>Only in a job response's parameters.</summary>
        JobResponse,

        /// <summary>In both.</summary>
        Both
    }

    /// <summary>
    /// One predefined OPC 40001-3 job parameter.
    /// </summary>
    /// <param name="Name">
    /// The parameter's <c>ID</c>, exactly as the specification spells it.
    /// </param>
    /// <param name="DataType">
    /// The built-in type of the value, or <see cref="BuiltInType.Null"/> for a
    /// parameter the specification types with a structure or enumeration of its
    /// own. Those are carried but not type-checked: the payload delivers them
    /// as an <c>ExtensionObject</c> whose body a server cannot decode without
    /// the sender's encoding context.
    /// </param>
    /// <param name="IsArray">Whether the value is an array.</param>
    /// <param name="Scope">Where the parameter may appear.</param>
    /// <param name="DeclaredType">
    /// The type as the specification writes it, for diagnostics.
    /// </param>
    public sealed record MachineryJobParameter(
        string Name,
        BuiltInType DataType,
        bool IsArray,
        MachineryJobParameterScope Scope,
        string DeclaredType);

    /// <summary>
    /// The predefined job parameters of OPC 40001-3 and the check that keeps a
    /// server honest about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 40001-3 §9 defines a fixed set of parameter <c>ID</c>s that travel
    /// inside the ISA-95 job order and job response payloads rather than as
    /// nodes in the address space. Each one has a conformance unit of its own —
    /// <c>Machinery Job Management Planned …</c> for a job order,
    /// <c>… Result …</c> for a response — and a server may only advertise a
    /// unit once it actually recognises the parameter and honours its declared
    /// type.
    /// </para>
    /// <para>
    /// Nothing here changes what a job order carries: an unknown parameter
    /// passes through untouched, which is what the series intends. What the
    /// check adds is that a <em>predefined</em> ID cannot arrive carrying the
    /// wrong type and still be accepted, because then the server would be
    /// advertising a unit it does not meet.
    /// </para>
    /// </remarks>
    public static class MachineryJobParameters
    {
        /// <summary>
        /// The predefined parameters, in the order OPC 40001-3 lists them.
        /// </summary>
        public static ArrayOf<MachineryJobParameter> All => s_all;

        /// <summary>
        /// Finds a predefined parameter by its <c>ID</c>.
        /// </summary>
        /// <param name="name">The parameter ID.</param>
        /// <returns>
        /// The definition, or <see langword="null"/> when the ID is not one of
        /// the predefined ones.
        /// </returns>
        public static MachineryJobParameter? Find(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            return s_byName.TryGetValue(name!, out MachineryJobParameter? parameter)
                ? parameter
                : null;
        }

        /// <summary>
        /// Throws when a predefined parameter appears with the wrong type or in
        /// the wrong direction.
        /// </summary>
        /// <param name="parameters">The parameters carried by the payload.</param>
        /// <param name="inJobOrder">
        /// Whether the payload is a job order rather than a job response.
        /// </param>
        /// <param name="subject">What is being checked, for the error message.</param>
        public static void Validate(
            ArrayOf<V2.ISA95ParameterDataType> parameters,
            bool inJobOrder,
            string subject)
        {
            for (int ii = 0; ii < parameters.Count; ii++)
            {
                Validate(parameters[ii], inJobOrder, subject);
            }
        }

        private static void Validate(
            V2.ISA95ParameterDataType? parameter,
            bool inJobOrder,
            string subject)
        {
            MachineryJobParameter? predefined = Find(parameter?.ID);
            if (predefined == null)
            {
                // Not predefined: OPC 40001-3 lets it travel untouched.
                return;
            }

            if (!Allows(predefined.Scope, inJobOrder))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "{0}: OPC 40001-3 defines the predefined parameter '{1}' only for " +
                    "a {2}, but it arrived in a {3}.",
                    subject,
                    predefined.Name,
                    predefined.Scope == MachineryJobParameterScope.JobOrder
                        ? "job order"
                        : "job response",
                    inJobOrder ? "job order" : "job response");
            }

            if (predefined.DataType == BuiltInType.Null)
            {
                // Typed by a structure or enumeration of the series' own; the
                // value arrives encoded and is carried through as it is.
                return;
            }

            Variant value = parameter!.Value;
            if (value.IsNull)
            {
                return;
            }

            bool isArray = value.TypeInfo.ValueRank != ValueRanks.Scalar;
            if (value.TypeInfo.BuiltInType != predefined.DataType ||
                isArray != predefined.IsArray)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "{0}: OPC 40001-3 declares the predefined parameter '{1}' as {2}{3}, " +
                    "but it arrived as {4}{5}.",
                    subject,
                    predefined.Name,
                    predefined.DeclaredType,
                    predefined.IsArray ? "[]" : string.Empty,
                    value.TypeInfo.BuiltInType,
                    isArray ? "[]" : string.Empty);
            }
        }

        private static bool Allows(MachineryJobParameterScope scope, bool inJobOrder)
        {
            return scope == MachineryJobParameterScope.Both ||
                (inJobOrder
                    ? scope == MachineryJobParameterScope.JobOrder
                    : scope == MachineryJobParameterScope.JobResponse);
        }

        private static readonly MachineryJobParameter[] s_all =
        [
            new MachineryJobParameter(
                "JobName",
                BuiltInType.LocalizedText,
                IsArray: true,
                MachineryJobParameterScope.Both,
                "LocalizedText"),
            new MachineryJobParameter(
                "OrderNumbers",
                BuiltInType.String,
                IsArray: true,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "Customers",
                BuiltInType.String,
                IsArray: true,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "CustomerOrderNumbers",
                BuiltInType.String,
                IsArray: true,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "JobExecutionMode",
                BuiltInType.Null,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "JobExecutionMode"),
            new MachineryJobParameter(
                "ReasonForStateChange",
                BuiltInType.LocalizedText,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "LocalizedText"),
            new MachineryJobParameter(
                "RunsPlanned",
                BuiltInType.UInt32,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "UInt32"),
            new MachineryJobParameter(
                "PlannedProductionTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Duration"),
            new MachineryJobParameter(
                "PlannedSetupTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Duration"),
            new MachineryJobParameter(
                "PlannedTimePerRun",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Duration"),
            new MachineryJobParameter(
                "PlannedQuantityPerRun",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Double"),
            new MachineryJobParameter(
                "PlannedOrderQuantity",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Double"),
            new MachineryJobParameter(
                "Overproduction",
                BuiltInType.Boolean,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Boolean"),
            new MachineryJobParameter(
                "PlannedDuration",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobOrder,
                "Duration"),
            new MachineryJobParameter(
                "JobAnnotation",
                BuiltInType.LocalizedText,
                IsArray: true,
                MachineryJobParameterScope.JobOrder,
                "LocalizedText"),
            new MachineryJobParameter(
                "RunsCompleted",
                BuiltInType.UInt32,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "UInt32"),
            new MachineryJobParameter(
                "RunsStarted",
                BuiltInType.UInt32,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "UInt32"),
            new MachineryJobParameter(
                "ActualQuantityCurrentRun",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Double"),
            new MachineryJobParameter(
                "ActualUnitBusyTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Duration"),
            new MachineryJobParameter(
                "ActualUnitSetupTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Duration"),
            new MachineryJobParameter(
                "ActualUnitDelayTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Duration"),
            new MachineryJobParameter(
                "ActualProductionTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Duration"),
            new MachineryJobParameter(
                "ProducedQuantity",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Double"),
            new MachineryJobParameter(
                "EstimatedRemainingTime",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Duration"),
            new MachineryJobParameter(
                "JobResult",
                BuiltInType.Null,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "JobResult"),
            new MachineryJobParameter(
                "GoodQuantity",
                BuiltInType.Double,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "Double"),
            new MachineryJobParameter(
                "AsBuiltBOM",
                BuiltInType.Null,
                IsArray: true,
                MachineryJobParameterScope.JobResponse,
                "BOMInformationDataType"),
            new MachineryJobParameter(
                "OutputPerformanceInfo",
                BuiltInType.Null,
                IsArray: true,
                MachineryJobParameterScope.JobResponse,
                "OutputPerformanceInfoDataType"),
            new MachineryJobParameter(
                "ComponentName",
                BuiltInType.LocalizedText,
                IsArray: true,
                MachineryJobParameterScope.Both,
                "LocalizedText"),
            new MachineryJobParameter(
                "DrawingNumber",
                BuiltInType.String,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "DrawingVersionNumber",
                BuiltInType.String,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "Location",
                BuiltInType.String,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "RelatedContainer",
                BuiltInType.String,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "String"),
            new MachineryJobParameter(
                "Identification",
                BuiltInType.Null,
                IsArray: false,
                MachineryJobParameterScope.Both,
                "OutputInformationDataType"),
            new MachineryJobParameter(
                "StartTime",
                BuiltInType.DateTime,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "DateTime"),
            new MachineryJobParameter(
                "EndTime",
                BuiltInType.DateTime,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "DateTime"),
            new MachineryJobParameter(
                "ProcessIrregularity",
                BuiltInType.Null,
                IsArray: false,
                MachineryJobParameterScope.JobResponse,
                "ProcessIrregularity"),
        ];

        private static readonly Dictionary<string, MachineryJobParameter> s_byName =
            BuildIndex();

        private static Dictionary<string, MachineryJobParameter> BuildIndex()
        {
            var index = new Dictionary<string, MachineryJobParameter>(
                s_all.Length,
                StringComparer.Ordinal);
            for (int ii = 0; ii < s_all.Length; ii++)
            {
                index[s_all[ii].Name] = s_all[ii];
            }
            return index;
        }
    }
}
