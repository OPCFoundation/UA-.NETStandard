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

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// The alias categories through which clients discover the assets of a
    /// server (OPC 10000-110 §8.2).
    /// </summary>
    [Flags]
    public enum AssetDiscovery
    {
        /// <summary>
        /// The assets are not published as aliases; the categories stay empty
        /// and <c>FindAlias</c> finds nothing.
        /// </summary>
        None = 0,

        /// <summary>
        /// <c>AssetsByProductInstanceUri</c> lists every asset under its
        /// <c>ProductInstanceUri</c>.
        /// </summary>
        ProductInstanceUri = 1,

        /// <summary>
        /// <c>AssetsByAssetId</c> lists every asset under its <c>AssetId</c>,
        /// or under <c>NoAssetIdAssigned</c> while it has none.
        /// </summary>
        AssetId = 2,

        /// <summary>
        /// Both categories.
        /// </summary>
        All = ProductInstanceUri | AssetId
    }
}
