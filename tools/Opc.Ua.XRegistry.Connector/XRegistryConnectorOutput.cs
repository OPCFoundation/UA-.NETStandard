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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Connector
{
    internal static class XRegistryConnectorOutput
    {
        public static string Readiness(bool ready, bool atomicWrites = false)
        {
            var response = new JsonObject { ["ready"] = ready };
            if (ready)
            {
                response["atomicWrites"] = atomicWrites;
            }
            return response.ToJsonString();
        }

        public static Task ReadyAsync(string mode, string address)
        {
            return WriteAsync(new JsonObject { ["status"] = "ready", ["mode"] = mode, ["address"] = address });
        }

        public static Task DescriptionAsync(string side, XRegistryEndpointDescription description)
        {
            var codec = new XRegistryProtocolCodec();
            return WriteAsync(new JsonObject
            {
                ["side"] = side,
                ["description"] = JsonNode.Parse(codec.EncodeDescription(description).Span)
            });
        }

        public static Task ReportAsync(XRegistrySyncReport report)
        {
            var records = new JsonArray();
            for (int index = 0; index < report.Records.Count; index++)
            {
                XRegistrySyncRecord record = report.Records[index];
                JsonNode value = new JsonObject
                {
                    ["path"] = record.Path,
                    ["kind"] = record.Kind.ToString(),
                    ["detail"] = record.Detail,
                    ["operationid"] = record.OperationId,
                    ["conflictid"] = record.ConflictId
                };
                records.Add(value);
            }
            return WriteAsync(new JsonObject
            {
                ["status"] = report.Status.ToString(),
                ["exitcode"] = report.ExitCode,
                ["dryrun"] = report.DryRun,
                ["complete"] = report.InventoryComplete,
                ["observed"] = report.Observed,
                ["converged"] = report.Converged,
                ["applied"] = report.Applied,
                ["deleted"] = report.Deleted,
                ["planned"] = report.Planned,
                ["conflicts"] = report.Conflicts,
                ["pending"] = report.Pending,
                ["failures"] = report.Failures,
                ["records"] = records
            });
        }

        public static Task ConflictsAsync(ArrayOf<XRegistrySyncConflict> conflicts)
        {
            var output = new JsonArray();
            for (int index = 0; index < conflicts.Count; index++)
            {
                XRegistrySyncConflict conflict = conflicts[index];
                JsonNode value = new JsonObject
                {
                    ["id"] = conflict.Id,
                    ["path"] = conflict.Path,
                    ["reason"] = conflict.Reason,
                    ["status"] = conflict.Status.ToString(),
                    ["resolution"] = conflict.Resolution.ToString(),
                    ["operationid"] = conflict.OperationId
                };
                output.Add(value);
            }
            return WriteAsync(new JsonObject { ["conflicts"] = output });
        }

        private static Task WriteAsync(JsonObject document)
        {
            return Console.Out.WriteLineAsync(document.ToJsonString());
        }
    }
}
