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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Asynchronously validates and/or transforms the effective
    /// <see cref="ReverseConnectClientConfiguration"/> that a
    /// <see cref="ReverseConnectManager"/> is about to activate.
    /// </summary>
    /// <remarks>
    /// A provider participates in the async lifecycle after the candidate
    /// configuration is selected and before any listener is opened. Providers
    /// are invoked outside the manager's lifecycle gate so they may perform
    /// asynchronous work (for example reading additional endpoints from a
    /// store) without blocking concurrent lifecycle operations.
    /// The default implementation is a pass-through
    /// (<see cref="DefaultReverseConnectConfigurationProvider"/>) so
    /// direct-constructor usage keeps its original behavior.
    /// </remarks>
    public interface IReverseConnectConfigurationProvider
    {
        /// <summary>
        /// Produces the effective reverse-connect configuration to activate.
        /// </summary>
        /// <param name="applicationConfiguration">
        /// Optional application-configuration context. Present when the
        /// lifecycle was started from an
        /// <see cref="ApplicationConfiguration"/>; <c>null</c> when started
        /// from a bare <see cref="ReverseConnectClientConfiguration"/>.
        /// </param>
        /// <param name="configuration">
        /// The candidate configuration supplied to the lifecycle.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The effective configuration to activate.</returns>
        ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
            ApplicationConfiguration? applicationConfiguration,
            ReverseConnectClientConfiguration configuration,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default pass-through
    /// <see cref="IReverseConnectConfigurationProvider"/> that returns the
    /// candidate configuration unchanged.
    /// </summary>
    public sealed class DefaultReverseConnectConfigurationProvider
        : IReverseConnectConfigurationProvider
    {
        /// <summary>
        /// Shared stateless instance used as the direct-constructor fallback.
        /// </summary>
        public static DefaultReverseConnectConfigurationProvider Instance { get; } = new();

        /// <inheritdoc/>
        public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
            ApplicationConfiguration? applicationConfiguration,
            ReverseConnectClientConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReverseConnectClientConfiguration>(
                configuration ?? new ReverseConnectClientConfiguration());
        }
    }
}
