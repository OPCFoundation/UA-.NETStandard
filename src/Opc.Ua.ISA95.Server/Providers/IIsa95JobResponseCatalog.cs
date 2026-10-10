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

using System.Threading;
using System.Threading.Tasks;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Provides the snapshot published by a <c>JobOrderResponseList</c> variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="IIsa95JobOrderCatalog"/> for the response
    /// side. It exists because <see cref="IIsa95JobResponseProviderV2"/> can only
    /// be asked for responses matching one job order id or one job order state:
    /// its state filter names a single top-level state, so "every response"
    /// cannot be expressed as a query and the list variable has no source
    /// without this.
    /// </para>
    /// <para>
    /// Job Control V1 declares no response list variable, so there is no V1
    /// counterpart here.
    /// </para>
    /// </remarks>
    public interface IIsa95JobResponseCatalog
    {
        /// <summary>
        /// Gets the current Job Control V2 job-response list.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// Every response the provider currently holds.
        /// </returns>
        ValueTask<ArrayOf<V2.ISA95JobResponseDataType>> GetJobResponsesV2Async(
            CancellationToken cancellationToken = default);
    }
}
