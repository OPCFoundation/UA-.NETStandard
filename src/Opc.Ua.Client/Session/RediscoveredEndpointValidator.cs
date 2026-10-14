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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Accepts or rejects the endpoint a <see cref="ManagedSession"/>
    /// rediscovered while it reconnects.
    /// </summary>
    /// <param name="current">The endpoint the session used before the
    /// rediscovery.</param>
    /// <param name="rediscovered">The endpoint the server's discovery endpoint
    /// returned, which the session would use from now on.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Good to use the rediscovered endpoint. Any other result keeps
    /// the current endpoint and fails the reconnect attempt: a Bad result with
    /// that result, an Uncertain or <c>null</c> result with
    /// <see cref="StatusCodes.BadSecurityChecksFailed"/>. An exception or a
    /// cancellation also keeps the current endpoint.</returns>
    /// <remarks>
    /// Install the validator before the session connects, through
    /// <see cref="ManagedSessionOptions.ValidateRediscoveredEndpoint"/>,
    /// <see cref="ManagedSessionBuilder.WithRediscoveredEndpointValidator"/>
    /// or by registering it in the service collection used by
    /// <see cref="IManagedSessionFactory"/>. See the "Validating a
    /// rediscovered endpoint" section of docs/Sessions.md.
    /// </remarks>
    /// <example>
    /// Reject a rediscovered endpoint that weakens the security of the
    /// session:
    /// <code><![CDATA[
    /// RediscoveredEndpointValidator keepSecurity = (current, rediscovered, ct) =>
    /// {
    ///     if (rediscovered.SecurityMode < current.SecurityMode)
    ///     {
    ///         return new ValueTask<ServiceResult>(
    ///             new ServiceResult(StatusCodes.BadSecurityModeRejected));
    ///     }
    ///     if (rediscovered.SecurityPolicyUri != current.SecurityPolicyUri)
    ///     {
    ///         return new ValueTask<ServiceResult>(
    ///             new ServiceResult(StatusCodes.BadSecurityPolicyRejected));
    ///     }
    ///     return new ValueTask<ServiceResult>(ServiceResult.Good);
    /// };
    ///
    /// ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    ///     .UseEndpoint(endpoint)
    ///     .WithRediscoveredEndpointValidator(keepSecurity)
    ///     .ConnectAsync(ct);
    /// ]]></code>
    /// </example>
    public delegate ValueTask<ServiceResult> RediscoveredEndpointValidator(
        EndpointDescription current,
        EndpointDescription rediscovered,
        CancellationToken ct);
}
