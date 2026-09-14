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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Performs awaited initialization that requires a published address space and
    /// initialized server subsystems, including registration of dependent NodeManagers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// StandardServer invokes this optional participant for its initial NodeManagers.
    /// The lifecycle invokes it for each newly committed Add or reload generation.
    /// Both await completion before returning to their caller. Custom hosts must invoke
    /// it after address-space creation and server initialization, not during preparation.
    /// </para>
    /// <para>
    /// Runtime readiness runs outside registration serialization. The generation remains
    /// published and owned by its startup operation: competing removal or reload is
    /// rejected until that operation completes. A readiness failure, including cancellation,
    /// is a post-commit failure, not a rollback. The live registration remains available
    /// for recovery or removal. DeleteAddressSpaceAsync must release any dependencies
    /// created before the failure.
    /// </para>
    /// </remarks>
    public interface INodeManagerReadinessParticipant
    {
        /// <summary>
        /// Completes initialization once runtime lifecycle operations are available.
        /// </summary>
        /// <param name="cancellationToken">The caller's startup or registration cancellation token.</param>
        ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default);
    }
}
