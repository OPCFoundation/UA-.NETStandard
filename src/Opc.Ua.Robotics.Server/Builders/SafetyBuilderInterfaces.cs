// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Builds aggregate and per-function safety state.
    /// </summary>
    public interface ISafetyStateBuilder : IRoboticsNodeBuilder<SafetyStateState>
    {
        /// <summary>
        /// Sets the optional component name.
        /// </summary>
        ISafetyStateBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized component name.
        /// </summary>
        ISafetyStateBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Sets the aggregate EmergencyStop value.
        /// </summary>
        ISafetyStateBuilder WithEmergencyStop(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Sets the aggregate OperationalMode value.
        /// </summary>
        ISafetyStateBuilder WithOperationalMode(
            OperationalModeEnumeration value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Sets the aggregate ProtectiveStop value.
        /// </summary>
        ISafetyStateBuilder WithProtectiveStop(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous aggregate EmergencyStop reads.
        /// </summary>
        ISafetyStateBuilder BindEmergencyStop(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous aggregate OperationalMode reads.
        /// </summary>
        ISafetyStateBuilder BindOperationalMode(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous aggregate ProtectiveStop reads.
        /// </summary>
        ISafetyStateBuilder BindProtectiveStop(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Adds an emergency-stop function.
        /// </summary>
        IEmergencyStopBuilder AddEmergencyStop(
            string browseName,
            Action<IEmergencyStopBuilder>? configure = null);

        /// <summary>
        /// Adds and names an emergency-stop function.
        /// </summary>
        IEmergencyStopBuilder AddEmergencyStop(
            string browseName,
            string name,
            Action<IEmergencyStopBuilder>? configure = null);

        /// <summary>
        /// Adds an emergency-stop function.
        /// </summary>
        IEmergencyStopBuilder AddEmergencyStop(
            QualifiedName browseName,
            Action<IEmergencyStopBuilder>? configure = null);

        /// <summary>
        /// Adds a protective-stop function.
        /// </summary>
        IProtectiveStopBuilder AddProtectiveStop(
            string browseName,
            Action<IProtectiveStopBuilder>? configure = null);

        /// <summary>
        /// Adds and names a protective-stop function.
        /// </summary>
        IProtectiveStopBuilder AddProtectiveStop(
            string browseName,
            string name,
            Action<IProtectiveStopBuilder>? configure = null);

        /// <summary>
        /// Adds a protective-stop function.
        /// </summary>
        IProtectiveStopBuilder AddProtectiveStop(
            QualifiedName browseName,
            Action<IProtectiveStopBuilder>? configure = null);
    }

    /// <summary>
    /// Builds an EmergencyStopFunctionType instance.
    /// </summary>
    public interface IEmergencyStopBuilder :
        IRoboticsNodeBuilder<EmergencyStopFunctionState>
    {
        /// <summary>
        /// Sets the required function name.
        /// </summary>
        IEmergencyStopBuilder WithName(string name);

        /// <summary>
        /// Sets Active.
        /// </summary>
        IEmergencyStopBuilder WithActive(
            bool active,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous Active reads.
        /// </summary>
        IEmergencyStopBuilder BindActive(
            Func<CancellationToken, ValueTask<DataValue>> read);
    }

    /// <summary>
    /// Builds a ProtectiveStopFunctionType instance.
    /// </summary>
    public interface IProtectiveStopBuilder :
        IRoboticsNodeBuilder<ProtectiveStopFunctionState>
    {
        /// <summary>
        /// Sets the required function name.
        /// </summary>
        IProtectiveStopBuilder WithName(string name);

        /// <summary>
        /// Sets Active.
        /// </summary>
        IProtectiveStopBuilder WithActive(
            bool active,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Sets Enabled.
        /// </summary>
        IProtectiveStopBuilder WithEnabled(
            bool enabled,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous Active reads.
        /// </summary>
        IProtectiveStopBuilder BindActive(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous Enabled reads.
        /// </summary>
        IProtectiveStopBuilder BindEnabled(
            Func<CancellationToken, ValueTask<DataValue>> read);
    }
}
