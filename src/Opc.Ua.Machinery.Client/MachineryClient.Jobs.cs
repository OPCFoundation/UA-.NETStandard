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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Client;
using JobsBrowseNames = Opc.Ua.Machinery.Jobs.BrowseNames;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Reads the job orders a machine currently manages, with their
        /// states, from <c>JobManagement/JobOrderControl/JobOrderList</c>.
        /// Returns an empty list when the machine publishes no job management
        /// or no list.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> ReadJobOrdersAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            Variant value = await ReadJobManagementListAsync(
                machine,
                JobsBrowseNames.JobOrderControl,
                V2.BrowseNames.JobOrderList,
                cancellationToken).ConfigureAwait(false);
            return value.TryGetStructure(out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders)
                ? orders
                : ArrayOf<V2.ISA95JobOrderAndStateDataType>.Empty;
        }

        /// <summary>
        /// Reads the job responses a machine publishes in
        /// <c>JobManagement/JobOrderResults/JobOrderResponseList</c>. Returns an
        /// empty list when the machine publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<V2.ISA95JobResponseDataType>> ReadJobResponsesAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            Variant value = await ReadJobManagementListAsync(
                machine,
                JobsBrowseNames.JobOrderResults,
                V2.BrowseNames.JobOrderResponseList,
                cancellationToken).ConfigureAwait(false);
            return value.TryGetStructure(out ArrayOf<V2.ISA95JobResponseDataType> responses)
                ? responses
                : ArrayOf<V2.ISA95JobResponseDataType>.Empty;
        }

        /// <summary>
        /// Reads the predefined OPC 40001-3 parameters of one job order the
        /// machine lists, or <see langword="null"/> when it lists no order with
        /// that identifier.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="jobOrderId">The job order identifier.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryJobOrderParameters?> ReadJobOrderParametersAsync(
            NodeId machine,
            string jobOrderId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobOrderId))
            {
                throw new ArgumentException("A job order identifier is required.", nameof(jobOrderId));
            }
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = await ReadJobOrdersAsync(
                machine,
                cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < orders.Count; ii++)
            {
                V2.ISA95JobOrderDataType? order = orders[ii]?.JobOrder;
                if (order != null && string.Equals(order.JobOrderID, jobOrderId, StringComparison.Ordinal))
                {
                    return MachineryJobOrderParameters.FromJobOrder(order);
                }
            }
            return null;
        }

        /// <summary>
        /// Reads the predefined OPC 40001-3 parameters of the current response
        /// to one job order, or <see langword="null"/> when the machine
        /// publishes no job management or has no response for the order.
        /// </summary>
        /// <remarks>
        /// The response comes from the ISA-95 <c>RequestJobResponseByJobOrderID</c>
        /// method below <c>JobOrderResults</c>, which answers for any order the
        /// machine knows — the published <c>JobOrderResponseList</c> may be
        /// empty when the server has no response catalog.
        /// </remarks>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="jobOrderId">The job order identifier.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryJobResponseParameters?> ReadJobResponseParametersAsync(
            NodeId machine,
            string jobOrderId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobOrderId))
            {
                throw new ArgumentException("A job order identifier is required.", nameof(jobOrderId));
            }
            Isa95JobControlV2Client? jobs = await JobManagementAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            if (jobs == null)
            {
                return null;
            }
            (V2.ISA95JobResponseDataType response, _) = await jobs
                .RequestJobResponseByJobOrderIdAsync(jobOrderId, cancellationToken)
                .ConfigureAwait(false);

            // An unknown order comes back as an empty response rather than a
            // fault, so the identifier is what tells the two apart.
            return response != null &&
                string.Equals(response.JobOrderID, jobOrderId, StringComparison.Ordinal)
                ? MachineryJobResponseParameters.FromJobResponse(response)
                : null;
        }

        private async ValueTask<Variant> ReadJobManagementListAsync(
            NodeId machine,
            string jobManagementChild,
            string listBrowseName,
            CancellationToken cancellationToken)
        {
            if (!TryGetNamespaceIndex(
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs,
                out ushort jobsNamespaceIndex))
            {
                return Variant.Null;
            }

            // OPC 40001-3 composes the ISA-95 Job Control V2 model, so a
            // server that publishes the Jobs model without it is broken.
            ushort isa95NamespaceIndex = NamespaceIndexOf(V2.Namespaces.ISA95JobControlV2);
            NodeId list = await ResolvePathAsync(
                machine,
                cancellationToken,
                new QualifiedName(JobsBrowseNames.JobManagement, jobsNamespaceIndex),
                new QualifiedName(jobManagementChild, jobsNamespaceIndex),
                new QualifiedName(listBrowseName, isa95NamespaceIndex)).ConfigureAwait(false);
            if (list.IsNull)
            {
                return Variant.Null;
            }

            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId { NodeId = list, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            return response.Results.Count > 0 && StatusCode.IsGood(response.Results[0].StatusCode)
                ? response.Results[0].WrappedValue
                : Variant.Null;
        }
    }
}
