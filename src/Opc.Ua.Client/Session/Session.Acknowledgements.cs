// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// The acknowledgement facet of a session.
    /// </summary>
    public partial class Session : ISessionAcknowledgements
    {
        /// <inheritdoc/>
        public AcknowledgementMode Mode => Acknowledgements?.Mode ?? AcknowledgementMode.Immediate;

        /// <summary>
        /// Gets or sets the acknowledgement configuration the subscription engines use. A
        /// managed session assigns its configuration before the session publishes.
        /// </summary>
        internal SessionAcknowledgements? Acknowledgements { get; set; }

        /// <inheritdoc/>
        public void Confirm(AcknowledgementHandle handle)
        {
            Acknowledgements?.Confirm(handle);
        }
    }
}
