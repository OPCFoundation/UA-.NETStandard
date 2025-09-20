/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        public static async ValueTask<(
            IList<object>,
            IList<ServiceResult>
            )> ReadValuesAsync(
                this ISession session,
                IList<NodeId> variableIds,
                IList<Type> expectedTypes,
                CancellationToken ct = default)
        {
            (DataValueCollection dataValues, IList<ServiceResult> errors) =
                await session.ReadValuesAsync(
                    variableIds,
                    ct).ConfigureAwait(false);

            object[] values = new object[dataValues.Count];
            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                object value = dataValues[ii].Value;

                // extract the body from extension objects.
                if (value is ExtensionObject extension &&
                    extension.Body is IEncodeable)
                {
                    value = extension.Body;
                }

                // check expected type.
                if (expectedTypes[ii] != null &&
                    !expectedTypes[ii].IsInstanceOfType(value))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "Value {0} does not have expected type: {1}.",
                        value,
                        expectedTypes[ii].Name);
                    continue;
                }

                // suitable value found.
                values[ii] = value;
            }
            return (values, errors);
        }

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(
            ResponseHeader,
            byte[],
            ReferenceDescriptionCollection
            )> BrowseAsync(
                this ISession session,
                RequestHeader requestHeader,
                ViewDescription view,
                NodeId nodeToBrowse,
                uint maxResultsToReturn,
                BrowseDirection browseDirection,
                NodeId referenceTypeId,
                bool includeSubtypes,
                uint nodeClassMask,
                CancellationToken ct = default)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;
            ByteStringCollection continuationPoints;
            (responseHeader, continuationPoints, referencesList, errors) =
                await session.BrowseAsync(
                    requestHeader,
                    view,
                    [nodeToBrowse],
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask,
                    ct).ConfigureAwait(false);

            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(continuationPoints.Count == 1);
            return (responseHeader, continuationPoints[0], referencesList[0]);
        }

        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(
            ResponseHeader,
            byte[],
            ReferenceDescriptionCollection
            )> BrowseNextAsync(
                this ISession session,
                RequestHeader requestHeader,
                bool releaseContinuationPoint,
                byte[] continuationPoint,
                CancellationToken ct = default)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;

            ByteStringCollection revisedContinuationPoints;
            (responseHeader, revisedContinuationPoints, referencesList, errors) =
                await session.BrowseNextAsync(requestHeader, [continuationPoint], releaseContinuationPoint, ct)
                    .ConfigureAwait(false);
            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(revisedContinuationPoints.Count == 1);
            return (responseHeader, revisedContinuationPoints[0], referencesList[0]);
        }
    }
}
