/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Selects statistic components and calculation metadata before binding them to an owner.
    /// </summary>
    public sealed class IaStatisticsBuilder
    {
        /// <summary>
        /// Registers one application-defined statistic in provider snapshot order.
        /// </summary>
        public IaStatisticsBuilder AddComponent(BaseVariableState component)
        {
            m_components.Add(component ?? throw new ArgumentNullException(nameof(component)));
            return this;
        }

        /// <summary>
        /// Selects an indirect component group; add every contained quantity in provider snapshot order.
        /// </summary>
        public IaStatisticsBuilder AddGroup(BaseObjectState group)
        {
            m_groups.Add(group ?? throw new ArgumentNullException(nameof(group)));
            return this;
        }

        /// <summary>
        /// Supplies indexed ObjectType metadata for vendor-defined statistic grouping objects.
        /// </summary>
        public IaStatisticsBuilder WithGroupTypeResolver(Func<NodeId, BaseObjectTypeState?> resolver)
        {
            m_resolveGroupType = resolver ?? throw new ArgumentNullException(nameof(resolver));
            return this;
        }

        /// <summary>
        /// Selects statistics collected since StartTime without rolling over.
        /// </summary>
        public IaStatisticsBuilder WithAggregate(string? resetCondition = null)
        {
            m_options = new IaStatisticsOptions { ResetCondition = resetCondition };
            return this;
        }

        /// <summary>
        /// Selects a rolling time window measured in milliseconds.
        /// </summary>
        public IaStatisticsBuilder WithDurationWindow(double milliseconds)
        {
            m_options = new IaStatisticsOptions { Rolling = true, WindowDuration = milliseconds };
            m_options.Validate();
            return this;
        }

        /// <summary>
        /// Selects a rolling window containing at most the given number of acquisitions.
        /// </summary>
        public IaStatisticsBuilder WithCountWindow(uint count)
        {
            m_options = new IaStatisticsOptions { Rolling = true, WindowNumberOfValues = count };
            m_options.Validate();
            return this;
        }

        /// <summary>
        /// Composes the configured interface on an application-owned object.
        /// </summary>
        public IaStatistics Build(
            ISystemContext context,
            BaseObjectState owner,
            IIaStatisticsProvider provider,
            CancellationToken cancellationToken = default)
        {
            return new IaStatistics(context, owner, m_components.ToArray(), provider,
                m_options with { ResolveGroupType = m_resolveGroupType ?? m_options.ResolveGroupType },
                m_groups.ToArray(), cancellationToken);
        }

        /// <summary>
        /// Uses the fluent attachment's indexed manager unless an explicit cross-manager resolver was supplied.
        /// </summary>
        public IaStatistics Build(
            INodeAttachContext context,
            BaseObjectState owner,
            IIaStatisticsProvider provider,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            Func<NodeId, BaseObjectTypeState?> resolver = m_resolveGroupType ??
                (node => context.Find(node) as BaseObjectTypeState);
            return new IaStatistics(context.SystemContext, owner, m_components.ToArray(), provider,
                m_options with { ResolveGroupType = resolver }, m_groups.ToArray(), cancellationToken);
        }

        /// <summary>The component order used by snapshots.</summary>
        private readonly List<BaseVariableState> m_components = [];

        /// <summary>The selected indirect grouping roots.</summary>
        private readonly List<BaseObjectState> m_groups = [];

        /// <summary>The owning application's ObjectType lookup.</summary>
        private Func<NodeId, BaseObjectTypeState?>? m_resolveGroupType;

        /// <summary>The selected interface metadata.</summary>
        private IaStatisticsOptions m_options = new();
    }
}
