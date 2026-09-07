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
