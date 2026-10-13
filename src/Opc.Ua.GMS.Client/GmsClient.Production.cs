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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsClient
    {
        /// <summary>
        /// Composes bounded retained-production reads with defined GMS metadata, without adding control methods.
        /// </summary>
        public async ValueTask<GmsProductionReadSnapshot> ReadProductionAsync(
            NodeId nodeId, CancellationToken cancellationToken = default)
        {
            using Operation operation = Begin(cancellationToken);
            CancellationToken ct = operation.Token;
            _ = await RequireGmsAsync(nodeId, ct).ConfigureAwait(false);
            MachineToolProductionReadSnapshot inherited = await m_inherited.ReadProductionAsync(nodeId,
                new MachineToolProductionReadOptions
                {
                    MaximumElements = m_options.MaximumElements,
                    MaximumReferences = m_options.MaximumReferences,
                    MaximumPayloadBytes = m_options.MaximumPayloadBytes
                }, ct).ConfigureAwait(false);
            var budget = new ReadBudget(m_options, Session.MessageContext);
            var jobs = new List<GmsJobReadSnapshot>();
            for (int j = 0; j < inherited.Jobs.Count; j++)
            {
                MachineToolJobReadSnapshot job = inherited.Jobs[j];
                budget.Element(job.NodeId);
                if (!await IsTypeAsync(job.TypeDefinitionId, ObjectTypeIds.GMSJobType, ct).ConfigureAwait(false))
                {
                    throw Missing("native GMSJobType");
                }
                var proxy = new GMSJobTypeClient(Session, job.NodeId, Telemetry);
                ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(proxy.ObjectId,
                    [Name(BrowseNames.BatchIdentifier), Name(BrowseNames.Duration),
                        Name(BrowseNames.RemainingTime), Name(BrowseNames.MeasurementReason)], budget, ct)
                    .ConfigureAwait(false);
                var parts = new List<GmsPartReadSnapshot>();
                for (int s = 0; s < job.PartSets.Count; s++)
                {
                    MachineToolPartSetReadSnapshot set = job.PartSets[s];
                    for (int p = 0; p < set.Parts.Count; p++)
                    {
                        parts.Add(await ReadPartAsync(set.Parts[p], budget, ct).ConfigureAwait(false));
                    }
                }
                jobs.Add(new GmsJobReadSnapshot(job, fields[0], fields[1], fields[2], fields[3], parts.ToArrayOf()));
            }
            await CheckVersionAsync(inherited.ProductionPlanId, inherited.NodeVersion, budget, ct)
                .ConfigureAwait(false);
            return new GmsProductionReadSnapshot(inherited, jobs.ToArrayOf());
        }

        /// <summary>
        /// Reads native part fields without reimplementing inherited counters or state transitions.
        /// </summary>
        private async ValueTask<GmsPartReadSnapshot> ReadPartAsync(
            MachineToolPartReadSnapshot part, ReadBudget budget, CancellationToken ct)
        {
            budget.Element(part.NodeId);
            if (!await IsTypeAsync(part.TypeDefinitionId, ObjectTypeIds.GMSPartType, ct).ConfigureAwait(false))
            {
                throw Missing("native GMSPartType");
            }
            var proxy = new GMSPartTypeClient(Session, part.NodeId, Telemetry);
            ArrayOf<GmsVariableReading> values = await ReadFieldsAsync(proxy.ObjectId,
                [Name(BrowseNames.PartDescription), Name(BrowseNames.PartAmendmentStatus),
                    Name(BrowseNames.ProductionNumber)], budget, ct).ConfigureAwait(false);
            ArrayOf<NodeId> catalogs = await ResolveFieldsAsync(proxy.ObjectId,
                [Name(BrowseNames.NestIdentifier), Name(BrowseNames.Operator), Name(BrowseNames.PartCarrierIdentifier),
                    Name(BrowseNames.ProcessingMachineIdentifier), Name(BrowseNames.ProcessParameter)], ct)
                .ConfigureAwait(false);
            var read = new GmsCatalogReadSnapshot?[catalogs.Count];
            for (int i = 0; i < catalogs.Count; i++)
            {
                NodeId catalog = catalogs[i];
                if (catalog.IsNull)
                {
                    continue;
                }
                budget.Element(catalog);
                ArrayOf<DataValue> value = await ReadValuesAsync([catalog], budget, ct).ConfigureAwait(false);
                ArrayOf<GmsVariableReading> fields = await ReadFieldsAsync(catalog,
                    [Name(BrowseNames.CatalogName), Name(BrowseNames.CatalogEntry),
                        new QualifiedName(Opc.Ua.BrowseNames.EnumValues),
                        new QualifiedName(Opc.Ua.BrowseNames.ValueAsText)], budget, ct).ConfigureAwait(false);
                Require(fields[0], BrowseNames.CatalogName);
                Require(fields[1], BrowseNames.CatalogEntry);
                Require(fields[2], Opc.Ua.BrowseNames.EnumValues);
                read[i] = new GmsCatalogReadSnapshot(catalog, value[0], fields[0], fields[1], fields[2], fields[3]);
            }
            return new GmsPartReadSnapshot(
                part, values[0], values[1], values[2], read[0], read[1], read[2], read[3], read[4]);
        }
    }
}
