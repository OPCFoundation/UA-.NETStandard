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
using System.Collections.Immutable;
using System.Linq;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed record WotPublicationPlan(
        WoTAtomicityEnum AppliedAtomicity,
        ArrayOf<ArrayOf<WotDependencyClosure>> Units);

    internal sealed record WotPublicationFootprint(
        ArrayOf<string> ActivationXids, ArrayOf<string> InputXids);

    internal static class WotPublicationPlanner
    {
        public static WotPublicationPlan Create(
            ArrayOf<WotDependencyClosure> closures,
            WoTAtomicityEnum atomicity,
            ArrayOf<WotPublicationFootprint> previous = default)
        {
            if (atomicity is not (WoTAtomicityEnum.PerResource or WoTAtomicityEnum.PerGroup or
                WoTAtomicityEnum.PerClosure or WoTAtomicityEnum.PerRegistry))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Unknown publication atomicity.");
            }
            if (closures.IsEmpty)
            {
                return new WotPublicationPlan(atomicity, []);
            }
            WotResource[] members = [.. closures.ToList().SelectMany(closure => closure.ActivationMembers.ToList())
                .GroupBy(member => member.Xid, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(member => member.Xid, StringComparer.Ordinal)];
            var partition = new Partition(members.Select(member => member.Xid));
            if (atomicity == WoTAtomicityEnum.PerRegistry)
            {
                partition.Join(members.Select(member => member.Xid));
            }
            else if (atomicity == WoTAtomicityEnum.PerGroup)
            {
                foreach (IGrouping<string, WotResource> group in members.GroupBy(member => member.GroupId))
                {
                    partition.Join(group.Select(member => member.Xid));
                }
            }
            var edges = closures.ToList().SelectMany(closure => closure.Dependencies).ToImmutableArray();
            foreach (WotDependencyClosure closure in closures)
            {
                if (atomicity == WoTAtomicityEnum.PerClosure || closure.HasCycle ||
                    previous.Contains(owner => owner.ActivationXids.Contains(xid =>
                        closure.ActivationMembers.Contains(member => member.Xid == xid))))
                {
                    partition.Join(closure.ActivationMembers.ToList().Select(member => member.Xid));
                }
                foreach (WotDependencyComponent component in closure.StronglyConnectedComponents)
                {
                    partition.Join(component.Members.ToList().Where(member => member.Enabled)
                        .Select(member => member.Xid));
                }
            }
            foreach (WotPublicationFootprint owner in previous)
            {
                partition.Join(owner.InputXids.ToList());
            }
            foreach (WotDependency edge in edges)
            {
                if (edge.RefType == "uav:projects" && edge.TargetXid is { } target)
                {
                    partition.Join([edge.SourceXid, target]);
                }
            }
            foreach (IGrouping<string, (string Model, string Xid)> model in members
                .SelectMany(member => (member.DefaultVersion?.Dependencies is { } metadata
                    ? metadata.OwnedModelUris : ArrayOf<string>.Empty)
                    .ToList().Select(model => (Model: model, member.Xid))).GroupBy(entry => entry.Model))
            {
                partition.Join(model.Select(entry => entry.Xid));
            }

            ArrayOf<WotDependencyComponent> ordered = Condense(members, edges, partition);
            bool groupingCycle = ordered.ToList().Any(component => component.Members.Count > 1);
            foreach (WotDependencyComponent component in ordered)
            {
                partition.Join(component.Members.ToList().Select(member => member.Xid));
            }
            ordered = Condense(members, edges, partition);
            var units = new List<ArrayOf<WotDependencyClosure>>();
            foreach (WotDependencyComponent component in ordered)
            {
                string root = partition.Root(component.Members[0].Xid);
                var active = new HashSet<string>(
                    members.Where(member => partition.Root(member.Xid) == root).Select(member => member.Xid),
                    StringComparer.Ordinal);
                List<WotDependencyClosure> work = closures.ToList().Where(closure =>
                        closure.ActivationMembers.Contains(member => active.Contains(member.Xid)))
                    .Select(closure => WotDependencyGraph.PartitionClosure(closure, active)).ToList();
                foreach (WotPublicationFootprint owner in previous)
                {
                    List<WotDependencyClosure> replaced = work.Where(closure =>
                        closure.ActivationMembers.Contains(member => owner.ActivationXids.Contains(member.Xid)))
                        .ToList();
                    if (replaced.Count > 1)
                    {
                        work.RemoveAll(replaced.Contains);
                        work.Add(WotDependencyGraph.CombinePublicationClosures(replaced));
                    }
                }
                units.Add(work.OrderBy(closure => closure.Key, StringComparer.Ordinal).ToArrayOf());
            }
            WoTAtomicityEnum applied = atomicity;
            if (atomicity == WoTAtomicityEnum.PerResource &&
                units.Any(unit => unit.ToList().Sum(closure => closure.ActivationMembers.Count) > 1))
            {
                applied = WoTAtomicityEnum.PerClosure;
            }
            if (atomicity == WoTAtomicityEnum.PerGroup &&
                units.Any(unit => unit.ToList().SelectMany(closure => closure.ActivationMembers.ToList())
                    .Select(member => member.GroupId).Distinct().Skip(1).Any()))
            {
                applied = groupingCycle && units.Count == 1
                    ? WoTAtomicityEnum.PerRegistry : WoTAtomicityEnum.PerClosure;
            }
            return new WotPublicationPlan(
                applied, units.ToArrayOf());
        }

        private static ArrayOf<WotDependencyComponent> Condense(
            WotResource[] members, ImmutableArray<WotDependency> edges, Partition partition)
        {
            ImmutableArray<WotResource> roots = members.Where(member => partition.Root(member.Xid) == member.Xid)
                .ToImmutableArray();
            ImmutableArray<WotDependency> condensed = edges
                .Where(edge => edge.Resolved && edge.TargetXid is { } target &&
                    partition.Contains(edge.SourceXid) && partition.Contains(target) &&
                    partition.Root(edge.SourceXid) != partition.Root(target))
                .Select(edge => new WotDependency(
                    partition.Root(edge.SourceXid), edge.TargetHref, partition.Root(edge.TargetXid!),
                    edge.RefType, true)).ToImmutableArray();
            return WotDependencyGraph.BuildStronglyConnectedComponents(roots, condensed, roots);
        }

        private sealed class Partition(IEnumerable<string> xids)
        {
            public bool Contains(string xid)
            {
                return m_parent.ContainsKey(xid);
            }

            public string Root(string xid)
            {
                string root = xid;
                while (m_parent[root] != root)
                {
                    root = m_parent[root];
                }
                while (xid != root)
                {
                    string next = m_parent[xid];
                    m_parent[xid] = root;
                    xid = next;
                }
                return root;
            }

            public void Join(IEnumerable<string> values)
            {
                string? root = null;
                foreach (string xid in values.Where(Contains))
                {
                    string next = Root(xid);
                    if (root is null)
                    {
                        root = next;
                    }
                    else if (string.CompareOrdinal(root, next) <= 0)
                    {
                        m_parent[next] = root;
                    }
                    else
                    {
                        m_parent[root] = next;
                        root = next;
                    }
                }
            }

            private readonly Dictionary<string, string> m_parent =
                xids.ToDictionary(xid => xid, StringComparer.Ordinal);
        }
    }
}
