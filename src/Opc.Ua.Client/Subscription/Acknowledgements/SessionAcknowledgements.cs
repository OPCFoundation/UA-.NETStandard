// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
