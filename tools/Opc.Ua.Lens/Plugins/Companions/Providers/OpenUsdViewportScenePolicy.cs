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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Opc.Ua;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Admits a bounded USDA preview profile before native loading or texture decoding.
    /// Every asset token resolves only within the digest-verified captured closure.
    /// This is deliberately not a general USD parser or an external asset resolver.
    /// </summary>
    internal static class OpenUsdViewportScenePolicy
    {
        /// <summary>
        /// Verifies closure, expansion, authored geometry and texture bounds before creating cache files.
        /// Unsupported binary/procedural scenes remain an explicit failure, not an unbounded native fallback.
        /// </summary>
        public static void Validate(string rootIdentifier, ArrayOf<OpenUsdVerifiedAsset> assets)
        {
            OpenUsdCompanionExporter.ValidateAssetIdentifier(rootIdentifier);
            if (assets.Count is < 1 or > 32 ||
                !rootIdentifier.EndsWith(".usda", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Embedded preview requires 1-32 captured assets and a USDA root.");
            }
            var contents = new Dictionary<string, OpenUsdVerifiedAsset>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (OpenUsdVerifiedAsset asset in assets)
            {
                OpenUsdCompanionExporter.ValidateAssetIdentifier(asset.Identifier);
                total += asset.Content.Length;
                if (!names.Add(asset.Identifier) || asset.Content.Length is < 1 or > 8 * 1024 * 1024 ||
                    total > OpenUsdCompanionProvider.MaxTotalAssetBytes)
                {
                    throw new InvalidDataException("The preview asset closure exceeds its size or identity bounds.");
                }
                contents.Add(asset.Identifier, asset);
            }
            if (!contents.ContainsKey(rootIdentifier))
            {
                throw new InvalidDataException("The exact-case preview root is missing.");
            }
            long textureBytes = 0;
            var layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
            foreach (OpenUsdVerifiedAsset asset in assets)
            {
                if (asset.Identifier.EndsWith(".usda", StringComparison.OrdinalIgnoreCase))
                {
                    layers.Add(asset.Identifier, ReadLayer(asset, contents));
                }
                else if (asset.Identifier.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    textureBytes += ValidateTexture(asset.Content.Span);
                    if (textureBytes > MaximumTextureBytes)
                    {
                        throw new InvalidDataException("Decoded preview textures exceed 32 MiB.");
                    }
                }
                else
                {
                    throw new NotSupportedException(
                        "Embedded preview admits USDA layers and bounded RGBA/RGB PNG textures only.");
                }
            }
            _ = Expand(rootIdentifier, layers, [], 0);
        }

        private static Layer ReadLayer(
            OpenUsdVerifiedAsset asset, Dictionary<string, OpenUsdVerifiedAsset> contents)
        {
            string text = s_utf8.GetString(asset.Content.Span);
            if (!text.StartsWith("#usda 1.0", StringComparison.Ordinal) ||
                text.Contains('\0', StringComparison.Ordinal))
            {
                throw new NotSupportedException("A preview layer must use the USDA 1.0 text format.");
            }
            var lexer = new Lexer(text);
            var references = new List<string>();
            int prims = 0;
            int numbers = 0;
            int depth = 0;
            int faceIndices = 0;
            string array = string.Empty;
            bool expectType = false;
            bool expectName = false;
            bool expectShader = false;
            while (lexer.Read() is { Kind: not TokenKind.End } token)
            {
                if (token.Value == "{")
                {
                    if (++depth > 64)
                    {
                        throw new InvalidDataException("The preview layer nesting limit was exceeded.");
                    }
                }
                else if (token.Value == "}")
                {
                    if (--depth < 0)
                    {
                        throw new InvalidDataException("The preview layer has unbalanced scopes.");
                    }
                }
                if (token.Kind == TokenKind.Asset)
                {
                    _ = RecordAsset(asset.Identifier, token.Value, contents, references);
                }
                if (token.Value == "]")
                {
                    array = string.Empty;
                }
                if (expectType)
                {
                    if (token.Kind != TokenKind.Identifier || !s_primTypes.Contains(token.Value))
                    {
                        throw new NotSupportedException("This USD prim type is outside the bounded preview profile.");
                    }
                    expectType = false;
                    expectName = true;
                }
                else if (expectName)
                {
                    if (token.Kind != TokenKind.String || token.Value.Length is < 1 or > 64)
                    {
                        throw new NotSupportedException("Preview prim names must be short canonical USD identifiers.");
                    }
                    OpenUsdCompanionExporter.ValidatePrimPath("/" + token.Value);
                    expectName = false;
                }
                if (expectShader && token.Kind == TokenKind.String)
                {
                    if (token.Value is not ("UsdPreviewSurface" or "UsdUVTexture" or
                        "UsdPrimvarReader_float2" or "UsdPrimvarReader_float3" or "UsdTransform2d"))
                    {
                        throw new NotSupportedException("Only standard preview-surface shaders are admitted.");
                    }
                    expectShader = false;
                }
                if (token.Kind == TokenKind.Identifier)
                {
                    if (token.Value is "class" or "inherits" or "specializes" or "variantSet" or "variantSets" or
                        "variants" or "clips" or "relocates" or "info:sourceAsset" or "info:sourceCode" or
                        "info:implementationSource" or "inf" or "nan" ||
                        token.Value.EndsWith(".timeSamples", StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            "Procedural, variant, clip, inherited and executable-shader scenes are not previewed.");
                    }
                    if (token.Value is "def" or "over")
                    {
                        if (++prims > MaximumPrims)
                        {
                            throw new InvalidDataException("The preview prim budget was exceeded.");
                        }
                        expectType = token.Value == "def";
                        expectName = token.Value == "over";
                    }
                    if (token.Value is "references" or "payload" or "subLayers")
                    {
                        ReadComposition(lexer, asset.Identifier, contents, references, token.Value == "subLayers");
                    }
                    if (token.Value is "faceVertexCounts" or "faceVertexIndices")
                    {
                        array = token.Value;
                    }
                    expectShader |= token.Value == "info:id";
                }
                else if (token.Kind == TokenKind.Number)
                {
                    if (++numbers > MaximumNumbers ||
                        !double.TryParse(
                            token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                        !double.IsFinite(value) || Math.Abs(value) > 1_000_000_000)
                    {
                        throw new InvalidDataException("The preview numeric/geometry budget was exceeded.");
                    }
                    if (array == "faceVertexCounts")
                    {
                        if (value != Math.Truncate(value) || value is < 3 or > 64 ||
                            (faceIndices += (int)value) > MaximumNumbers)
                        {
                            throw new InvalidDataException("Mesh face expansion exceeds the preview geometry bound.");
                        }
                    }
                    else if (array == "faceVertexIndices" &&
                        (value != Math.Truncate(value) || value is < 0 or > MaximumNumbers))
                    {
                        throw new InvalidDataException("A mesh index exceeds the preview geometry bound.");
                    }
                }
            }
            if (depth != 0 || expectType || expectName || expectShader)
            {
                throw new InvalidDataException("The preview layer is incomplete.");
            }
            return new Layer(prims, numbers, asset.Content.Length, [.. references]);
        }

        private static void ReadComposition(
            Lexer lexer, string layer, Dictionary<string, OpenUsdVerifiedAsset> contents,
            List<string> references, bool sublayers)
        {
            if (lexer.Read().Value != "=")
            {
                throw new InvalidDataException("A preview composition declaration requires an explicit value.");
            }
            bool list = lexer.Peek().Value == "[";
            if (list)
            {
                _ = lexer.Read();
            }
            if (list && lexer.Peek().Value == "]")
            {
                _ = lexer.Read();
                return;
            }
            while (true)
            {
                Token token = lexer.Read();
                if (token.Kind != TokenKind.Asset)
                {
                    throw new NotSupportedException(
                        "Preview composition requires captured asset references, never internal references.");
                }
                string asset = RecordAsset(layer, token.Value, contents, references);
                if (!asset.EndsWith(".usda", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("A preview composition arc must target a captured USDA layer.");
                }
                if (lexer.Peek().Value == "<")
                {
                    if (sublayers)
                    {
                        throw new InvalidDataException("A sublayer cannot target a prim.");
                    }
                    _ = lexer.Read();
                    var path = new StringBuilder();
                    while (lexer.Read() is { Value: not ">" } part)
                    {
                        if (part.Kind == TokenKind.End || path.Length + part.Value.Length > 512)
                        {
                            throw new InvalidDataException("An asset prim path is unbounded or incomplete.");
                        }
                        path.Append(part.Value);
                    }
                    OpenUsdCompanionExporter.ValidatePrimPath(path.ToString());
                }
                if (lexer.Peek().Value == "(")
                {
                    throw new NotSupportedException("Authored layer offsets are outside the preview profile.");
                }
                if (!list)
                {
                    return;
                }
                Token separator = lexer.Read();
                if (separator.Value == "]")
                {
                    return;
                }
                if (separator.Value != ",")
                {
                    throw new InvalidDataException("The preview composition list is incomplete.");
                }
                if (lexer.Peek().Value == "]")
                {
                    _ = lexer.Read();
                    return;
                }
            }
        }

        private static string RecordAsset(
            string layer, string value, Dictionary<string, OpenUsdVerifiedAsset> contents, List<string> references)
        {
            string relative = value.StartsWith("./", StringComparison.Ordinal) ? value[2..] : value;
            OpenUsdCompanionExporter.ValidateAssetIdentifier(relative);
            int separator = layer.LastIndexOf('/');
            string resolved = separator < 0 ? relative : layer[..(separator + 1)] + relative;
            if (!contents.ContainsKey(resolved))
            {
                throw new InvalidDataException("A native asset dependency is outside the captured closure.");
            }
            if (resolved.EndsWith(".usda", StringComparison.OrdinalIgnoreCase))
            {
                if (references.Count >= 64)
                {
                    throw new InvalidDataException("The preview layer reference budget was exceeded.");
                }
                references.Add(resolved);
            }
            return resolved;
        }

        private static (long Prims, long Numbers, long Bytes) Expand(
            string identifier, Dictionary<string, Layer> layers, HashSet<string> visiting, int depth)
        {
            if (depth > 8 || !visiting.Add(identifier))
            {
                throw new InvalidDataException("Cyclic or excessively deep preview composition is refused.");
            }
            Layer layer = layers[identifier];
            long prims = layer.Prims;
            long numbers = layer.Numbers;
            long bytes = layer.Bytes;
            foreach (string reference in layer.References)
            {
                (long childPrims, long childNumbers, long childBytes) = Expand(reference, layers, visiting, depth + 1);
                prims += childPrims;
                numbers += childNumbers;
                bytes += childBytes;
                if (prims > MaximumPrims || numbers > MaximumNumbers ||
                    bytes > OpenUsdCompanionProvider.MaxTotalAssetBytes)
                {
                    throw new InvalidDataException("Expanded preview composition exceeds its scene budget.");
                }
            }
            visiting.Remove(identifier);
            return (prims, numbers, bytes);
        }

        private static long ValidateTexture(ReadOnlySpan<byte> png)
        {
            if (png.Length < 33 || !png[..8].SequenceEqual(s_pngSignature) ||
                BinaryPrimitives.ReadUInt32BigEndian(png[8..]) != 13 ||
                !png.Slice(12, 4).SequenceEqual("IHDR"u8) || png[24] != 8 ||
                png[25] is not (2 or 6) || png[26] != 0 || png[27] != 0 || png[28] != 0)
            {
                throw new NotSupportedException("Preview textures require non-interlaced 8-bit RGB/RGBA PNG.");
            }
            uint width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
            if (width is < 1 or > 2048 || height is < 1 or > 2048)
            {
                throw new InvalidDataException("A texture exceeds the preview's 2048-pixel dimension bound.");
            }
            int offset = 8;
            bool hasData = false;
            while (offset <= png.Length - 12)
            {
                uint size = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
                ReadOnlySpan<byte> kind = png.Slice(offset + 4, 4);
                if (size > png.Length - offset - 12)
                {
                    throw new InvalidDataException("A preview PNG chunk is truncated.");
                }
                if (!kind.SequenceEqual("IHDR"u8) && !kind.SequenceEqual("IDAT"u8) &&
                    !kind.SequenceEqual("IEND"u8))
                {
                    throw new NotSupportedException("PNG metadata/profile expansion is not admitted in previews.");
                }
                if (kind.SequenceEqual("IHDR"u8) && offset != 8)
                {
                    throw new InvalidDataException("A preview texture cannot replace its validated PNG dimensions.");
                }
                hasData |= kind.SequenceEqual("IDAT"u8);
                offset += checked((int)size + 12);
                if (kind.SequenceEqual("IEND"u8))
                {
                    if (!hasData || size != 0 || offset != png.Length)
                    {
                        throw new InvalidDataException("The preview PNG has no complete image payload.");
                    }
                    return (long)width * height * 4;
                }
            }
            throw new InvalidDataException("The preview PNG is incomplete.");
        }

        internal const int MaximumPrims = 4096;
        internal const int MaximumNumbers = 250_000;
        internal const int MaximumTextureBytes = 32 * 1024 * 1024;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private static readonly byte[] s_pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly HashSet<string> s_primTypes = new(StringComparer.Ordinal)
        {
            "Xform", "Scope", "Cube", "Cylinder", "Cone", "Capsule", "Sphere", "Mesh",
            "Camera", "Material", "Shader", "DomeLight", "DistantLight", "SphereLight", "RectLight", "DiskLight"
        };

        private sealed record Layer(int Prims, int Numbers, int Bytes, ArrayOf<string> References);

        private enum TokenKind
        {
            End,
            Identifier,
            Number,
            String,
            Asset,
            Punctuation
        }

        private readonly record struct Token(TokenKind Kind, string Value);

        private sealed class Lexer(string text)
        {
            public Token Read()
            {
                if (m_buffered)
                {
                    m_buffered = false;
                    return m_next;
                }
                return ReadCore();
            }

            public Token Peek()
            {
                if (!m_buffered)
                {
                    m_next = ReadCore();
                    m_buffered = true;
                }
                return m_next;
            }

            private Token ReadCore()
            {
                while (m_position < text.Length)
                {
                    if (char.IsWhiteSpace(text[m_position]))
                    {
                        m_position++;
                    }
                    else if (text[m_position] == '#')
                    {
                        while (m_position < text.Length && text[m_position] != '\n')
                        {
                            m_position++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                if (m_position == text.Length)
                {
                    return new Token(TokenKind.End, string.Empty);
                }
                if (++m_tokens > 1_000_000)
                {
                    throw new InvalidDataException("The preview layer token budget was exceeded.");
                }
                char first = text[m_position++];
                if (first is '"' or '\'' or '@')
                {
                    bool triple = first != '@' && m_position + 1 < text.Length &&
                        text[m_position] == first && text[m_position + 1] == first;
                    if (triple)
                    {
                        m_position += 2;
                    }
                    int start = m_position;
                    while (m_position < text.Length)
                    {
                        if (m_position - start > 65536)
                        {
                            throw new InvalidDataException("A preview string exceeds its length limit.");
                        }
                        if (text[m_position] == '\\')
                        {
                            m_position += 2;
                            continue;
                        }
                        if (text[m_position] == first && (!triple ||
                            (m_position + 2 < text.Length && text[m_position + 1] == first &&
                                text[m_position + 2] == first)))
                        {
                            string value = text[start..m_position];
                            m_position += triple ? 3 : 1;
                            return new Token(first == '@' ? TokenKind.Asset : TokenKind.String, value);
                        }
                        m_position++;
                    }
                    throw new InvalidDataException("A preview string or asset token is not terminated.");
                }
                if (char.IsAsciiLetter(first) || first == '_')
                {
                    int start = m_position - 1;
                    while (m_position < text.Length &&
                        (char.IsAsciiLetterOrDigit(text[m_position]) || text[m_position] is '_' or ':' or '.'))
                    {
                        if (m_position - start >= 512)
                        {
                            throw new InvalidDataException("A preview identifier exceeds its length limit.");
                        }
                        m_position++;
                    }
                    return new Token(TokenKind.Identifier, text[start..m_position]);
                }
                if (char.IsAsciiDigit(first) || first is '-' or '+' ||
                    (first == '.' && m_position < text.Length && char.IsAsciiDigit(text[m_position])))
                {
                    int start = m_position - 1;
                    while (m_position < text.Length &&
                        (char.IsAsciiDigit(text[m_position]) || text[m_position] is '.' or '-' or '+' or 'e' or 'E'))
                    {
                        if (m_position - start >= 128)
                        {
                            throw new InvalidDataException("A preview numeric literal exceeds its length limit.");
                        }
                        m_position++;
                    }
                    return new Token(TokenKind.Number, text[start..m_position]);
                }
                return new Token(TokenKind.Punctuation, first.ToString());
            }

            private int m_position;
            private int m_tokens;
            private bool m_buffered;
            private Token m_next;
        }
    }
}
