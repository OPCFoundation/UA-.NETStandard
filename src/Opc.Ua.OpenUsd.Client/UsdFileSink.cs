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
        private const string TranslateOp = "xformOp:translate";
        private const string RotateOp = "xformOp:rotateXYZ";
        private const string ScaleOp = "xformOp:scale";

        private static readonly char[] s_pathSeparator = ['/'];

        private static readonly DateTime s_epoch =
            new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string m_path;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, Variant> m_values = new(StringComparer.Ordinal);
        private readonly List<(string Prim, string Prop)> m_order = [];
        private readonly Dictionary<string, TransformComponents> m_transforms =
            new(StringComparer.Ordinal);
        private readonly List<string> m_transformOrder = [];

        private readonly Dictionary<string, SortedList<double, Variant>> m_timeSamples =
            new(StringComparer.Ordinal);

        private readonly List<(string Prim, string Prop)> m_tsOrder = [];
        private readonly Dictionary<string, SortedList<double, TransformComponents>>
            m_transformTimeSamples = new(StringComparer.Ordinal);
        private readonly List<string> m_transformTsOrder = [];

        private readonly Dictionary<string, (OpenUsdCompositionArc Arc, string? Asset, bool Active)> m_prims =
            new(StringComparer.Ordinal);

        private readonly List<string> m_primOrder = [];
        private int m_batchDepth;
        private bool m_pendingWrite;

        public UsdFileSink(string path)
        {
            m_path = path;
        }

        /// <inheritdoc/>
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
                WriteOrDefer();
            }
        }

        /// <inheritdoc/>
        public void SetAttribute(string primPath, string propertyName, Variant value)
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
                if (TrySetTransform(m_transforms, m_transformOrder, primPath, propertyName, value))
                {
                    WriteOrDefer();
                    return;
                }
                string key = primPath + "|" + propertyName;
                if (!m_values.ContainsKey(key))
                {
                    m_order.Add((primPath, propertyName));
                }
                m_values[key] = value;
                WriteOrDefer();
            }
        }

        /// <inheritdoc/>
        public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value)
        {
            if (!IsValidPrimPath(primPath) || !IsValidPropertyName(propertyName))
            {
                return;
            }
            double frame = (time.ToUniversalTime() - s_epoch).TotalSeconds;
            lock (m_gate)
            {
                if (TrySetTransformTimeSample(primPath, propertyName, frame, value))
                {
                    WriteOrDefer();
                    return;
                }
                string key = primPath + "|" + propertyName;
                if (!m_timeSamples.TryGetValue(key, out SortedList<double, Variant>? samples))
                {
                    samples = [];
                    m_timeSamples[key] = samples;
                    m_tsOrder.Add((primPath, propertyName));
                }
                samples[frame] = value;
                WriteOrDefer();
            }
        }

        /// <inheritdoc/>
        public IDisposable BeginBatch()
        {
            lock (m_gate)
            {
                m_batchDepth++;
            }
            return new BatchScope(this);
        }

        /// <summary>
        /// Writes immediately unless a batch is open, in which case the write is
        /// deferred until the outermost batch scope is disposed (a single file write
        /// for an entire history replay instead of one per sample).
        /// </summary>
        private void WriteOrDefer()
        {
            if (m_batchDepth == 0)
            {
                Write();
            }
            else
            {
                m_pendingWrite = true;
            }
        }

        private void EndBatch()
        {
            lock (m_gate)
            {
                if (m_batchDepth > 0 && --m_batchDepth == 0 && m_pendingWrite)
                {
                    m_pendingWrite = false;
                    Write();
                }
            }
        }

        private sealed class BatchScope : IDisposable
        {
            private UsdFileSink? m_owner;

            public BatchScope(UsdFileSink owner)
            {
                m_owner = owner;
            }

            public void Dispose()
            {
                UsdFileSink? owner = Interlocked.Exchange(ref m_owner, null);
                owner?.EndBatch();
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

        /// <summary>
        /// A USD property name is one or more identifier segments separated by ':'
        /// (the USD namespace separator), e.g. "xformOp:rotateZ", "inputs:emissiveColor".
        /// </summary>
        /// <param name="propertyName"></param>
        /// <returns></returns>
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

        /// <summary>
        /// USD identifier: starts with a letter or '_', then letters/digits/'_'.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
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
            public List<(string Prop, string UsdType, string Value)> Props { get; } = [];
            public List<(string Prop, string UsdType, string Block)> TimeSamples { get; } = [];
            public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
            public List<string> ChildOrder { get; } = [];

            /// <summary>
            /// Composition metadata (§5.12/§5.13): reference/payload asset, instanceable, active.
            /// </summary>
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
                Variant value = m_values[prim + "|" + prop];
                Node node = NavigateTo(root, rootOrder, prim);
                (string usdType, string formatted) = FormatValue(prop, value);
                node.Props.Add((prop, usdType, formatted));
            }
            foreach (string prim in m_transformOrder)
            {
                Node node = NavigateTo(root, rootOrder, prim);
                node.Props.Add(("xformOp:transform", "matrix4d", FormatTransform(m_transforms[prim])));
            }
            foreach ((string prim, string prop) in m_tsOrder)
            {
                SortedList<double, Variant> samples = m_timeSamples[prim + "|" + prop];
                Node node = NavigateTo(root, rootOrder, prim);
                string usdType = "double";
                var block = new StringBuilder();
                block.Append("{\n");
                foreach (KeyValuePair<double, Variant> kv in samples)
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
            foreach (string prim in m_transformTsOrder)
            {
                SortedList<double, TransformComponents> samples = m_transformTimeSamples[prim];
                Node node = NavigateTo(root, rootOrder, prim);
                var block = new StringBuilder();
                block.Append("{\n");
                foreach (KeyValuePair<double, TransformComponents> sample in samples)
                {
                    block.Append("                ")
                        .Append(sample.Key.ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(": ")
                        .Append(FormatTransform(sample.Value))
                        .Append(",\n");
                }
                block.Append("            }");
                node.TimeSamples.Add(("xformOp:transform", "matrix4d", block.ToString()));
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
            sb.Append('\n')
                .Append(indent).Append("{\n");
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

        /// <summary>
        /// A USD asset reference (for example <c>@pump.usda@</c>) must not contain characters
        /// that would break the layer syntax; reject newlines and quotes.
        /// </summary>
        /// <param name="assetRef"></param>
        /// <returns></returns>
        private static bool IsSafeAssetRef(string? assetRef)
        {
            return !string.IsNullOrEmpty(assetRef) &&
                assetRef!.IndexOfAny(['\n', '\r', '"']) < 0;
        }

        private static string F(double x)
        {
            return x.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Escape a USD string/token value: backslash and quote are escaped and
        /// control characters (newline, carriage return, tab) are rendered as
        /// escape sequences so a value cannot break out of the quoted literal.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static string EscapeToken(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static (string UsdType, string Value) FormatValue(string prop, Variant value)
        {
            if (value.TryGetValue(out ArrayOf<float> colour) && colour.Count >= 3)
            {
                string body = "(" + F(colour[0]) + ", " + F(colour[1]) + ", " + F(colour[2]) + ")";
                return prop.EndsWith("displayColor", StringComparison.OrdinalIgnoreCase)
                    ? ("color3f[]", "[" + body + "]")
                    : ("color3f", body);
            }
            if (value.TryGetValue(out ArrayOf<double> vector) && vector.Count >= 3)
            {
                string body = "(" + F(vector[0]) + ", " + F(vector[1]) + ", " + F(vector[2]) + ")";
                return ("double3", body);
            }
            if (value.TryGetValue(out string token))
            {
                return ("token", "\"" + EscapeToken(token) + "\"");
            }
            if (value.TryGetValue(out double d))
            {
                return ("double", F(d));
            }
            return ("double", F(0.0));
        }

        private static bool TrySetTransform(
            Dictionary<string, TransformComponents> transforms,
            List<string> order,
            string primPath,
            string propertyName,
            in Variant value)
        {
            if (!IsTransformProperty(propertyName) ||
                !TryGetVector3(value, out Vector3 vector))
            {
                return false;
            }
            if (!transforms.TryGetValue(primPath, out TransformComponents transform))
            {
                transform = TransformComponents.Identity;
                order.Add(primPath);
            }
            transforms[primPath] = transform.With(propertyName, vector);
            return true;
        }

        private bool TrySetTransformTimeSample(
            string primPath,
            string propertyName,
            double frame,
            in Variant value)
        {
            if (!IsTransformProperty(propertyName) ||
                !TryGetVector3(value, out Vector3 vector))
            {
                return false;
            }
            if (!m_transformTimeSamples.TryGetValue(
                    primPath,
                    out SortedList<double, TransformComponents>? samples))
            {
                samples = [];
                m_transformTimeSamples[primPath] = samples;
                m_transformTsOrder.Add(primPath);
            }
            TransformComponents transform = samples.TryGetValue(
                frame,
                out TransformComponents existing)
                ? existing
                : TransformComponents.Identity;
            samples[frame] = transform.With(propertyName, vector);
            return true;
        }

        private static bool IsTransformProperty(string propertyName)
        {
            return string.Equals(propertyName, TranslateOp, StringComparison.Ordinal) ||
                string.Equals(propertyName, RotateOp, StringComparison.Ordinal) ||
                string.Equals(propertyName, ScaleOp, StringComparison.Ordinal);
        }

        private static bool TryGetVector3(in Variant value, out Vector3 vector)
        {
            if (value.TryGetValue(out ArrayOf<double> doubles) && doubles.Count == 3)
            {
                vector = new Vector3(doubles[0], doubles[1], doubles[2]);
                return true;
            }
            if (value.TryGetValue(out ArrayOf<float> floats) && floats.Count == 3)
            {
                vector = new Vector3(floats[0], floats[1], floats[2]);
                return true;
            }
            vector = default;
            return false;
        }

        private static string FormatTransform(in TransformComponents transform)
        {
            const double toRadians = Math.PI / 180.0;
            double cx = Math.Cos(transform.Rotation.X * toRadians);
            double sx = Math.Sin(transform.Rotation.X * toRadians);
            double cy = Math.Cos(transform.Rotation.Y * toRadians);
            double sy = Math.Sin(transform.Rotation.Y * toRadians);
            double cz = Math.Cos(transform.Rotation.Z * toRadians);
            double sz = Math.Sin(transform.Rotation.Z * toRadians);

            double r00 = cy * cz;
            double r01 = cy * sz;
            double r02 = -sy;
            double r10 = (sx * sy * cz) - (cx * sz);
            double r11 = (sx * sy * sz) + (cx * cz);
            double r12 = sx * cy;
            double r20 = (cx * sy * cz) + (sx * sz);
            double r21 = (cx * sy * sz) - (sx * cz);
            double r22 = cx * cy;

            var result = new StringBuilder();
            result.Append("( (")
                .Append(F(transform.Scale.X * r00)).Append(", ")
                .Append(F(transform.Scale.X * r01)).Append(", ")
                .Append(F(transform.Scale.X * r02)).Append(", 0.0000), (")
                .Append(F(transform.Scale.Y * r10)).Append(", ")
                .Append(F(transform.Scale.Y * r11)).Append(", ")
                .Append(F(transform.Scale.Y * r12)).Append(", 0.0000), (")
                .Append(F(transform.Scale.Z * r20)).Append(", ")
                .Append(F(transform.Scale.Z * r21)).Append(", ")
                .Append(F(transform.Scale.Z * r22)).Append(", 0.0000), (")
                .Append(F(transform.Translation.X)).Append(", ")
                .Append(F(transform.Translation.Y)).Append(", ")
                .Append(F(transform.Translation.Z)).Append(", 1.0000) )");
            return result.ToString();
        }

        private readonly record struct Vector3(double X, double Y, double Z);

        private readonly record struct TransformComponents(
            Vector3 Translation,
            Vector3 Rotation,
            Vector3 Scale)
        {
            public static TransformComponents Identity =>
                new(default, default, new Vector3(1, 1, 1));

            public TransformComponents With(string propertyName, Vector3 value)
            {
                return propertyName switch
                {
                    TranslateOp => this with { Translation = value },
                    RotateOp => this with { Rotation = value },
                    _ => this with { Scale = value }
                };
            }
        }
    }
}
