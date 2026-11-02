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
using Opc.Ua.Client;

namespace Opc.Ua.ECM.Client
{
    /// <summary>
    /// Creates ECM clients over the container-owned managed session.
    /// </summary>
    public sealed class EcmClientFactory
    {
        /// <summary>
        /// Creates a factory borrowing the existing managed-session delegate.
        /// </summary>
        public EcmClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a typed client without closing or disposing the shared session.
        /// </summary>
        public async ValueTask<EcmClient> CreateAsync(
            NodeId standby,
            NodeId powerOff = default,
            CancellationToken cancellationToken = default)
        {
            if (standby.IsNull)
            {
                throw new ArgumentException("A standby object is required.", nameof(standby));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new EcmClient(session, standby, m_telemetry, powerOff);
        }

        /// <summary>
        /// Creates a WOL-only client without closing or disposing the shared session.
        /// </summary>
        public async ValueTask<EcmWolClient> CreateWolAsync(
            NodeId powerOff,
            CancellationToken cancellationToken = default)
        {
            if (powerOff.IsNull)
            {
                throw new ArgumentException("A WOL object is required.", nameof(powerOff));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new EcmWolClient(session, powerOff, m_telemetry);
        }

        /// <summary>The container-owned session delegate.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
