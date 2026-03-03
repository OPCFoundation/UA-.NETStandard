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

namespace Opc.Ua
{
    /// <summary>
    /// Interface for extended methods for JSON encoders based on IEncoder.
    /// </summary>
    public interface IJsonEncoder : IEncoder
    {
        /// <summary>
        /// The type of JSON encoding being used.
        /// </summary>
        JsonEncodingType EncodingToUse { get; }

        /// <summary>
        /// Force the Json encoder to encode namespace URI instead of
        /// namespace Index in NodeIds.
        /// </summary>
        bool ForceNamespaceUri { get; set; }

        /// <summary>
        /// Force the Json encoder to suppress UA specific artifacts needed for decoding.
        /// </summary>
        bool SuppressArtifacts { get; set; }

        /// <summary>
        /// Push the begin of an array on the encoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the array field.</param>
        void PushArray(string fieldName);

        /// <summary>
        /// Push the begin of a structure on the encoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the structure field.</param>
        void PushStructure(string fieldName);

        /// <summary>
        /// Pop the array from the encoder stack.
        /// </summary>
        void PopArray();

        /// <summary>
        /// Pop the structure from the encoder stack.
        /// </summary>
        void PopStructure();

        /// <summary>
        /// Call an IEncoder action where the alternate encoding type is applied
        /// before the call to the Action and restored before return.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        void UsingAlternateEncoding<T>(
            Action<string, T> action,
            string fieldName,
            T value,
            JsonEncodingType useEncodingType);
    }
}
