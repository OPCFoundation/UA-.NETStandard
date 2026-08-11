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

        private static async ValueTask<IMotionDeviceSystemBuilder>
            AddMotionDeviceSystemCoreAsync(
                IRoboticsBuildContext buildContext,
                QualifiedName browseName,
                Action<IMotionDeviceSystemBuilder> configure,
                CancellationToken cancellationToken)
        {
            RoboticsBuildScope? scope = null;
            bool registrationSucceeded = false;
            try
            {
                scope = new RoboticsBuildScope(buildContext, browseName);
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
                if (!registrationSucceeded)
                {
                    scope?.Abort();
                }
            }
        }
    }
}
