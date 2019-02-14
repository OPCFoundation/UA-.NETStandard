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

namespace Opc.Ua.Com.Client
{        
    /// <summary>
    /// A object which maps a segment to a UA object.
    /// </summary>
    public partial class AeSourceState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="AeSourceState"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="areaId">The area id.</param>
        /// <param name="qualifiedName">The qualified name for the source.</param>
        /// <param name="name">The name of the source.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public AeSourceState(
            ISystemContext context,
            string areaId,
            string qualifiedName,
            string name,
            ushort namespaceIndex)
            :
                base(null)
        {
            m_areaId = areaId;
            m_qualifiedName = qualifiedName;

            this.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType;
            this.NodeId = AeModelUtils.ConstructIdForSource(m_areaId, name, namespaceIndex);
            this.BrowseName = new QualifiedName(name, namespaceIndex);
            this.DisplayName = this.BrowseName.Name;
            this.Description = null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;
            this.EventNotifier = EventNotifiers.None;

            this.AddReference(ReferenceTypeIds.HasNotifier, true, AeModelUtils.ConstructIdForArea(m_areaId, namespaceIndex));
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the qualified name for the source.
        /// </summary>
        /// <value>The qualified name for the source.</value>
        public string QualifiedName
        {
            get { return m_qualifiedName; }
        }
        #endregion

        #region Private Fields
        private string m_areaId;
        private string m_qualifiedName;
        #endregion
    }
}
