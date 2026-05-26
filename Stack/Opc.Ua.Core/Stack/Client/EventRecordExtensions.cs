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

namespace Opc.Ua
{
    /// <summary>
    /// Hand-written convenience extensions for the source-generated
    /// <see cref="ConditionTypeRecord"/>. Lives in <c>Opc.Ua.Client</c>
    /// (alongside the alarm client APIs) so that the standard
    /// <c>Opc.Ua.Core</c> assembly that emits the generated record
    /// does not need to take on client-side helpers.
    /// </summary>
    public partial record ConditionTypeRecord
    {
        /// <summary>
        /// The NodeId of the condition instance the event was raised
        /// from. This is an alias for <see cref="BaseEventTypeRecord.SourceNode"/>
        /// — Part 9 of the OPC UA specification defines the
        /// "ConditionId" of an event as the NodeId of the condition
        /// object that fired the event, which is reported through the
        /// <c>SourceNode</c> field.
        /// </summary>
        public NodeId ConditionId => SourceNode;
    }
}
