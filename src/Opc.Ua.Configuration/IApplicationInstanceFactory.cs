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

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Creates fresh <see cref="IApplicationInstance"/> objects for a given
    /// hosted OPC UA server. Registered as a singleton by
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaConfigurationServiceCollectionExtensions.AddApplicationInstance(IOpcUaBuilder)"/>.
    /// </summary>
    /// <remarks>
    /// Each hosted server requires its own
    /// <see cref="IApplicationInstance"/> with distinct PKI roots,
    /// endpoint URLs, and certificate stores. A process-wide singleton
    /// would cause configuration overwrite and lifecycle conflicts;
    /// this factory keeps a single registered service that produces
    /// per-host instances on demand.
    /// </remarks>
    public interface IApplicationInstanceFactory
    {
        /// <summary>
        /// Creates a new <see cref="IApplicationInstance"/> using the
        /// supplied <paramref name="telemetry"/> for logging / tracing.
        /// </summary>
        /// <param name="telemetry">The telemetry context the new
        /// application instance should use.</param>
        IApplicationInstance Create(ITelemetryContext telemetry);
    }
}
