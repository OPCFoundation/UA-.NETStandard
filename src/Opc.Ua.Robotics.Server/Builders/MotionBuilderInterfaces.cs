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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Builds a Robotics motion device.
    /// </summary>
    public interface IMotionDeviceBuilder : IRoboticsNodeBuilder<MotionDeviceState>
    {
        /// <summary>
        /// Sets inherited DI identification fields.
        /// </summary>
        IMotionDeviceBuilder WithIdentification(Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Sets the required motion-device category.
        /// </summary>
        IMotionDeviceBuilder WithCategory(MotionDeviceCategoryEnumeration category);

        /// <summary>
        /// Sets the required motion-device category.
        /// </summary>
        IMotionDeviceBuilder WithMotionDeviceCategory(
            MotionDeviceCategoryEnumeration category);

        /// <summary>
        /// Sets the optional component name.
        /// </summary>
        IMotionDeviceBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized component name.
        /// </summary>
        IMotionDeviceBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Adds an axis to the mandatory Axes folder.
        /// </summary>
        IAxisBuilder AddAxis(string browseName, Action<IAxisBuilder>? configure = null);

        /// <summary>
        /// Adds an axis to the mandatory Axes folder.
        /// </summary>
        IAxisBuilder AddAxis(QualifiedName browseName, Action<IAxisBuilder>? configure = null);

        /// <summary>
        /// Adds a power train to the mandatory PowerTrains folder.
        /// </summary>
        IPowerTrainBuilder AddPowerTrain(
            string browseName,
            Action<IPowerTrainBuilder>? configure = null);

        /// <summary>
        /// Adds a power train to the mandatory PowerTrains folder.
        /// </summary>
        IPowerTrainBuilder AddPowerTrain(
            QualifiedName browseName,
            Action<IPowerTrainBuilder>? configure = null);

        /// <summary>
        /// Materializes and returns the optional FlangeLoad.
        /// </summary>
        ILoadBuilder WithFlangeLoad();

        /// <summary>
        /// Materializes and configures the optional FlangeLoad.
        /// </summary>
        IMotionDeviceBuilder WithFlangeLoad(Action<ILoadBuilder> configure);

        /// <summary>
        /// Adds an auxiliary component to AdditionalComponents.
        /// </summary>
        IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            string browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null);

        /// <summary>
        /// Adds an auxiliary component to AdditionalComponents.
        /// </summary>
        IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            QualifiedName browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null);

        /// <summary>
        /// Adds a drive to AdditionalComponents.
        /// </summary>
        IDriveBuilder AddDrive(string browseName, Action<IDriveBuilder>? configure = null);

        /// <summary>
        /// Adds a drive to AdditionalComponents.
        /// </summary>
        IDriveBuilder AddDrive(QualifiedName browseName, Action<IDriveBuilder>? configure = null);

        /// <summary>
        /// Sets the mandatory speed override value.
        /// </summary>
        IMotionDeviceBuilder WithSpeedOverride(
            double value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous reads for SpeedOverride.
        /// </summary>
        IMotionDeviceBuilder BindSpeedOverrideRead(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous writes for SpeedOverride.
        /// </summary>
        IMotionDeviceBuilder BindSpeedOverrideWrite(
            Func<Variant, CancellationToken, ValueTask<ServiceResult>> write);

        /// <summary>
        /// Binds asynchronous reads and writes for SpeedOverride.
        /// </summary>
        IMotionDeviceBuilder BindSpeedOverride(
            Func<CancellationToken, ValueTask<DataValue>> read,
            Func<Variant, CancellationToken, ValueTask<ServiceResult>> write);

        /// <summary>
        /// Adds the standard Controls relationship from a task control.
        /// </summary>
        IMotionDeviceBuilder UsesTaskControl(ITaskControlBuilder taskControl);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IMotionDeviceBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds a Robotics axis and its telemetry.
    /// </summary>
    public interface IAxisBuilder : IRoboticsNodeBuilder<AxisState>
    {
        /// <summary>
        /// Sets the required motion profile.
        /// </summary>
        IAxisBuilder WithMotionProfile(AxisMotionProfileEnumeration profile);

        /// <summary>
        /// Marks the axis as virtual for Requires-cardinality validation.
        /// </summary>
        IAxisBuilder AsVirtual(bool isVirtual = true);

        /// <summary>
        /// Sets mandatory ActualPosition telemetry.
        /// </summary>
        IAxisBuilder WithActualPosition(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes and sets optional ActualSpeed telemetry.
        /// </summary>
        IAxisBuilder WithActualSpeed(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes and sets optional ActualAcceleration telemetry.
        /// </summary>
        IAxisBuilder WithActualAcceleration(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous reads for ActualPosition.
        /// </summary>
        IAxisBuilder BindActualPosition(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Materializes ActualSpeed and binds asynchronous reads.
        /// </summary>
        IAxisBuilder BindActualSpeed(Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Materializes ActualAcceleration and binds asynchronous reads.
        /// </summary>
        IAxisBuilder BindActualAcceleration(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Materializes and returns the optional AdditionalLoad.
        /// </summary>
        ILoadBuilder WithAdditionalLoad();

        /// <summary>
        /// Materializes and configures the optional AdditionalLoad.
        /// </summary>
        IAxisBuilder WithAdditionalLoad(Action<ILoadBuilder> configure);

        /// <summary>
        /// Adds the standard Requires relationship to a power train.
        /// </summary>
        IAxisBuilder Requires(IPowerTrainBuilder powerTrain);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IAxisBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds the standard Robotics LoadType.
    /// </summary>
    public interface ILoadBuilder : IRoboticsNodeBuilder<LoadState>
    {
        /// <summary>
        /// Sets the mandatory mass and optional engineering metadata.
        /// </summary>
        ILoadBuilder WithMass(
            double mass,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes and sets CenterOfMass.
        /// </summary>
        ILoadBuilder WithCenterOfMass(ThreeDFrame centerOfMass);

        /// <summary>
        /// Materializes and sets Inertia.
        /// </summary>
        ILoadBuilder WithInertia(ThreeDVector inertia);
    }
}
