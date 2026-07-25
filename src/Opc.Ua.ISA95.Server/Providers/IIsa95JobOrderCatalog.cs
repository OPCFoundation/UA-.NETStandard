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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System.Threading;
using System.Threading.Tasks;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Provides version-specific snapshots for JobOrderList variables.
    /// </summary>
    public interface IIsa95JobOrderCatalog
    {
        /// <summary>
        /// Gets the current Job Control V1 job-order list.
        /// </summary>
        ValueTask<ArrayOf<V1.ISA95JobOrderDataType>> GetJobOrdersV1Async(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current Job Control V2 job-order and state list.
        /// </summary>
        ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> GetJobOrdersV2Async(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the maximum number of downloadable job orders.
        /// </summary>
        ushort MaxDownloadableJobOrders { get; }
    }
}
