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

using Opc.Ua;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Versioned client operations, not an application execution-state machine.
    /// </summary>
    internal sealed record Isa95JobTask(
        CompanionOperation Operation,
        string MethodName,
        V1.ISA95JobOrderCommandEnum V1Command = V1.ISA95JobOrderCommandEnum.Undefined)
    {
        public bool IsMutation => Operation.Safety == CompanionOperationSafety.DeploymentMutation;

        public bool UsesJobOrder => Operation.Id is "store-job" or "update-job";

        public static ArrayOf<Isa95JobTask> ForVersion(bool version2)
        {
            return version2 ? s_v2 : s_v1;
        }

        public static Isa95JobTask Find(bool version2, string operationId)
        {
            foreach (Isa95JobTask task in ForVersion(version2))
            {
                if (task.Operation.Id == operationId)
                {
                    return task;
                }
            }
            throw IndustrialCompanionAccess.Unsupported(
                "The selected Job Control version does not expose this guided operation. " +
                "V1 has no Pause, Resume or Abort command; execution transitions are server-owned.");
        }

        private static Isa95JobTask Mutation(
            bool version2,
            string id,
            string name,
            V1.ISA95JobOrderCommandEnum command = V1.ISA95JobOrderCommandEnum.Undefined)
        {
            CompanionInputDefinition request = id is "store-job" or "update-job"
                ? new CompanionInputDefinition(
                    "jobOrder", "Job order", BuiltInType.ExtensionObject,
                    "A complete typed job order, including work masters, parameters and resource requirements.")
                {
                    DataTypeId = version2
                        ? V2.DataTypeIds.ISA95JobOrderDataType
                        : V1.DataTypeIds.ISA95JobOrderDataType
                }
                : s_jobId;
            ArrayOf<CompanionInputDefinition> inputs = version2
                ? [request, s_comment]
                : [request];
            return new Isa95JobTask(
                new CompanionOperation(id, name + " job", CompanionOperationSafety.DeploymentMutation)
                {
                    Inputs = inputs
                },
                version2 ? name : "ReceiveJobOrder",
                command);
        }

        private static Isa95JobTask Observation(bool version2, bool response)
        {
            return new Isa95JobTask(
                new CompanionOperation(
                    response ? "request-job-response" : "observe-job-status",
                    response ? "Read typed job response" : "Read job order and current status",
                    CompanionOperationSafety.ReadOnly)
                {
                    Inputs = [s_jobId]
                },
                response || !version2
                    ? version2 ? "RequestJobResponseByJobOrderID" : "RequestJobResponse"
                    : string.Empty);
        }

        private static readonly CompanionInputDefinition s_jobId = new(
            "jobOrderId", "Job order ID", BuiltInType.String, "The exact server job order ID (1-256 characters).");

        private static readonly CompanionInputDefinition s_comment = new(
            "comment", "Comment", BuiltInType.LocalizedText,
            "Optional localized comments sent unchanged to the server.", Required: false)
        {
            ValueRank = ValueRanks.OneDimension
        };

        private static readonly ArrayOf<Isa95JobTask> s_v1 =
        [
            Mutation(false, "store-job", "Store", V1.ISA95JobOrderCommandEnum.Store),
            Mutation(false, "start-job", "Start", V1.ISA95JobOrderCommandEnum.Start),
            Mutation(false, "update-job", "Update", V1.ISA95JobOrderCommandEnum.Update),
            Mutation(false, "cancel-job", "Cancel", V1.ISA95JobOrderCommandEnum.Cancel),
            Mutation(false, "clear-job", "Clear", V1.ISA95JobOrderCommandEnum.Clear),
            Observation(false, true)
        ];

        private static readonly ArrayOf<Isa95JobTask> s_v2 =
        [
            Mutation(true, "store-job", "Store"),
            Mutation(true, "start-job", "Start"),
            Mutation(true, "update-job", "Update"),
            Mutation(true, "pause-job", "Pause"),
            Mutation(true, "resume-job", "Resume"),
            Mutation(true, "abort-job", "Abort"),
            Mutation(true, "cancel-job", "Cancel"),
            Mutation(true, "clear-job", "Clear"),
            Observation(true, false),
            Observation(true, true)
        ];
    }
}
