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
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Server.Materialization
{
    /// <summary>
    /// Selects how an old environment projection generation is retired.
    /// </summary>
    public enum AasProjectionRetirementPolicy
    {
        /// <summary>
        /// Retire the old generation after active work drains.
        /// </summary>
        Graceful,

        /// <summary>
        /// Retire the old generation immediately.
        /// </summary>
        Immediate
    }

    /// <summary>
    /// Marks the host-specific registration carried by an
    /// <see cref="AasEnvironmentProjectionHandle"/>.
    /// </summary>
    /// <remarks>
    /// Implementations are opaque to the materialization pipeline; only the
    /// owning <see cref="IAasEnvironmentProjectionHost"/> interprets them. The
    /// handle names this rather than the concrete runtime registration so that
    /// the host interface can be implemented outside the stack - a test double
    /// can record the sequence of add, reload and remove without a running
    /// server, and an alternative projection strategy is expressible at all.
    /// </remarks>
    public interface IAasProjectionRegistration
    {
        /// <summary>
        /// Gets the stable identifier the host assigned to this registration.
        /// </summary>
        Guid Id { get; }
    }

    /// <summary>
    /// The live projection handle returned by the AAS projection host.
    /// </summary>
    public sealed class AasEnvironmentProjectionHandle
    {
        /// <summary>
        /// Initializes a handle.
        /// </summary>
        /// <param name="registration">The host-specific projection registration.</param>
        public AasEnvironmentProjectionHandle(IAasProjectionRegistration? registration)
        {
            Registration = registration;
        }

        /// <summary>
        /// Gets the host-specific projection registration.
        /// </summary>
        public IAasProjectionRegistration? Registration { get; }
    }
}
