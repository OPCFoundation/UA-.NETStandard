/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Opc.Ua.Client
{
    [JsonSerializable(typeof(BrowserOptions))]
    internal partial class BrowserOptionsContext : JsonSerializerContext;

    /// <summary>
    /// Stores the options to use for a browse operation. Can be serialized and
    /// deserialized.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public record class BrowserOptions
    {
        /// <summary>
        /// Request header to use for the browse operations.
        /// </summary>
        [DataMember(Order = 0)]
        public RequestHeader? RequestHeader { get; init; }

        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        [DataMember(Order = 1)]
        public ViewDescription? View { get; init; }

        /// <summary>
        /// The maximum number of references to return in a single browse operation.
        /// </summary>
        [DataMember(Order = 2)]
        public uint MaxReferencesReturned { get; init; }

        /// <summary>
        /// The direction to browse.
        /// </summary>
        [DataMember(Order = 3)]
        public BrowseDirection BrowseDirection { get; init; } = BrowseDirection.Forward;

        /// <summary>
        /// The reference type to follow.
        /// </summary>
        [DataMember(Order = 4)]
        public NodeId ReferenceTypeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether subtypes of the reference type should be included.
        /// </summary>
        [DataMember(Order = 5)]
        public bool IncludeSubtypes { get; init; } = true;

        /// <summary>
        /// The classes of the target nodes.
        /// </summary>
        [DataMember(Order = 6)]
        public int NodeClassMask { get; init; }

        /// <summary>
        /// The results to return.
        /// </summary>
        [DataMember(Order = 7)]
        public uint ResultMask { get; init; } = (uint)BrowseResultMask.All;

        /// <summary>
        /// gets or set the policy which is used to prevent the allocation
        /// of too many Continuation Points in the browse operation
        /// </summary>
        [DataMember(Order = 8)]
        public ContinuationPointPolicy ContinuationPointPolicy { get; init; }

        /// <summary>
        /// Max nodes to browse in a single operation.
        /// </summary>
        [DataMember(Order = 9)]
        public uint MaxNodesPerBrowse { get; set; }

        /// <summary>
        /// Max continuation points to use when ContinuationPointPolicy is set
        /// to Balanced.
        /// </summary>
        [DataMember(Order = 10)]
        public ushort MaxBrowseContinuationPoints { get; set; }
    }

    /// <summary>
    /// controls how the browser treats continuation points if the server has
    /// restrictions on their number.
    /// </summary>
    public enum ContinuationPointPolicy
    {
        /// <summary>
        /// Ignore how many Continuation Points are in use already.
        /// Rebrowse nodes for which BadNoContinuationPoint or
        /// BadInvalidContinuationPoint was raised. Can be used
        /// whenever the server has no restrictions no the maximum
        /// number of continuation points
        /// </summary>
        Default,

        /// <summary>
        /// Restrict the number of nodes which are browsed in a
        /// single service call to the maximum number of
        /// continuation points the server can allocae
        /// (if set to a value different from 0)
        /// </summary>
        Balanced
    }
}
