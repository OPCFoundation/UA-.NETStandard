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
