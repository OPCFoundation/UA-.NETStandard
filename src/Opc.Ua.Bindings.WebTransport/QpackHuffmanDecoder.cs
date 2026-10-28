// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Decodes bounded RFC 7541 Huffman strings used by QPACK.
    /// </summary>
    internal static class QpackHuffmanDecoder
    {
        private static readonly TrieNode[] s_nodes = CreateTrie();

        /// <summary>
        /// Decodes one Huffman string and validates its EOS padding.
        /// </summary>
        internal static bool TryDecode(ReadOnlySpan<byte> encoded, int maximumDecodedBytes, out byte[] decoded)
        {
            if (maximumDecodedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
            }

            List<byte> output = new(Math.Min(encoded.Length, maximumDecodedBytes));
            int nodeIndex = 0;
            int trailingBits = 0;
            bool trailingBitsAreOnes = true;

            foreach (byte value in encoded)
            {
                for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
                {
                    int bit = (value >> bitIndex) & 1;
                    nodeIndex = bit == 0 ? s_nodes[nodeIndex].Zero : s_nodes[nodeIndex].One;
                    if (nodeIndex < 0)
                    {
                        decoded = [];
                        return false;
                    }

                    trailingBits++;
                    trailingBitsAreOnes &= bit == 1;
                    int symbol = s_nodes[nodeIndex].Symbol;
                    if (symbol < 0)
                    {
                        continue;
                    }
                    if (symbol == 256 || output.Count == maximumDecodedBytes)
                    {
                        decoded = [];
                        return false;
                    }

                    output.Add((byte)symbol);
                    nodeIndex = 0;
                    trailingBits = 0;
                    trailingBitsAreOnes = true;
                }
            }

            if (trailingBits > 7 || !trailingBitsAreOnes)
            {
                decoded = [];
                return false;
            }

            decoded = output.ToArray();
            return true;
        }

        private static TrieNode[] CreateTrie()
        {
            List<TrieNode> nodes = [new TrieNode()];
            for (int symbol = 0; symbol < QpackHuffmanCodeTable.Codes.Length; symbol++)
            {
                (byte bitLength, uint code) = QpackHuffmanCodeTable.Codes[symbol];
                int nodeIndex = 0;
                for (int bitIndex = 31; bitIndex >= 32 - bitLength; bitIndex--)
                {
                    bool one = ((code >> bitIndex) & 1) != 0;
                    TrieNode node = nodes[nodeIndex];
                    int childIndex = one ? node.One : node.Zero;
                    if (childIndex < 0)
                    {
                        childIndex = nodes.Count;
                        if (one)
                        {
                            node.One = childIndex;
                        }
                        else
                        {
                            node.Zero = childIndex;
                        }
                        nodes[nodeIndex] = node;
                        nodes.Add(new TrieNode());
                    }
                    nodeIndex = childIndex;
                }

                TrieNode terminal = nodes[nodeIndex];
                terminal.Symbol = symbol;
                nodes[nodeIndex] = terminal;
            }

            return nodes.ToArray();
        }

        private sealed class TrieNode
        {
            internal int Zero { get; set; }

            internal int One { get; set; }

            internal int Symbol { get; set; }

            internal TrieNode()
            {
                Zero = -1;
                One = -1;
                Symbol = -1;
            }
        }
    }
}
