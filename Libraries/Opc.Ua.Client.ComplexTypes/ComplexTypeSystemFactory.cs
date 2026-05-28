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
using Opc.Ua.ComplexTypes;

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
    /// <c>IOpcUaBuilder.AddComplexTypes()</c>. By default the factory
    /// produces type loaders backed by the AOT-friendly
    /// <see cref="DefaultComplexTypeFactory"/>. Hosts that need
    /// runtime concrete .NET classes for custom DataTypes register the
    /// Reflection.Emit-based <c>ComplexTypeBuilderFactory</c> from
    /// <c>Opc.Ua.ComplexTypes.Emit</c> via the
    /// <c>AddComplexTypesWithReflectionEmit()</c> builder extension —
    /// it swaps the registered factory descriptor for this one.
    /// </remarks>
    public sealed class ComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance backed by
        /// <see cref="DefaultComplexTypeFactory"/>.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public ComplexTypeSystemFactory(ITelemetryContext telemetry)
            : this(telemetry, static () => new DefaultComplexTypeFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance backed by a caller-supplied
        /// <see cref="IComplexTypeFactory"/> source. The
        /// <paramref name="complexTypeFactoryFactory"/> delegate is
        /// invoked once per <see cref="Create(ISession)"/> call so each
        /// <see cref="ComplexTypeSystem"/> gets its own builder
        /// factory.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <param name="complexTypeFactoryFactory">Delegate that
        /// produces a fresh <see cref="IComplexTypeFactory"/> per
        /// session.</param>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public ComplexTypeSystemFactory(
            ITelemetryContext telemetry,
            Func<IComplexTypeFactory> complexTypeFactoryFactory)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_complexTypeFactoryFactory = complexTypeFactoryFactory ??
                throw new ArgumentNullException(nameof(complexTypeFactoryFactory));
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
                session,
                m_complexTypeFactoryFactory(),
                m_telemetry);
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly Func<IComplexTypeFactory> m_complexTypeFactoryFactory;
    }
}
