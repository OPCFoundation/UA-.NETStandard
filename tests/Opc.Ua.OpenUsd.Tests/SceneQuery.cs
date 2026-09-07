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

using System.Collections.Generic;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Small query helpers over a <see cref="MaterializedScene"/>, so the assertions in the
    /// server test fixtures read close to the address-space concepts they check.
    /// </summary>
    internal static class SceneQuery
    {
        /// <summary>
        /// The materialized prim at an absolute SdfPath (for example <c>/Plant</c>).
        /// </summary>
        public static UsdPrimState Prim(this MaterializedScene ms, string path)
        {
            return ms.Result.PrimsByPath[path];
        }

        /// <summary>
        /// The materialized attribute keyed <c>&lt;primPath&gt;.&lt;name&gt;</c>.
        /// </summary>
        public static UsdAttributeState Attr(this MaterializedScene ms, string key)
        {
            return ms.Result.AttributesByPath[key];
        }

        /// <summary>
        /// The attribute's ArrayDimensions as a plain array, or <c>null</c> for a scalar — the
        /// <c>ArrayOf&lt;uint&gt;</c> a materialized Variable carries unwraps to a
        /// <c>uint[]?</c> that NUnit compares element-wise.
        /// </summary>
        public static uint[]? Dims(this UsdAttributeState attribute)
        {
            return attribute.ArrayDimensions.ToArray();
        }

        /// <summary>
        /// The boxed value of a materialized attribute, or <c>null</c> when unset.
        /// </summary>
        public static object? BoxedValue(this UsdAttributeState attribute)
        {
            return attribute.Value.AsBoxedObject();
        }

        /// <summary>
        /// The materialized AddIns of type <typeparamref name="T"/> under a prim's
        /// <c>AppliedSchemas</c> folder, in address-space order.
        /// </summary>
        public static List<T> AppliedSchemas<T>(this MaterializedScene ms, string primPath)
            where T : NodeState
        {
            FolderState? folder = ms.Prim(primPath).AppliedSchemas;
            if (folder == null)
            {
                return new List<T>();
            }
            return MaterializationHarness.ChildrenOfType<T>(ms.Context, folder);
        }

        /// <summary>
        /// The materialized composition arcs under a prim's <c>Composition</c> folder, in
        /// address-space order.
        /// </summary>
        public static List<UsdCompositionArcState> CompositionArcs(
            this MaterializedScene ms, string primPath)
        {
            FolderState? folder = ms.Prim(primPath).Composition;
            if (folder == null)
            {
                return new List<UsdCompositionArcState>();
            }
            return MaterializationHarness.ChildrenOfType<UsdCompositionArcState>(ms.Context, folder);
        }

        /// <summary>
        /// The absolute SdfPath of a parsed prim, walked from its parent chain.
        /// </summary>
        public static string PathOf(this Scene.UsdPrim prim)
        {
            var parts = new List<string>();
            Scene.UsdPrim? current = prim;
            while (current != null)
            {
                parts.Insert(0, current.Name);
                current = current.Parent;
            }
            return "/" + string.Join("/", parts);
        }

        /// <summary>
        /// The total number of authored attribute connections across a whole scene.
        /// </summary>
        public static int TotalConnections(this Scene.UsdStage stage)
        {
            int total = 0;
            foreach (Scene.UsdPrim prim in stage.AllPrims())
            {
                foreach (Scene.UsdAttribute attribute in prim.Attributes)
                {
                    total += attribute.Connections.Count;
                }
            }
            return total;
        }

        /// <summary>
        /// Nulls every attribute value and clears every attribute connection across a scene, so
        /// two scenes can be compared on structure and provenance alone. This is the workaround
        /// for the two exporter round-trip defects the fixtures pin separately.
        /// </summary>
        public static Scene.UsdStage NormalizeValuesAndConnections(this Scene.UsdStage stage)
        {
            foreach (Scene.UsdPrim prim in stage.AllPrims())
            {
                foreach (Scene.UsdAttribute attribute in prim.Attributes)
                {
                    attribute.Value = Scene.UsdValue.Null;
                    attribute.Connections.Clear();
                }
            }
            return stage;
        }
    }
}
