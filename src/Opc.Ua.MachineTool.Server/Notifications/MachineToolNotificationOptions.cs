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

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Explicitly enables the released Notification/Messages surface for one provider-backed machine.
    /// </summary>
    public sealed record MachineToolNotificationOptions
    {
        /// <summary>
        /// Gets the required, application-owned provider for all operator-visible errors, warnings and messages.
        /// </summary>
        public IMachineToolNotificationProvider? Provider { get; init; }

        /// <summary>
        /// Gets the maximum simultaneous condition identities, from one through 1024.
        /// </summary>
        public int MaximumAlerts { get; init; } = 128;

        /// <summary>
        /// Gets the maximum informational messages in one provider batch, from one through 1024.
        /// </summary>
        public int MaximumMessages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum characters in each identifier or localized text, from one through 65536.
        /// </summary>
        public int MaximumTextLength { get; init; } = 4096;

        /// <summary>
        /// Gets the cooperative acquisition/publication deadline, greater than zero and at most one minute.
        /// Providers and custom event sinks must honor cancellation.
        /// </summary>
        public TimeSpan RefreshTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets an optional generated alert-subtype factory. It must return an unpublished, unattached child of the
        /// supplied Messages node, with fresh application-owned NodeIds, and must not modify existing nodes.
        /// The default is the released concrete AlertType. Application event IDs never replace standard IDs.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, CancellationToken, ValueTask<AlertState>>?
            CreateAlertAsync
        { get; init; }

        /// <summary>
        /// Rejects incomplete selection before allocating any machine or event type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void Validate()
        {
            if (Provider == null ||
                MaximumAlerts is < 1 or > 1024 ||
                MaximumMessages is < 1 or > 1024 ||
                MaximumTextLength is < 1 or > 65536 ||
                RefreshTimeout <= TimeSpan.Zero ||
                RefreshTimeout > TimeSpan.FromMinutes(1))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Notifications require a provider and bounded alert, message and text limits.");
            }
        }
    }
}
