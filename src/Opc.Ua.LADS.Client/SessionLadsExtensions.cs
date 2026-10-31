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
using Opc.Ua.Client;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Extensions that create a <see cref="LadsClient"/> on a connected session.
    /// </summary>
    public static class SessionLadsExtensions
    {
        /// <summary>
        /// Creates a LADS client that borrows the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public static LadsClient GetLadsClient(this ISession session, ITelemetryContext telemetry)
        {
            return new LadsClient(session, telemetry);
        }

        /// <summary>
        /// Creates a LADS client with options that borrows the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The options; <c>null</c> uses <see cref="LadsClientOptions.Default"/>.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public static LadsClient GetLadsClient(
            this ISession session,
            ITelemetryContext telemetry,
            LadsClientOptions? options)
        {
            return new LadsClient(session, telemetry, options);
        }
    }
}
