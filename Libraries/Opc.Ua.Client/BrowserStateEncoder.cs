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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Provides binary encode/decode methods for browser state types,
    /// replacing DataContractSerializer usage.
    /// </summary>
    internal static class BrowserStateEncoder
    {
        internal static void EncodeBrowserOptions(
            BinaryEncoder encoder,
            BrowserOptions options)
        {
            // RequestHeader (IEncodeable)
            encoder.WriteExtensionObject(null,
                options.RequestHeader != null
                    ? new ExtensionObject(options.RequestHeader)
                    : ExtensionObject.Null);

            // ViewDescription (IEncodeable)
            encoder.WriteExtensionObject(null,
                options.View != null
                    ? new ExtensionObject(options.View)
                    : ExtensionObject.Null);

            encoder.WriteUInt32(null, options.MaxReferencesReturned);
            encoder.WriteEnumerated(null, options.BrowseDirection);
            encoder.WriteNodeId(null, options.ReferenceTypeId);
            encoder.WriteBoolean(null, options.IncludeSubtypes);
            encoder.WriteInt32(null, options.NodeClassMask);
            encoder.WriteUInt32(null, options.ResultMask);
            encoder.WriteInt32(null, (int)options.ContinuationPointPolicy);
            encoder.WriteUInt32(null, options.MaxNodesPerBrowse);
            encoder.WriteUInt16(null, options.MaxBrowseContinuationPoints);
        }

        internal static BrowserOptions DecodeBrowserOptions(
            BinaryDecoder decoder)
        {
            // RequestHeader
            RequestHeader? requestHeader =
                decoder.ReadEncodeableAsExtensionObject<RequestHeader>(null);

            // ViewDescription
            ViewDescription? view =
                decoder.ReadEncodeableAsExtensionObject<ViewDescription>(null);

            uint maxReferencesReturned = decoder.ReadUInt32(null);
            BrowseDirection browseDirection =
                decoder.ReadEnumerated<BrowseDirection>(null);
            NodeId referenceTypeId = decoder.ReadNodeId(null);
            bool includeSubtypes = decoder.ReadBoolean(null);
            int nodeClassMask = decoder.ReadInt32(null);
            uint resultMask = decoder.ReadUInt32(null);
            var continuationPointPolicy =
                (ContinuationPointPolicy)decoder.ReadInt32(null);
            uint maxNodesPerBrowse = decoder.ReadUInt32(null);
            ushort maxBrowseContinuationPoints = decoder.ReadUInt16(null);

            return new BrowserOptions
            {
                RequestHeader = requestHeader,
                View = view,
                MaxReferencesReturned = maxReferencesReturned,
                BrowseDirection = browseDirection,
                ReferenceTypeId = referenceTypeId,
                IncludeSubtypes = includeSubtypes,
                NodeClassMask = nodeClassMask,
                ResultMask = resultMask,
                ContinuationPointPolicy = continuationPointPolicy,
                MaxNodesPerBrowse = maxNodesPerBrowse,
                MaxBrowseContinuationPoints = maxBrowseContinuationPoints
            };
        }
    }
}
