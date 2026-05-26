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
    /// A sample returned from an <see cref="IHistorianModifiedProvider.ReadModifiedAsync"/>
    /// call: the underlying <see cref="DataValue"/> plus the
    /// <see cref="ModificationInfo"/> describing how/when/by-whom the
    /// sample was modified or deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modified history (Part 11 §5.2.5) returns all replaced and deleted
    /// versions of samples in the requested time range plus their
    /// <see cref="ModificationInfo"/>. Providers must order results by
    /// <c>SourceTimestamp</c> ascending; samples for the same source
    /// timestamp must be ordered by <see cref="ModificationInfo.ModificationTime"/>
    /// ascending.
    /// </para>
    /// </remarks>
    public readonly record struct ModifiedDataValue(DataValue Value, ModificationInfo Info);
}
