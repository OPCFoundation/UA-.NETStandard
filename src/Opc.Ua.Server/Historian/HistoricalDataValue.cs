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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A raw historical sample returned from an <see cref="IHistorianDataProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Value"/> is the underlying <see cref="DataValue"/> with its
    /// <c>SourceTimestamp</c> set to the storage key (Part 11 §5.2.4 — values
    /// are addressed by <c>SourceTimestamp</c>). The framework reconciles
    /// <c>ServerTimestamp</c>, <c>TimestampsToReturn</c>, index ranges and
    /// data encodings before forwarding the value to the client.
    /// </para>
    /// <para>
    /// <see cref="IsBound"/> indicates that this sample is the closest
    /// matching sample outside the requested time window (the "before" /
    /// "after" bound). The framework only emits a bound when the request
    /// specified <c>ReturnBounds=true</c>; providers should always supply
    /// them when available so the framework can decide whether to keep them.
    /// </para>
    /// </remarks>
    public readonly record struct HistoricalDataValue(DataValue Value, bool IsBound = false);
}
