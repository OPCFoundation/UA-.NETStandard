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

#ifndef _OpcRegistry_H_
#define _OpcRegistry_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"

//==============================================================================
// FUNCTION: OpcRegGetValue
// PURPOSE:  Gets a string value from the registry.

bool OPCUTILS_API OpcRegGetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	LPTSTR* ptsValue
);

//==============================================================================
// FUNCTION: OpcRegGetValue
// PURPOSE:  Gets a DWORD value from the registry.

bool OPCUTILS_API OpcRegGetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	DWORD*  pdwValue
);

//==============================================================================
// FUNCTION: OpcRegGetValue
// PURPOSE:  Gets a DWORD value from the registry.

bool OPCUTILS_API OpcRegGetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	BYTE**  ppValue,
	DWORD*  pdwLength
);

//==============================================================================
// FUNCTION: OpcRegSetValue
// PURPOSE:  Sets a string value in the registry.

bool OPCUTILS_API OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	LPCTSTR tsValue
);

//==============================================================================
// FUNCTION: OpcRegSetValue
// PURPOSE:  Gets a DWORD value from the registry.

bool OPCUTILS_API OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	DWORD   dwValue
);

//==============================================================================
// FUNCTION: OpcRegSetValue
// PURPOSE:  Sets a string value in the registry.

bool OPCUTILS_API OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	BYTE*   pValue,
	DWORD   dwLength
);

//==============================================================================
// FUNCTION: OpcRegDeleteKey
// PURPOSE:  Recursively deletes a key and all sub keys.
// NOTES:

bool OPCUTILS_API OpcRegDeleteKey(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey
);

#endif //ndef _OpcRegistry_H_
