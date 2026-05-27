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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ModelChange
{
    /// <summary>
    /// Describes a single address-space model change reported by the server.
    /// </summary>
    /// <param name="Verb">The change verb.</param>
    /// <param name="AffectedNode">NodeId of the node affected.</param>
    /// <param name="TypeDefinition">Optional type definition NodeId.</param>
    public record struct ModelChange(
        ModelChangeVerb Verb,
        NodeId AffectedNode,
        NodeId? TypeDefinition);

    /// <summary>
    /// The kind of address-space change reported.
    /// </summary>
    [Flags]
    public enum ModelChangeVerb : byte
    {
        /// <summary>
        /// No change.
        /// </summary>
        None = 0,
        /// <summary>
        /// A new node was added.
        /// </summary>
        NodeAdded = 1,
        /// <summary>
        /// An existing node was deleted.
        /// </summary>
        NodeDeleted = 2,
        /// <summary>
        /// A reference was added.
        /// </summary>
        ReferenceAdded = 4,
        /// <summary>
        /// A reference was deleted.
        /// </summary>
        ReferenceDeleted = 8,
        /// <summary>
        /// The DataType attribute changed.
        /// </summary>
        DataTypeChanged = 16,
    }

    /// <summary>
    /// Event arguments raised when the server reports address-space model changes.
    /// </summary>
    public sealed class ModelChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The reported changes.
        /// </summary>
        public IReadOnlyList<ModelChange> Changes { get; }

        /// <summary>
        /// True when the change indicates that the entire cache
        /// should be invalidated.
        /// </summary>
        public bool RequiresFullCacheInvalidation { get; }

        /// <summary>
        /// Constructs new args.
        /// </summary>
        public ModelChangedEventArgs(
            IReadOnlyList<ModelChange> changes,
            bool requiresFullCacheInvalidation)
        {
            Changes = changes ?? Array.Empty<ModelChange>();
            RequiresFullCacheInvalidation = requiresFullCacheInvalidation;
        }
    }

    /// <summary>
    /// Tracks server-reported address-space model changes via an
    /// event subscription on the Server object's
    /// <c>GeneralModelChangeEventType</c> notifier.
    /// </summary>
    public interface IModelChangeTracker : IAsyncDisposable
    {
        /// <summary>
        /// Raised when model changes are observed.
        /// </summary>
        event EventHandler<ModelChangedEventArgs>? ModelChanged;

        /// <summary>
        /// True once tracking has been started.
        /// </summary>
        bool IsTracking { get; }

        /// <summary>
        /// Starts tracking model changes.
        /// </summary>
        ValueTask StartTrackingAsync(CancellationToken ct = default);

        /// <summary>
        /// Stops tracking model changes.
        /// </summary>
        ValueTask StopTrackingAsync(CancellationToken ct = default);
    }
}
