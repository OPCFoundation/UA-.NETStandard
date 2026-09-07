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
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Dependency-injected factory that produces an <see cref="OpenUsdConnector"/> bound
    /// to a caller-supplied <see cref="ISession"/> and <see cref="IUsdSink"/>, threading
    /// the host's <see cref="ITelemetryContext"/> and default
    /// <see cref="OpenUsdConnectorOptions"/>. Registered as a singleton by the
    /// <c>AddOpenUsdConnector(...)</c> fluent/DI extensions; the public
    /// <see cref="OpenUsdConnector"/> constructors remain the non-DI fallback.
    /// </summary>
    public sealed class OpenUsdConnectorFactory
    {
        private readonly OpenUsdConnectorOptions m_defaultOptions;

        /// <summary>
        /// Initializes a new instance with default options.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public OpenUsdConnectorFactory(ITelemetryContext telemetry)
            : this(telemetry, new OpenUsdConnectorOptions())
        {
        }

        /// <summary>
        /// Initializes a new instance with the supplied default options.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="telemetry"/> or <paramref name="defaultOptions"/> is <c>null</c>.
        /// </exception>
        public OpenUsdConnectorFactory(ITelemetryContext telemetry, OpenUsdConnectorOptions defaultOptions)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_defaultOptions = defaultOptions ?? throw new ArgumentNullException(nameof(defaultOptions));
        }

        /// <summary>
        /// The shared telemetry context the factory threads into each connector.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Creates a connector bound to <paramref name="session"/> and
        /// <paramref name="sink"/> using the factory's default options.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="session"/> or <paramref name="sink"/> is <c>null</c>.
        /// </exception>
        public OpenUsdConnector Create(ISession session, IUsdSink sink)
            => Create(session, sink, null);

        /// <summary>
        /// Creates a connector bound to <paramref name="session"/> and
        /// <paramref name="sink"/>, overriding the factory's default options when
        /// <paramref name="options"/> is supplied.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="session"/> or <paramref name="sink"/> is <c>null</c>.
        /// </exception>
        public OpenUsdConnector Create(ISession session, IUsdSink sink, OpenUsdConnectorOptions? options)
            => new OpenUsdConnector(session, sink, options ?? m_defaultOptions, Telemetry);
    }
}
