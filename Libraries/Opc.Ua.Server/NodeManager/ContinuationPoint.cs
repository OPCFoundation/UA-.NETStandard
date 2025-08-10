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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The table of all reference types known to the server.
    /// </summary>
    /// <remarks>This class is thread safe.</remarks>
    public class ContinuationPoint : IDisposable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ContinuationPoint() { }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(Data);
            }
        }

        /// <summary>
        /// A unique identifier for the continuation point.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The node manager that created the continuation point.
        /// </summary>
        public INodeManager Manager { get; set; }

        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription View { get; set; }

        /// <summary>
        /// The node being browsed.
        /// </summary>
        public object NodeToBrowse { get; set; }

        /// <summary>
        /// The maximum number of results to return.
        /// </summary>
        public uint MaxResultsToReturn { get; set; }

        /// <summary>
        /// What direction to follow the references.
        /// </summary>
        public BrowseDirection BrowseDirection { get; set; }

        /// <summary>
        /// The reference type of the references to return.
        /// </summary>
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Whether subtypes of the reference type should be return as well.
        /// </summary>
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// The node class of the target nodes for the references to return.
        /// </summary>
        public uint NodeClassMask { get; set; }

        /// <summary>
        /// The values to return.
        /// </summary>
        public BrowseResultMask ResultMask { get; set; }

        /// <summary>
        /// The index where browsing halted.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Node manager specific data that is necessary to continue the browse.
        /// </summary>
        /// <remarks>
        /// A node manager needs to hold onto unmanaged resources to continue the browse.
        /// If this is the case then the object stored here must implement the Idispose
        /// interface. This will ensure the unmanaged resources are freed if the continuation
        /// point expires.
        /// </remarks>
        public object Data { get; set; }

        /// <summary>
        /// Whether the ReferenceTypeId should be returned in the result.
        /// </summary>
        public bool ReferenceTypeIdRequired => ((int)ResultMask & (int)BrowseResultMask.ReferenceTypeId) != 0;

        /// <summary>
        /// Whether the IsForward flag should be returned in the result.
        /// </summary>
        public bool IsForwardRequired => ((int)ResultMask & (int)BrowseResultMask.IsForward) != 0;

        /// <summary>
        /// Whether the NodeClass should be returned in the result.
        /// </summary>
        public bool NodeClassRequired => ((int)ResultMask & (int)BrowseResultMask.NodeClass) != 0;

        /// <summary>
        /// Whether the BrowseName should be returned in the result.
        /// </summary>
        public bool BrowseNameRequired => ((int)ResultMask & (int)BrowseResultMask.BrowseName) != 0;

        /// <summary>
        /// Whether the DisplayName should be returned in the result.
        /// </summary>
        public bool DisplayNameRequired => ((int)ResultMask & (int)BrowseResultMask.DisplayName) != 0;

        /// <summary>
        /// Whether the TypeDefinition should be returned in the result.
        /// </summary>
        public bool TypeDefinitionRequired => ((int)ResultMask & (int)BrowseResultMask.TypeDefinition) != 0;

        /// <summary>
        /// False if it is not necessary to read the attributes a target node.
        /// </summary>
        /// <remarks>
        /// This flag is true if the NodeClass filter is set or the target node attributes are returned in the result.
        /// </remarks>
        public bool TargetAttributesRequired
        {
            get
            {
                if (NodeClassMask != 0)
                {
                    return true;
                }

                return (
                        (int)ResultMask
                        & (
                            (int)BrowseResultMask.NodeClass
                            | (int)BrowseResultMask.BrowseName
                            | (int)BrowseResultMask.DisplayName
                            | (int)BrowseResultMask.TypeDefinition
                        )
                    ) != 0;
            }
        }
    }
}
