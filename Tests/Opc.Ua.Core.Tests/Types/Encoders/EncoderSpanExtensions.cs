using System;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Adds IEncoder Extension Method for tests targeting a netstandard 2.0 assembly (no span support) but being run on net 8 or higher
    /// </summary>
    internal static class EncoderSpanExtensions
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Bridges encoder.WriteByteString(string, ReadOnlySpan<byte>) to the byte[] overloads.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"><paramref name="encoder"/> is <c>null</c>.</exception>
        public static void WriteByteString(this IEncoder encoder, string fieldName, ReadOnlySpan<byte> value)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }

            // Preserve test expectations:
            // - value == ReadOnlySpan<byte>.Empty (null/default span) => encode null
            // - value.IsEmpty (but not Equal to .Empty) => encode empty array
            // - otherwise => encode the span content
            if (value == ReadOnlySpan<byte>.Empty)
            {
                encoder.WriteByteString(fieldName, null);
                return;
            }

            if (value.IsEmpty)
            {
                encoder.WriteByteString(fieldName, []);
                return;
            }

            encoder.WriteByteString(fieldName, value.ToArray());
        }
#endif
    }
}
