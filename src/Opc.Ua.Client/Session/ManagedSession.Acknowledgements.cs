/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// The acknowledgement facet of a managed session. The configuration is fixed when the
    /// session is created and applies to every inner session it uses.
    /// </summary>
    public partial class ManagedSession : ISessionAcknowledgements
    {
        /// <inheritdoc/>
        public AcknowledgementMode Mode => m_acknowledgements?.Mode ?? AcknowledgementMode.Immediate;

        /// <inheritdoc/>
        public void Confirm(AcknowledgementHandle handle)
        {
            m_acknowledgements?.Confirm(handle);
        }

        /// <summary>
        /// Gets the acknowledgement configuration of the session, or <c>null</c>.
        /// </summary>
        internal SessionAcknowledgements? AcknowledgementSettings => m_acknowledgements;

        /// <summary>
        /// Creates the acknowledgement configuration from the session options.
        /// </summary>
        /// <param name="options">The session options.</param>
        /// <returns>The configuration, or <c>null</c> for the defaults.</returns>
        private static SessionAcknowledgements? CreateAcknowledgements(ManagedSessionOptions options)
        {
            return options.AcknowledgementMode == AcknowledgementMode.Immediate &&
                options.AcknowledgementProvider == null &&
                options.AcknowledgementObserver == null
                ? null
                : new SessionAcknowledgements(
                    options.AcknowledgementMode,
                    options.AcknowledgementProvider,
                    options.AcknowledgementObserver);
        }

        private SessionAcknowledgements? m_acknowledgements;
    }
}
