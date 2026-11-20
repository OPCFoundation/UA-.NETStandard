/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Extension methods for Robot Intent build contexts.
    /// </summary>
    public static class RobotIntentBuildContextExtensions
    {
        /// <summary>
        /// Adds one intent controller below Server/RobotIntent/Controllers.
        /// </summary>
        public static async ValueTask<IIntentControllerBuilder> AddIntentControllerAsync(
            this IRobotIntentBuildContext context,
            string browseName,
            System.Action<IIntentControllerBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new System.ArgumentNullException(nameof(context));
            }
            if (configure == null)
            {
                throw new System.ArgumentNullException(nameof(configure));
            }
            var builder = new IntentControllerBuilder(context, browseName);
            configure(builder);
            await builder.RegisterAsync(cancellationToken).ConfigureAwait(false);
            return builder;
        }
    }
}
