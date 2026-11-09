/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// The relationship to inspect without recursively expanding a graph.
    /// </summary>
    public enum MachineVisionRelationship
    {
        /// <summary>The standard same-entity relationship.</summary>
        SameEntity,

        /// <summary>The Part 2 optical path.</summary>
        OpticalPath,

        /// <summary>The Part 2 data transmission path.</summary>
        DataTransmission,

        /// <summary>Interfaces implemented by the selected object.</summary>
        Interface
    }

    /// <summary>
    /// One bounded page of deduplicated relationship references.
    /// </summary>
    /// <param name="References">Namespace-qualified references, preserving direction and type definitions.</param>
    /// <param name="NextIndex">The next unique-reference offset for a subsequent request.</param>
    /// <param name="IsComplete">Whether no further references existed at the time of the browse.</param>
    public sealed record MachineVisionRelationshipPage(
        ArrayOf<ReferenceDescription> References, uint NextIndex, bool IsComplete);

    /// <summary>
    /// Bounded Part 2 relationship operations on a Machine Vision session.
    /// </summary>
    public static class MachineVisionRelationshipClientExtensions
    {
        /// <summary>
        /// Reads one relationship page and releases any server browse continuation point.
        /// Pages are live views; they are not a snapshot across concurrent address-space edits.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static async ValueTask<MachineVisionRelationshipPage> ReadRelationshipsAsync(
            this MachineVisionClient client,
            NodeId node,
            MachineVisionRelationship relationship,
            BrowseDirection direction = BrowseDirection.Both,
            uint maximumResults = 50,
            uint startIndex = 0,
            CancellationToken cancellationToken = default)
        {
            MachineVisionAssetClientExtensions.Register(client);
            if (node.IsNull)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (maximumResults is 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResults),
                    "Relationship pages allow 1-100 references.");
            }
            if (startIndex > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Relationship offsets cannot exceed 10000.");
            }
            if (direction is not (BrowseDirection.Forward or BrowseDirection.Inverse or BrowseDirection.Both))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }
            NodeId referenceType = relationship switch
            {
                MachineVisionRelationship.SameEntity => ReferenceTypeIds.RepresentsSameEntityAs,
                MachineVisionRelationship.Interface => ReferenceTypeIds.HasInterface,
                MachineVisionRelationship.OpticalPath =>
                    ExpandedNodeId.ToNodeId(Amcm.ReferenceTypeIds.HasOpticalPathTo, client.Session.NamespaceUris),
                MachineVisionRelationship.DataTransmission =>
                    ExpandedNodeId.ToNodeId(Amcm.ReferenceTypeIds.TransmitsDataTo, client.Session.NamespaceUris),
                _ => throw new ArgumentOutOfRangeException(nameof(relationship))
            };
            if (referenceType.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The relationship namespace is absent.");
            }
            ArrayOf<BrowseDescription> requests =
            [
                new BrowseDescription
                {
                    NodeId = node,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceType,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            var page = new List<ReferenceDescription>();
            var seen = new HashSet<(ExpandedNodeId Node, NodeId Type, bool Forward)>();
            ByteString continuation = default;
            Exception? failure = null;
            MachineVisionRelationshipPage? resultPage = null;
            try
            {
                BrowseResponse response = await client.Session.BrowseAsync(
                    null, null, maximumResults, requests, cancellationToken).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requests);
                BrowseResult result = response.Results[0];
                int scanned = 0;
                while (true)
                {
                    if (++scanned > 20000)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The relationship browse exceeded its traversal budget.");
                    }
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        if (result.ContinuationPoint.Length != 0)
                        {
                            continuation = result.ContinuationPoint;
                        }
                        throw new ServiceResultException(result.StatusCode);
                    }
                    continuation = result.ContinuationPoint;
                    bool hasMore = false;
                    foreach (ReferenceDescription reference in result.References)
                    {
                        if (++scanned > 20000)
                        {
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                "The relationship browse exceeded its traversal budget.");
                        }
                        if (!seen.Add((reference.NodeId, reference.ReferenceTypeId, reference.IsForward)))
                        {
                            continue;
                        }
                        if ((uint)seen.Count <= startIndex)
                        {
                            continue;
                        }
                        if ((uint)page.Count == maximumResults)
                        {
                            hasMore = true;
                            break;
                        }
                        page.Add(reference);
                    }
                    if (hasMore || continuation.Length == 0)
                    {
                        resultPage = new MachineVisionRelationshipPage(
                            page.ToArrayOf(), startIndex + (uint)page.Count, !hasMore);
                        break;
                    }
                    ArrayOf<ByteString> points = [continuation];
                    BrowseNextResponse next =
                        await client.Session.BrowseNextAsync(null, false, points, cancellationToken)
                        .ConfigureAwait(false);
                    ClientBase.ValidateResponse(next.Results, points);
                    result = next.Results[0];
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            try
            {
                if (continuation.Length != 0)
                {
                    ArrayOf<ByteString> points = [continuation];
                    BrowseNextResponse released = await client.Session.BrowseNextAsync(
                        null, true, points, CancellationToken.None).ConfigureAwait(false);
                    ClientBase.ValidateResponse(released.Results, points);
                    if (StatusCode.IsBad(released.Results[0].StatusCode))
                    {
                        throw new ServiceResultException(released.Results[0].StatusCode);
                    }
                }
            }
            catch (Exception cleanup)
            {
                failure = failure == null ? cleanup :
                    new AggregateException("Relationship browse and continuation cleanup failed.", failure, cleanup);
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return resultPage ?? throw new InvalidOperationException("The relationship browse produced no result.");
        }
    }
}
