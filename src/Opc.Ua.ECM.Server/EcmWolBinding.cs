// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Redaction;
using Opc.Ua.Server;
using static Opc.Ua.ECM.Server.EcmModelNodes;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Binds a standalone WOL object without requiring standby management or owning the provider.
    /// </summary>
    public sealed class EcmWolBinding : IAsyncDisposable
    {
        /// <summary>
        /// Configures an unregistered WOL graph. Control uses the same role and DI policy as standby.
        /// </summary>
        public EcmWolBinding(
            ISystemContext context,
            EnergyDevicePowerOffState powerOff,
            IEcmWolProvider provider,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_context = context;
            PowerOff = powerOff ?? throw new ArgumentNullException(nameof(powerOff));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_authorize = authorize;
            m_exclusiveAccess = exclusiveAccess;
            m_logger = context.Telemetry.CreateLogger<EcmWolBinding>();
            ConfigureMetadata(powerOff, provider);
            Required(PowerOff.SwitchOffWOL).OnCallAsync = async (caller, method, objectId, ct) =>
            {
                EcmControlResult result = await m_operations.ExecuteAsync(
                    token => SwitchOffAsync(caller, token), ct).ConfigureAwait(false);
                return new SwitchOffWOLMethodStateResult
                {
                    ServiceResult = new ServiceResult(result.Status),
                    ModeID = result.ModeId,
                    CurrentTimeToDestination = result.CurrentTimeToDestination,
                    RegularTimeToOperate = result.RegularTimeToOperate,
                    TimeMinLengthOfStay = result.TimeMinLengthOfStay,
                    ReturnCode = result.ReturnCode.GetValueOrDefault()
                };
            };
        }

        /// <summary>
        /// Gets the generated WOL object.
        /// </summary>
        public EnergyDevicePowerOffState PowerOff { get; }

        /// <summary>Gets the server associated with this binding's system context.</summary>
        internal IServerInternal? Server => (m_context as ServerSystemContext)?.Server;

        /// <summary>Gets whether this binding has been disposed.</summary>
        internal bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

        /// <summary>
        /// Joins provider work and removes callbacks without disposing the borrowed provider.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref m_disposed, 1);
            try
            {
                await m_operations.StopAsync(() => Required(PowerOff.SwitchOffWOL).OnCallAsync = null)
                    .ConfigureAwait(false);
            }
            finally
            {
                await m_operations.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates all provider metadata before publishing values or installing callbacks.
        /// </summary>
        internal static void ConfigureMetadata(EnergyDevicePowerOffState node, IEcmWolProvider provider)
        {
            ByteString mac = ByteString.From(provider.MacAddress.Span);
            double recovery = provider.RegularTimeToOperate;
            double minimum = provider.TimeMinPause;
            uint power = provider.ModePowerConsumption;
            if (mac.Length != 6)
            {
                throw new ArgumentException("WOLMagicPacket requires six MAC address bytes.", nameof(provider));
            }
            EcmControlResult.ValidateDuration(recovery, nameof(provider));
            EcmControlResult.ValidateDuration(minimum, nameof(provider));
            var packetNode = Required(node.WOLMagicPacket);
            var recoveryNode = Required(node.RegularTimeToOperate);
            var minimumNode = Required(node.TimeMinPause);
            var powerNode = Required(node.ModePowerConsumption);
            _ = Required(node.SwitchOffWOL);
            packetNode.Value = mac;
            recoveryNode.Value = recovery;
            minimumNode.Value = minimum;
            powerNode.Value = power;
        }

        /// <summary>
        /// Requires the reserved destination on acceptance and zero destination on device rejection.
        /// </summary>
        internal static void ValidateResult(EcmControlResult result)
        {
            if ((StatusCode.IsGood(result.Status) && result.ModeId != 0xFE) ||
                (StatusCode.IsUncertain(result.Status) && result.ModeId != 0))
            {
                throw new InvalidOperationException(
                    "WOL sleep requires ModeID 0xFE when accepted and zero when rejected.");
            }
        }

        /// <summary>
        /// Calls the provider once after authorization, preserving independent Call and device status.
        /// </summary>
        private async ValueTask<EcmControlResult> SwitchOffAsync(
            ISystemContext caller,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!await EcmPowerAccess.IsAuthorizedAsync(
                    caller, PowerOff.NodeId, m_authorize, m_exclusiveAccess, cancellationToken).ConfigureAwait(false))
                {
                    return new EcmControlResult(StatusCodes.BadUserAccessDenied, null);
                }
                EcmControlResult result = await m_provider.SwitchOffWolAsync(cancellationToken).ConfigureAwait(false);
                ValidateResult(result);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (m_logger.IsEnabled(LogLevel.Error))
                {
                    m_logger.WolProviderFailed(exception.ToString());
                }
                return new EcmControlResult(StatusCodes.BadUnexpectedError, null);
            }
        }

        /// <summary>The borrowed WOL provider.</summary>
        private readonly IEcmWolProvider m_provider;

        /// <summary>The system context used to create and publish the bound nodes.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The optional application policy.</summary>
        private readonly Func<ISystemContext, CancellationToken, ValueTask<bool>>? m_authorize;

        /// <summary>The optional DI ownership service.</summary>
        private readonly ILockService? m_exclusiveAccess;

        /// <summary>The provider diagnostic logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The shared admission and serialization mechanism.</summary>
        private readonly EcmBindingOperations m_operations = new();

        /// <summary>Whether disposal has started.</summary>
        private int m_disposed;
    }

    /// <summary>
    /// Source-generated standalone WOL diagnostics.
    /// </summary>
    internal static partial class EcmWolBindingLog
    {
        /// <summary>
        /// Records a provider failure with classified exception text.
        /// </summary>
        [LoggerMessage(
            EventId = EcmServerEventIds.EcmWolBinding,
            Level = LogLevel.Error,
            Message = "The ECM WOL provider failed: {Error}")]
        public static partial void WolProviderFailed(this ILogger logger, [SensitiveText] string error);
    }
}
