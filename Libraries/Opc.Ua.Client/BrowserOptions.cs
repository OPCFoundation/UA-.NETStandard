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

using System.Text.Json.Serialization;

namespace Opc.Ua.Client
{
    [JsonSerializable(typeof(BrowserOptions))]
    internal partial class BrowserOptionsContext : JsonSerializerContext;

    /// <summary>
    /// Stores the options to use for a browse operation. Can be serialized and
    /// deserialized via the source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public partial record class BrowserOptions
    {
        /// <summary>
        /// Request header to use for the browse operations.
        /// </summary>
        [DataTypeField(Order = 0)]
        public RequestHeader? RequestHeader { get; set; }

        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        [DataTypeField(Order = 1)]
        public ViewDescription? View { get; set; }

        /// <summary>
        /// The maximum number of references to return in a single browse operation.
        /// </summary>
        [DataTypeField(Order = 2)]
        public uint MaxReferencesReturned { get; set; }

        /// <summary>
        /// The direction to browse.
        /// </summary>
        [DataTypeField(Order = 3)]
        public BrowseDirection BrowseDirection { get; set; } = BrowseDirection.Forward;

        /// <summary>
        /// The reference type to follow.
        /// </summary>
        [DataTypeField(Order = 4)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Whether subtypes of the reference type should be included.
        /// </summary>
        [DataTypeField(Order = 5, DefaultValueHandling = DefaultValueHandling.Emit)]
        public bool IncludeSubtypes { get; set; } = true;

        /// <summary>
        /// The classes of the target nodes.
        /// </summary>
        [DataTypeField(Order = 6)]
        public int NodeClassMask { get; set; }

        /// <summary>
        /// The results to return.
        /// </summary>
        [DataTypeField(Order = 7)]
        public uint ResultMask { get; set; } = (uint)BrowseResultMask.All;

        /// <summary>
        /// gets or set the policy which is used to prevent the allocation
        /// of too many Continuation Points in the browse operation
        /// </summary>
        [DataTypeField(Order = 8)]
        public ContinuationPointPolicy ContinuationPointPolicy { get; set; }

        /// <summary>
        /// Max nodes to browse in a single operation.
        /// </summary>
        [DataTypeField(Order = 9)]
        public uint MaxNodesPerBrowse { get; set; }

        /// <summary>
        /// Max continuation points to use when ContinuationPointPolicy is set
        /// to Balanced.
        /// </summary>
        [DataTypeField(Order = 10)]
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
