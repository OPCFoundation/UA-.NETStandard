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

using System.Text.Json.Nodes;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Mcp.Tools
{
    internal sealed partial class GmsJson
    {
        /// <summary>
        /// Encodes native retained jobs and parts with inherited counters, programs and state history.
        /// </summary>
        internal JsonObject Production(GmsProductionReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.Inherited.MachineId.ToString(),
                ["productionPlanId"] = value.Inherited.ProductionPlanId.IsNull
                    ? null : value.Inherited.ProductionPlanId.ToString(),
                ["nodeVersion"] = Reading(value.Inherited.NodeVersion),
                ["activeProgram"] = Program(value.Inherited.ActiveProgram),
                ["partsProducedInLifetime"] = Reading(value.Inherited.PartsProducedInLifetime),
                ["completeGmsProductionFacetClaimed"] = false,
                ["limitation"] = "The released model does not define GMS_ProductionStatisticsType. " +
                    "Only defined native jobs/parts and inherited statistics are reported.",
                ["jobs"] = Array(value.Jobs, job => new JsonObject
                {
                    ["nodeId"] = job.Inherited.NodeId.ToString(),
                    ["typeDefinitionId"] = job.Inherited.TypeDefinitionId.ToString(),
                    ["browseName"] = job.Inherited.BrowseName.ToString(),
                    ["inherited"] = Readings(
                        ("identifier", job.Inherited.Identifier), ("numberInList", job.Inherited.NumberInList),
                        ("runsCompleted", job.Inherited.RunsCompleted), ("runsPlanned", job.Inherited.RunsPlanned),
                        ("runsPlannedIsValid", job.Inherited.RunsPlannedIsValid),
                        ("customerOrderIdentifier", job.Inherited.CustomerOrderIdentifier),
                        ("orderIdentifier", job.Inherited.OrderIdentifier),
                        ("partsCompleted", job.Inherited.PartsCompleted), ("partsGood", job.Inherited.PartsGood)),
                    ["native"] = Variables(
                        ("batchIdentifier", job.BatchIdentifier), ("duration", job.Duration),
                        ("remainingTime", job.RemainingTime), ("measurementReason", job.MeasurementReason)),
                    ["state"] = State(job.Inherited.State),
                    ["programs"] = Array(job.Inherited.Programs, Program),
                    ["partSets"] = Array(job.Inherited.PartSets, PartSet),
                    ["parts"] = Array(job.Parts, Part)
                })
            };
        }

        /// <summary>
        /// Encodes a native part and its Number-valued catalogs without coercing their scalar or array types.
        /// </summary>
        private JsonObject Part(GmsPartReadSnapshot value)
        {
            MachineToolPartReadSnapshot part = value.Inherited;
            return new JsonObject
            {
                ["nodeId"] = part.NodeId.ToString(),
                ["typeDefinitionId"] = part.TypeDefinitionId.ToString(),
                ["browseName"] = part.BrowseName.ToString(),
                ["inherited"] = Readings(
                    ("name", part.Name), ("quality", part.Quality), ("irregularity", part.Irregularity),
                    ("identifier", part.Identifier), ("customerOrderIdentifier", part.CustomerOrderIdentifier)),
                ["state"] = State(part.State),
                ["native"] = Variables(
                    ("partDescription", value.PartDescription), ("partAmendmentStatus", value.PartAmendmentStatus),
                    ("productionNumber", value.ProductionNumber)),
                ["nestIdentifier"] = Catalog(value.NestIdentifier),
                ["operator"] = Catalog(value.Operator),
                ["partCarrierIdentifier"] = Catalog(value.PartCarrierIdentifier),
                ["processingMachineIdentifier"] = Catalog(value.ProcessingMachineIdentifier),
                ["processParameter"] = Catalog(value.ProcessParameter)
            };
        }

        /// <summary>
        /// Relates current-run counters to actual native part identities without duplicating their samples.
        /// </summary>
        private JsonObject PartSet(MachineToolPartSetReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["fields"] = Readings(
                    ("name", value.Name), ("containsMixedParts", value.ContainsMixedParts),
                    ("partsPlannedPerRun", value.PartsPlannedPerRun),
                    ("partsCompletedPerRun", value.PartsCompletedPerRun)),
                ["partNodeIds"] = Array(value.Parts, part => JsonValue.Create(part.NodeId.ToString()))
            };
        }

        /// <summary>
        /// Preserves standard program identity, list order and optional state history.
        /// </summary>
        private JsonObject Program(MachineToolProgramReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["fields"] = Readings(("name", value.Name), ("numberInList", value.NumberInList),
                    ("jobIdentifier", value.JobIdentifier), ("jobNodeId", value.JobNodeId)),
                ["state"] = value.State == null ? null : State(value.State)
            };
        }

        /// <summary>
        /// Retains state/transition declaration identities, numbers and independent acquisition timestamps.
        /// </summary>
        private JsonObject State(MachineToolProductionStateReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["fields"] = Readings(
                    ("currentState", value.CurrentState), ("currentStateId", value.CurrentStateId),
                    ("currentStateNumber", value.CurrentStateNumber), ("lastTransition", value.LastTransition),
                    ("lastTransitionId", value.LastTransitionId), ("lastTransitionNumber", value.LastTransitionNumber),
                    ("lastTransitionTime", value.LastTransitionTime))
            };
        }

        /// <summary>
        /// Distinguishes absent catalog variables from present catalogs with unavailable samples.
        /// </summary>
        private JsonObject? Catalog(GmsCatalogReadSnapshot? value)
        {
            return value == null ? null : new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["reading"] = Reading(value.Value),
                ["fields"] = Variables(
                    ("catalogName", value.CatalogName), ("catalogEntry", value.CatalogEntry),
                    ("enumValues", value.EnumValues), ("valueAsText", value.ValueAsText))
            };
        }
    }
}
