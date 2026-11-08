/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// Allows a caller to require a specific server ApplicationUri before session credentials are activated.
    /// </summary>
    public interface IExpectedServerApplicationUriSession
    {
        /// <summary>
        /// Requires the CreateSession endpoint description to report the specified ApplicationUri.
        /// </summary>
        /// <param name="expectedApplicationUri">The expected absolute server ApplicationUri.</param>
        /// <remarks>
        /// Configure this requirement before opening the session. A mismatch rejects the session before
        /// ActivateSession sends user credentials. Generic sessions do not enable this check by default.
        /// </remarks>
        void RequireServerApplicationUri(string expectedApplicationUri);
    }
}
