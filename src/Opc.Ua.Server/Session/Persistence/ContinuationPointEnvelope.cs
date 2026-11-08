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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Serializable envelope for a mirrored continuation point.
    /// </summary>
    /// <remarks>
    /// The built-in Session creates history envelopes with only the continuation identifier, session identifier and
    /// kind; Browse envelopes also include the original request parameters and index. The envelope does not carry
    /// <see cref="ContinuationPoint.Data"/> or reconstruct a cursor on a backup. A client resuming through the built-in
    /// Session store with only this envelope receives <see cref="StatusCodes.BadContinuationPointInvalid"/> and must
    /// issue a new Browse or HistoryRead to continue. Portable historian continuations use a separate
    /// <see cref="IHistoryContinuationPointStore"/>.
    /// </remarks>
    public sealed class ContinuationPointEnvelope
    {
        /// <summary>
        /// The continuation point id.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// The session id that owned the continuation point on the active replica.
        /// </summary>
        public NodeId OwnerSessionId { get; init; }

        /// <summary>
        /// The continuation point family.
        /// </summary>
        public ContinuationPointKind Kind { get; init; }

        /// <summary>
        /// The original browse target, when available.
        /// </summary>
        public NodeId BrowseNodeId { get; init; }

        /// <summary>
        /// The browse view, when available.
        /// </summary>
        public ViewDescription? View { get; init; }

        /// <summary>
        /// The requested maximum references per Browse result.
        /// </summary>
        public uint MaxResultsToReturn { get; init; }

        /// <summary>
        /// The requested browse direction.
        /// </summary>
        public BrowseDirection BrowseDirection { get; init; }

        /// <summary>
        /// The requested reference type filter.
        /// </summary>
        public NodeId ReferenceTypeId { get; init; }

        /// <summary>
        /// Whether reference subtypes were included.
        /// </summary>
        public bool IncludeSubtypes { get; init; }

        /// <summary>
        /// The requested node-class mask.
        /// </summary>
        public uint NodeClassMask { get; init; }

        /// <summary>
        /// The requested browse result mask.
        /// </summary>
        public BrowseResultMask ResultMask { get; init; }

        /// <summary>
        /// The index at which the original replica paused.
        /// </summary>
        public int Index { get; init; }
    }
}
