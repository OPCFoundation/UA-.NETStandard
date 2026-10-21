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
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Saves history continuation points within the session limit.
    /// </summary>
    internal static class HistoryContinuationPoints
    {
        /// <summary>
        /// Saves a history continuation point for the session of the request.
        /// </summary>
        /// <remarks>
        /// OPC 10000-4 7.9: once a request has used MaxHistoryContinuationPoints
        /// points the remaining operations get Bad_NoContinuationPoints, while
        /// points of prior requests are freed when needed. Returns <c>false</c> when
        /// the point was refused; the caller then returns Bad_NoContinuationPoints.
        /// Sessions other than <see cref="Opc.Ua.Server.Session"/> use the legacy
        /// <see cref="ISession.SaveHistoryContinuationPoint(Guid, object)"/>.
        /// Without a session nothing is saved, as before.
        /// </remarks>
        public static bool TrySave(ServerSystemContext context, Guid id, object continuationPoint)
        {
            OperationContext operationContext = context?.OperationContext;
            ISession session = operationContext?.Session;

            if (session == null)
            {
                return true;
            }

            if (session is Opc.Ua.Server.Session serverSession)
            {
                return serverSession.TrySaveHistoryContinuationPoint(
                    id,
                    continuationPoint,
                    operationContext.RequestId);
            }

            session.SaveHistoryContinuationPoint(id, continuationPoint);
            return true;
        }
    }
}
