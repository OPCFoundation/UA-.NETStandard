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

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A composed USD stage — the root of a materialized scene
    /// (draft OPC UA — OpenUSD Scene Materialization §5.1).
    /// </summary>
    public sealed class UsdStage
    {
        /// <summary>
        /// Creates a stage.
        /// </summary>
        /// <param name="stageName">The stage name, used as the BrowseName of the
        /// materialized <c>UsdStageType</c> Object.</param>
        public UsdStage(string stageName)
        {
            StageName = stageName ?? string.Empty;
        }

        /// <summary>
        /// The stage name.
        /// </summary>
        public string StageName { get; }

        /// <summary>
        /// The identifier of the root layer the stage was composed from.
        /// </summary>
        public string RootLayerIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// The stage's default prim name, when authored.
        /// </summary>
        public string DefaultPrim { get; set; } = string.Empty;

        /// <summary>
        /// The stage up axis (<c>Y</c> or <c>Z</c>). Does not auto-reconcile with a geodetic
        /// frame; it is recorded so a consumer can compose the correct local transform (§5.8).
        /// </summary>
        public string UpAxis { get; set; } = "Z";

        /// <summary>
        /// Metres per stage unit.
        /// </summary>
        public double MetersPerUnit { get; set; } = 1.0;

        /// <summary>
        /// Kilograms per stage mass unit, when authored.
        /// </summary>
        public double? KilogramsPerUnit { get; set; }

        /// <summary>
        /// Time codes per second, when authored. Relates USD time codes to wall clock only
        /// together with an explicit epoch declared by a recording profile (§9).
        /// </summary>
        public double? TimeCodesPerSecond { get; set; }

        /// <summary>
        /// The start of the stage timeline, when authored.
        /// </summary>
        public double? StartTimeCode { get; set; }

        /// <summary>
        /// The end of the stage timeline, when authored.
        /// </summary>
        public double? EndTimeCode { get; set; }

        /// <summary>
        /// Free-form documentation authored on the stage.
        /// </summary>
        public string Documentation { get; set; } = string.Empty;

        /// <summary>
        /// The composed root prims.
        /// </summary>
        public IList<UsdPrim> RootPrims { get; } = new List<UsdPrim>();

        /// <summary>
        /// Adds a root prim to the stage.
        /// </summary>
        /// <param name="prim">The prim to add.</param>
        /// <returns>The added prim, for chaining.</returns>
        public UsdPrim AddRootPrim(UsdPrim prim)
        {
            if (prim == null)
            {
                throw new ArgumentNullException(nameof(prim));
            }
            prim.Parent = null;
            RootPrims.Add(prim);
            return prim;
        }

        /// <summary>
        /// Enumerates every prim in the stage, depth first in authored order.
        /// </summary>
        /// <returns>All prims.</returns>
        public IEnumerable<UsdPrim> AllPrims()
        {
            foreach (UsdPrim root in RootPrims)
            {
                foreach (UsdPrim prim in root.DescendantsAndSelf())
                {
                    yield return prim;
                }
            }
        }

        /// <summary>
        /// Finds a prim by absolute path.
        /// </summary>
        /// <param name="path">The absolute SdfPath.</param>
        /// <returns>The prim, or <c>null</c> when the stage contains no such path.</returns>
        public UsdPrim? Find(string path)
        {
            foreach (UsdPrim root in RootPrims)
            {
                UsdPrim? found = root.Find(path);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
