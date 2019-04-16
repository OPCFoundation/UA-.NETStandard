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
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Maps an alarm area to a UA object node.
    /// </summary>
    public partial class AreaState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the area.
        /// </summary>
        public AreaState(
            ISystemContext context,
            AreaState parent,
            NodeId nodeId,
            AreaConfiguration configuration) 
        : 
            base(parent)
        {
            Initialize(context);

            // initialize the area with the fixed metadata.
            this.SymbolicName = configuration.Name;
            this.NodeId = nodeId;
            this.BrowseName = new QualifiedName(Utils.Format("{0}", configuration.Name), nodeId.NamespaceIndex);
            this.DisplayName = BrowseName.Name;
            this.Description = null;
            this.ReferenceTypeId = ReferenceTypeIds.HasNotifier;
            this.TypeDefinitionId = ObjectTypeIds.FolderType;
            this.EventNotifier = EventNotifiers.SubscribeToEvents;
        }
        #endregion
    }
}
