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

namespace Opc.Ua
{
    /// <summary>
    /// Selects the concrete buffer manager implementation.
    /// </summary>
    public enum BufferManagerImplementationKind
    {
        /// <summary>
        /// Uses the build-specific default implementation.
        /// </summary>
        Auto,

        /// <summary>
        /// Uses the fast implementation without buffer cookies.
        /// </summary>
        Fast,

        /// <summary>
        /// Uses the cookie-based implementation with one trailing cookie byte.
        /// </summary>
        Cookie,

        /// <summary>
        /// Uses the tracing implementation with cookie and allocation diagnostics.
        /// </summary>
        MemoryTracing
    }

    /// <summary>
    /// Configures <see cref="DefaultBufferManagerFactory"/> behavior.
    /// </summary>
    public sealed class BufferManagerFactoryOptions
    {
        /// <summary>
        /// Gets or sets the preferred implementation kind.
        /// </summary>
        public BufferManagerImplementationKind ImplementationKind { get; set; } =
            BufferManagerImplementationKind.Auto;

        /// <summary>
        /// Gets or sets the shared process-wide outstanding byte budget.
        /// </summary>
        /// <remarks>
        /// A value greater than zero enables <see cref="Bindings.LimitingBufferManager"/> wrapping.
        /// </remarks>
        public long? MaxOutstandingBytesPerProcess { get; set; }
    }
}
