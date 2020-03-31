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
using System.Collections;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Opc.Ua.Com
{
	/// <summary>
	/// Defines all well known COM DA HRESULT codes.
	/// </summary>
	public static class ResultIds
	{		
	    // Defines all well known COM DA HRESULT codes.

		/// <remarks/>
		public const int S_OK                          = +0x00000000; // 0x00000000
		/// <remarks/>
		public const int S_FALSE                       = +0x00000001; // 0x00000001
		/// <remarks/>
		public const int E_NOTIMPL                     = -0x7FFFBFFF; // 0x80004001
		/// <remarks/>
		public const int E_OUTOFMEMORY                 = -0x7FF8FFF2; // 0x8007000E
		/// <remarks/>
		public const int E_INVALIDARG                  = -0x7FF8FFA9; // 0x80070057
		/// <remarks/>
		public const int E_NOINTERFACE                 = -0x7FFFBFFE; // 0x80004002
		/// <remarks/>
		public const int E_POINTER                     = -0x7FFFBFFD; // 0x80004003
		/// <remarks/>
		public const int E_FAIL                        = -0x7FFFBFFB; // 0x80004005
		/// <remarks/>
        public const int E_ACCESSDENIED                = -0x7FF8FFFB; // 0x80070005
        
		/// <remarks/>
		public const int CONNECT_E_NOCONNECTION        = -0x7FFBFE00; // 0x80040200
		/// <remarks/>
		public const int CONNECT_E_ADVISELIMIT         = -0x7FFBFDFF; // 0x80040201
		/// <remarks/>
		public const int DISP_E_TYPEMISMATCH           = -0x7FFDFFFB; // 0x80020005
		/// <remarks/>
		public const int DISP_E_OVERFLOW               = -0x7FFDFFF6; // 0x8002000A
		/// <remarks/>
		public const int E_INVALIDHANDLE               = -0x3FFBFFFF; // 0xC0040001
		/// <remarks/>
		public const int E_BADTYPE                     = -0x3FFBFFFC; // 0xC0040004
		/// <remarks/>
		public const int E_PUBLIC                      = -0x3FFBFFFB; // 0xC0040005
		/// <remarks/>
		public const int E_BADRIGHTS                   = -0x3FFBFFFA; // 0xC0040006
		/// <remarks/>
		public const int E_UNKNOWNITEMID               = -0x3FFBFFF9; // 0xC0040007
		/// <remarks/>
		public const int E_INVALIDITEMID               = -0x3FFBFFF8; // 0xC0040008
		/// <remarks/>
		public const int E_INVALIDFILTER               = -0x3FFBFFF7; // 0xC0040009
		/// <remarks/>
		public const int E_UNKNOWNPATH                 = -0x3FFBFFF6; // 0xC004000A
		/// <remarks/>
		public const int E_RANGE                       = -0x3FFBFFF5; // 0xC004000B
		/// <remarks/>
		public const int E_DUPLICATENAME               = -0x3FFBFFF4; // 0xC004000C
		/// <remarks/>
		public const int S_UNSUPPORTEDRATE             = +0x0004000D; // 0x0004000D
		/// <remarks/>
		public const int S_CLAMP                       = +0x0004000E; // 0x0004000E
		/// <remarks/>
		public const int S_INUSE                       = +0x0004000F; // 0x0004000F
		/// <remarks/>
		public const int E_INVALIDCONFIGFILE           = -0x3FFBFFF0; // 0xC0040010
		/// <remarks/>
		public const int E_NOTFOUND                    = -0x3FFBFFEF; // 0xC0040011
		/// <remarks/>
		public const int E_INVALID_PID                 = -0x3FFBFDFD; // 0xC0040203
		/// <remarks/>
		public const int E_DEADBANDNOTSET              = -0x3FFBFC00; // 0xC0040400
		/// <remarks/>
		public const int E_DEADBANDNOTSUPPORTED        = -0x3FFBFBFF; // 0xC0040401
		/// <remarks/>
		public const int E_NOBUFFERING                 = -0x3FFBFBFE; // 0xC0040402
		/// <remarks/>
		public const int E_INVALIDCONTINUATIONPOINT    = -0x3FFBFBFD; // 0xC0040403
		/// <remarks/>
		public const int S_DATAQUEUEOVERFLOW           = +0x00040404; // 0x00040404	
		/// <remarks/>
		public const int E_RATENOTSET                  = -0x3FFBFBFB; // 0xC0040405
		/// <remarks/>
		public const int E_NOTSUPPORTED                = -0x3FFBFBFA; // 0xC0040406

	    // Defines all well known Complex Data HRESULT codes.

		/// <remarks/>
		public const int E_TYPE_CHANGED                = -0x3FFBFBF9; // 0xC0040407
		/// <remarks/>
		public const int E_FILTER_DUPLICATE            = -0x3FFBFBF8; // 0xC0040408
		/// <remarks/>
		public const int E_FILTER_INVALID              = -0x3FFBFBF7; // 0xC0040409
		/// <remarks/>
		public const int E_FILTER_ERROR                = -0x3FFBFBF6; // 0xC004040A
		/// <remarks/>
		public const int S_FILTER_NO_DATA              = +0x0004040B; // 0xC004040B
        
	    // Defines all well known COM HDA HRESULT codes.

        /// <remarks/>
		public const int E_MAXEXCEEDED      = -0X3FFBEFFF; // 0xC0041001
		/// <remarks/>
		public const int S_NODATA           = +0x40041002; // 0x40041002
		/// <remarks/>
		public const int S_MOREDATA         = +0x40041003; // 0x40041003
		/// <remarks/>
		public const int E_INVALIDAGGREGATE = -0X3FFBEFFC; // 0xC0041004
		/// <remarks/>
		public const int S_CURRENTVALUE     = +0x40041005; // 0x40041005
		/// <remarks/>
		public const int S_EXTRADATA        = +0x40041006; // 0x40041006
		/// <remarks/>
		public const int W_NOFILTER         = -0x7FFBEFF9; // 0x80041007
		/// <remarks/>
		public const int E_UNKNOWNATTRID    = -0x3FFBEFF8; // 0xC0041008
		/// <remarks/>
		public const int E_NOT_AVAIL        = -0x3FFBEFF7; // 0xC0041009
		/// <remarks/>
		public const int E_INVALIDDATATYPE  = -0x3FFBEFF6; // 0xC004100A
		/// <remarks/>
		public const int E_DATAEXISTS       = -0x3FFBEFF5; // 0xC004100B
		/// <remarks/>
		public const int E_INVALIDATTRID    = -0x3FFBEFF4; // 0xC004100C
		/// <remarks/>
		public const int E_NODATAEXISTS     = -0x3FFBEFF3; // 0xC004100D
		/// <remarks/>
		public const int S_INSERTED         = +0x4004100E; // 0x4004100E
		/// <remarks/>
		public const int S_REPLACED         = +0x4004100F; // 0x4004100F

	    // Defines all well known COM AE HRESULT codes.

		/// <remarks/>
		public const int S_ALREADYACKED         = +0x00040200; // 0x00040200
		/// <remarks/>
		public const int S_INVALIDBUFFERTIME    = +0x00040201; // 0x00040201
		/// <remarks/>
		public const int S_INVALIDMAXSIZE       = +0x00040202; // 0x00040202
		/// <remarks/>
		public const int S_INVALIDKEEPALIVETIME = +0x00040203; // 0x00040203
		/// <remarks/>
		public const int E_INVALIDBRANCHNAME    = -0x3FFBFDFD; // 0xC0040203
		/// <remarks/>
		public const int E_INVALIDTIME          = -0x3FFBFDFC; // 0xC0040204
		/// <remarks/>
		public const int E_BUSY                 = -0x3FFBFDFB; // 0xC0040205
		/// <remarks/>
		public const int E_NOINFO               = -0x3FFBFDFA; // 0xC0040206

        #region Static Helper Functions
        /// <summary>
		/// Returns the name of the error code.
		/// </summary>
        public static string GetBrowseName(int identifier)
		{
			FieldInfo[] fields = typeof(ResultIds).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
                if (identifier == (int)field.GetValue(typeof(ResultIds)))
				{
					return field.Name;
				}
			}

			return System.String.Empty;
		}

		/// <summary>
		/// Returns the numeric value for the error code.
		/// </summary>
        public static uint GetIdentifier(string browseName)
		{
			FieldInfo[] fields = typeof(ResultIds).GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (FieldInfo field in fields)
			{
				if (field.Name == browseName)
				{
                    return (uint)field.GetValue(typeof(ResultIds));
				}
			}

			return 0;
        }
        #endregion
	}
}
