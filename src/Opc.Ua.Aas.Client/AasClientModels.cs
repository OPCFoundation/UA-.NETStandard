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

using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Client
{
    /// <summary>
    /// A browsed AAS node entry.
    /// </summary>
    /// <param name="NodeId">The target NodeId.</param>
    /// <param name="BrowseName">The target BrowseName.</param>
    /// <param name="DisplayName">The target DisplayName.</param>
    public sealed record AasBrowseEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName);

    /// <summary>
    /// The value read from an AAS element value Variable together with its declared xsd type.
    /// </summary>
    /// <param name="ElementNodeId">The owning element NodeId.</param>
    /// <param name="ValueNodeId">The element's Value Variable NodeId.</param>
    /// <param name="ValueType">The declared xsd type recovered from the Variable DataType.</param>
    /// <param name="RawValue">The OPC UA value.</param>
    /// <param name="LexicalValue">The canonical xsd lexical form.</param>
    public sealed record AasValueReadResult(
        NodeId ElementNodeId,
        NodeId ValueNodeId,
        AASDataTypeDefXsdDataType ValueType,
        Variant RawValue,
        string LexicalValue);

    /// <summary>
    /// The result of calling <c>AASOperationType.Invoke</c>.
    /// </summary>
    /// <param name="CallStatusCode">The OPC UA Call operation StatusCode.</param>
    /// <param name="OutputValues">The output arguments returned by the operation.</param>
    /// <param name="InoutputResults">The inoutput arguments returned by the operation.</param>
    /// <param name="Success">The AAS operation success flag.</param>
    /// <param name="Diagnostic">The AAS operation diagnostic text.</param>
    public sealed record AasOperationInvokeResult(
        StatusCode CallStatusCode,
        ArrayOf<Variant> OutputValues,
        ArrayOf<Variant> InoutputResults,
        bool Success,
        string Diagnostic);
}
