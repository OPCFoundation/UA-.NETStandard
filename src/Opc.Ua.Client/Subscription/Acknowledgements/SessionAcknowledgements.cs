/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    /// <summary>
    /// The acknowledgement configuration of a session: the mode, the providers the
    /// subscription engines use and the confirmations of the application.
    /// </summary>
    internal sealed class SessionAcknowledgements : ISessionAcknowledgements
    {
        /// <summary>
        /// Creates the acknowledgement configuration of a session.
        /// </summary>
        /// <param name="mode">When the session acknowledges notifications.</param>
        /// <param name="provider">An application provider, or <c>null</c>.</param>
        /// <param name="observer">An application observer, or <c>null</c>.</param>
        /// <exception cref="ArgumentException">An application provider is combined with
        /// <see cref="AcknowledgementMode.AfterConfirmation"/>.</exception>
        public SessionAcknowledgements(
            AcknowledgementMode mode,
            ISubscriptionAcknowledgementProvider? provider,
            ISubscriptionAcknowledgementObserver? observer)
        {
            Mode = mode;
            Observer = observer;
            if (mode == AcknowledgementMode.AfterConfirmation)
            {
                if (provider != null)
                {
                    throw new ArgumentException(
                        "An acknowledgement provider cannot be combined with acknowledging after confirmation.",
                        nameof(provider));
                }
                m_confirmations = new ConfirmedAcknowledgementProvider();
                Provider = m_confirmations;
            }
            else
            {
                Provider = provider;
            }
        }

        /// <inheritdoc/>
        public AcknowledgementMode Mode { get; }

        /// <summary>
        /// Gets the provider the subscription engines consult, or <c>null</c> to use the
        /// legacy deferral event.
        /// </summary>
        public ISubscriptionAcknowledgementProvider? Provider { get; }

        /// <summary>
        /// Gets the observer the subscription engines report outcomes to, or <c>null</c>.
        /// </summary>
        public ISubscriptionAcknowledgementObserver? Observer { get; }

        /// <inheritdoc/>
        public void Confirm(AcknowledgementHandle handle)
        {
            m_confirmations?.Confirm(handle);
        }

        private readonly ConfirmedAcknowledgementProvider? m_confirmations;
    }
}
