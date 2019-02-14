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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using OpcRcw.Ae;

namespace Opc.Ua.Com.Client
{        
    /// <summary>
    /// A object which represents a COM AE condition.
    /// </summary>
    public partial class AeConditionState : AlarmConditionState
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="AeConditionState"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="acknowledgeMethod">The acknowledge method.</param>
        public AeConditionState(ISystemContext context, NodeHandle handle, AddCommentMethodState acknowledgeMethod)
        :
            base(null)
        {
            AeParsedNodeId parsedNodeId = (AeParsedNodeId)handle.ParsedNodeId;

            this.NodeId = handle.NodeId;

            this.TypeDefinitionId = AeParsedNodeId.Construct(
                Constants.CONDITION_EVENT, 
                parsedNodeId.CategoryId, 
                parsedNodeId.ConditionName, 
                parsedNodeId.NamespaceIndex);

            this.Acknowledge = acknowledgeMethod;
            this.AddChild(acknowledgeMethod);
        }
        #endregion

        #region Public Properties
        #endregion

        #region Private Fields
        #endregion
    }
}
