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

using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// The ring of <c>ResultType</c> variables an OPC 40001-101
    /// <c>ResultManagement</c> object publishes in its <c>Results</c> folder.
    /// </summary>
    /// <remarks>
    /// The variables are created with the machine and never added or removed
    /// afterwards: the newest result is written to the first slot and the
    /// previous contents shift down. A client can therefore subscribe once and
    /// keep receiving results, and the address space stays fixed while the
    /// server runs.
    /// </remarks>
    internal sealed class MachineryResultVariables
    {
        public static MachineryResultVariables Create(
            MachineryBuildScope scope,
            FolderState results,
            int capacity)
        {
            var slots = new List<ResultState>(capacity);
            for (int ii = 0; ii < capacity; ii++)
            {
                string name = string.Concat(
                    "Result_",
                    (ii + 1).ToString(CultureInfo.InvariantCulture));
                ResultState slot = MachineryBuilderUtilities.AddComponentChild(
                    scope.Context,
                    results,
                    new QualifiedName(name, scope.BuildContext.InstanceNamespaceIndex),
                    static (ctx, parent, browseName) =>
                        ctx.CreateInstanceOfResultType(parent, browseName));
                slots.Add(slot);
            }
            return new MachineryResultVariables(slots);
        }

        private MachineryResultVariables(List<ResultState> slots)
        {
            m_slots = slots;
        }

        /// <summary>
        /// Gets how many results the folder publishes.
        /// </summary>
        public int Count => m_slots.Count;

        /// <summary>
        /// Shifts the published results down and writes
        /// <paramref name="result"/> into the first slot.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="result">The newly published result.</param>
        public void Publish(ISystemContext context, MachineryResult result)
        {
            if (m_slots.Count == 0)
            {
                return;
            }
            lock (m_lock)
            {
                for (int ii = m_slots.Count - 1; ii > 0; ii--)
                {
                    Write(context, m_slots[ii], m_slots[ii - 1].Value);
                }
                Write(context, m_slots[0], result.Data);
            }
        }

        private static void Write(ISystemContext context, ResultState slot, ResultDataType? data)
        {
            if (data == null)
            {
                return;
            }
            slot.Value = data;

            // ResultType exposes the parts of ResultDataType as sub-variables,
            // so the structured children have to follow the value rather than
            // keep whatever the factory put there.
            if (slot.ResultMetaData != null && data.ResultMetaData != null)
            {
                slot.ResultMetaData.Value = data.ResultMetaData;
            }
            if (slot.ResultContent != null)
            {
                slot.ResultContent.Value = data.ResultContent;
            }
            slot.ClearChangeMasks(context, includeChildren: true);
        }

        private readonly List<ResultState> m_slots;
        private readonly Lock m_lock = new();
    }
}
