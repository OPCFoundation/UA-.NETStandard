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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates the parameters of a <see cref="ViewDescription"/> that do not
    /// depend on the View node itself (OPC 10000-4, 7.44 and Table 178).
    /// </summary>
    internal static class ViewDescriptionValidator
    {
        /// <summary>
        /// Throws if the timestamp and version of the view description are
        /// inconsistent with each other or with the view id.
        /// </summary>
        /// <remarks>
        /// Either the Timestamp or the ViewVersion may be set, but not both
        /// (Bad_ViewParameterMismatch). Both select a version of a View, so
        /// setting either without a ViewId (the entire AddressSpace) requests
        /// a version that is not available (Bad_ViewTimestampInvalid or
        /// Bad_ViewVersionInvalid) rather than an unknown View.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        public static void ValidateParameters(ViewDescription? view)
        {
            if (view == null)
            {
                return;
            }

            bool hasTimestamp = view.Timestamp != DateTimeUtc.MinValue;
            bool hasVersion = view.ViewVersion != 0;

            if (hasTimestamp && hasVersion)
            {
                throw new ServiceResultException(StatusCodes.BadViewParameterMismatch);
            }

            if (view.ViewId.IsNull)
            {
                if (hasTimestamp)
                {
                    throw new ServiceResultException(StatusCodes.BadViewTimestampInvalid);
                }

                if (hasVersion)
                {
                    throw new ServiceResultException(StatusCodes.BadViewVersionInvalid);
                }
            }
        }
    }
}
