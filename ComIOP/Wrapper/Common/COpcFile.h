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

#ifndef _COpcFile_H_
#define _COpcFile_H_

#include "COpcString.h"

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

//==============================================================================
// CLASS:   COpcFile
// PURPOSE  Facilitiates manipulation of XML Elements,

class COpcFile
{
    OPC_CLASS_NEW_DELETE();

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcFile();
            
    // Destructor
    ~COpcFile();

    //==========================================================================
    // Public Methods

	// Create
	bool Create(const COpcString& cFileName);

	// Open
	bool Open(const COpcString& cFileName, bool bReadOnly = true);

	// Close
	void Close();

	// Read
	UINT Read(BYTE* pBuffer, UINT uSize);

	// Write
	UINT Write(BYTE* pBuffer, UINT uSize);
	
	// GetFileSize
	UINT GetFileSize();

	// GetLastModified
	FILETIME GetLastModified();

	// GetMemoryMapping
	BYTE* GetMemoryMapping();

private:

    //==========================================================================
    // Private Members

	HANDLE m_hFile;
	HANDLE m_hMapping;
	BYTE*  m_pView;
	bool   m_bReadOnly;
};

#endif // _COpcFile_H_
