/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Factory for the server's <see cref="ISessionManager"/>.
    /// </summary>
    /// <remarks>
    /// Register an implementation (e.g. via
    /// <c>StandardServer.SessionManagerFactory</c>) so the server builds a
    /// custom session manager — for example a distributed one that mirrors
    /// session state across replicas — instead of the default
    /// <see cref="SessionManager"/>. This keeps the customization additive: the
    /// default path is unchanged when no factory is supplied.
    /// </remarks>
    public interface ISessionManagerFactory
    {
        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="timeProvider">The server time provider.</param>
        /// <param name="serverCertificateProvider">
        /// Resolves the server's instance certificate for a given security
        /// policy URI (used by distributed managers to reconstruct a mirrored
        /// session). May return <c>null</c> when no certificate is configured
        /// for the policy.
        /// </param>
        /// <returns>The session manager to use.</returns>
        ISessionManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider);
    }
}
