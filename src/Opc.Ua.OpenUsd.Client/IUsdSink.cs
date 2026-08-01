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

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// USD-side sink the <see cref="OpenUsdConnector"/> writes into. Values cross the
    /// boundary as <see cref="Variant"/> (never <see cref="object"/>): a scalar
    /// attribute is a <c>double</c>, a structured Translation/Rotation attribute is a
    /// three-element <c>double</c> array, a colour is a three-element <c>float</c>
    /// array (<see cref="ArrayOf{T}"/>), and a token/visibility value is a
    /// <c>string</c>.
    /// </summary>
    public interface IUsdSink
    {
        /// <summary>
        /// Writes (or overwrites) the current value of the named USD attribute on the
        /// prim at <paramref name="primPath"/>.
        /// </summary>
        void SetAttribute(string primPath, string propertyName, Variant value);

        /// <summary>
        /// Authors a USD time sample (for <c>UaHistoryToUsd</c> playback/scrubbing).
        /// The <paramref name="time"/> is mapped to a USD frame (seconds since the Unix
        /// epoch).
        /// </summary>
        void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value);

        /// <summary>
        /// Composes a component prim (§5.12): a <see cref="OpenUsdCompositionArc.Child"/>
        /// is an inline over/def prim; a Reference/Payload/Instance authors the
        /// references/payload (and <c>instanceable</c> for Instance) to the component
        /// asset. <paramref name="active"/> = <c>false</c> deactivates a removed
        /// component prim (dynamic composition, §5.13).
        /// </summary>
        void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active);

        /// <summary>
        /// Begins a batch. While the returned scope is open the sink may defer
        /// expensive flushes (e.g. rewriting a backing file) until the scope is
        /// disposed; sinks that do not buffer return a no-op scope. History replay uses
        /// this to author many time samples with a single flush.
        /// </summary>
        IDisposable BeginBatch();
    }
}
