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

#ifndef _COpcTextReader_H
#define _COpcTextReader_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcText.h"

//==============================================================================
// CLASS:   COpcTextReader
// PURPOSE: Extracts tokens from a stream.

class OPCUTILS_API COpcTextReader
{
    OPC_CLASS_NEW_DELETE();

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcTextReader(const COpcString& cBuffer);  
    COpcTextReader(LPCSTR szBuffer, UINT uLength = -1);  
    COpcTextReader(LPCWSTR szBuffer, UINT uLength = -1);  
 
    // Destructor
    ~COpcTextReader(); 

    //==========================================================================
    // Public Methods
  
    // GetNext
    bool GetNext(COpcText& cText);

    // GetBuf
    LPCWSTR GetBuf() const { return m_szBuf; }

private:

    //==========================================================================
    // Private Methods

    // ReadData
    bool ReadData();

    // FindToken
    bool FindToken(COpcText& cText);

    // FindLiteral
    bool FindLiteral(COpcText& cText);

    // FindNonWhitespace
    bool FindNonWhitespace(COpcText& cText);

    // FindWhitespace
    bool FindWhitespace(COpcText& cText);
    
    // FindDelimited
    bool FindDelimited(COpcText& cText);

    // FindEnclosed
    bool FindEnclosed(COpcText& cText);

    // CheckForHalt
    bool CheckForHalt(COpcText& cText, UINT uIndex);
    
    // CheckForDelim
    bool CheckForDelim(COpcText& cText, UINT uIndex);

    // SkipWhitespace
    UINT SkipWhitespace(COpcText& cText);

    // CopyData
    void CopyData(COpcText& cText, UINT uStart, UINT uEnd);

    //==========================================================================
    // Private Members

    LPWSTR m_szBuf;
    UINT   m_uLength;
    UINT   m_uEndOfData;
};

#endif //ndef _COpcTextReader_H
