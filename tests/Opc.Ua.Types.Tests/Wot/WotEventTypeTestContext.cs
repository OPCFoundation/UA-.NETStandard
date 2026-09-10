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
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// An explicit companion type declaration and its OPC UA inheritance chain,
    /// supplied through the public document-backed local context.
    /// </summary>
    internal sealed class WotEventTypeTestContext : IDisposable
    {
        public WotEventTypeTestContext(
            string identity,
            string supertype = "i=2782",
            string browseName = "nsu=urn:test:pump;CustomAlarmType")
        {
            AddType(
                "i=2041", "ua:BaseEventType", null,
                """
                ,"properties": {
                  "EventId": {
                    "uav:id": "i=2042",
                    "uav:browseName": "ua:EventId",
                    "type": "string",
                    "contentEncoding": "base64",
                    "uav:mapToType": "i=15",
                    "uav:modellingRule": "Mandatory",
                    "links": [ { "rel": "ua:HasTypeDefinition", "href": "i=68" } ]
                  }
                }
                """);
            AddType("i=2782", "ua:ConditionType", "i=2041");
            AddType("i=2881", "ua:AcknowledgeableConditionType", "i=2782");
            AddType("i=2915", "ua:AlarmConditionType", "i=2881");
            AddType("i=2955", "ua:LimitAlarmType", "i=2915");
            AddType(identity, browseName, supertype);
            Resolver = new WotDocumentNodeResolver(m_documents);
        }

        public IWotNodeResolver Resolver { get; }

        public void Dispose()
        {
            foreach (WotDocument document in m_documents)
            {
                document.Dispose();
            }
        }

        private void AddType(string identity, string browseName, string parent, string members = "")
        {
            string links = parent is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"tm:extends\",\"href\":\"" + parent + "\"}]";
            m_documents.Add(WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": {
                    "ua": "http://opcfoundation.org/UA/",
                    "tm": "https://www.w3.org/2019/wot/tm#"
                  },
                  "@type": [ "tm:ThingModel", "uav:objectType" ],
                """ +
                "\"uav:id\":\"" +
                identity +
                "\",\"uav:browseName\":\"" +
                browseName +
                "\"" +
                links +
                members +
                "}")));
        }

        private readonly List<WotDocument> m_documents = [];
    }
}
