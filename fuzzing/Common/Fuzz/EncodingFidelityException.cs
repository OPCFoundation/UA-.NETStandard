// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Raised by a differential fuzz target when a value survives decoding but does not
    /// re-encode to an equivalent representation.
    /// <para>
    /// This is a fidelity finding, not a robustness one. Nothing crashed, hung or corrupted
    /// memory: the stack accepted the input and produced a value whose encoded form differs
    /// between two encodings or between two generations of the same encoding.
    /// </para>
    /// <para>
    /// The distinction matters because the two properties hold over different input sets.
    /// Robustness is expected for arbitrary bytes and is therefore enforced everywhere.
    /// Fidelity is a property of well formed values: an arbitrary mutated blob can decode
    /// into a value that no encoding is required to represent losslessly, so fidelity is
    /// enforced against curated inputs and continuous fuzzing rather than against historical
    /// crash corpora collected for unrelated targets.
    /// </para>
    /// </summary>
    public sealed class EncodingFidelityException : InvalidOperationException
    {
        /// <summary>
        /// Creates the exception with the supplied message.
        /// </summary>
        public EncodingFidelityException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates the exception with the supplied message and inner exception.
        /// </summary>
        public EncodingFidelityException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates the exception with a default message.
        /// </summary>
        public EncodingFidelityException()
            : base("Encoding fidelity check failed.")
        {
        }
    }
}
