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
using System.Text;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolNotificationController
    {
        /// <summary>
        /// Validates the entire batch before a machine factory runs or an existing condition changes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateSnapshot(
            IMachineryBuildContext context, MachineToolNotificationOptions options,
            MachineToolNotificationSnapshot snapshot, ulong sequence)
        {
            if (snapshot == null)
            {
                throw new ServiceResultException(StatusCodes.BadNoData, "The notification provider returned no data.");
            }
            if (snapshot.Alerts.Count > options.MaximumAlerts || snapshot.Messages.Count > options.MaximumMessages)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The notification inventory exceeds its configured bounds.");
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            int bytes = 0;
            foreach (MachineToolAlert alert in snapshot.Alerts)
            {
                if (alert == null ||
                    !identities.Add(alert.Key) ||
                    alert.Severity is < 334 or > 1000 ||
                    (alert.AllowedOperations & ~MachineToolConditionOperations.All) != 0 ||
                    (!StatusCode.IsBad(alert.Sample.StatusCode) && alert.Sample.SourceTimestamp == default))
                {
                    throw Invalid("Alerts need unique identities, warning/error severity and acquisition times.");
                }
                CountText(alert.Key);
                CountText(alert.ErrorCode);
                CountText(alert.Message.Text);
                CountText(alert.Message.Locale, required: false);
                if (!alert.ConditionClassId.IsNull &&
                    !context.Context.TypeTable.IsTypeOf(
                        alert.ConditionClassId, Ua.ObjectTypeIds.BaseConditionClassType))
                {
                    throw Invalid("An alert condition class must derive from BaseConditionClassType.");
                }
            }
            foreach (MachineToolMessage message in snapshot.Messages)
            {
                if (message == null ||
                    sequence == ulong.MaxValue ||
                    message.Sequence != ++sequence ||
                    message.Severity is < 1 or > 333 ||
                    message.Time == default)
                {
                    throw Invalid("Messages need consecutive sequences, informational severity and occurrence times.");
                }
                CountText(message.Identifier);
                CountText(message.Message.Text);
                CountText(message.Message.Locale, required: false);
            }
            void CountText(string? text, bool required = true)
            {
                if ((required && string.IsNullOrWhiteSpace(text)) || text?.Length > options.MaximumTextLength)
                {
                    throw Invalid("A notification identifier or text is missing or exceeds its configured bound.");
                }
                if (text != null)
                {
                    bytes = checked(bytes + Encoding.UTF8.GetByteCount(text));
                    if (bytes > 1024 * 1024)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The complete notification batch exceeds one mebibyte of text.");
                    }
                }
            }
        }
    }
}
