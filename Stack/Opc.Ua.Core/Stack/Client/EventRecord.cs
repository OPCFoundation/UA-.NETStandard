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
    /// Abstract anchor record for all source-generated event records
    /// emitted by the <c>EventRecordGenerator</c>. The generated
    /// <c>BaseEventTypeRecord</c> derives directly from this type so
    /// every model's record hierarchy shares the same root and can
    /// be passed around as <c>EventRecord</c> by consumers that do
    /// not care about the specific event subtype.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Opc.Ua.Core</c> rather than <c>Opc.Ua.Client</c>
    /// because the standard UA NodeSet's generated records (which
    /// sit alongside the <c>*State</c> classes in
    /// <c>Opc.Ua.Core.Types</c>) need to derive from it. Vendor
    /// extensions in higher-level assemblies inherit through the
    /// generated record chain transparently.
    /// </remarks>
    public abstract record EventRecord;
}