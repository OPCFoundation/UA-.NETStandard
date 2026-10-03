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

using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Records, for any <see cref="ISession"/> implementation, whether its client
    /// application certificate passed the server's validation when the session was
    /// created.
    /// </summary>
    /// <remarks>
    /// A certificate whose validation error an
    /// <see cref="StandardServer.OnApplicationCertificateError"/> override accepted
    /// still signs the session, but it does not establish a trusted application
    /// identity for role assignment (OPC 10000-3 4.9, OPC 10000-18 4.4.4). The
    /// provenance is kept outside the session object so that a session created by a
    /// custom <see cref="SessionManager"/> factory is covered like a
    /// <see cref="Session"/>.
    /// </remarks>
    internal static class ClientCertificateProvenance
    {
        /// <summary>
        /// Whether the client certificate of the session passed validation. A
        /// session that was never marked is validated.
        /// </summary>
        public static bool IsValidated(ISession session)
        {
            return !s_unvalidated.TryGetValue(session, out _);
        }

        /// <summary>
        /// Records whether the client certificate of the session passed validation.
        /// </summary>
        public static void SetValidated(ISession session, bool validated)
        {
            if (validated)
            {
                s_unvalidated.Remove(session);
            }
            else
            {
                s_unvalidated.GetValue(session, static _ => s_marker);
            }
        }

        private static readonly object s_marker = new();

        private static readonly ConditionalWeakTable<ISession, object> s_unvalidated =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif
    }
}
