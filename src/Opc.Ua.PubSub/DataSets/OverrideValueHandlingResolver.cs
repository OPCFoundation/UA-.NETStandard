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

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Resolves the effective <see cref="DataValue"/> that a subscriber
    /// must apply to a target slot when the incoming
    /// <see cref="DataSetField"/> is missing or carries a
    /// <see cref="StatusCode"/> whose severity is bad. The behaviour
    /// is controlled by the per-target
    /// <see cref="OverrideValueHandling"/> enum:
    /// <list type="bullet">
    ///   <item><see cref="OverrideValueHandling.Disabled"/> — the
    ///   incoming value is applied as-is (the override is ignored).</item>
    ///   <item><see cref="OverrideValueHandling.LastUsableValue"/> —
    ///   the last good value retained by the subscriber slot is reused
    ///   (the incoming value is suppressed). If no good value has ever
    ///   been observed the configured override value is used as the
    ///   seed.</item>
    ///   <item><see cref="OverrideValueHandling.OverrideValue"/> — the
    ///   configured override value replaces the incoming
    ///   value.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Implements the OverrideValueHandling rules described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.10">
    /// Part 14 §6.2.10 SubscribedDataSet</see>, in particular
    /// §6.2.10.2.4 OverrideValueHandling.
    /// </remarks>
    public static class OverrideValueHandlingResolver
    {
        /// <summary>
        /// Computes the <see cref="DataValue"/> that must be written
        /// to the target slot. The returned value is
        /// <see cref="DataValue.IsNull"/> when the subscriber must
        /// not write anything (e.g.
        /// <see cref="OverrideValueHandling.LastUsableValue"/> with
        /// neither a last-good value nor an override).
        /// </summary>
        /// <param name="handling">Per-target override policy.</param>
        /// <param name="overrideValue">Configured override value used by
        /// the <see cref="OverrideValueHandling.OverrideValue"/> and the
        /// initial <see cref="OverrideValueHandling.LastUsableValue"/>
        /// branches.</param>
        /// <param name="incoming">Field decoded from the inbound
        /// message. Pass <see langword="null"/> to model the
        /// "field missing" case (e.g. a delta frame omitted the
        /// field).</param>
        /// <param name="lastGood">Last value successfully applied to
        /// the target slot. Pass <see cref="DataValue.Null"/> when
        /// the subscriber has not yet observed a good value.</param>
        /// <returns>The <see cref="DataValue"/> the subscriber must
        /// apply. Callers must inspect
        /// <see cref="DataValue.IsNull"/> on the result to decide
        /// whether a write is required.</returns>
        public static DataValue Resolve(
            OverrideValueHandling handling,
            Variant overrideValue,
            DataSetField? incoming,
            DataValue lastGood)
        {
            bool hasIncoming = incoming is not null;
            bool incomingIsBad = hasIncoming &&
                StatusCode.IsBad(incoming!.StatusCode);

            switch (handling)
            {
                case OverrideValueHandling.Disabled:
                    return hasIncoming ? ToDataValue(incoming!) : DataValue.Null;
                case OverrideValueHandling.LastUsableValue:
                    if (hasIncoming && !incomingIsBad)
                    {
                        return ToDataValue(incoming!);
                    }
                    if (!lastGood.IsNull)
                    {
                        return lastGood;
                    }
                    if (!overrideValue.IsNull)
                    {
                        return new DataValue(overrideValue);
                    }
                    return DataValue.Null;
                case OverrideValueHandling.OverrideValue:
                    if (hasIncoming && !incomingIsBad)
                    {
                        return ToDataValue(incoming!);
                    }
                    return new DataValue(overrideValue);
                default:
                    return hasIncoming ? ToDataValue(incoming!) : DataValue.Null;
            }
        }

        private static DataValue ToDataValue(DataSetField field)
        {
            return new DataValue(
                field.Value,
                field.StatusCode,
                field.SourceTimestamp,
                field.ServerTimestamp,
                field.SourcePicoSeconds,
                field.ServerPicoSeconds);
        }
    }
}
