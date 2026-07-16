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

namespace Opc.Ua.PubSub.Adapter.Actions
{
    /// <summary>
    /// Resolves a PubSub Action target to the external OPC UA object and
    /// method that an <see cref="ServerActionHandler"/> calls when the
    /// action is invoked. The optional output field names are applied, in
    /// order, to the method's output arguments when the result is mapped back
    /// to a PubSub Action response; positions without a configured name fall
    /// back to a generated <c>Output{i}</c> name.
    /// </summary>
    public readonly record struct ActionMethodBinding
    {
        /// <summary>
        /// Initializes a new <see cref="ActionMethodBinding"/>.
        /// </summary>
        /// <param name="objectId">
        /// The external object that provides the method to call.
        /// </param>
        /// <param name="methodId">
        /// The external method invoked for the action.
        /// </param>
        /// <param name="outputFieldNames">
        /// Optional output field names applied, in order, to the method's
        /// output arguments. Defaults to empty, in which case generated names
        /// are used.
        /// </param>
        public ActionMethodBinding(
            NodeId objectId,
            NodeId methodId,
            ArrayOf<string> outputFieldNames = default)
        {
            ObjectId = objectId;
            MethodId = methodId;
            OutputFieldNames = outputFieldNames;
        }

        /// <summary>
        /// The external object that provides the method to call.
        /// </summary>
        public NodeId ObjectId { get; init; }

        /// <summary>
        /// The external method invoked for the action.
        /// </summary>
        public NodeId MethodId { get; init; }

        /// <summary>
        /// Output field names applied, in order, to the method's output
        /// arguments. Empty when generated names should be used.
        /// </summary>
        public ArrayOf<string> OutputFieldNames { get; init; }
    }
}
