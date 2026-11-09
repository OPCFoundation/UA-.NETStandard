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

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Entry points for progressively building Robotics topology instances.
    /// </summary>
    public static class RoboticsBuildContextExtensions
    {
        /// <summary>
        /// Configures, validates, assigns and asynchronously registers one
        /// MotionDeviceSystem below the DI DeviceSet.
        /// </summary>
        public static async ValueTask<IMotionDeviceSystemBuilder>
            AddMotionDeviceSystemAsync(
                this IRoboticsBuildContext buildContext,
                string browseName,
                Action<IMotionDeviceSystemBuilder> configure,
                CancellationToken cancellationToken = default)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            using IDisposable lease = RoboticsBuildScope.AcquireBuildLease(buildContext);
            return await AddMotionDeviceSystemCoreAsync(
                buildContext,
                RoboticsBuilderUtilities.NormalizeBrowseName(buildContext, browseName),
                configure,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Configures, validates, assigns and asynchronously registers one
        /// MotionDeviceSystem below the DI DeviceSet.
        /// </summary>
        public static async ValueTask<IMotionDeviceSystemBuilder>
            AddMotionDeviceSystemAsync(
                this IRoboticsBuildContext buildContext,
                QualifiedName browseName,
                Action<IMotionDeviceSystemBuilder> configure,
                CancellationToken cancellationToken = default)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            using IDisposable lease = RoboticsBuildScope.AcquireBuildLease(buildContext);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(buildContext, browseName);
            return await AddMotionDeviceSystemCoreAsync(
                buildContext,
                normalized,
                configure,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates or adopts a derived system through the existing Robotics validation and registration path.
        /// </summary>
        /// <typeparam name="TState">The derived motion-device-system state.</typeparam>
        /// <param name="buildContext">The existing Robotics owner context.</param>
        /// <param name="browseName">The system's instance browse name.</param>
        /// <param name="createSystem">
        /// Creates an initialized subtype with the supplied parent and browse name, or returns an
        /// unregistered existing instance with that identity.
        /// </param>
        /// <param name="configure">Configures the standard topology and companion members.</param>
        /// <param name="cancellationToken">Cancels configuration and registration.</param>
        /// <returns>The registered system builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The factory returns an incompatible instance or the topology is invalid.
        /// </exception>
        public static async ValueTask<IMotionDeviceSystemBuilder> AddMotionDeviceSystemAsync<TState>(
            this IRoboticsBuildContext buildContext,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createSystem,
            Action<IMotionDeviceSystemBuilder> configure,
            CancellationToken cancellationToken = default)
            where TState : MotionDeviceSystemState
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
            if (createSystem == null)
            {
                throw new ArgumentNullException(nameof(createSystem));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            using IDisposable lease = RoboticsBuildScope.AcquireBuildLease(buildContext);
            QualifiedName normalized = RoboticsBuilderUtilities.NormalizeBrowseName(buildContext, browseName);
            return await AddMotionDeviceSystemCoreAsync(
                buildContext,
                normalized,
                configure,
                cancellationToken,
                (context, parent, name) => RoboticsBuilderUtilities.CreateCompanionInstance(
                    context,
                    parent,
                    name,
                    createSystem,
                    ObjectTypes.MotionDeviceSystemType)).ConfigureAwait(false);
        }

        private static async ValueTask<IMotionDeviceSystemBuilder>
            AddMotionDeviceSystemCoreAsync(
                IRoboticsBuildContext buildContext,
                QualifiedName browseName,
                Action<IMotionDeviceSystemBuilder> configure,
                CancellationToken cancellationToken,
                Func<ISystemContext, NodeState, QualifiedName, MotionDeviceSystemState>? createSystem = null)
        {
            RoboticsBuildScope? scope = null;
            bool registrationSucceeded = false;
            try
            {
                scope = new RoboticsBuildScope(buildContext, browseName, createSystem);
                configure(scope.RootBuilder);

                CancellationToken contextToken = buildContext.CancellationToken;
                if (!contextToken.CanBeCanceled ||
                    contextToken == cancellationToken ||
                    !cancellationToken.CanBeCanceled)
                {
                    CancellationToken effectiveToken = cancellationToken.CanBeCanceled
                        ? cancellationToken
                        : contextToken;
                    await scope.RegisterAsync(effectiveToken).ConfigureAwait(false);
                }
                else
                {
                    using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                        contextToken,
                        cancellationToken);
                    await scope.RegisterAsync(linkedSource.Token).ConfigureAwait(false);
                }

                registrationSucceeded = true;
                return scope.RootBuilder;
            }
            finally
            {
                if (!registrationSucceeded && scope != null)
                {
                    await scope.AbortAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
