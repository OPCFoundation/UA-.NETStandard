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

using System;
using System.Collections.Generic;

namespace Opc.Ua.AMB.Server.Structure
{
    /// <summary>
    /// Adds the entries of a <c>Requirements</c> or <c>Capabilities</c>
    /// folder (OPC 10000-110 §10.6, §10.7).
    /// </summary>
    public interface IAssetEntriesBuilder
    {
        /// <summary>
        /// Adds an entry: a variable organized by the folder.
        /// </summary>
        /// <param name="name">The browse name, in the namespace of the asset.</param>
        /// <param name="value">The value; its type decides the data type.</param>
        /// <param name="dictionaryEntry">
        /// An entry of an external dictionary such as IEC CDD or ECLASS the
        /// variable references with <c>0:HasDictionaryEntry</c>, as the
        /// specification recommends; none when null.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or used already.
        /// </exception>
        IAssetEntriesBuilder Add(string name, Variant value, ExpandedNodeId dictionaryEntry = default);
    }

    /// <summary>
    /// An entry of a <c>Requirements</c> or <c>Capabilities</c> folder.
    /// </summary>
    /// <param name="Name">The browse name.</param>
    /// <param name="Value">The value.</param>
    /// <param name="DictionaryEntry">The dictionary entry, or null.</param>
    internal sealed record AssetEntry(string Name, Variant Value, ExpandedNodeId DictionaryEntry);

    /// <summary>
    /// The entries an application declared for a folder.
    /// </summary>
    internal sealed class AssetEntriesRequest : IAssetEntriesBuilder
    {
        /// <summary>
        /// Gets the entries.
        /// </summary>
        public List<AssetEntry> Entries { get; } = [];

        /// <inheritdoc/>
        public IAssetEntriesBuilder Add(string name, Variant value, ExpandedNodeId dictionaryEntry = default)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An entry needs a name.", nameof(name));
            }
            if (Entries.Exists(entry => string.Equals(entry.Name, name, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"The folder has an entry named '{name}' already.", nameof(name));
            }
            Entries.Add(new AssetEntry(name, value, dictionaryEntry));
            return this;
        }
    }
}
