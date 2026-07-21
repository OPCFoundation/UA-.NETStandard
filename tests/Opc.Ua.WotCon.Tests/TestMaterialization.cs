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

using System.Text;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Shared builders for WoT test documents (Thing Descriptions / Thing
    /// Models) used across the registry and materialization test fixtures.
    /// </summary>
    internal static class TestMaterialization
    {
        /// <summary>Builds a minimal Thing Description document.</summary>
        public static byte[] Td(string id, string variant = "1", params string[] extendsHrefs)
        {
            var builder = new StringBuilder();
            builder.Append("{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",");
            builder.Append("\"@type\":\"uav:object\",");
            builder.Append("\"id\":\"").Append(id).Append("\",");
            builder.Append("\"title\":\"").Append(id).Append('-').Append(variant).Append("\",");
            builder.Append("\"properties\":{\"value\":{\"type\":\"number\",\"forms\":[{\"href\":\"x\"}]}}");
            AppendLinks(builder, extendsHrefs);
            builder.Append('}');
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>Builds a minimal Thing Model document.</summary>
        public static byte[] Tm(string id, string variant = "1", params string[] extendsHrefs)
        {
            var builder = new StringBuilder();
            builder.Append("{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",");
            builder.Append("\"@type\":\"tm:ThingModel\",");
            builder.Append("\"id\":\"").Append(id).Append("\",");
            builder.Append("\"title\":\"").Append(id).Append('-').Append(variant).Append("\",");
            builder.Append("\"properties\":{\"value\":{\"type\":\"number\",\"forms\":[{\"href\":\"x\"}]}}");
            AppendLinks(builder, extendsHrefs);
            builder.Append('}');
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>Builds a syntactically invalid JSON document.</summary>
        public static byte[] InvalidJson() => Encoding.UTF8.GetBytes("{ not valid json ");

        private static void AppendLinks(StringBuilder builder, string[] extendsHrefs)
        {
            if (extendsHrefs is not { Length: > 0 })
            {
                return;
            }
            builder.Append(",\"links\":[");
            for (int i = 0; i < extendsHrefs.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"rel\":\"tm:extends\",\"href\":\"")
                    .Append(extendsHrefs[i]).Append("\"}");
            }
            builder.Append(']');
        }
    }
}
