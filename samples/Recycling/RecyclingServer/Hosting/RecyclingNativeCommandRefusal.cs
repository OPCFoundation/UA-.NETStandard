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
using Opc.Ua.Extrusion.Server;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Explicit read-only authorities for the native family binders. No command is accepted on behalf
    /// of a process-service credential, and no success-shaped no-op can mutate a native cache.
    /// </summary>
    internal sealed class RecyclingNativeCommandRefusal(RecyclingAuthorization authorization) :
        IExtrusionCommandProvider,
        IGranulateDryingAuthorization,
        IPlasticsRubberAccessPolicy,
        IStartDeviceProvider,
        IMonitoredParameterProvider,
        IClosedLoopControlProvider,
        IMachineConfigurationProvider
    {
        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            ExtrusionCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            authorization.Resolve(command.Context);
            return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public ValueTask<ExtrusionCommandResult> ExecuteAsync(
            ExtrusionCommand command, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public bool CanControl(ISystemContext context) => false;

        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            ISystemContext context, GranulateDryingCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            authorization.Resolve(context);
            return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public ValueTask AuthorizeAsync(
            PlasticsRubberRequestContext context, NodeId memberId, CancellationToken cancellationToken = default)
        {
            authorization.Resolve(context.SystemContext);
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetStartBlockedAsync(
            PlasticsRubberRequestContext context, bool blocked, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask WriteSettingAsync(
            PlasticsRubberRequestContext context, MonitoredParameterSetting setting, double value,
            CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetAutomaticMonitoringAsync(
            PlasticsRubberRequestContext context, bool automatic, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetSensitivityAsync(
            PlasticsRubberRequestContext context, ushort sensitivity, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetAlarmSuppressionAsync(
            PlasticsRubberRequestContext context, ushort suppression, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<MonitoredParameterLimits> ResetMonitoringAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetParametersAsync(
            PlasticsRubberRequestContext context, ArrayOf<PIDParametersDataType> parameters,
            CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetAutomaticModeAsync(
            PlasticsRubberRequestContext context, bool automatic, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StartTuningAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StopTuningAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetMachineNameAsync(
            PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetLocationNameAsync(
            PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetTimeZoneAsync(
            PlasticsRubberRequestContext context,
            TimeZoneDataType timeZone,
            CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetMachineTimeAsync(
            PlasticsRubberRequestContext context, DateTimeUtc utcTime, TimeZoneDataType timeZone,
            CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> GetPageAsync(
            PlasticsRubberRequestContext context, string id, CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> GetCurrentPageAsync(
            PlasticsRubberRequestContext context, uint visualizationUnit,
            CancellationToken cancellationToken = default)
        {
            Refuse(cancellationToken);
            return default;
        }

        private static void Refuse(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "Native production controls require original-user forwarding. " +
                "Use the configured process command seam.");
        }
    }
}
