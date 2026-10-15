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

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Host-supplied evidence that this exact session and target belong to a live,
    /// verified owned simulation, not physical weighing equipment. No implementation
    /// is installed by default. Loopback, server names and model namespaces are not evidence.
    /// </summary>
    internal interface IWeighingSimulationPolicy
    {
        /// <summary>
        /// Authorizes the operation against the owned process lifecycle and authenticated
        /// server identity, or throws. Called at preparation and immediately before dispatch.
        /// This does not replace deployment authorization or a fresh user confirmation.
        /// </summary>
        ValueTask<WeighingSimulationGrant> AuthorizeAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Non-secret identity of the verified owned run, policy revision and fixed expiry.
    /// Authorizations for the same run must retain its original deadline, not extend it.
    /// Restarting or revoking a run must invalidate the grant returned by the policy.
    /// </summary>
    internal sealed record WeighingSimulationGrant(Guid RunId, string Revision, DateTimeOffset ExpiresAt);
}
