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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Scene.Conversion
{
    /// <summary>
    /// Parses a <c>.usda</c> text layer into a composed <see cref="UsdStage"/>, per draft
    /// OPC UA — OpenUSD Scene Materialization §7.1 (import / materialize).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the C# port of the specification reference converter's <c>parse_usda</c> reader and
    /// its scoped composition (<c>_apply_overlays</c>, <c>_merge</c>, <c>_remap_paths</c>,
    /// <c>_clone</c>) — see <c>metaverse-specs/extras/openusd-scene/tools/scene_common.py</c>. It is
    /// self-contained (no <c>usd-core</c>) and supports the subset of the <c>.usda</c> grammar the
    /// specification example assets use.
    /// </para>
    /// <para>
    /// Full USD composition is intentionally out of scope. <see cref="ParseFile"/> reproduces the
    /// reference converter's <em>scoped</em> composition for the published example layers: an
    /// <c>instanceable</c> reference reconstructs its paired <see cref="UsdArcKindEnum.Instance"/>
    /// arc, and referenced sub-assets (<c>robot.usda</c>, <c>tool.usda</c>) are resolved relative to
    /// the referencing layer's directory and merged with path remapping.
    /// </para>
    /// </remarks>
    public static
#if NET8_0_OR_GREATER
    partial
#endif
    class UsdaReader
    {
        private const string PrimPattern = "^\\s*(def|over|class)\\s+(?:(\\w+)\\s+)?\"([^\"]+)\"\\s*(\\(?)";
        private const string AttrPattern = "^\\s*(custom\\s+)?(uniform\\s+)?([\\w:\\[\\]]+)\\s+([\\w:]+)(\\.connect|\\.timeSamples)?\\s*(?:=\\s*(.*))?$";
        private const string RelPattern = "^\\s*(custom\\s+)?rel\\s+([\\w:]+)\\s*=\\s*(.*)$";
        private const string HeaderPattern = "#usda\\s+1\\.0\\s*\\(";
        private const string TargetPattern = "<([^>]*)>";
        private const string JointPattern = "/J[1-6]$";
        private const string IntPattern = "^[-+]?\\d+$";
        private const string FloatPattern = "^[-+]?(\\d+(\\.\\d*)?|\\.\\d+)([eE][-+]?\\d+)?$";
        private const string DocTriplePattern = "doc\\s*=\\s*\"\"\"(.*?)\"\"\"";
        private const string DocSinglePattern = "doc\\s*=\\s*\"([^\"]*)\"";
        private const string KindPattern = "kind\\s*=\\s*\"([^\"]+)\"";
        private const string ActivePattern = "active\\s*=\\s*(true|false)";
        private const string InstanceablePattern = "instanceable\\s*=\\s*(true|false)";
        private const string ApiSchemasPattern = "apiSchemas\\s*=\\s*\\[([^\\]]*)\\]";
        private const string QuotedTokenPattern = "\"([^\"]*)\"";
        private const string ArcPattern =
            "(?:(prepend|append|delete)\\s+)?(references|payload|inherits|specializes)" +
            "\\s*=\\s*@([^@]+)@(?:<([^>]*)>)?";
        private const string VariantPattern = "variants\\s*=\\s*\\{\\s*string\\s+(\\w+)\\s*=\\s*\"([^\"]*)\"\\s*\\}";
        private const string VariantSetBlockPattern = "^\\s*variantSet\\s+\"([^\"]+)\"\\s*=\\s*\\{(.*)$";

        // The regular-expression source generator (System.Text.RegularExpressions.GeneratedRegex) is
        // only available on net8.0+. On the down-level library targets (net472/net48/netstandard2.1)
        // fall back to cached, precompiled Regex instances so the same accessors compile everywhere.
#if NET8_0_OR_GREATER
        [GeneratedRegex(PrimPattern, RegexOptions.CultureInvariant)]
        private static partial Regex PrimRegex();

        [GeneratedRegex(AttrPattern, RegexOptions.CultureInvariant)]
        private static partial Regex AttrRegex();

        [GeneratedRegex(RelPattern, RegexOptions.CultureInvariant)]
        private static partial Regex RelRegex();

        [GeneratedRegex(HeaderPattern, RegexOptions.CultureInvariant)]
        private static partial Regex HeaderRegex();

        [GeneratedRegex(TargetPattern, RegexOptions.CultureInvariant)]
        private static partial Regex TargetRegex();

        [GeneratedRegex(JointPattern, RegexOptions.CultureInvariant)]
        private static partial Regex JointRegex();

        [GeneratedRegex(IntPattern, RegexOptions.CultureInvariant)]
        private static partial Regex IntRegex();

        [GeneratedRegex(FloatPattern, RegexOptions.CultureInvariant)]
        private static partial Regex FloatRegex();

        [GeneratedRegex(DocTriplePattern, RegexOptions.Singleline | RegexOptions.CultureInvariant)]
        private static partial Regex DocTripleRegex();

        [GeneratedRegex(DocSinglePattern, RegexOptions.CultureInvariant)]
        private static partial Regex DocSingleRegex();

        [GeneratedRegex(KindPattern, RegexOptions.CultureInvariant)]
        private static partial Regex KindRegex();

        [GeneratedRegex(ActivePattern, RegexOptions.CultureInvariant)]
        private static partial Regex ActiveRegex();

        [GeneratedRegex(InstanceablePattern, RegexOptions.CultureInvariant)]
        private static partial Regex InstanceableRegex();

        [GeneratedRegex(ApiSchemasPattern, RegexOptions.CultureInvariant)]
        private static partial Regex ApiSchemasRegex();

        [GeneratedRegex(QuotedTokenPattern, RegexOptions.CultureInvariant)]
        private static partial Regex QuotedTokenRegex();

        [GeneratedRegex(ArcPattern, RegexOptions.CultureInvariant)]
        private static partial Regex ArcRegex();

        [GeneratedRegex(VariantPattern, RegexOptions.CultureInvariant)]
        private static partial Regex VariantRegex();

        [GeneratedRegex(VariantSetBlockPattern, RegexOptions.CultureInvariant)]
        private static partial Regex VariantSetBlockRegex();
#else
        private static readonly Regex s_primRegex = new Regex(PrimPattern, RegexOptions.CultureInvariant);
        private static Regex PrimRegex() => s_primRegex;

        private static readonly Regex s_attrRegex = new Regex(AttrPattern, RegexOptions.CultureInvariant);
        private static Regex AttrRegex() => s_attrRegex;

        private static readonly Regex s_relRegex = new Regex(RelPattern, RegexOptions.CultureInvariant);
        private static Regex RelRegex() => s_relRegex;

        private static readonly Regex s_headerRegex = new Regex(HeaderPattern, RegexOptions.CultureInvariant);
        private static Regex HeaderRegex() => s_headerRegex;

        private static readonly Regex s_targetRegex = new Regex(TargetPattern, RegexOptions.CultureInvariant);
        private static Regex TargetRegex() => s_targetRegex;

        private static readonly Regex s_jointRegex = new Regex(JointPattern, RegexOptions.CultureInvariant);
        private static Regex JointRegex() => s_jointRegex;

        private static readonly Regex s_intRegex = new Regex(IntPattern, RegexOptions.CultureInvariant);
        private static Regex IntRegex() => s_intRegex;

        private static readonly Regex s_floatRegex = new Regex(FloatPattern, RegexOptions.CultureInvariant);
        private static Regex FloatRegex() => s_floatRegex;

        private static readonly Regex s_docTripleRegex =
            new Regex(DocTriplePattern, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static Regex DocTripleRegex() => s_docTripleRegex;

        private static readonly Regex s_docSingleRegex = new Regex(DocSinglePattern, RegexOptions.CultureInvariant);
        private static Regex DocSingleRegex() => s_docSingleRegex;

        private static readonly Regex s_kindRegex = new Regex(KindPattern, RegexOptions.CultureInvariant);
        private static Regex KindRegex() => s_kindRegex;

        private static readonly Regex s_activeRegex = new Regex(ActivePattern, RegexOptions.CultureInvariant);
        private static Regex ActiveRegex() => s_activeRegex;

        private static readonly Regex s_instanceableRegex = new Regex(InstanceablePattern, RegexOptions.CultureInvariant);
        private static Regex InstanceableRegex() => s_instanceableRegex;

        private static readonly Regex s_apiSchemasRegex = new Regex(ApiSchemasPattern, RegexOptions.CultureInvariant);
        private static Regex ApiSchemasRegex() => s_apiSchemasRegex;

        private static readonly Regex s_quotedTokenRegex = new Regex(QuotedTokenPattern, RegexOptions.CultureInvariant);
        private static Regex QuotedTokenRegex() => s_quotedTokenRegex;

        private static readonly Regex s_arcRegex = new Regex(ArcPattern, RegexOptions.CultureInvariant);
        private static Regex ArcRegex() => s_arcRegex;

        private static readonly Regex s_variantRegex = new Regex(VariantPattern, RegexOptions.CultureInvariant);
        private static Regex VariantRegex() => s_variantRegex;

        private static readonly Regex s_variantSetBlockRegex =
            new Regex(VariantSetBlockPattern, RegexOptions.CultureInvariant);
        private static Regex VariantSetBlockRegex() => s_variantSetBlockRegex;
#endif

        /// <summary>
        /// Parses a <c>.usda</c> file into a composed stage.
        /// </summary>
        /// <param name="path">The path to the <c>.usda</c> layer.</param>
        /// <param name="stageName">The stage name; defaults to the file name without extension.</param>
        /// <param name="applyExampleOverlays">When <c>true</c> (the default), applies the reference
        /// converter's scoped composition for the published example layers (pumps, robotics). Set to
        /// <c>false</c> when parsing an already-flattened composed layer (for example a layer emitted
        /// by <see cref="UsdaWriter"/>) to avoid composing overlays twice.</param>
        /// <returns>The composed stage.</returns>
        public static UsdStage ParseFile(string path, string? stageName = null, bool applyExampleOverlays = true)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            string text = File.ReadAllText(path);
            string name = stageName ?? Path.GetFileNameWithoutExtension(path);
            UsdStage stage = ParseCore(text, name, path, applyExampleOverlays);
            stage.RootLayerIdentifier = Path.GetFileName(path);
            return stage;
        }

        /// <summary>
        /// Parses a <c>.usda</c> layer supplied as text into a composed stage.
        /// </summary>
        /// <param name="text">The <c>.usda</c> layer text.</param>
        /// <param name="stageName">The stage name.</param>
        /// <returns>The composed stage. Example overlays are not applied — the text is treated as an
        /// already-flattened composed layer.</returns>
        public static UsdStage Parse(string text, string stageName)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            return ParseCore(text, stageName ?? string.Empty, null, applyExampleOverlays: false);
        }

        private static UsdStage ParseCore(string rawText, string stageName, string? sourcePath, bool applyExampleOverlays)
        {
            // Layer metadata is read from the raw text: the '#usda 1.0 ( ... )' header is a magic
            // token, not a comment, and the paren scan is string-aware, so it survives untouched.
            // Comments are then stripped only from the prim body that follows.
            string rawBody = ReadLayerMetadata(rawText, out var meta);
            string body = StripComments(rawBody);

            var stage = new UsdStage(stageName)
            {
                DefaultPrim = meta.DefaultPrim,
                UpAxis = meta.UpAxis,
                MetersPerUnit = meta.MetersPerUnit,
                KilogramsPerUnit = meta.KilogramsPerUnit,
                TimeCodesPerSecond = meta.TimeCodesPerSecond,
                StartTimeCode = meta.StartTimeCode,
                EndTimeCode = meta.EndTimeCode,
                Documentation = meta.Documentation,
            };

            ParseBody(body, stage);

            if (applyExampleOverlays && sourcePath != null)
            {
                ApplyExampleOverlays(stage, sourcePath);
            }
            return stage;
        }

        private static void ParseBody(string body, UsdStage stage)
        {
            var lines = new List<string>();
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length > 0)
                {
                    lines.Add(line);
                }
            }

            var stack = new List<UsdPrim>();
            UsdPrim? pending = null;
            bool inMeta = false;
            var metaLines = new List<string>();

            // Nesting depth inside a prim's '( ... )' metadata block, tracked across lines so a ')'
            // inside a quoted string, a tuple/array or a customData dictionary never closes the
            // block prematurely (§6.3). Only a ')' seen at depth 0 ends the metadata.
            int metaDepth = 0;

            // A multi-line '.timeSamples = { ... }' block is accumulated across lines here so its
            // braces are never mistaken for prim scope braces.
            UsdAttribute? samplesTarget = null;
            var samplesBuffer = new StringBuilder();

            // A 'variantSet "name" = { ... }' block spans several lines and nests prim/attribute
            // braces; it is accumulated here (tracking brace depth) so those inner braces never pop
            // the prim scope stack, then parsed into full variant branches once the block closes.
            UsdVariantSet? variantSetTarget = null;
            var variantSetBuffer = new StringBuilder();
            int variantSetDepth = 0;

            foreach (string ln in lines)
            {
                if (samplesTarget != null)
                {
                    int closeBrace = FindUnquoted(ln, '}');
                    if (closeBrace >= 0)
                    {
                        samplesBuffer.Append(ln, 0, closeBrace);
                        ParseTimeSamples(samplesBuffer.ToString(), samplesTarget);
                        samplesTarget = null;
                        samplesBuffer.Clear();
                    }
                    else
                    {
                        samplesBuffer.Append(ln).Append('\n');
                    }
                    continue;
                }

                if (variantSetTarget != null)
                {
                    if (AccumulateVariantSetBlock(ln, variantSetBuffer, ref variantSetDepth))
                    {
                        foreach (UsdPrim branch in ParseVariantBranches(variantSetBuffer.ToString()))
                        {
                            variantSetTarget.Variants.Add(branch);
                        }
                        variantSetTarget = null;
                        variantSetBuffer.Clear();
                        variantSetDepth = 0;
                    }
                    continue;
                }

                if (pending != null && inMeta)
                {
                    int closeParen = ScanPrimMetaClose(ln, ref metaDepth);
                    if (closeParen >= 0)
                    {
                        metaLines.Add(ln.Substring(0, closeParen));
                        inMeta = false;
                        ApplyPrimMeta(pending, metaLines);
                        metaLines = new List<string>();
                        if (FindUnquoted(ln.Substring(closeParen + 1), '{') >= 0)
                        {
                            Attach(stage, stack, pending);
                            pending = null;
                        }
                        continue;
                    }
                    metaLines.Add(ln);
                    continue;
                }

                Match pm = PrimRegex().Match(ln);
                if (pm.Success)
                {
                    string spec = pm.Groups[1].Value;
                    string typ = pm.Groups[2].Value;
                    string name = pm.Groups[3].Value;
                    bool paren = pm.Groups[4].Value.Length > 0;

                    pending = new UsdPrim(name, typ)
                    {
                        Specifier = spec switch
                        {
                            "over" => UsdSpecifierEnum.Over,
                            "class" => UsdSpecifierEnum.Class,
                            _ => UsdSpecifierEnum.Def,
                        },
                    };

                    if (paren || FindUnquoted(ln, '(') >= 0)
                    {
                        int openParen = FindUnquoted(ln, '(');
                        string after = openParen >= 0 ? ln.Substring(openParen + 1) : string.Empty;
                        metaDepth = 0;
                        int closeParen = ScanPrimMetaClose(after, ref metaDepth);
                        if (closeParen >= 0)
                        {
                            metaLines = new List<string> { after.Substring(0, closeParen) };
                            ApplyPrimMeta(pending, metaLines);
                            metaLines = new List<string>();
                            if (FindUnquoted(after.Substring(closeParen + 1), '{') >= 0)
                            {
                                Attach(stage, stack, pending);
                                pending = null;
                            }
                        }
                        else
                        {
                            inMeta = true;
                            metaLines = new List<string> { after };
                        }
                    }
                    else if (ContainsChar(ln, '{'))
                    {
                        Attach(stage, stack, pending);
                        pending = null;
                    }
                    continue;
                }

                if (pending != null && ContainsChar(ln, '{'))
                {
                    Attach(stage, stack, pending);
                    pending = null;
                    continue;
                }

                if (stack.Count > 0)
                {
                    Match vsm = VariantSetBlockRegex().Match(ln);
                    if (vsm.Success)
                    {
                        string setName = vsm.Groups[1].Value;
                        UsdPrim owner = stack[stack.Count - 1];
                        UsdVariantSet? set = owner.VariantSets.FirstOrDefault(
                            v => string.Equals(v.SetName, setName, StringComparison.Ordinal));
                        if (set == null)
                        {
                            set = new UsdVariantSet(setName);
                            owner.VariantSets.Add(set);
                        }

                        variantSetTarget = set;
                        variantSetBuffer.Clear();
                        variantSetDepth = 1;
                        if (AccumulateVariantSetBlock(vsm.Groups[2].Value, variantSetBuffer, ref variantSetDepth))
                        {
                            foreach (UsdPrim branch in ParseVariantBranches(variantSetBuffer.ToString()))
                            {
                                set.Variants.Add(branch);
                            }
                            variantSetTarget = null;
                            variantSetBuffer.Clear();
                            variantSetDepth = 0;
                        }
                        continue;
                    }
                }

                if (ContainsChar(ln, '}'))
                {
                    // A single-line '.timeSamples = { ... }' declaration carries a '}' that closes
                    // the sample block, not a prim scope; route it to attribute handling below
                    // instead of popping the stack.
                    Match tsm = AttrRegex().Match(ln);
                    if (!tsm.Success
                        || !string.Equals(tsm.Groups[5].Value, ".timeSamples", StringComparison.Ordinal))
                    {
                        int count = ln.Count(c => c == '}');
                        for (int i = 0; i < count; i++)
                        {
                            if (stack.Count > 0)
                            {
                                stack.RemoveAt(stack.Count - 1);
                            }
                        }
                        continue;
                    }
                }

                if (stack.Count == 0)
                {
                    continue;
                }

                UsdPrim cur = stack[stack.Count - 1];

                Match rm = RelRegex().Match(ln);
                if (rm.Success)
                {
                    var rel = new UsdRelationship(rm.Groups[2].Value)
                    {
                        Custom = rm.Groups[1].Value.Length > 0,
                    };
                    foreach (string target in ParseTargets(rm.Groups[3].Value))
                    {
                        rel.Targets.Add(target);
                    }
                    cur.Relationships.Add(rel);
                    continue;
                }

                Match am = AttrRegex().Match(ln);
                if (am.Success)
                {
                    bool custom = am.Groups[1].Value.Length > 0;
                    bool uniform = am.Groups[2].Value.Length > 0;
                    string typeName = am.Groups[3].Value;
                    string attrName = am.Groups[4].Value;
                    string suffix = am.Groups[5].Value;
                    string? valueText = am.Groups[6].Success ? am.Groups[6].Value : null;
                    bool connect = string.Equals(suffix, ".connect", StringComparison.Ordinal);
                    bool timeSamples = string.Equals(suffix, ".timeSamples", StringComparison.Ordinal);

                    if (timeSamples)
                    {
                        // The default (Value) and its time samples may be authored on separate
                        // lines that share one attribute name, so attach the samples to the
                        // existing attribute where present rather than declaring a duplicate.
                        UsdAttribute target = FindOrCreateAttribute(cur, attrName, typeName, uniform, custom);
                        string block = valueText ?? string.Empty;
                        int open = FindUnquoted(block, '{');
                        string afterOpen = open >= 0 ? block.Substring(open + 1) : string.Empty;
                        int close = FindUnquoted(afterOpen, '}');
                        if (close >= 0)
                        {
                            ParseTimeSamples(afterOpen.Substring(0, close), target);
                        }
                        else
                        {
                            samplesTarget = target;
                            samplesBuffer.Clear();
                            samplesBuffer.Append(afterOpen).Append('\n');
                        }
                        continue;
                    }

                    // A default value, its time samples and its connections may be co-authored on
                    // separate lines that share one attribute name, so coalesce them onto a single
                    // UsdAttribute (found by name, or created) rather than declaring a duplicate.
                    // USD permits a default value co-authored with a connection (§5.4, §7.1).
                    UsdAttribute attr = FindOrCreateNamedAttribute(cur, attrName, typeName, uniform, custom);
                    if (connect)
                    {
                        // A '.connect' authors either a single '<target>' or a bracketed
                        // '[<t1>, <t2>]' list; preserve every target in authored order (§5.4).
                        foreach (string target in ParseConnectionTargets(valueText))
                        {
                            attr.Connections.Add(target);
                        }
                    }
                    else
                    {
                        attr.Value = ParseValue(valueText);
                    }
                }
            }
        }

        private static void Attach(UsdStage stage, List<UsdPrim> stack, UsdPrim prim)
        {
            if (stack.Count > 0)
            {
                stack[stack.Count - 1].AddChild(prim);
            }
            else
            {
                stage.AddRootPrim(prim);
            }
            stack.Add(prim);
        }

        /// <summary>
        /// Appends the in-scope characters of a <c>variantSet "name" = { ... }</c> block chunk to
        /// <paramref name="buffer"/> while tracking brace <paramref name="depth"/>, honouring
        /// double-quoted spans and <c>@asset@</c> spans so their braces are never counted.
        /// </summary>
        /// <param name="chunk">The next slice of the block (already past the opening brace).</param>
        /// <param name="buffer">Accumulates the block's inner content (excluding the outer braces).</param>
        /// <param name="depth">The running brace depth; starts at 1 for the opening brace.</param>
        /// <returns><c>true</c> when the block's closing brace is reached (depth returns to 0).</returns>
        private static bool AccumulateVariantSetBlock(string chunk, StringBuilder buffer, ref int depth)
        {
            bool inQuote = false;
            bool inAsset = false;
            foreach (char ch in chunk)
            {
                if (ch == '"' && !inAsset)
                {
                    inQuote = !inQuote;
                    buffer.Append(ch);
                    continue;
                }
                if (ch == '@' && !inQuote)
                {
                    inAsset = !inAsset;
                    buffer.Append(ch);
                    continue;
                }
                if (!inQuote && !inAsset)
                {
                    if (ch == '{')
                    {
                        depth++;
                        buffer.Append(ch);
                        continue;
                    }
                    if (ch == '}')
                    {
                        depth--;
                        if (depth <= 0)
                        {
                            return true;
                        }
                        buffer.Append(ch);
                        continue;
                    }
                }
                buffer.Append(ch);
            }
            buffer.Append('\n');
            return false;
        }

        /// <summary>
        /// Parses the inner content of a <c>variantSet</c> block into its variant branches. Each
        /// <c>"branch" { ... }</c> body is re-parsed by wrapping it as a <c>def</c> prim and running
        /// the same body parser, so a branch may itself carry attributes, relationships, child prims
        /// and even nested variant sets (§5.6). Branch order is preserved.
        /// </summary>
        /// <param name="blockContent">The block interior (the text between the outer braces).</param>
        /// <returns>The parsed branches, in authored order.</returns>
        private static List<UsdPrim> ParseVariantBranches(string blockContent)
        {
            var branches = new List<UsdPrim>();
            int i = 0;
            int n = blockContent.Length;
            while (i < n)
            {
                while (i < n && blockContent[i] != '"')
                {
                    i++;
                }
                if (i >= n)
                {
                    break;
                }
                int nameStart = i + 1;
                int nameEnd = blockContent.IndexOf('"', nameStart);
                if (nameEnd < 0)
                {
                    break;
                }
                string branchName = blockContent.Substring(nameStart, nameEnd - nameStart);

                int j = nameEnd + 1;
                while (j < n && char.IsWhiteSpace(blockContent[j]))
                {
                    j++;
                }
                // A branch may carry its own '( ... )' metadata before its body; skip it as a
                // balanced span so any braces inside the metadata are not mistaken for the body.
                if (j < n && blockContent[j] == '(')
                {
                    j = SkipBalanced(blockContent, j, '(', ')');
                    while (j < n && char.IsWhiteSpace(blockContent[j]))
                    {
                        j++;
                    }
                }
                if (j >= n || blockContent[j] != '{')
                {
                    i = nameEnd + 1;
                    continue;
                }

                int bodyStart = j + 1;
                int bodyEnd = SkipBalanced(blockContent, j, '{', '}') - 1;
                if (bodyEnd < bodyStart)
                {
                    break;
                }
                string body = blockContent.Substring(bodyStart, bodyEnd - bodyStart);

                var temp = new UsdStage("variant");
                ParseBody("def \"" + branchName + "\" {\n" + body + "\n}", temp);
                UsdPrim branch = temp.RootPrims.Count > 0
                    ? temp.RootPrims[0]
                    : new UsdPrim(branchName);
                branch.Parent = null;
                branches.Add(branch);
                i = bodyEnd + 1;
            }
            return branches;
        }

        /// <summary>
        /// Returns the index just past the matching close delimiter for the open delimiter at
        /// <paramref name="start"/>, honouring double-quoted and <c>@asset@</c> spans.
        /// </summary>
        private static int SkipBalanced(string text, int start, char open, char close)
        {
            int depth = 0;
            bool inQuote = false;
            bool inAsset = false;
            for (int k = start; k < text.Length; k++)
            {
                char ch = text[k];
                if (ch == '"' && !inAsset)
                {
                    inQuote = !inQuote;
                }
                else if (ch == '@' && !inQuote)
                {
                    inAsset = !inAsset;
                }
                else if (!inQuote && !inAsset)
                {
                    if (ch == open)
                    {
                        depth++;
                    }
                    else if (ch == close)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return k + 1;
                        }
                    }
                }
            }
            return text.Length;
        }

        /// <summary>
        /// Removes <c>#</c> line comments, honouring double-quoted spans (port of
        /// <c>_strip_comments</c>). Quote state is tracked per line, matching the reference.
        /// </summary>
        /// <param name="text">The raw layer text.</param>
        /// <returns>The text with comments removed.</returns>
        internal static string StripComments(string text)
        {
            var outLines = new List<string>();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                bool inQuote = false;
                var sb = new StringBuilder(line.Length);
                foreach (char ch in line)
                {
                    if (ch == '"')
                    {
                        inQuote = !inQuote;
                    }
                    if (ch == '#' && !inQuote)
                    {
                        break;
                    }
                    sb.Append(ch);
                }
                outLines.Add(sb.ToString());
            }
            return string.Join("\n", outLines);
        }

        /// <summary>
        /// Parses a scalar attribute value token into a <see cref="UsdValue"/>
        /// (port of <c>_parse_value</c>).
        /// </summary>
        /// <remarks>
        /// Integers become <see cref="UsdValueKind.Integer"/>, floating point values
        /// <see cref="UsdValueKind.Double"/>, tuples <c>(a, b, c)</c>
        /// <see cref="UsdValueKind.Tuple"/>, arrays <c>[a, b, c]</c>
        /// <see cref="UsdValueKind.Array"/>, path references <c>&lt;/Path&gt;</c> and asset
        /// paths <c>@asset@</c> their own kinds carrying the inner text, and unquoted words
        /// <see cref="UsdValueKind.Token"/>.
        /// </remarks>
        /// <param name="raw">The raw value text, or <c>null</c>.</param>
        /// <returns>The parsed value, or <see cref="UsdValue.Null"/> when the text is empty.</returns>
        internal static UsdValue ParseValue(string? raw)
        {
            string v = (raw ?? string.Empty).Trim().TrimEnd(',').Trim();
            if (v.Length == 0)
            {
                return UsdValue.Null;
            }
            if (v.Length >= 2 && v[0] == '<' && v[v.Length - 1] == '>')
            {
                return UsdValue.FromPathReference(v.Substring(1, v.Length - 2));
            }
            if (v.Length >= 2 && v[0] == '@' && v[v.Length - 1] == '@')
            {
                return UsdValue.FromAssetPath(v.Substring(1, v.Length - 2));
            }
            if (string.Equals(v, "true", StringComparison.Ordinal))
            {
                return UsdValue.From(true);
            }
            if (string.Equals(v, "false", StringComparison.Ordinal))
            {
                return UsdValue.From(false);
            }
            if (TryParseLiteral(v, out UsdValue literal))
            {
                return literal;
            }
            if (IntRegex().IsMatch(v))
            {
                return ParseIntegral(v);
            }
            if (FloatRegex().IsMatch(v))
            {
                return UsdValue.From(
                    double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            // A bare word is a token; anything that still carries quotes is a string whose
            // quoting the literal parser could not resolve (for example an unterminated one).
            return v.Contains('"', StringComparison.Ordinal)
                ? UsdValue.FromString(v.Trim('"'))
                : UsdValue.FromToken(v);
        }

        /// <summary>
        /// Parses an integral literal.
        /// </summary>
        /// <remarks>
        /// A literal that does not fit a signed 64 bit integer - a <c>uint64</c> above
        /// <see cref="long.MaxValue"/>, which is what the conversion layer authors as text - is
        /// carried as a token holding its exact digits. It therefore neither overflows the parse
        /// nor loses precision to a double, and the coercion layer reads it back into a
        /// <c>uint64</c>.
        /// </remarks>
        /// <param name="text">The literal text.</param>
        /// <returns>The parsed value.</returns>
        private static UsdValue ParseIntegral(string text)
        {
            return long.TryParse(
                text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? UsdValue.From(parsed)
                : UsdValue.FromToken(text);
        }

        private static List<string> ParseTargets(string raw)
        {
            var targets = new List<string>();
            foreach (Match m in TargetRegex().Matches(raw))
            {
                targets.Add(m.Groups[1].Value);
            }
            if (targets.Count == 0)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length > 0 && !string.Equals(trimmed, "[]", StringComparison.Ordinal))
                {
                    UsdValue parsed = ParseValue(raw);
                    AppendTextTargets(parsed, targets);
                }
            }
            return targets;
        }

        /// <summary>
        /// Collects the target paths carried by a parsed value, accepting either a single
        /// textual value or a list of them.
        /// </summary>
        private static void AppendTextTargets(UsdValue parsed, List<string> targets)
        {
            if (parsed.TryGetText(out string single))
            {
                targets.Add(single);
                return;
            }
            if (parsed.TryGetArray(out ArrayOf<UsdValue> list))
            {
                System.ReadOnlySpan<UsdValue> items = list.Span;
                for (int ii = 0; ii < items.Length; ii++)
                {
                    if (items[ii].TryGetText(out string s))
                    {
                        targets.Add(s);
                    }
                }
            }
        }

        /// <summary>
        /// Parses the right-hand side of an attribute <c>.connect</c> into its ordered targets.
        /// Accepts a single bare path reference (<c>&lt;target&gt;</c>), a bracketed path-reference
        /// list (<c>[&lt;t1&gt;, &lt;t2&gt;]</c>) authored by the writer for several targets, and an
        /// empty list (<c>[]</c>). Preserves authored order (§5.4).
        /// </summary>
        private static List<string> ParseConnectionTargets(string? valueText)
        {
            var targets = new List<string>();
            AppendTextTargets(ParseValue(valueText), targets);
            return targets;
        }

        /// <summary>
        /// Finds the most recent attribute on <paramref name="prim"/> with the given name that has
        /// no time samples yet, or creates and attaches a new one. Lets a <c>.timeSamples</c> block
        /// join the attribute an authored default declared, so one attribute carries both (§7.1).
        /// </summary>
        private static UsdAttribute FindOrCreateAttribute(
            UsdPrim prim, string name, string typeName, bool uniform, bool custom)
        {
            for (int i = prim.Attributes.Count - 1; i >= 0; i--)
            {
                UsdAttribute existing = prim.Attributes[i];
                if (string.Equals(existing.Name, name, StringComparison.Ordinal)
                    && existing.TimeSamples.Count == 0)
                {
                    return existing;
                }
            }
            var created = new UsdAttribute(name, typeName)
            {
                Variability = uniform ? UsdVariabilityEnum.Uniform : UsdVariabilityEnum.Varying,
                Custom = custom,
            };
            prim.Attributes.Add(created);
            return created;
        }

        /// <summary>
        /// Finds the most recent attribute on <paramref name="prim"/> with the given name —
        /// regardless of whether it already carries a default, time samples or connections — or
        /// creates and attaches a new one. USD (and this writer) author an attribute's default
        /// value, its time samples and its '.connect' targets on separate lines that share one
        /// attribute name; coalescing every facet onto a single <see cref="UsdAttribute"/> keeps a
        /// value co-authored with a connection as one attribute rather than splitting it into two,
        /// so the composed scene (and its signature) reflect what was authored (§5.4, §7.1).
        /// </summary>
        private static UsdAttribute FindOrCreateNamedAttribute(
            UsdPrim prim, string name, string typeName, bool uniform, bool custom)
        {
            for (int i = prim.Attributes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(prim.Attributes[i].Name, name, StringComparison.Ordinal))
                {
                    return prim.Attributes[i];
                }
            }
            var created = new UsdAttribute(name, typeName)
            {
                Variability = uniform ? UsdVariabilityEnum.Uniform : UsdVariabilityEnum.Varying,
                Custom = custom,
            };
            prim.Attributes.Add(created);
            return created;
        }

        /// <summary>
        /// Parses the body of a <c>.timeSamples = { … }</c> block (the text between the braces) into
        /// the attribute's ordered sample map. Each entry is <c>timeCode: value</c>, comma separated;
        /// the time code may be negative or fractional and the value may be any shape the reader
        /// otherwise supports (scalar, tuple, array, asset path, token). Later samples for the same
        /// time code overwrite earlier ones, matching USD's composed semantics.
        /// </summary>
        private static void ParseTimeSamples(string inner, UsdAttribute attr)
        {
            int depth = 0;
            bool inQuote = false;
            char quote = '\0';
            bool inAsset = false;
            int start = 0;
            int colon = -1;
            for (int i = 0; i <= inner.Length; i++)
            {
                bool atEnd = i == inner.Length;
                char c = atEnd ? '\0' : inner[i];
                if (!atEnd && inQuote)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        inQuote = false;
                    }
                    continue;
                }
                if (!atEnd && inAsset)
                {
                    if (c == '@')
                    {
                        inAsset = false;
                    }
                    continue;
                }
                if (atEnd || (c == ',' && depth == 0))
                {
                    AddTimeSample(attr, inner, start, i, colon);
                    start = i + 1;
                    colon = -1;
                    continue;
                }
                switch (c)
                {
                    case '"':
                    case '\'':
                        inQuote = true;
                        quote = c;
                        break;
                    case '@':
                        inAsset = true;
                        break;
                    case '(':
                    case '[':
                    case '<':
                        depth++;
                        break;
                    case ')':
                    case ']':
                    case '>':
                        if (depth > 0)
                        {
                            depth--;
                        }
                        break;
                    case ':':
                        if (depth == 0 && colon < 0)
                        {
                            colon = i;
                        }
                        break;
                }
            }
        }

        private static void AddTimeSample(UsdAttribute attr, string inner, int start, int end, int colon)
        {
            if (colon < start || colon >= end)
            {
                // No 'timeCode: value' separator: an empty entry (trailing comma) or malformed.
                return;
            }
            string timeText = inner.Substring(start, colon - start).Trim();
            string valueText = inner.Substring(colon + 1, end - colon - 1).Trim();
            if (timeText.Length == 0
                || !double.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double timeCode))
            {
                // Fail closed: never publish a sample under an unparseable time code.
                return;
            }
            attr.TimeSamples[timeCode] = ParseValue(valueText);
        }

        // Scans one line of a prim '( ... )' metadata block and returns the index of the ')' that
        // closes the block, or -1 when the block continues on a later line. Honours double/single
        // quoted spans and '@…@' asset spans, and tracks nesting of (), [] and {} across lines via
        // <paramref name="depth"/> (the metadata's own opening paren is depth 0, already consumed),
        // so a ')' inside a quoted string, a tuple/array or a customData dictionary never closes the
        // block prematurely (§6.3). Callers reset depth to 0 before scanning a new block's first
        // line and carry the updated value into the scan of each continuation line.
        private static int ScanPrimMetaClose(string line, ref int depth)
        {
            bool inQuote = false;
            char quote = '\0';
            bool inAsset = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        inQuote = false;
                    }
                    continue;
                }
                if (inAsset)
                {
                    if (c == '@')
                    {
                        inAsset = false;
                    }
                    continue;
                }
                switch (c)
                {
                    case '"':
                    case '\'':
                        inQuote = true;
                        quote = c;
                        break;
                    case '@':
                        inAsset = true;
                        break;
                    case '(':
                    case '[':
                    case '{':
                        depth++;
                        break;
                    case ']':
                    case '}':
                        if (depth > 0)
                        {
                            depth--;
                        }
                        break;
                    case ')':
                        if (depth == 0)
                        {
                            return i;
                        }
                        depth--;
                        break;
                }
            }
            return -1;
        }

        // Returns the index of the first occurrence of <paramref name="target"/> that is not inside
        // a double/single-quoted span or an '@…@' asset delimiter, or -1. Used to find a
        // '.timeSamples' block's closing brace without tripping on a braced character in a string.
        private static int FindUnquoted(string line, char target)
        {
            bool inQuote = false;
            char quote = '\0';
            bool inAsset = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        inQuote = false;
                    }
                    continue;
                }
                if (inAsset)
                {
                    if (c == '@')
                    {
                        inAsset = false;
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quote = c;
                }
                else if (c == '@')
                {
                    inAsset = true;
                }
                else if (c == target)
                {
                    return i;
                }
            }
            return -1;
        }

        // ----- literal parsing (numbers / quoted strings / tuples / arrays) -----

        private static bool TryParseLiteral(string s, out UsdValue result)
        {
            int pos = 0;
            if (!TryParseLiteralValue(s, ref pos, out result))
            {
                return false;
            }
            SkipWhitespace(s, ref pos);
            return pos == s.Length;
        }

        private static bool TryParseLiteralValue(string s, ref int pos, out UsdValue result)
        {
            result = UsdValue.Null;
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length)
            {
                return false;
            }
            char c = s[pos];
            if (c == '(')
            {
                return TryParseSequence(s, ref pos, ')', asTuple: true, out result);
            }
            if (c == '[')
            {
                return TryParseSequence(s, ref pos, ']', asTuple: false, out result);
            }
            if (c == '"' || c == '\'')
            {
                return TryParseQuoted(s, ref pos, out result);
            }
            if (c == '@')
            {
                return TryParseAssetReference(s, ref pos, out result);
            }
            if (c == '<')
            {
                return TryParsePathReference(s, ref pos, out result);
            }
            if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
            {
                return TryParseNumber(s, ref pos, out result);
            }
            return false;
        }

        // A USD asset-path element inside a bracketed array literal is authored '@path@'
        // (symmetric with the writer's asset[] rendering, §6.2), so accept it as a leaf.
        private static bool TryParseAssetReference(string s, ref int pos, out UsdValue result)
        {
            result = UsdValue.Null;
            int end = s.IndexOf('@', pos + 1);
            if (end < 0)
            {
                return false;
            }
            result = UsdValue.FromAssetPath(s.Substring(pos + 1, end - pos - 1));
            pos = end + 1;
            return true;
        }

        // A path-reference element inside a bracketed list is authored '<path>' — used by a
        // multi-target '.connect' list (§5.4). Accept it so the list re-parses to its targets.
        private static bool TryParsePathReference(string s, ref int pos, out UsdValue result)
        {
            result = UsdValue.Null;
            int end = s.IndexOf('>', pos + 1);
            if (end < 0)
            {
                return false;
            }
            result = UsdValue.FromPathReference(s.Substring(pos + 1, end - pos - 1));
            pos = end + 1;
            return true;
        }

        private static bool TryParseSequence(
            string s, ref int pos, char close, bool asTuple, out UsdValue result)
        {
            result = UsdValue.Null;
            pos++; // consume opening bracket
            var items = new List<UsdValue>();
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == close)
            {
                pos++;
                result = Compose(items, asTuple);
                return true;
            }
            while (true)
            {
                if (!TryParseLiteralValue(s, ref pos, out UsdValue item))
                {
                    return false;
                }
                items.Add(item);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                {
                    return false;
                }
                if (s[pos] == ',')
                {
                    pos++;
                    SkipWhitespace(s, ref pos);
                    if (pos < s.Length && s[pos] == close)
                    {
                        pos++;
                        break;
                    }
                    continue;
                }
                if (s[pos] == close)
                {
                    pos++;
                    break;
                }
                return false;
            }
            result = Compose(items, asTuple);
            return true;
        }

        private static UsdValue Compose(List<UsdValue> items, bool asTuple)
        {
            ArrayOf<UsdValue> values = items.ToArrayOf();
            return asTuple ? UsdValue.FromTuple(values) : UsdValue.FromArray(values);
        }

        private static bool TryParseQuoted(string s, ref int pos, out UsdValue result)
        {
            result = UsdValue.Null;
            char quote = s[pos];
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '\\' && pos + 1 < s.Length)
                {
                    char next = s[pos + 1];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        _ => next,
                    });
                    pos += 2;
                    continue;
                }
                if (c == quote)
                {
                    pos++;
                    result = UsdValue.FromString(sb.ToString());
                    return true;
                }
                sb.Append(c);
                pos++;
            }
            return false;
        }

        private static bool TryParseNumber(string s, ref int pos, out UsdValue result)
        {
            result = UsdValue.Null;
            int start = pos;
            while (pos < s.Length)
            {
                char c = s[pos];
                if ((c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.' || c == 'e' || c == 'E')
                {
                    pos++;
                    continue;
                }
                break;
            }
            string token = s.Substring(start, pos - start);
            if (IntRegex().IsMatch(token))
            {
                result = ParseIntegral(token);
                return true;
            }
            if (FloatRegex().IsMatch(token))
            {
                result = UsdValue.From(
                    double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                return true;
            }
            return false;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            {
                pos++;
            }
        }

        // ----- layer metadata -----

        private readonly struct LayerMeta
        {
            public LayerMeta(string documentation, string defaultPrim, string upAxis, double metersPerUnit,
                double? kilogramsPerUnit, double? timeCodesPerSecond, double? startTimeCode, double? endTimeCode)
            {
                Documentation = documentation;
                DefaultPrim = defaultPrim;
                UpAxis = upAxis;
                MetersPerUnit = metersPerUnit;
                KilogramsPerUnit = kilogramsPerUnit;
                TimeCodesPerSecond = timeCodesPerSecond;
                StartTimeCode = startTimeCode;
                EndTimeCode = endTimeCode;
            }

            public string Documentation { get; }
            public string DefaultPrim { get; }
            public string UpAxis { get; }
            public double MetersPerUnit { get; }
            public double? KilogramsPerUnit { get; }
            public double? TimeCodesPerSecond { get; }
            public double? StartTimeCode { get; }
            public double? EndTimeCode { get; }
        }

        private static string ReadLayerMetadata(string text, out LayerMeta meta)
        {
            meta = new LayerMeta(string.Empty, string.Empty, "Z", 1.0, null, null, null, null);

            Match header = HeaderRegex().Match(text);
            if (!header.Success)
            {
                return text;
            }
            int open = header.Index + header.Length - 1;
            if (!TryFindMatchingParen(text, open, out int close))
            {
                return text;
            }
            string block = text.Substring(open + 1, close - open - 1);

            string doc = ExtractDoc(block);
            string defaultPrim = ExtractQuoted(block, "defaultPrim");
            string upAxisValue = ExtractQuoted(block, "upAxis");
            meta = new LayerMeta(
                doc,
                defaultPrim,
                upAxisValue.Length > 0 ? upAxisValue : "Z",
                ExtractNumber(block, "metersPerUnit") ?? 1.0,
                ExtractNumber(block, "kilogramsPerUnit"),
                ExtractNumber(block, "timeCodesPerSecond"),
                ExtractNumber(block, "startTimeCode"),
                ExtractNumber(block, "endTimeCode"));

            return text.Substring(close + 1);
        }

        private static bool TryFindMatchingParen(string text, int openIndex, out int closeIndex)
        {
            closeIndex = -1;
            int i = openIndex + 1;
            bool inTriple = false;
            bool inQuote = false;
            while (i < text.Length)
            {
                if (inTriple)
                {
                    if (IsTripleQuote(text, i))
                    {
                        inTriple = false;
                        i += 3;
                        continue;
                    }
                    i++;
                    continue;
                }
                if (inQuote)
                {
                    if (text[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (text[i] == '"')
                    {
                        inQuote = false;
                    }
                    i++;
                    continue;
                }
                if (IsTripleQuote(text, i))
                {
                    inTriple = true;
                    i += 3;
                    continue;
                }
                if (text[i] == '"')
                {
                    inQuote = true;
                    i++;
                    continue;
                }
                if (text[i] == ')')
                {
                    closeIndex = i;
                    return true;
                }
                i++;
            }
            return false;
        }

        private static bool IsTripleQuote(string text, int i)
        {
            return i + 3 <= text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"';
        }

        private static string ExtractDoc(string block)
        {
            Match triple = DocTripleRegex().Match(block);
            if (triple.Success)
            {
                return triple.Groups[1].Value.Trim();
            }
            Match single = DocSingleRegex().Match(block);
            return single.Success ? single.Groups[1].Value.Trim() : string.Empty;
        }

        private static string ExtractQuoted(string block, string key)
        {
            Match m = Regex.Match(block, Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static double? ExtractNumber(string block, string key)
        {
            Match m = Regex.Match(block, Regex.Escape(key) + "\\s*=\\s*([-+0-9.eE]+)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }
            return null;
        }

        // ----- prim metadata -----

        private static void ApplyPrimMeta(UsdPrim prim, List<string> metaLines)
        {
            string block = string.Join("\n", metaLines);

            Match doc = DocTripleRegex().Match(block);
            if (doc.Success)
            {
                prim.Documentation = doc.Groups[1].Value.Trim();
            }
            else
            {
                Match docSingle = DocSingleRegex().Match(block);
                if (docSingle.Success)
                {
                    prim.Documentation = docSingle.Groups[1].Value.Trim();
                }
            }

            Match kind = KindRegex().Match(block);
            if (kind.Success)
            {
                prim.Kind = ParseKind(kind.Groups[1].Value);
            }

            Match active = ActiveRegex().Match(block);
            if (active.Success)
            {
                prim.Active = string.Equals(active.Groups[1].Value, "true", StringComparison.Ordinal);
            }

            Match instance = InstanceableRegex().Match(block);
            bool instanceable = instance.Success && string.Equals(instance.Groups[1].Value, "true", StringComparison.Ordinal);
            prim.Instanceable = instanceable;

            Match apis = ApiSchemasRegex().Match(block);
            if (apis.Success)
            {
                foreach (Match schema in QuotedTokenRegex().Matches(apis.Groups[1].Value))
                {
                    prim.ApiSchemas.Add(new UsdApiSchema(schema.Groups[1].Value));
                }
            }

            foreach (Match arc in ArcRegex().Matches(block))
            {
                UsdArcKindEnum arcKind = arc.Groups[2].Value switch
                {
                    "payload" => UsdArcKindEnum.Payload,
                    "inherits" => UsdArcKindEnum.Inherit,
                    "specializes" => UsdArcKindEnum.Specialize,
                    _ => UsdArcKindEnum.Reference,
                };
                prim.Composition.Add(new UsdCompositionArc(arcKind)
                {
                    AssetPath = arc.Groups[3].Value,
                    PrimPath = arc.Groups[4].Success ? arc.Groups[4].Value : string.Empty,
                    ListPosition = ParseListPosition(arc.Groups[1].Success ? arc.Groups[1].Value : "prepend"),
                });
            }

            foreach (Match variant in VariantRegex().Matches(block))
            {
                prim.VariantSets.Add(new UsdVariantSet(variant.Groups[1].Value, variant.Groups[2].Value));
            }

            if (instanceable)
            {
                UsdCompositionArc? reference = prim.Composition.FirstOrDefault(
                    a => a.ArcKind == UsdArcKindEnum.Reference || a.ArcKind == UsdArcKindEnum.Payload);
                if (reference != null)
                {
                    prim.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Instance)
                    {
                        AssetPath = reference.AssetPath,
                        PrimPath = reference.PrimPath,
                        ListPosition = reference.ListPosition,
                    });
                }
            }

            ApplyCustomPrimMeta(prim, block);
        }

        // Metadata field keys the reader already binds to typed prim state above; they are excluded
        // from the §6.3 custom-metadata dictionary so a round trip re-emits them from that typed
        // state, not twice. A list-op arc (prepend/append/delete references/payload/… and
        // apiSchemas/variantSets) is excluded by its qualifier rather than by name.
        private static readonly HashSet<string> s_wellKnownPrimMeta = new HashSet<string>(
            new[]
            {
                "doc", "documentation", "kind", "active", "instanceable",
                "apiSchemas", "references", "payload", "inherits", "specializes",
                "variants", "variantSets", "typeName", "specifier",
            }, StringComparer.Ordinal);

        /// <summary>
        /// Populates <see cref="UsdPrim.Metadata"/> with the §6.3 custom (non-well-known) metadata
        /// authored in the prim's <c>( … )</c> block, so metadata-for-metadata (well-known + custom)
        /// survives a round trip (§7.4). Each entry is a <c>[type] key = value</c> field; the last
        /// word before <c>=</c> is the key, an optional leading type token is discarded, and a
        /// leading <c>prepend</c>/<c>append</c>/<c>delete</c> qualifier marks a list-op arc that the
        /// typed parsing above already handled, so it is skipped here. A nested <c>{ … }</c> maps to
        /// a nested <see cref="Dictionary{TKey,TValue}"/> (§6.3), supported to arbitrary depth.
        /// </summary>
        private static void ApplyCustomPrimMeta(UsdPrim prim, string block)
        {
            int pos = 0;
            while (TryReadMetaEntry(block, ref pos, out string key, out UsdValue value, out bool qualified))
            {
                if (key.Length == 0 || qualified || s_wellKnownPrimMeta.Contains(key))
                {
                    continue;
                }
                prim.Metadata[key] = value;
            }
        }

        // Reads one '[qualifier] [type] key = value' entry from a metadata block or dictionary body.
        // Returns false only when no further entry remains. When the chunk is not a well-formed
        // 'key = value' (for example leftover list-op arc text), it still advances <paramref
        // name="pos"/> and returns true with an empty key so the caller skips it and keeps scanning.
        private static bool TryReadMetaEntry(
            string s, ref int pos, out string key, out UsdValue value, out bool qualified)
        {
            key = string.Empty;
            value = UsdValue.Null;
            qualified = false;

            SkipMetaSeparators(s, ref pos);
            if (pos >= s.Length)
            {
                return false;
            }

            var words = new List<string>();
            var word = new StringBuilder();
            bool sawEquals = false;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '=')
                {
                    pos++;
                    sawEquals = true;
                    break;
                }
                if (c == '\n' || c == ',' || c == ';')
                {
                    pos++;
                    break;
                }
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    if (word.Length > 0)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                    }
                    pos++;
                    continue;
                }
                if (c == '{' || c == '(' || c == '<' || c == '@' || c == '"' || c == '\'')
                {
                    // A value token before any '=' is leftover from a preceding entry (for example a
                    // bare '<Path>' after a list-op arc). Consume and ignore it so scanning advances.
                    ReadMetaValue(s, ref pos);
                    break;
                }
                word.Append(c);
                pos++;
            }
            if (word.Length > 0)
            {
                words.Add(word.ToString());
            }

            if (!sawEquals || words.Count == 0)
            {
                return true;
            }

            key = words[words.Count - 1];
            for (int i = 0; i < words.Count - 1; i++)
            {
                if (string.Equals(words[i], "prepend", StringComparison.Ordinal)
                    || string.Equals(words[i], "append", StringComparison.Ordinal)
                    || string.Equals(words[i], "delete", StringComparison.Ordinal))
                {
                    qualified = true;
                }
            }

            value = ReadMetaValue(s, ref pos);
            return true;
        }

        // Reads a single metadata value: a nested '{ … }' dictionary (parsed recursively into a
        // Dictionary<string, UsdValue>), or a scalar/tuple/array/asset/path token read up to the next
        // depth-0 ',' or newline and parsed by ParseValue. Honours quoted and '@…@' spans.
        private static UsdValue ReadMetaValue(string s, ref int pos)
        {
            SkipInlineMetaWhitespace(s, ref pos);
            if (pos >= s.Length)
            {
                return UsdValue.Null;
            }
            if (s[pos] == '{')
            {
                int end = SkipBalanced(s, pos, '{', '}');
                int innerStart = pos + 1;
                int innerLen = Math.Max(0, end - 1 - innerStart);
                string inner = s.Substring(innerStart, innerLen);
                pos = end;
                var dict = new Dictionary<string, UsdValue>(StringComparer.Ordinal);
                int innerPos = 0;
                while (TryReadMetaEntry(inner, ref innerPos, out string k, out UsdValue v, out _))
                {
                    if (k.Length > 0)
                    {
                        dict[k] = v;
                    }
                }
                return UsdValue.FromDictionary(dict);
            }

            int valueStart = pos;
            int depth = 0;
            bool inQuote = false;
            char quote = '\0';
            bool inAsset = false;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (inQuote)
                {
                    if (c == '\\')
                    {
                        pos++;
                    }
                    else if (c == quote)
                    {
                        inQuote = false;
                    }
                    pos++;
                    continue;
                }
                if (inAsset)
                {
                    if (c == '@')
                    {
                        inAsset = false;
                    }
                    pos++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quote = c;
                    pos++;
                    continue;
                }
                if (c == '@')
                {
                    inAsset = true;
                    pos++;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                    pos++;
                    continue;
                }
                if (c == ')' || c == ']' || c == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    pos++;
                    continue;
                }
                if (depth == 0 && (c == ',' || c == '\n'))
                {
                    break;
                }
                pos++;
            }
            return ParseValue(s.Substring(valueStart, pos - valueStart).Trim());
        }

        private static void SkipMetaSeparators(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ',' || c == ';')
                {
                    pos++;
                    continue;
                }
                break;
            }
        }

        private static void SkipInlineMetaWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r'))
            {
                pos++;
            }
        }

        private static UsdPrimKindEnum ParseKind(string kind)
        {
            return kind.ToLowerInvariant() switch
            {
                "model" => UsdPrimKindEnum.Model,
                "group" => UsdPrimKindEnum.Group,
                "assembly" => UsdPrimKindEnum.Assembly,
                "component" => UsdPrimKindEnum.Component,
                "subcomponent" => UsdPrimKindEnum.Subcomponent,
                _ => UsdPrimKindEnum.Unspecified,
            };
        }

        private static UsdListOpTypeEnum ParseListPosition(string position)
        {
            return position switch
            {
                "append" => UsdListOpTypeEnum.Append,
                "delete" => UsdListOpTypeEnum.Delete,
                _ => UsdListOpTypeEnum.Prepend,
            };
        }

        // ----- scoped composition (example overlays) -----

        private static void ApplyExampleOverlays(UsdStage stage, string sourcePath)
        {
            string baseName = Path.GetFileName(sourcePath).ToLowerInvariant();
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;

            if (string.Equals(baseName, "plant.usda", StringComparison.Ordinal))
            {
                UsdPrim? p101 = stage.Find("/Plant/Pumps/P101");
                if (p101 != null)
                {
                    p101.Composition.Add(MakeArc(UsdArcKindEnum.Reference, "pump.usda", "/Pump", UsdListOpTypeEnum.Append));
                    p101.Composition.Add(MakeArc(UsdArcKindEnum.Instance, "pump.usda", "/Pump", UsdListOpTypeEnum.Append));
                }
                UsdPrim? impeller = stage.Find("/Plant/Pumps/P101/Impeller");
                if (impeller != null)
                {
                    foreach (UsdAttribute a in impeller.Attributes)
                    {
                        if (string.Equals(a.Name, "xformOp:rotateZ", StringComparison.Ordinal))
                        {
                            a.Live = true;
                            a.Variability = UsdVariabilityEnum.Varying;
                        }
                    }
                }
                return;
            }

            if (string.Equals(baseName, "cell.usda", StringComparison.Ordinal))
            {
                string robotPath = Path.Combine(directory, "robot.usda");
                string toolPath = Path.Combine(directory, "tool.usda");

                if (File.Exists(robotPath))
                {
                    UsdPrim robot = ParseFile(robotPath, "RobotAsset", applyExampleOverlays: false).RootPrims[0];
                    foreach (string mountPath in new[] { "/Cell/Robots/R1", "/Cell/Robots/R2" })
                    {
                        UsdPrim? mount = stage.Find(mountPath);
                        if (mount == null)
                        {
                            continue;
                        }
                        Merge(mount, robot);
                        mount.Kind = UsdPrimKindEnum.Component;
                        mount.Composition.Add(MakeArc(UsdArcKindEnum.Reference, "robot.usda", "/Robot", UsdListOpTypeEnum.Append));
                        mount.Composition.Add(MakeArc(UsdArcKindEnum.Instance, "robot.usda", "/Robot", UsdListOpTypeEnum.Append));

                        UsdPrim? basePrim = stage.Find(mountPath + "/Base");
                        basePrim?.ApiSchemas.Add(new UsdApiSchema("CollectionAPI") { ExpansionRule = "expandPrims" });

                        string baseRoot = mountPath + "/Base";
                        foreach (UsdPrim prim in stage.AllPrims())
                        {
                            if (prim.Path.StartsWith(baseRoot, StringComparison.Ordinal) && JointRegex().IsMatch(prim.Path))
                            {
                                foreach (UsdAttribute a in prim.Attributes)
                                {
                                    if (a.Name.StartsWith("xformOp:rotate", StringComparison.Ordinal))
                                    {
                                        a.Live = true;
                                    }
                                }
                            }
                        }
                    }
                }

                if (File.Exists(toolPath))
                {
                    UsdPrim tool = ParseFile(toolPath, "ToolAsset", applyExampleOverlays: false).RootPrims[0];
                    UsdPrim? flange = stage.Find("/Cell/Robots/R1/Base/J1/J2/J3/J4/J5/J6/Flange");
                    if (flange != null)
                    {
                        string oldToolPath = tool.Path;
                        UsdPrim toolClone = ClonePrim(tool, "Tool");
                        toolClone.Composition.Add(MakeArc(UsdArcKindEnum.Reference, "tool.usda", "/Gripper", UsdListOpTypeEnum.Append));
                        flange.AddChild(toolClone);
                        RemapPaths(toolClone, oldToolPath, toolClone.Path);
                    }
                }
            }
        }

        private static UsdCompositionArc MakeArc(UsdArcKindEnum kind, string asset, string primPath, UsdListOpTypeEnum position)
        {
            return new UsdCompositionArc(kind)
            {
                AssetPath = asset,
                PrimPath = primPath,
                ListPosition = position,
            };
        }

        private static void Merge(UsdPrim destination, UsdPrim source)
        {
            foreach (UsdAttribute a in source.Attributes)
            {
                destination.Attributes.Add(CloneAttribute(a));
            }
            foreach (UsdRelationship r in source.Relationships)
            {
                destination.Relationships.Add(CloneRelationship(r));
            }
            foreach (UsdApiSchema api in source.ApiSchemas)
            {
                destination.ApiSchemas.Add(CloneApiSchema(api));
            }
            foreach (UsdPrim child in source.Children)
            {
                destination.AddChild(ClonePrim(child, null));
            }
            RemapPaths(destination, source.Path, destination.Path);
        }

        private static UsdPrim ClonePrim(UsdPrim source, string? newName)
        {
            var clone = new UsdPrim(newName ?? source.Name, source.TypeName)
            {
                Specifier = source.Specifier,
                Kind = source.Kind,
                Active = source.Active,
                Instanceable = source.Instanceable,
                Documentation = source.Documentation,
            };
            foreach (UsdAttribute a in source.Attributes)
            {
                clone.Attributes.Add(CloneAttribute(a));
            }
            foreach (UsdRelationship r in source.Relationships)
            {
                clone.Relationships.Add(CloneRelationship(r));
            }
            foreach (UsdCompositionArc arc in source.Composition)
            {
                clone.Composition.Add(CloneArc(arc));
            }
            foreach (UsdApiSchema api in source.ApiSchemas)
            {
                clone.ApiSchemas.Add(CloneApiSchema(api));
            }
            foreach (UsdVariantSet vs in source.VariantSets)
            {
                var clonedSet = new UsdVariantSet(vs.SetName, vs.Selection);
                foreach (UsdPrim branch in vs.Variants)
                {
                    clonedSet.Variants.Add(ClonePrim(branch, null));
                }
                clone.VariantSets.Add(clonedSet);
            }
            foreach (KeyValuePair<string, UsdValue> kv in source.Metadata)
            {
                clone.Metadata[kv.Key] = kv.Value;
            }
            foreach (UsdPrim child in source.Children)
            {
                clone.AddChild(ClonePrim(child, null));
            }
            return clone;
        }

        private static UsdAttribute CloneAttribute(UsdAttribute source)
        {
            var clone = new UsdAttribute(source.Name, source.TypeName)
            {
                Value = source.Value,
                Variability = source.Variability,
                Custom = source.Custom,
                Interpolation = source.Interpolation,
                Live = source.Live,
            };
            foreach (string connection in source.Connections)
            {
                clone.Connections.Add(connection);
            }
            foreach (KeyValuePair<double, UsdValue> sample in source.TimeSamples)
            {
                clone.TimeSamples[sample.Key] = sample.Value;
            }
            return clone;
        }

        private static UsdRelationship CloneRelationship(UsdRelationship source)
        {
            var clone = new UsdRelationship(source.Name) { Custom = source.Custom };
            foreach (string target in source.Targets)
            {
                clone.Targets.Add(target);
            }
            return clone;
        }

        private static UsdCompositionArc CloneArc(UsdCompositionArc source)
        {
            return new UsdCompositionArc(source.ArcKind)
            {
                AssetPath = source.AssetPath,
                PrimPath = source.PrimPath,
                ListPosition = source.ListPosition,
                VariantSet = source.VariantSet,
                VariantSelection = source.VariantSelection,
            };
        }

        private static UsdApiSchema CloneApiSchema(UsdApiSchema source)
        {
            return new UsdApiSchema(source.SchemaName) { ExpansionRule = source.ExpansionRule };
        }

        private static void RemapPaths(UsdPrim prim, string oldPrefix, string newPrefix)
        {
            foreach (UsdAttribute a in prim.Attributes)
            {
                if (a.Connections.Count > 0)
                {
                    var remapped = a.Connections.Select(c => Remap(c, oldPrefix, newPrefix)).ToList();
                    a.Connections.Clear();
                    foreach (string c in remapped)
                    {
                        a.Connections.Add(c);
                    }
                }
            }
            foreach (UsdRelationship r in prim.Relationships)
            {
                var remapped = r.Targets.Select(t => Remap(t, oldPrefix, newPrefix)).ToList();
                r.Targets.Clear();
                foreach (string t in remapped)
                {
                    r.Targets.Add(t);
                }
            }
            foreach (UsdPrim child in prim.Children)
            {
                RemapPaths(child, oldPrefix, newPrefix);
            }
        }

        private static string Remap(string path, string oldPrefix, string newPrefix)
        {
            if (string.Equals(path, oldPrefix, StringComparison.Ordinal))
            {
                return newPrefix;
            }
            if (path.StartsWith(oldPrefix + "/", StringComparison.Ordinal)
                || path.StartsWith(oldPrefix + ".", StringComparison.Ordinal))
            {
                var remapped = new StringBuilder(newPrefix.Length + (path.Length - oldPrefix.Length));
                remapped.Append(newPrefix);
                remapped.Append(path, oldPrefix.Length, path.Length - oldPrefix.Length);
                return remapped.ToString();
            }
            return path;
        }

        // Ordinal single-character containment without String.Contains(char, StringComparison),
        // which is unavailable on the down-level library targets (net472/net48).
        private static bool ContainsChar(string value, char c)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == c)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
