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
 *
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
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Creates the default buffer manager implementations used by the stack.
    /// </summary>
    public sealed class DefaultBufferManagerFactory : IBufferManagerFactory, IDisposable
    {
        /// <summary>
        /// Gets the shared default factory instance.
        /// </summary>
        public static DefaultBufferManagerFactory Instance { get; } = new();

        /// <summary>
        /// Initializes the factory with default options.
        /// </summary>
        public DefaultBufferManagerFactory()
            : this(new BufferManagerFactoryOptions())
        {
        }

        /// <summary>
        /// Initializes the factory with explicit options.
        /// </summary>
        /// <param name="options">The factory options to apply.</param>
        public DefaultBufferManagerFactory(BufferManagerFactoryOptions? options)
        {
            m_options = CloneOptions(options);

            if (m_options.MaxOutstandingBytesPerProcess < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "MaxOutstandingBytesPerProcess must be zero, null, or positive.");
            }

            if (m_options.MaxOutstandingBytesPerProcess > 0)
            {
                m_memoryLimiter = new BufferManagerMemoryLimiter(
                    m_options.MaxOutstandingBytesPerProcess.Value);
            }
        }

        /// <inheritdoc/>
        public IBufferManager Create(string name, int maxBufferSize, ITelemetryContext telemetry)
        {
            IBufferManager manager = CreateInnerManager(name, maxBufferSize, telemetry, m_options.ImplementationKind);

            if (m_memoryLimiter != null)
            {
                manager = new LimitingBufferManager(manager, m_memoryLimiter);
            }

            return manager;
        }

        /// <summary>
        /// Disposes owned resources.
        /// </summary>
        public void Dispose()
        {
            m_memoryLimiter?.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Creates the configured inner manager.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="implementationKind">The implementation to create.</param>
        /// <returns>The created buffer manager.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static IBufferManager CreateInnerManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            BufferManagerImplementationKind implementationKind)
        {
            BufferManagerImplementationKind resolvedKind = implementationKind;

            if (resolvedKind == BufferManagerImplementationKind.Auto)
            {
                resolvedKind = BufferManager.GetDefaultImplementationKind();
            }

            return resolvedKind switch
            {
                BufferManagerImplementationKind.Fast =>
                    new FastBufferManager(name, maxBufferSize, telemetry),
                BufferManagerImplementationKind.Cookie =>
                    new CookieBufferManager(name, maxBufferSize, telemetry),
                BufferManagerImplementationKind.MemoryTracing =>
                    new TracingBufferManager(name, maxBufferSize, telemetry),
                _ => throw new ArgumentOutOfRangeException(nameof(implementationKind))
            };
        }

        /// <summary>
        /// Clones the factory options to avoid external mutation.
        /// </summary>
        /// <param name="options">The options to clone.</param>
        /// <returns>The cloned options.</returns>
        internal static BufferManagerFactoryOptions CloneOptions(BufferManagerFactoryOptions? options)
        {
            if (options == null)
            {
                return new BufferManagerFactoryOptions();
            }

            return new BufferManagerFactoryOptions
            {
                ImplementationKind = options.ImplementationKind,
                MaxOutstandingBytesPerProcess = options.MaxOutstandingBytesPerProcess
            };
        }

        private readonly BufferManagerMemoryLimiter? m_memoryLimiter;
        private readonly BufferManagerFactoryOptions m_options;
    }
}
