/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Types.Tests.Wot
{
    internal static class WotTestAssertions
    {
        public static T Required<T>([NotNull] this T? value)
            where T : class
        {
            return value ?? throw new AssertionException("The required test value was null.");
        }

        public static T Required<T>([NotNull] this T? value)
            where T : struct
        {
            return value ?? throw new AssertionException("The required test value was null.");
        }

        /// <param name="nodeSet">The node set.</param>
        /// <param name="sourceNodeId">The source node ID.</param>
        /// <param name="browseName">The browse name.</param>
        public static UAObjectType EventTypeOf(UANodeSet nodeSet, string sourceNodeId, string browseName)
        {
            UANode[] nodes = nodeSet.Items.Required();
            UAObject source = nodes.OfType<UAObject>().Single(node => node.NodeId == sourceNodeId);
            string ownerId = source.References.Required().Single(reference =>
                reference.IsForward && reference.ReferenceType is "HasTypeDefinition" or "i=40").Value.Required();
            UAObjectType owner = nodes.OfType<UAObjectType>().Single(node => node.NodeId == ownerId);
            string eventId = owner.References.Required().Single(reference =>
                reference.IsForward && reference.ReferenceType is "GeneratesEvent" or "i=41").Value.Required();
            UAObjectType eventType = nodes.OfType<UAObjectType>().Single(node => node.NodeId == eventId);
            Assert.That(eventType.BrowseName, Is.EqualTo(browseName));
            return eventType;
        }
    }
}
