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
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

namespace Opc.Ua.Mcp.Tools
{
    internal static partial class MachineToolJson
    {
        /// <summary>
        /// Maps the complete bounded retained-production graph, including state history and part-set contents.
        /// </summary>
        internal static JsonObject Production(MachineToolClient client, MachineToolProductionReadSnapshot value)
        {
            return new JsonObject
            {
                ["machineNodeId"] = value.MachineId.ToString(),
                ["productionPlanNodeId"] = OptionalNode(value.ProductionPlanId),
                ["nodeVersion"] = Reading(client, value.NodeVersion),
                ["partsProducedInLifetime"] = Reading(client, value.PartsProducedInLifetime),
                ["activeProgram"] = Program(client, value.ActiveProgram),
                ["jobs"] = Array(value.Jobs, job => new JsonObject
                {
                    ["nodeId"] = job.NodeId.ToString(),
                    ["typeDefinitionId"] = job.TypeDefinitionId.ToString(),
                    ["browseName"] = job.BrowseName.ToString(),
                    ["readings"] = Readings(client, ("identifier", job.Identifier), ("numberInList", job.NumberInList),
                        ("runsCompleted", job.RunsCompleted), ("runsPlanned", job.RunsPlanned),
                        ("runsPlannedIsValid", job.RunsPlannedIsValid),
                        ("customerOrderIdentifier", job.CustomerOrderIdentifier),
                        ("orderIdentifier", job.OrderIdentifier),
                        ("partsCompleted", job.PartsCompleted), ("partsGood", job.PartsGood)),
                    ["state"] = State(client, job.State),
                    ["programs"] = Array(job.Programs, program => Program(client, program)),
                    ["partSets"] = Array(job.PartSets, set => new JsonObject
                    {
                        ["nodeId"] = set.NodeId.ToString(),
                        ["typeDefinitionId"] = set.TypeDefinitionId.ToString(),
                        ["browseName"] = set.BrowseName.ToString(),
                        ["readings"] = Readings(client, ("name", set.Name),
                            ("containsMixedParts", set.ContainsMixedParts),
                            ("partsPlannedPerRun", set.PartsPlannedPerRun),
                            ("partsCompletedPerRun", set.PartsCompletedPerRun)),
                        ["parts"] = Array(set.Parts, part => new JsonObject
                        {
                            ["nodeId"] = part.NodeId.ToString(),
                            ["typeDefinitionId"] = part.TypeDefinitionId.ToString(),
                            ["browseName"] = part.BrowseName.ToString(),
                            ["readings"] = Readings(client, ("name", part.Name), ("quality", part.Quality),
                                ("irregularity", part.Irregularity), ("identifier", part.Identifier),
                                ("customerOrderIdentifier", part.CustomerOrderIdentifier)),
                            ["state"] = State(client, part.State)
                        })
                    })
                })
            };
        }

        /// <summary>
        /// Preserves the active/ordinary program distinction and optional ordinary-program state.
        /// </summary>
        private static JsonObject Program(MachineToolClient client, MachineToolProgramReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["readings"] = Readings(client, ("name", value.Name), ("numberInList", value.NumberInList),
                    ("jobIdentifier", value.JobIdentifier), ("jobNodeId", value.JobNodeId)),
                ["state"] = value.State == null ? null : State(client, value.State)
            };
        }

        /// <summary>
        /// Preserves each original state/transition value and its independent acquisition metadata.
        /// </summary>
        private static JsonObject State(MachineToolClient client, MachineToolProductionStateReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["readings"] = Readings(client, ("currentState", value.CurrentState),
                    ("currentStateId", value.CurrentStateId), ("currentStateNumber", value.CurrentStateNumber),
                    ("lastTransition", value.LastTransition), ("lastTransitionId", value.LastTransitionId),
                    ("lastTransitionNumber", value.LastTransitionNumber),
                    ("lastTransitionTime", value.LastTransitionTime))
            };
        }
    }
}
