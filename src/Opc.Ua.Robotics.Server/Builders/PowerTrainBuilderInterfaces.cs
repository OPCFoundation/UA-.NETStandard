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
    /// Builds a Robotics power train.
    /// </summary>
    public interface IPowerTrainBuilder : IRoboticsNodeBuilder<PowerTrainState>
    {
        /// <summary>
        /// Sets the optional component name.
        /// </summary>
        IPowerTrainBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized component name.
        /// </summary>
        IPowerTrainBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Adds a contained motor.
        /// </summary>
        IMotorBuilder AddMotor(string browseName, Action<IMotorBuilder>? configure = null);

        /// <summary>
        /// Adds a contained motor.
        /// </summary>
        IMotorBuilder AddMotor(QualifiedName browseName, Action<IMotorBuilder>? configure = null);

        /// <summary>
        /// Adds an optional contained gear.
        /// </summary>
        IGearBuilder AddGear(string browseName, Action<IGearBuilder>? configure = null);

        /// <summary>
        /// Adds an optional contained gear.
        /// </summary>
        IGearBuilder AddGear(QualifiedName browseName, Action<IGearBuilder>? configure = null);

        /// <summary>
        /// Adds the standard Moves relationship.
        /// </summary>
        IPowerTrainBuilder Moves(IAxisBuilder axis);

        /// <summary>
        /// Adds the standard HasSlave relationship.
        /// </summary>
        IPowerTrainBuilder HasSlave(IPowerTrainBuilder slave);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IPowerTrainBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds a Robotics motor.
    /// </summary>
    public interface IMotorBuilder : IRoboticsNodeBuilder<MotorState>
    {
        /// <summary>
        /// Sets inherited DI identification fields.
        /// </summary>
        IMotorBuilder WithIdentification(Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Sets mandatory motor temperature telemetry.
        /// </summary>
        IMotorBuilder WithMotorTemperature(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes and sets the optional brake-released telemetry.
        /// </summary>
        IMotorBuilder WithBrakeReleased(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes and sets the optional effective-load-rate telemetry.
        /// </summary>
        IMotorBuilder WithEffectiveLoadRate(
            ushort value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Binds asynchronous motor-temperature reads.
        /// </summary>
        IMotorBuilder BindMotorTemperature(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Materializes BrakeReleased and binds asynchronous reads.
        /// </summary>
        IMotorBuilder BindBrakeReleased(Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Materializes EffectiveLoadRate and binds asynchronous reads.
        /// </summary>
        IMotorBuilder BindEffectiveLoadRate(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Adds the optional standard IsDrivenBy relationship.
        /// </summary>
        IMotorBuilder IsDrivenBy(IDriveBuilder drive);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IMotorBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds a Robotics gear.
    /// </summary>
    public interface IGearBuilder : IRoboticsNodeBuilder<GearState>
    {
        /// <summary>
        /// Sets inherited DI identification fields.
        /// </summary>
        IGearBuilder WithIdentification(Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Sets the required transmission ratio.
        /// </summary>
        IGearBuilder WithGearRatio(int numerator, uint denominator);

        /// <summary>
        /// Materializes and sets optional pitch in millimetres per output-side
        /// revolution.
        /// </summary>
        IGearBuilder WithPitch(
            double millimetresPerRevolution,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IGearBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds a Robotics drive.
    /// </summary>
    public interface IDriveBuilder : IRoboticsNodeBuilder<DriveState>
    {
        /// <summary>
        /// Sets the mandatory product code.
        /// </summary>
        IDriveBuilder WithProductCode(string productCode);

        /// <summary>
        /// Sets the optional asset identifier.
        /// </summary>
        IDriveBuilder WithAssetId(string assetId);

        /// <summary>
        /// Sets the optional component name.
        /// </summary>
        IDriveBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized component name.
        /// </summary>
        IDriveBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IDriveBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }

    /// <summary>
    /// Builds an AuxiliaryComponentType instance.
    /// </summary>
    public interface IAuxiliaryComponentBuilder :
        IRoboticsNodeBuilder<AuxiliaryComponentState>
    {
        /// <summary>
        /// Sets the mandatory product code.
        /// </summary>
        IAuxiliaryComponentBuilder WithProductCode(string productCode);

        /// <summary>
        /// Sets the optional asset identifier.
        /// </summary>
        IAuxiliaryComponentBuilder WithAssetId(string assetId);

        /// <summary>
        /// Sets the optional component name.
        /// </summary>
        IAuxiliaryComponentBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized component name.
        /// </summary>
        IAuxiliaryComponentBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IAuxiliaryComponentBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }
}
