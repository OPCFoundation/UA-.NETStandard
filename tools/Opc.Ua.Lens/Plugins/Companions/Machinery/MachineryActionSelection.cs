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
using System.Linq;
using Opc.Ua;
using Opc.Ua.Machinery.Result;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Pins one inspected item, its source evidence and the borrowed authenticated session.
    /// </summary>
    internal sealed class MachineryActionSelection
    {
        /// <summary>
        /// Captures a selection without reading or authorizing any server operation.
        /// </summary>
        public MachineryActionSelection(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            MachineryItem item, ArrayOf<CompanionValue> values, Action requireCurrent)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Machine = machine ?? throw new ArgumentNullException(nameof(machine));
            Item = item ?? throw new ArgumentNullException(nameof(item));
            m_requireCurrent = requireCurrent ?? throw new ArgumentNullException(nameof(requireCurrent));
            if (machine.NodeId.IsNull ||
                item.NodeId.IsNull ||
                !Enum.IsDefined(section) ||
                values.Count > context.MaxFields)
            {
                throw new ArgumentException("A bounded, inspected Machinery selection is required.", nameof(item));
            }
            Section = section;
            Values = values;
            Target = new CompanionTarget("machinery", item.NodeId, item.Name,
                section == MachinerySection.ProcessValues ? "Process value" : section.ToString());
            m_binding = new CompanionOperationDraft(Target,
                new CompanionOperation("selected-action", "Selected item", CompanionOperationSafety.ReadOnly),
                null, context.Session, DateTimeOffset.MaxValue);
            m_certificate = context.Session.Endpoint.ServerCertificate.Copy();
            m_messageContext = context.Session.MessageContext;
            RequireCurrent();
        }

        /// <summary>
        /// Gets the borrowed session context.
        /// </summary>
        public CompanionContext Context { get; }

        /// <summary>
        /// Gets the actual containing owner, which may be direct management objects rather than a machine.
        /// </summary>
        public MachineryMachine Machine { get; }

        /// <summary>
        /// Gets the inspected section.
        /// </summary>
        public MachinerySection Section { get; }

        /// <summary>
        /// Gets the exact selected item.
        /// </summary>
        public MachineryItem Item { get; }

        /// <summary>
        /// Gets only the fields displayed for this item.
        /// </summary>
        public ArrayOf<CompanionValue> Values { get; }

        /// <summary>
        /// Gets the display-export and zero-adjustment target.
        /// </summary>
        public CompanionTarget Target { get; }

        /// <summary>
        /// Rejects changed parent selection, session, identity, namespaces, encoding context or peer.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void RequireCurrent()
        {
            m_requireCurrent();
            if (!m_binding.MatchesSession(Context.Session) ||
                !ReferenceEquals(m_messageContext, Context.Session.MessageContext) ||
                m_certificate != Context.Session.Endpoint.ServerCertificate)
            {
                throw new InvalidOperationException("The Machinery session or peer changed. Inspect again.");
            }
        }

        /// <summary>
        /// Returns a real, exact result identifier, never the row label or a Good-null slot.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public string RequireResultId()
        {
            RequireCurrent();
            CompanionValue[] published = [.. Values.ToList().Where(value => value.Name == "Published result")];
            CompanionValue[] identifiers = [.. Values.ToList().Where(value => value.Name == "Result ID")];
            if (Section != MachinerySection.Results ||
                published.Length != 1 ||
                identifiers.Length != 1 ||
                published[0].SourceNodeId != Item.NodeId ||
                !StatusCode.IsGood(published[0].Quality) ||
                !StatusCode.IsGood(identifiers[0].Quality) ||
                !published[0].Value.TryGetValue<ResultDataType>(
                    out ResultDataType? result, Context.Session.MessageContext) ||
                result?.ResultMetaData?.ResultId is not { } actual ||
                !identifiers[0].Value.TryGetValue(out string? exact) ||
                string.IsNullOrWhiteSpace(exact) ||
                exact.Length > 256 ||
                !string.Equals(actual, exact, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Inspect a published result with a real, exact Result ID first.");
            }
            return exact;
        }

        private readonly Action m_requireCurrent;
        private readonly CompanionOperationDraft m_binding;
        private readonly ByteString m_certificate;
        private readonly IServiceMessageContext m_messageContext;
    }
}
