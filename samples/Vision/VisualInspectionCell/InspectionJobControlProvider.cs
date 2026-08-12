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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.ISA95.Server.Providers;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Vision.VisualInspectionCell
{
    internal sealed class InspectionJobControlProvider :
        IIsa95JobOrderReceiverV2,
        IIsa95JobResponseProviderV2,
        IIsa95JobResponseReceiverV2,
        IIsa95JobStatusSourceV2,
        IIsa95JobExecutionController,
        IIsa95JobOrderCatalog,
        IIsa95JobOrderCatalogChangeSource,
        IDisposable
    {
        public InspectionJobControlProvider(TimeProvider? timeProvider = null)
        {
            m_provider = new InMemoryIsa95JobControlProvider(
                new Isa95JobControlProviderOptions
                {
                    MaxJobOrders = AllowedOrders.Count,
                    MaxJobResponses = 16,
                    ResponseRetention = TimeSpan.Zero
                },
                timeProvider ?? TimeProvider.System);
        }

        public ushort MaxDownloadableJobOrders => m_provider.MaxDownloadableJobOrders;

        public ValueTask<ArrayOf<Opc.Ua.ISA95.JobControl.V1.ISA95JobOrderDataType>> GetJobOrdersV1Async(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ArrayOf<Opc.Ua.ISA95.JobControl.V1.ISA95JobOrderDataType>>(
                ArrayOf<Opc.Ua.ISA95.JobControl.V1.ISA95JobOrderDataType>.Empty);
        }

        public ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> GetJobOrdersV2Async(
            CancellationToken cancellationToken = default)
        {
            return m_provider.GetJobOrdersV2Async(cancellationToken);
        }

        public async ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken cancellationToken = default)
        {
            if (jobOrder == null)
            {
                throw new ArgumentNullException(nameof(jobOrder));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!AllowedOrders.Contains(jobOrder.JobOrderID))
            {
                return new Isa95JobOrderReceiptV2
                {
                    Result = new ServiceResult(StatusCodes.BadUserAccessDenied,
                        LocalizedText.From("The visual-inspection sample accepts only its fixed inspection/rework orders.")),
                    ReturnStatus = Isa95JobReturnStatus.InvalidRequest
                };
            }
            return await m_provider.ReceiveJobOrderAsync(operation, jobOrder, comment, cancellationToken)
                .ConfigureAwait(false);
        }

        public ValueTask<Isa95JobResponseByIdResultV2> RequestJobResponseByJobOrderIdAsync(
            string jobOrderId,
            CancellationToken cancellationToken = default)
        {
            return m_provider.RequestJobResponseByJobOrderIdAsync(jobOrderId, cancellationToken);
        }

        public ValueTask<Isa95JobResponsesByStateResultV2> RequestJobResponsesByStateAsync(
            ArrayOf<V2.ISA95StateDataType> state,
            CancellationToken cancellationToken = default)
        {
            return m_provider.RequestJobResponsesByStateAsync(state, cancellationToken);
        }

        public ValueTask<Isa95JobResponseReceiptV2> ReceiveJobResponseAsync(
            V2.ISA95JobResponseDataType response,
            CancellationToken cancellationToken = default)
        {
            return m_provider.ReceiveJobResponseAsync(response, cancellationToken);
        }

        public IAsyncEnumerable<Isa95JobStatusNotificationV2> SubscribeAsync(
            CancellationToken cancellationToken = default)
        {
            return m_provider.SubscribeAsync(cancellationToken);
        }

        public ValueTask<Isa95JobOrderReceiptV2> TransitionAsync(
            string jobOrderId,
            Isa95JobExecutionTransition transition,
            CancellationToken cancellationToken = default)
        {
            return m_provider.TransitionAsync(jobOrderId, transition, cancellationToken);
        }

        public IAsyncEnumerable<Isa95JobOrderCatalogChange> SubscribeCatalogChangesAsync(
            CancellationToken cancellationToken = default)
        {
            return m_provider.SubscribeCatalogChangesAsync(cancellationToken);
        }

        public async ValueTask SeedAsync(CancellationToken cancellationToken)
        {
            foreach (V2.ISA95JobOrderDataType order in SeedOrders())
            {
                _ = await m_provider.ReceiveJobOrderAsync(
                    Isa95JobOrderOperationV2.StoreAndStart,
                    order,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _ = await m_provider.TransitionAsync(
                    order.JobOrderID,
                    Isa95JobExecutionTransition.BeginExecution,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            m_provider.Dispose();
        }

        private static IEnumerable<V2.ISA95JobOrderDataType> SeedOrders()
        {
            yield return new V2.ISA95JobOrderDataType
            {
                JobOrderID = InspectionOrderId,
                Description = new[] {
                    LocalizedText.From("Inspect machined bracket against dimensional recipe.")
                }.ToArrayOf(),
                Priority = 10
            };
            yield return new V2.ISA95JobOrderDataType
            {
                JobOrderID = ReworkRejectOrderId,
                Description = new[] {
                    LocalizedText.From("Route nonconforming bracket to rework or reject.")
                }.ToArrayOf(),
                Priority = 20
            };
        }

        public const string InspectionOrderId = "VIS-INSP-BRACKET-001";
        public const string ReworkRejectOrderId = "VIS-REWORK-REJECT-001";
        private static readonly HashSet<string> AllowedOrders =
        [
            InspectionOrderId,
            ReworkRejectOrderId
        ];

        private readonly InMemoryIsa95JobControlProvider m_provider;
    }
}
