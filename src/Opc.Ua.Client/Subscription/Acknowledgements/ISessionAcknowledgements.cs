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

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The acknowledgement facet of a session.
    /// </summary>
    public interface ISessionAcknowledgements
    {
        /// <summary>
        /// Gets when the session acknowledges notifications.
        /// </summary>
        AcknowledgementMode Mode { get; }

        /// <summary>
        /// Confirms that a notification was processed, so that its acknowledgement can be
        /// sent. Only has an effect in <see cref="AcknowledgementMode.AfterConfirmation"/>
        /// mode, for an acknowledgement that has not ended yet. Confirming a handle more than
        /// once, after its acknowledgement ended, or after its subscription was retired has
        /// no effect.
        /// </summary>
        /// <param name="handle">The acknowledgement of the processed notification.</param>
        void Confirm(AcknowledgementHandle handle);
    }

    /// <summary>
    /// Extensions to obtain the acknowledgement facet of a session.
    /// </summary>
    public static class SessionAcknowledgementsExtensions
    {
        /// <summary>
        /// Gets the acknowledgement facet of a session, if it supports one.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="acknowledgements">The facet.</param>
        /// <returns><c>true</c> if the session supports the facet.</returns>
        public static bool TryGetAcknowledgements(
            this ISession session,
            [NotNullWhen(true)] out ISessionAcknowledgements? acknowledgements)
        {
            acknowledgements = session as ISessionAcknowledgements;
            return acknowledgements != null;
        }
    }
}
