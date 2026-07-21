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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Bounds enforced by the registry service before a document is accepted or
    /// persisted. They cap the resource cost of a hostile or misbehaving client
    /// and mirror the bounded-resolution limits used by the
    /// <see cref="Opc.Ua.Wot.WotNodeSetConverter"/>.
    /// </summary>
    public sealed class WotRegistryPersistenceBounds
    {
        /// <summary>Gets or sets the maximum accepted size of a single document.</summary>
        public int MaxDocumentBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>Gets or sets the maximum number of retained versions per resource.</summary>
        public int MaxVersionsPerResource { get; set; } = 32;

        /// <summary>Gets or sets the maximum number of resources per group.</summary>
        public int MaxResourcesPerGroup { get; set; } = 1024;

        /// <summary>Gets or sets the maximum number of groups.</summary>
        public int MaxGroups { get; set; } = 64;

        /// <summary>
        /// Gets or sets the maximum number of concurrently open FileType handles
        /// per document resource.
        /// </summary>
        public int MaxOpenFileHandles { get; set; } = 8;

        /// <summary>
        /// Gets or sets the maximum JSON nesting depth accepted when the
        /// service parses a document for metadata extraction.
        /// </summary>
        public int MaxJsonDepth { get; set; } = 64;

        /// <summary>
        /// Gets or sets the maximum number of xRegistry labels/attributes
        /// retained on a single entity (registry, group or resource).
        /// </summary>
        public int MaxLabelsPerEntity { get; set; } = 64;

        /// <summary>
        /// Gets or sets the maximum length of a label key. Also bounds the
        /// BrowseName/NodeId path segment materialized for the label.
        /// </summary>
        public int MaxLabelKeyLength { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum length of a label value.
        /// </summary>
        public int MaxLabelValueLength { get; set; } = 4096;

        /// <summary>
        /// Validates the bounds and throws when any limit is not strictly positive.
        /// </summary>
        public void Validate()
        {
            EnsurePositive(MaxDocumentBytes, nameof(MaxDocumentBytes));
            EnsurePositive(MaxVersionsPerResource, nameof(MaxVersionsPerResource));
            EnsurePositive(MaxResourcesPerGroup, nameof(MaxResourcesPerGroup));
            EnsurePositive(MaxGroups, nameof(MaxGroups));
            EnsurePositive(MaxJsonDepth, nameof(MaxJsonDepth));
            EnsurePositive(MaxLabelsPerEntity, nameof(MaxLabelsPerEntity));
            EnsurePositive(MaxLabelKeyLength, nameof(MaxLabelKeyLength));
            EnsurePositive(MaxLabelValueLength, nameof(MaxLabelValueLength));
        }

        private static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name, value, "The configured limit must be a positive value.");
            }
        }
    }

    /// <summary>
    /// The well-known group identifiers a WoT registry always exposes: the two
    /// reserved Thing Description and Thing Model groups.
    /// </summary>
    public static class WotRegistryGroups
    {
        /// <summary>The reserved Thing Description group id.</summary>
        public const string ThingDescriptions = "thingdescriptions";

        /// <summary>The reserved Thing Model group id.</summary>
        public const string ThingModels = "thingmodels";
    }
}
