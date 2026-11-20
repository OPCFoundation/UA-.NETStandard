/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Server.Jobs
{
    /// <summary>
    /// Checks a job order's predefined OPC 40001-3 parameters before the
    /// application's receiver ever sees it.
    /// </summary>
    /// <remarks>
    /// The check sits in front of the receiver rather than inside it because
    /// the receiver is the application's, and the conformance claim is the
    /// library's. A job order carrying a predefined parameter with the wrong
    /// type is refused here, so the server never both advertises the unit and
    /// stores a payload that breaks it.
    /// </remarks>
    internal sealed class PredefinedParameterJobOrderReceiver : IIsa95JobOrderReceiverV2
    {
        public PredefinedParameterJobOrderReceiver(IIsa95JobOrderReceiverV2 inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken cancellationToken = default)
        {
            if (jobOrder != null)
            {
                MachineryJobParameters.Validate(
                    jobOrder.JobOrderParameters,
                    inJobOrder: true,
                    string.IsNullOrEmpty(jobOrder.JobOrderID)
                        ? "Job order"
                        : "Job order '" + jobOrder.JobOrderID + "'");
            }

            return m_inner.ReceiveJobOrderAsync(
                operation,
                jobOrder!,
                comment,
                cancellationToken);
        }

        private readonly IIsa95JobOrderReceiverV2 m_inner;
    }
}
