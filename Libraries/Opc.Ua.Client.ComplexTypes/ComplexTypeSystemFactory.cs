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

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Dependency-injected factory that produces a fresh
    /// <see cref="ComplexTypeSystem"/> bound to a caller-supplied
    /// <see cref="ISession"/> and the host's
    /// <see cref="ITelemetryContext"/>.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by
    /// <c>IOpcUaBuilder.AddComplexTypes()</c>. Consumers (typically
    /// <c>ManagedSession</c> hosts) resolve this factory and call
    /// <see cref="Create(ISession)"/> per session to obtain a
    /// type-loader scoped to that session.
    /// </remarks>
    public sealed class ComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public ComplexTypeSystemFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="ComplexTypeSystem"/> bound to
        /// <paramref name="session"/> and the host's
        /// <see cref="ITelemetryContext"/>.
        /// </summary>
        /// <param name="session">The client session to load custom
        /// types for.</param>
        /// <returns>A fresh <see cref="ComplexTypeSystem"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public ComplexTypeSystem Create(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return new ComplexTypeSystem(
                new NodeCacheResolver(session, m_telemetry),
                new ComplexTypeBuilderFactory(),
                m_telemetry);
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
