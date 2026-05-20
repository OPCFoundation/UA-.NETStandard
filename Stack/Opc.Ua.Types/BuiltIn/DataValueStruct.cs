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
using System.Runtime.InteropServices;

namespace Opc.Ua
{
    /// <summary>
    /// Experimental value-type counterpart of <see cref="DataValue"/>.
    /// Provided strictly for benchmarking (see
    /// <c>Tests/Opc.Ua.Core.Tests/Types/BuiltIn/DataValueBenchmarks.cs</c>)
    /// — production code paths still use <see cref="DataValue"/>; the
    /// experiment quantifies the class-vs-struct GC / copy trade-off
    /// before committing to a migration. See
    /// <c>plans/24-subscription-v2-gc-reduction.md</c> for the
    /// orthogonal pooling track.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Payload (64-bit) is approximately 48 bytes: <see cref="Variant"/>
    /// 24 B + <see cref="StatusCode"/> 4 B + 2× <see cref="DateTimeUtc"/>
    /// 16 B + 2× <see cref="ushort"/> 4 B. The struct is intentionally
    /// declared <see cref="LayoutKind.Auto"/> so the JIT can pack the
    /// mismatched-size fields without us hand-tuning the layout.
    /// </para>
    /// <para>
    /// Mutation pattern: use <see cref="Builder"/> for the decoder
    /// hot path so we don't pay an N× copy via repeated <c>with</c>
    /// expressions. The <c>Build()</c> method commits the mutable
    /// builder to an immutable struct in a single move.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct DataValueStruct : IEquatable<DataValueStruct>
    {
        /// <summary>
        /// Constructs a <see cref="DataValueStruct"/> from its
        /// constituent fields. Mirrors the canonical
        /// <see cref="DataValue(Variant, StatusCode, DateTimeUtc, DateTimeUtc)"/>
        /// constructor.
        /// </summary>
        public DataValueStruct(
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp,
            DateTimeUtc serverTimestamp,
            ushort sourcePicoseconds = 0,
            ushort serverPicoseconds = 0)
        {
            WrappedValue = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
            ServerTimestamp = serverTimestamp;
            SourcePicoseconds = sourcePicoseconds;
            ServerPicoseconds = serverPicoseconds;
        }

        /// <summary>The value of the DataValue.</summary>
        public Variant WrappedValue { get; }

        /// <summary>The status code associated with the value.</summary>
        public StatusCode StatusCode { get; }

        /// <summary>The source timestamp associated with the value.</summary>
        public DateTimeUtc SourceTimestamp { get; }

        /// <summary>Additional resolution for the source timestamp.</summary>
        public ushort SourcePicoseconds { get; }

        /// <summary>The server timestamp associated with the value.</summary>
        public DateTimeUtc ServerTimestamp { get; }

        /// <summary>Additional resolution for the server timestamp.</summary>
        public ushort ServerPicoseconds { get; }

        /// <summary>
        /// True when the struct holds no payload (default zero value).
        /// </summary>
        public bool IsNull
            => WrappedValue.IsNull &&
               StatusCode.Code == 0 &&
               SourceTimestamp == DateTimeUtc.MinValue &&
               ServerTimestamp == DateTimeUtc.MinValue &&
               SourcePicoseconds == 0 &&
               ServerPicoseconds == 0;

        /// <inheritdoc/>
        public bool Equals(DataValueStruct other)
        {
            return StatusCode == other.StatusCode &&
                ServerTimestamp == other.ServerTimestamp &&
                SourceTimestamp == other.SourceTimestamp &&
                ServerPicoseconds == other.ServerPicoseconds &&
                SourcePicoseconds == other.SourcePicoseconds &&
                WrappedValue == other.WrappedValue;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is DataValueStruct other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (!WrappedValue.IsNull)
            {
                return WrappedValue.GetHashCode();
            }
            return StatusCode.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(DataValueStruct a, DataValueStruct b) => a.Equals(b);

        /// <inheritdoc/>
        public static bool operator !=(DataValueStruct a, DataValueStruct b) => !a.Equals(b);

        /// <summary>
        /// Mutable builder used by hot-path callers (binary decoder)
        /// to avoid producing intermediate <see cref="DataValueStruct"/>
        /// copies for every field assignment.
        /// </summary>
        public struct Builder : IEquatable<Builder>
        {
            /// <summary>Value field.</summary>
            public Variant WrappedValue;

            /// <summary>StatusCode field.</summary>
            public StatusCode StatusCode;

            /// <summary>Source timestamp field.</summary>
            public DateTimeUtc SourceTimestamp;

            /// <summary>Source picoseconds field.</summary>
            public ushort SourcePicoseconds;

            /// <summary>Server timestamp field.</summary>
            public DateTimeUtc ServerTimestamp;

            /// <summary>Server picoseconds field.</summary>
            public ushort ServerPicoseconds;

            /// <summary>
            /// Materialises the builder's current state into an
            /// immutable <see cref="DataValueStruct"/>.
            /// </summary>
            public readonly DataValueStruct Build()
                => new DataValueStruct(
                    WrappedValue,
                    StatusCode,
                    SourceTimestamp,
                    ServerTimestamp,
                    SourcePicoseconds,
                    ServerPicoseconds);

            /// <inheritdoc/>
            public readonly bool Equals(Builder other)
                => Build().Equals(other.Build());

            /// <inheritdoc/>
            public override readonly bool Equals(object? obj)
                => obj is Builder other && Equals(other);

            /// <inheritdoc/>
            public override readonly int GetHashCode() => Build().GetHashCode();

            /// <inheritdoc/>
            public static bool operator ==(Builder a, Builder b) => a.Equals(b);

            /// <inheritdoc/>
            public static bool operator !=(Builder a, Builder b) => !a.Equals(b);
        }

        /// <summary>
        /// Returns the status-code-good static helper, mirroring
        /// <see cref="DataValue.IsGood(DataValue?)"/>.
        /// </summary>
        public static bool IsGood(DataValueStruct value)
            => StatusCode.IsGood(value.StatusCode);

        /// <summary>
        /// Returns the status-code-bad static helper, mirroring
        /// <see cref="DataValue.IsBad(DataValue?)"/>.
        /// </summary>
        public static bool IsBad(DataValueStruct value)
            => StatusCode.IsBad(value.StatusCode);
    }
}
