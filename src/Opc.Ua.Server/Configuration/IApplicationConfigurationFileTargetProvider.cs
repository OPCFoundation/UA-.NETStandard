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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional extension of <see cref="IApplicationConfigurationFileProvider"/>
    /// for providers that honour the OPC 10000-12 §7.8.5.2 <c>CloseAndUpdate</c>
    /// <c>Targets</c> and <c>RestartDelayTime</c> arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ApplicationConfigurationFile"/> handler detects this
    /// interface at runtime. When the provider implements it, the Client's
    /// <c>Targets</c> are passed to validation and apply ("Contents of the file
    /// which are not referenced by a target are ignored"), a non-Good
    /// per-target result rejects the whole update with <c>Uncertain</c>, and an
    /// update that interrupts active Sessions is applied only after the
    /// Client-supplied <c>RestartDelayTime</c> so the Client can receive the
    /// <c>CloseAndUpdate</c> response first.
    /// </para>
    /// <para>
    /// Providers that only implement
    /// <see cref="IApplicationConfigurationFileProvider"/> keep the previous
    /// behaviour: the whole file is validated and applied immediately.
    /// </para>
    /// </remarks>
    public interface IApplicationConfigurationFileTargetProvider : IApplicationConfigurationFileProvider
    {
        /// <summary>
        /// Validates the targets of a proposed configuration update without
        /// applying it.
        /// </summary>
        /// <param name="configuration">The proposed configuration content.</param>
        /// <param name="targets">The Client-supplied update targets (at least one).</param>
        /// <param name="cancellationToken">A token used to cancel the validation.</param>
        /// <returns>The per-target results and how the update must be applied.</returns>
        /// <exception cref="ServiceResultException">
        /// Thrown when the proposed configuration as a whole is invalid; the
        /// carried <see cref="StatusCode"/> is surfaced to the Client.
        /// </exception>
        ValueTask<ApplicationConfigurationUpdatePlan> ValidateConfigurationAsync(
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically applies the targets of a previously validated
        /// configuration. Contents not referenced by a target are ignored. On
        /// failure the implementation must throw having left the active
        /// configuration unchanged. On success it updates
        /// <see cref="IApplicationConfigurationFileProvider.CurrentVersion"/>
        /// and <see cref="IApplicationConfigurationFileProvider.LastUpdateTime"/>.
        /// </summary>
        /// <param name="configuration">The validated configuration content.</param>
        /// <param name="targets">The Client-supplied update targets.</param>
        /// <param name="cancellationToken">A token used to cancel the apply.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the configuration cannot be applied.
        /// </exception>
        ValueTask ApplyConfigurationAsync(
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The outcome of
    /// <see cref="IApplicationConfigurationFileTargetProvider.ValidateConfigurationAsync(ByteString, ArrayOf{ConfigurationUpdateTargetType}, CancellationToken)"/>.
    /// </summary>
    public sealed class ApplicationConfigurationUpdatePlan
    {
        /// <summary>
        /// The per-target validation results, in the order of the
        /// <c>Targets</c> argument. An empty array means every target is
        /// Good. If any element is not Good no changes are applied and
        /// <c>CloseAndUpdate</c> returns <c>Uncertain</c> (§7.8.5.2).
        /// </summary>
        public ArrayOf<StatusCode> TargetResults { get; init; }

        /// <summary>
        /// <see langword="true"/> when applying the update will interrupt
        /// active Sessions (for example because endpoints are closed). The
        /// apply is then deferred by the Client-supplied
        /// <c>RestartDelayTime</c> (§7.8.5.2).
        /// </summary>
        public bool InterruptsSessions { get; init; }

        /// <summary>
        /// The <c>CurrentVersion</c> the configuration will have once a
        /// deferred apply completes. Returned as <c>NewVersion</c> when the
        /// apply is deferred; ignored when the update is applied immediately.
        /// </summary>
        public uint NewVersion { get; init; }
    }
}
