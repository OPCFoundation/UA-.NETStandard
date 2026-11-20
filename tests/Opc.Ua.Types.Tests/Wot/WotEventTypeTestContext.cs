// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        /// <summary>
        /// Initializes a new <c>WotEventTypeTestContext</c> instance.
        /// </summary>
        /// <param name="supertype">The supertype used to initialize the instance.</param>
        /// <param name="browseName">The browse name used to initialize the instance.</param>
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

        /// <summary>
        /// Gets the resolver for <c>WotEventTypeTestContext</c>.
        /// </summary>
        public IWotNodeResolver Resolver { get; }

        public void Dispose()
        {
            foreach (WotDocument document in m_documents)
            {
                document.Dispose();
            }
        }

        private void AddType(string identity, string browseName, string? parent, string members = "")
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
