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
using Opc.Ua.Di.Server;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Creates companion subtypes through the existing Robotics topology builders.
    /// </summary>
    public static class RoboticsCompanionBuilderExtensions
    {
        /// <summary>
        /// Enlists companion bindings in this system's single registration and rollback.
        /// </summary>
        /// <remarks>
        /// Ownership of the participant transfers on success. The caller retains an unenlisted
        /// participant if this method throws. Participants are prepared before topology validation,
        /// bound after publication, and committed only after every binding has succeeded.
        /// </remarks>
        /// <param name="builder">The owning system builder.</param>
        /// <param name="participant">A single-use binding for the same root and manager.</param>
        /// <returns>The owning system builder.</returns>
        public static IMotionDeviceSystemBuilder WithRegistrationParticipant(
            this IMotionDeviceSystemBuilder builder,
            ICompanionRegistrationParticipant participant)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            if (builder is not MotionDeviceSystemBuilder implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The system builder does not support registration participants.");
            }
            implementation.Scope.Enlist(participant);
            return builder;
        }

        /// <summary>
        /// Adds a derived motion device while preserving standard topology validation and lifetime.
        /// </summary>
        /// <typeparam name="TState">The derived motion-device state.</typeparam>
        /// <param name="builder">The containing system builder.</param>
        /// <param name="browseName">The device's instance browse name.</param>
        /// <param name="createDevice">Creates an initialized device with the supplied parent and name.</param>
        /// <param name="configure">Optional device configuration.</param>
        /// <returns>The motion-device builder.</returns>
        /// <exception cref="ArgumentNullException">The builder or factory is null.</exception>
        /// <exception cref="ServiceResultException">The builder or factory instance is incompatible.</exception>
        public static IMotionDeviceBuilder AddMotionDevice<TState>(
            this IMotionDeviceSystemBuilder builder,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createDevice,
            Action<IMotionDeviceBuilder>? configure = null)
            where TState : MotionDeviceState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (createDevice == null)
            {
                throw new ArgumentNullException(nameof(createDevice));
            }
            if (builder is not MotionDeviceSystemBuilder implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The system builder does not support companion motion-device factories.");
            }
            return implementation.AddMotionDevice(browseName, createDevice, configure);
        }

        /// <summary>
        /// Adds a derived axis while preserving standard relationships, validation and lifetime.
        /// </summary>
        /// <typeparam name="TState">The derived axis state.</typeparam>
        /// <param name="builder">The containing motion-device builder.</param>
        /// <param name="browseName">The axis's instance browse name.</param>
        /// <param name="createAxis">Creates an initialized axis with the supplied parent and name.</param>
        /// <param name="configure">Optional axis configuration.</param>
        /// <returns>The axis builder.</returns>
        /// <exception cref="ArgumentNullException">The builder or factory is null.</exception>
        /// <exception cref="ServiceResultException">The builder or factory instance is incompatible.</exception>
        public static IAxisBuilder AddAxis<TState>(
            this IMotionDeviceBuilder builder,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createAxis,
            Action<IAxisBuilder>? configure = null)
            where TState : AxisState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (createAxis == null)
            {
                throw new ArgumentNullException(nameof(createAxis));
            }
            if (builder is not MotionDeviceBuilder implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The motion-device builder does not support companion axis factories.");
            }
            return implementation.AddAxis(browseName, createAxis, configure);
        }

        /// <summary>
        /// Adds a derived motor while preserving standard relationships, validation and lifetime.
        /// </summary>
        /// <typeparam name="TState">The derived motor state.</typeparam>
        /// <param name="builder">The containing power-train builder.</param>
        /// <param name="browseName">The motor's instance browse name.</param>
        /// <param name="createMotor">Creates an initialized motor with the supplied parent and name.</param>
        /// <param name="configure">Optional motor configuration.</param>
        /// <returns>The motor builder.</returns>
        /// <exception cref="ArgumentNullException">The builder or factory is null.</exception>
        /// <exception cref="ServiceResultException">The builder or factory instance is incompatible.</exception>
        public static IMotorBuilder AddMotor<TState>(
            this IPowerTrainBuilder builder,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createMotor,
            Action<IMotorBuilder>? configure = null)
            where TState : MotorState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (createMotor == null)
            {
                throw new ArgumentNullException(nameof(createMotor));
            }
            if (builder is not PowerTrainBuilder implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The power-train builder does not support companion motor factories.");
            }
            return implementation.AddMotor(browseName, createMotor, configure);
        }
    }
}
