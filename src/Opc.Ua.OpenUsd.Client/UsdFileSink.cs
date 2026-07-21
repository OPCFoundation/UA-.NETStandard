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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Sink that authors a text USD override layer (<c>live.usda</c>). Each change
    /// rewrites the file as a single merged prim tree of <c>over</c> opinions, so
    /// composing it above the base asset (see <c>stage.usda</c>) yields the pump
    /// driven by live OPC UA data. This is the C# equivalent of a Nucleus
    /// <c>.live</c> layer; no USD library is required to author text USD.
    /// </summary>
    public sealed class UsdFileSink : IUsdSink
    {
        private static readonly char[] s_pathSeparator = ['/'];
        private static readonly DateTime s_epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string m_path;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, object> m_values = new(StringComparer.Ordinal);
        private readonly List<(string Prim, string Prop)> m_order = new();
        private readonly Dictionary<string, SortedList<double, object>> m_timeSamples =
            new(StringComparer.Ordinal);
        private readonly List<(string Prim, string Prop)> m_tsOrder = new();
        private readonly Dictionary<string, (OpenUsdCompositionArc Arc, string? Asset, bool Active)> m_prims =
            new(StringComparer.Ordinal);
        private readonly List<string> m_primOrder = new();

        public UsdFileSink(string path)
        {
            m_path = path;
        }

        public void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active)
        {
            if (!IsValidPrimPath(primPath))
            {
                return;
            }
            lock (m_gate)
            {
                if (!m_prims.ContainsKey(primPath))
                {
                    m_primOrder.Add(primPath);
                }
                m_prims[primPath] = (arc, assetReference, active);
                Write();
            }
        }

        public void SetAttribute(string primPath, string propertyName, object value)
        {
            // Validate names before authoring: prim-path segments and the
            // (namespaced) property name come from the server's binding model,
            // which the connector treats as untrusted for the purpose of file
            // authoring. Reject anything that is not a valid USD identifier so a
            // hostile or malformed name cannot corrupt or inject into the layer.
            if (!IsValidPrimPath(primPath) || !IsValidPropertyName(propertyName))
            {
                return;
            }
            lock (m_gate)
            {
                string key = primPath + "|" + propertyName;
                if (!m_values.ContainsKey(key))
                {
                    m_order.Add((primPath, propertyName));
                }
                m_values[key] = value;
                Write();
            }
        }

        public void SetTimeSample(string primPath, string propertyName, DateTime time, object value)
        {
            if (!IsValidPrimPath(primPath) || !IsValidPropertyName(propertyName))
            {
                return;
            }
            double frame = (time.ToUniversalTime() - s_epoch).TotalSeconds;
            lock (m_gate)
            {
                string key = primPath + "|" + propertyName;
                if (!m_timeSamples.TryGetValue(key, out SortedList<double, object>? samples))
                {
                    samples = new SortedList<double, object>();
                    m_timeSamples[key] = samples;
                    m_tsOrder.Add((primPath, propertyName));
                }
                samples[frame] = value;
                Write();
            }
        }

        private static bool IsValidPrimPath(string primPath)
        {
            if (string.IsNullOrEmpty(primPath))
            {
                return false;
            }
            string[] segs = primPath.Split(s_pathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 0)
            {
                return false;
            }
            foreach (string seg in segs)
            {
                if (!IsValidIdentifier(seg))
                {
                    return false;
                }
            }
            return true;
        }

        // A USD property name is one or more identifier segments separated by ':'
        // (the USD namespace separator), e.g. "xformOp:rotateZ", "inputs:emissiveColor".
        private static bool IsValidPropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }
            foreach (string part in propertyName.Split(':'))
            {
                if (!IsValidIdentifier(part))
                {
                    return false;
                }
            }
            return true;
        }

        // USD identifier: starts with a letter or '_', then letters/digits/'_'.
        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            char c0 = s[0];
            if (!(char.IsLetter(c0) || c0 == '_'))
            {
                return false;
            }
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class Node
        {
            public List<(string Prop, string UsdType, string Value)> Props { get; } = new();
            public List<(string Prop, string UsdType, string Block)> TimeSamples { get; } = new();
            public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
            public List<string> ChildOrder { get; } = new();
            // Composition metadata (§5.12/§5.13): reference/payload asset, instanceable, active.
            public string? Reference { get; set; }
            public string? Payload { get; set; }
            public bool Instanceable { get; set; }
            public bool? Active { get; set; }

            public Node Child(string name)
            {
                if (!Children.TryGetValue(name, out Node? n))
                {
                    n = new Node();
                    Children[name] = n;
                    ChildOrder.Add(name);
                }
                return n;
            }
        }

        private Node NavigateTo(Node root, List<string> rootOrder, string prim)
        {
            Node node = root;
            foreach (string seg in prim.Split(s_pathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (node == root && !rootOrder.Contains(seg))
                {
                    rootOrder.Add(seg);
                }
                node = node.Child(seg);
            }
            return node;
        }

        private void Write()
        {
            var root = new Node();
            var rootOrder = new List<string>();
            foreach ((string prim, string prop) in m_order)
            {
                object value = m_values[prim + "|" + prop];
                Node node = NavigateTo(root, rootOrder, prim);
                (string usdType, string formatted) = FormatValue(prop, value);
                node.Props.Add((prop, usdType, formatted));
            }
            foreach ((string prim, string prop) in m_tsOrder)
            {
                SortedList<double, object> samples = m_timeSamples[prim + "|" + prop];
                Node node = NavigateTo(root, rootOrder, prim);
                string usdType = "double";
                var block = new StringBuilder();
                block.Append("{\n");
                foreach (KeyValuePair<double, object> kv in samples)
                {
                    (string t, string formatted) = FormatValue(prop, kv.Value);
                    usdType = t;
                    block.Append("                ")
                         .Append(kv.Key.ToString("0.000", CultureInfo.InvariantCulture))
                         .Append(": ").Append(formatted).Append(",\n");
                }
                block.Append("            }");
                node.TimeSamples.Add((prop, usdType, block.ToString()));
            }
            foreach (string prim in m_primOrder)
            {
                (OpenUsdCompositionArc arc, string? asset, bool active) = m_prims[prim];
                Node node = NavigateTo(root, rootOrder, prim);
                node.Active = active;
                if (arc != OpenUsdCompositionArc.Child && IsSafeAssetRef(asset))
                {
                    if (arc == OpenUsdCompositionArc.Payload)
                    {
                        node.Payload = asset;
                    }
                    else
                    {
                        node.Reference = asset;
                        node.Instanceable = arc == OpenUsdCompositionArc.Instance;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("#usda 1.0\n(\n    doc = \"OPC UA -> OpenUSD live bindings (override layer)\"\n)\n\n");
            foreach (string name in rootOrder)
            {
                Emit(sb, root.Children[name], name, string.Empty);
                sb.Append('\n');
            }

            string? dir = Path.GetDirectoryName(m_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(m_path, sb.ToString());
        }

        private static void Emit(StringBuilder sb, Node node, string name, string indent)
        {
            sb.Append(indent).Append("over \"").Append(name).Append('"');
            // Composition metadata block (references/payload/instanceable/active).
            var meta = new List<string>();
            if (node.Reference != null)
            {
                meta.Add($"prepend references = {node.Reference}");
            }
            if (node.Payload != null)
            {
                meta.Add($"prepend payload = {node.Payload}");
            }
            if (node.Instanceable)
            {
                meta.Add("instanceable = true");
            }
            if (node.Active.HasValue)
            {
                meta.Add("active = " + (node.Active.Value ? "true" : "false"));
            }
            if (meta.Count > 0)
            {
                sb.Append(" (\n");
                foreach (string m in meta)
                {
                    sb.Append(indent).Append("    ").Append(m).Append('\n');
                }
                sb.Append(indent).Append(')');
            }
            sb.Append('\n');
            sb.Append(indent).Append("{\n");
            foreach ((string prop, string usdType, string value) in node.Props)
            {
                sb.Append(indent).Append("    ").Append(usdType).Append(' ')
                  .Append(prop).Append(" = ").Append(value).Append('\n');
            }
            foreach ((string prop, string usdType, string block) in node.TimeSamples)
            {
                sb.Append(indent).Append("    ").Append(usdType).Append(' ')
                  .Append(prop).Append(".timeSamples = ").Append(block).Append('\n');
            }
            foreach (string child in node.ChildOrder)
            {
                Emit(sb, node.Children[child], child, indent + "    ");
            }
            sb.Append(indent).Append("}\n");
        }

        // A USD asset reference (e.g. @pump.usda@</Pump>) must not contain characters
        // that would break the layer syntax; reject newlines and quotes.
        private static bool IsSafeAssetRef(string? assetRef)
            => !string.IsNullOrEmpty(assetRef)
               && assetRef!.IndexOfAny(['\n', '\r', '"']) < 0;

        private static string F(double x)
            => x.ToString("0.0000", CultureInfo.InvariantCulture);

        // Escape a USD string/token value: backslash and quote are escaped and
        // control characters (newline, carriage return, tab) are rendered as
        // escape sequences so a value cannot break out of the quoted literal.
        private static string EscapeToken(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static (string UsdType, string Value) FormatValue(string prop, object value)
        {
            switch (value)
            {
                case float[] c when prop.EndsWith("displayColor", StringComparison.OrdinalIgnoreCase):
                    return ("color3f[]", "[(" + F(c[0]) + ", " + F(c[1]) + ", " + F(c[2]) + ")]");
                case float[] c:
                    return ("color3f", "(" + F(c[0]) + ", " + F(c[1]) + ", " + F(c[2]) + ")");
                case string s:
                    return ("token", "\"" + EscapeToken(s) + "\"");
                case double d:
                    return ("double", F(d));
                default:
                    return ("double", F(System.Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            }
        }
    }
}
