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
using System.Collections.Generic;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Per-asset state held by the <see cref="AssetRegistry"/>.
    /// </summary>
    internal sealed class AssetEntry
    {
        public AssetEntry(string name, IWoTAssetState asset)
        {
            Name = name;
            Asset = asset;
        }

        public string Name { get; }

        public IWoTAssetState Asset { get; }

        public WotAssetFileManager? FileManager { get; set; }

        public IWotAssetProvider? Provider { get; set; }

        /// <summary>
        /// Variables created from TD properties keyed by NodeId.
        /// </summary>
        public Dictionary<NodeId, (BaseDataVariableState Variable, WotPropertyTag Tag)> Properties { get; } = [];

        /// <summary>
        /// Methods created from TD actions keyed by NodeId.
        /// </summary>
        public Dictionary<NodeId, (MethodState Method, WotActionTag Tag)> Actions { get; } = [];

        /// <summary>
        /// Active observation callbacks keyed by monitored-item id, used to
        /// route value changes from the provider back to the right variable.
        /// </summary>
        public Dictionary<uint, OnWotValueChange> SubscriberCallbacks { get; } = [];
    }
}
