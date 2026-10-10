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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Shared model-language normalization for provider and bridge compatibility checks.
    /// Includes must already be resolved; no document acquisition is performed.
    /// </summary>
    public static class XRegistryModelDefinition
    {
        /// <summary>
        /// Validates a model and adds implicit standard definitions without changing the supplied JSON.
        /// </summary>
        public static JsonElement Normalize(JsonElement model)
        {
            var rules = new XRegistryModelRules(XRegistryModelRules.Object(JsonNode.Parse(model.GetRawText())));
            using JsonDocument result = JsonDocument.Parse(rules.CompleteModel().ToJsonString());
            return result.RootElement.Clone();
        }
    }
}
