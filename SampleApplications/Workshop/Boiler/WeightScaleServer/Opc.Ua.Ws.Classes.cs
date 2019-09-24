/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua.Di;
using Opc.Ua;

namespace Opc.Ua.Ws
{
    #region WeightScaleState Class
    #if (!OPCUA_EXCLUDE_WeightScaleState)
    /// <summary>
    /// Stores an instance of the WeightScaleType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WeightScaleState : DeviceState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public WeightScaleState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Ws.ObjectTypes.WeightScaleType, Opc.Ua.Ws.Namespaces.OpcUaWs, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AgAAAB4AAABodHRwOi8vcGhpLXdhcmUuY29tL0ZFSVNUVS9XUy8fAAAAaHR0cDovL29wY2ZvdW5kYXRp" +
           "b24ub3JnL1VBL0RJL/////+EYIACAQAAAAEAFwAAAFdlaWdodFNjYWxlVHlwZUluc3RhbmNlAQGlOwEB" +
           "pTulOwAAAf////8KAAAAJGCACgEAAAABAAwAAABQYXJhbWV0ZXJTZXQBAaY7AwAAAAAXAAAARmxhdCBs" +
           "aXN0IG9mIFBhcmFtZXRlcnMALwA6pjsAAP////8BAAAANWCJCgIAAAACAAsAAAB3ZWlnaHRTY2FsZQEB" +
           "6zsDAAAAAA4AAABBY3R1YWwgd2VpZ2h0LgAvAQBACes7AAAAC/////8BAf////8DAAAAFWCJCgIAAAAA" +
           "AA8AAABJbnN0cnVtZW50UmFuZ2UBAe47AC4ARO47AAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BwAAAEVVUmFuZ2UBAe87AC4ARO87AAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAAAEAAAAEVuZ2lu" +
           "ZWVyaW5nVW5pdHMBAfA7AC4ARPA7AAABAHcD/////wEB/////wAAAAAkYIAKAQAAAAEACQAAAE1ldGhv" +
           "ZFNldAEBqDsDAAAAABQAAABGbGF0IGxpc3Qgb2YgTWV0aG9kcwAvADqoOwAA/////wIAAAAkYYIKBAAA" +
           "AAEABAAAAFRhcmUBAfE7AwAAAAANAAAAVGFyZSBiYWxuYWNlLgAvAQHxO/E7AAABAf////8AAAAAJGGC" +
           "CgQAAAABAAQAAABaZXJvAQHyOwMAAAAADQAAAFplcm8gYmFsbmFjZS4ALwEB8jvyOwAAAQH/////AAAA" +
           "ADVgiQoCAAAAAgAMAAAATWFudWZhY3R1cmVyAQG8OwMAAAAAMAAAAE5hbWUgb2YgdGhlIGNvbXBhbnkg" +
           "dGhhdCBtYW51ZmFjdHVyZWQgdGhlIGRldmljZQAuAES8OwAAABX/////AQH/////AAAAADVgiQoCAAAA" +
           "AgAFAAAATW9kZWwBAb47AwAAAAAYAAAATW9kZWwgbmFtZSBvZiB0aGUgZGV2aWNlAC4ARL47AAAAFf//" +
           "//8BAf////8AAAAANWCJCgIAAAACABAAAABIYXJkd2FyZVJldmlzaW9uAQG/OwMAAAAALAAAAFJldmlz" +
           "aW9uIGxldmVsIG9mIHRoZSBoYXJkd2FyZSBvZiB0aGUgZGV2aWNlAC4ARL87AAAADP////8BAf////8A" +
           "AAAANWCJCgIAAAACABAAAABTb2Z0d2FyZVJldmlzaW9uAQHAOwMAAAAANQAAAFJldmlzaW9uIGxldmVs" +
           "IG9mIHRoZSBzb2Z0d2FyZS9maXJtd2FyZSBvZiB0aGUgZGV2aWNlAC4ARMA7AAAADP////8BAf////8A" +
           "AAAANWCJCgIAAAACAA4AAABEZXZpY2VSZXZpc2lvbgEBwTsDAAAAACQAAABPdmVyYWxsIHJldmlzaW9u" +
           "IGxldmVsIG9mIHRoZSBkZXZpY2UALgBEwTsAAAAM/////wEB/////wAAAAA1YIkKAgAAAAIADAAAAERl" +
           "dmljZU1hbnVhbAEBwzsDAAAAAFoAAABBZGRyZXNzIChwYXRobmFtZSBpbiB0aGUgZmlsZSBzeXN0ZW0g" +
           "b3IgYSBVUkwgfCBXZWIgYWRkcmVzcykgb2YgdXNlciBtYW51YWwgZm9yIHRoZSBkZXZpY2UALgBEwzsA" +
           "AAAM/////wEB/////wAAAAA1YIkKAgAAAAIADAAAAFNlcmlhbE51bWJlcgEBxTsDAAAAAE0AAABJZGVu" +
           "dGlmaWVyIHRoYXQgdW5pcXVlbHkgaWRlbnRpZmllcywgd2l0aGluIGEgbWFudWZhY3R1cmVyLCBhIGRl" +
           "dmljZSBpbnN0YW5jZQAuAETFOwAAAAz/////AQH/////AAAAADVgiQoCAAAAAgAPAAAAUmV2aXNpb25D" +
           "b3VudGVyAQHHOwMAAAAAaQAAAEFuIGluY3JlbWVudGFsIGNvdW50ZXIgaW5kaWNhdGluZyB0aGUgbnVt" +
           "YmVyIG9mIHRpbWVzIHRoZSBzdGF0aWMgZGF0YSB3aXRoaW4gdGhlIERldmljZSBoYXMgYmVlbiBtb2Rp" +
           "ZmllZAAuAETHOwAAAAb/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion
}