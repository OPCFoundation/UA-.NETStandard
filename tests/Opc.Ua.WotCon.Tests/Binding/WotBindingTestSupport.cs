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

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using Opc.Ua.WotCon.Binding;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>Shared helpers for the protocol-binding planner and registry tests.</summary>
    internal static class WotBindingTestSupport
    {
        /// <summary>Extracts a single, non-empty form for an affordance from a document.</summary>
        public static WotAffordanceForm Form(string json, string affordance)
        {
            ImmutableArray<WotAffordanceForm> forms = WotFormExtractor.Extract(Encoding.UTF8.GetBytes(json));
            return forms.First(f => f.AffordanceName == affordance &&
                f.FormElement.ValueKind == JsonValueKind.Object);
        }

        /// <summary>Extracts all forms from a document.</summary>
        public static ImmutableArray<WotAffordanceForm> Forms(string json)
            => WotFormExtractor.Extract(Encoding.UTF8.GetBytes(json));

        /// <summary>Creates a default plan context.</summary>
        public static WotBindingPlanContext Context() => new WotBindingPlanContext();

        /// <summary>Wraps a property affordance with a single form in a Thing Description.</summary>
        public static string Property(string name, string formJson)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"properties\":{\"" + name + "\":{\"type\":\"number\"," +
                "\"forms\":[" + formJson + "]}}}";
        }

        /// <summary>Wraps an action affordance with a single form in a Thing Description.</summary>
        public static string Action(string name, string formJson)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"actions\":{\"" + name + "\":{\"forms\":[" + formJson + "]}}}";
        }

        /// <summary>Wraps an event affordance with a single form in a Thing Description.</summary>
        public static string Event(string name, string formJson)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"t\",\"events\":{\"" + name + "\":{\"forms\":[" + formJson + "]}}}";
        }
    }
}
