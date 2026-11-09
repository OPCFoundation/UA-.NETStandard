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
