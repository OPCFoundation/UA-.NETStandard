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

namespace Quickstarts.DataAccessServer
{
    /// <summary>
    /// Stores information about a segment in the system.
    /// </summary>
    public class UnderlyingSystemSegment
    {
        #region Public Members
        /// <summary>
        /// Initializes a new instance of the <see cref="UnderlyingSystemSegment"/> class.
        /// </summary>
        public UnderlyingSystemSegment()
        {
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the unique id for the segment.
        /// </summary>
        /// <value>The unique id for the segment</value>
        public string Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        /// <summary>
        /// Gets or sets the name of the segment.
        /// </summary>
        /// <value>The name of the segment.</value>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or sets the type of the segment.
        /// </summary>
        /// <value>The type of the segment.</value>
        public string SegmentType
        {
            get { return m_segmentType; }
            set { m_segmentType = value; }
        }
        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        private string m_id;
        private string m_name;
        private string m_segmentType;
        #endregion
    }
}
