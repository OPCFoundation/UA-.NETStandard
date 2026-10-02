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
