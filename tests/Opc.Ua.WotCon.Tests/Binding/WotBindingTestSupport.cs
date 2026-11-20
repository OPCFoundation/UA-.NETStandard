/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Shared helpers for the protocol-binding planner and registry tests.
    /// </summary>
    internal static class WotBindingTestSupport
    {
        /// <summary>
        /// Extracts a single, non-empty form for an affordance from a document.
        /// </summary>
        public static WotAffordanceForm Form(string json, string affordance)
        {
            ImmutableArray<WotAffordanceForm> forms = WotFormExtractor.Extract(Encoding.UTF8.GetBytes(json));
            return forms.First(f => f.AffordanceName == affordance &&
                f.FormElement.ValueKind == JsonValueKind.Object);
        }

        /// <summary>
        /// Extracts all forms from a document.
        /// </summary>
        public static ImmutableArray<WotAffordanceForm> Forms(string json)
        {
            return WotFormExtractor.Extract(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Creates a default plan context.
        /// </summary>
        public static WotBindingPlanContext Context()
        {
            return new();
        }

        /// <summary>
        /// Wraps a property affordance with a single form in a Thing Description.
        /// </summary>
        public static string Property(string name, string formJson, string extraAffordanceTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraAffordanceTerms) ? string.Empty : "," + extraAffordanceTerms;
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"properties\":{\"" +
                name +
                "\":{\"type\":\"number\"" +
                terms +
                ",\"forms\":[" +
                formJson +
                "]}}}";
        }

        /// <summary>
        /// Wraps an action affordance with a single form in a Thing Description.
        /// </summary>
        public static string Action(string name, string formJson, string extraAffordanceTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraAffordanceTerms) ? string.Empty : extraAffordanceTerms + ",";
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"actions\":{\"" +
                name +
                "\":{" +
                terms +
                "\"forms\":[" +
                formJson +
                "]}}}";
        }

        /// <summary>
        /// Wraps an event affordance with a single form in a Thing Description.
        /// </summary>
        public static string Event(string name, string formJson, string extraAffordanceTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraAffordanceTerms) ? string.Empty : extraAffordanceTerms + ",";
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"events\":{\"" +
                name +
                "\":{" +
                terms +
                "\"forms\":[" +
                formJson +
                "]}}}";
        }
    }
}
