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

using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Client.V2
{
    /// <summary>
    /// A browsed AAS V2 node entry.
    /// </summary>
    /// <param name="NodeId">The target NodeId.</param>
    /// <param name="BrowseName">The target BrowseName.</param>
    /// <param name="DisplayName">The target DisplayName.</param>
    public sealed record AasBrowseEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName);

    /// <summary>
    /// The value read from an AAS V2 element together with its declared AAS value type.
    /// </summary>
    /// <param name="ElementNodeId">The owning element NodeId.</param>
    /// <param name="ValueNodeId">The element's Value Variable NodeId.</param>
    /// <param name="ValueTypeNodeId">The element's ValueType Property NodeId.</param>
    /// <param name="ValueType">The declared AAS V2 value type.</param>
    /// <param name="RawValue">The OPC UA value.</param>
    public sealed record AasValueReadResult(
        NodeId ElementNodeId,
        NodeId ValueNodeId,
        NodeId ValueTypeNodeId,
        AASValueTypeDataType ValueType,
        Variant RawValue);

    /// <summary>
    /// The result of calling an AAS V2 <c>Operation</c> Method.
    /// </summary>
    /// <param name="CallStatusCode">The OPC UA Call operation StatusCode.</param>
    /// <param name="OutputValues">The output arguments returned by the Method.</param>
    /// <param name="Success">Whether the OPC UA Call StatusCode was good.</param>
    /// <param name="Diagnostic">The diagnostic text for a failed Call.</param>
    public sealed record AasOperationInvokeResult(
        StatusCode CallStatusCode,
        ArrayOf<Variant> OutputValues,
        bool Success,
        string Diagnostic);
}
