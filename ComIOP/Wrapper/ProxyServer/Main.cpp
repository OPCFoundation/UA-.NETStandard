/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

#include "stdafx.h"

using namespace System;
using namespace System::Text;
using namespace System::IO;
using namespace System::Diagnostics;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Collections::Generic;
using namespace Opc::Ua::Configuration;

extern "C" int WINAPI _tWinMain(
    HINSTANCE hInstance, 
	HINSTANCE hPrevInstance, 
    LPTSTR    lpCmdLine, 
    int       nShowCmd);

ref class ComProxyApplicationInstance : public ApplicationInstance
{
public:

    virtual void LoadInstallConfig(String^ configFile) override
    {
        LoadInstallFromUnmanagedResource();
        ApplicationInstance::LoadInstallConfig(configFile);
    }

    virtual array<ArgumentDescription^>^ GetArgumentDescriptions() override
    {
		List<ArgumentDescription^>^ descriptions = gcnew List<ArgumentDescription^>();
		descriptions->AddRange(ApplicationInstance::GetArgumentDescriptions());
		descriptions->AddRange(s_SupportedArguments);
		return descriptions->ToArray();
    }
        
	virtual bool ProcessCommand(bool silent, Dictionary<String^, String^>^ args) override
	{
		if (args->ContainsKey("/import"))
		{
			String^ errors = PseudoComServer::Import(args["/import"]);

			if (!String::IsNullOrEmpty(errors))
			{
				throw gcnew Opc::Ua::ServiceResultException(errors);
			}

			return true;
		}

		if (args->ContainsKey("/delete"))
		{
			PseudoComServer::Delete(args["/delete"]);
			return true;
		}

		if (args->ContainsKey("/export"))
		{
			String^ arg = nullptr;

			if (args->TryGetValue("/progid", arg))
			{
				array<String^>^ progIds = arg->Split(progIds, StringSplitOptions::RemoveEmptyEntries);
				PseudoComServer::Export(args["/export"], progIds);
			}
			else
			{
				PseudoComServer::Export(args["/export"]);
			}

			return true;
		}

		return ApplicationInstance::ProcessCommand(silent, args);
	}

private:

    static array<ArgumentDescription^>^ s_SupportedArguments = gcnew array<ArgumentDescription^>
    {
        gcnew ArgumentDescription("/regserver", false, false, "Registers the COM EXE server."),
        gcnew ArgumentDescription("/unregserver", false, false, "Unregisters the COM EXE server."),
        gcnew ArgumentDescription("/service", false, false, "Registers the COM EXE server as Windows Service."),
        gcnew ArgumentDescription("/import", true, true, "Imports and registers COM psuedo-servers."),
        gcnew ArgumentDescription("/export", true, true, "Exports the COM psuedo-servers to a file."),
        gcnew ArgumentDescription("/delete", true, true, "Deletes the COM psuedo-servers contained a file."),
        gcnew ArgumentDescription("/progid", true, true, "A comma seperated list of progIds for COM psuedo-servers.")
    };

    bool LoadInstallFromUnmanagedResource()  
    { 
        HRSRC info = FindResource(NULL, L"CONFIG", L"BIN"); 

        if (info == NULL)
        {
            return false;
        }

        HGLOBAL glow = LoadResource(NULL, info); 

        if (glow == NULL)
        {
            return false;
        }

        try
        {
            char* lpResLock = (char*)LockResource(glow);  

            if (lpResLock == NULL)
            {
                return false;
            }

            DWORD dwSizeRes = SizeofResource(NULL, info);

            if (dwSizeRes > 0)
            {
                try
                {
                    array<unsigned char>^ bytes = gcnew array<unsigned char>(dwSizeRes);
                    Marshal::Copy((IntPtr)lpResLock, bytes, 0, dwSizeRes);
                    MemoryStream^ istrm = gcnew MemoryStream(bytes, false);
                    InstallConfig = this->LoadInstallConfigFromStream(istrm);
                    return true;
                }
                catch (Exception^ e)
                {
                    Opc::Ua::Utils::Trace(e, "Unexpected error loading default installation configuration.");
                }
            }
        }
        finally
        {
            FreeResource(glow);
        }

        return false;
    }
};

int main(array<System::String ^>^ args)
{   
    ComProxyApplicationInstance^ application = gcnew ComProxyApplicationInstance();
    application->ApplicationName   = "UA COM Proxy Server";
    application->ApplicationType   = Opc::Ua::ApplicationType::Client;
    application->ConfigSectionName = "Opc.Ua.ComProxyServer";

    try
    {	
        if (application->ProcessCommandLine(true))
        {
            return 0;
        }
    }
    catch (Exception^ e)
    {
        Opc::Ua::Utils::Trace(e, "Could not process command line arguments.");

		if (Environment::UserInteractive)
		{
			StringBuilder^ message = gcnew StringBuilder();

			for (Exception^ ii = e; ii != nullptr; ii = ii->InnerException)
			{
				message->Append(ii->Message);
				message->Append("\r\n");
			}

			LPWSTR szMessage = (LPWSTR)Marshal::StringToCoTaskMemUni(message->ToString()).ToPointer();
			LPWSTR szCaption = (LPWSTR)Marshal::StringToCoTaskMemUni(application->ApplicationName).ToPointer();
			::MessageBox(NULL, szMessage, szCaption, 0);
			CoTaskMemFree(szMessage);
			CoTaskMemFree(szCaption);
		}

        return 0;
    }

	#ifdef _DEBUG
	if (Environment::UserInteractive)
	{
		bool halt = false;
		
		if (args != nullptr && args->Length > 0)
		{
			if (String::Compare(args[0], "/RegServer", true) == 0 || String::Compare(args[0], "/UnRegServer", true) == 0 )
			{
				halt = false;
			}
		}

		if (halt)
		{
			::MessageBox(NULL, L"Process halted to allow time to attach Debugger", L"UA COM Proxy Server", 0);
		}
	}
	#endif

    Assembly^ assembly = Assembly::GetExecutingAssembly();
	array<Module^>^ modules = assembly->GetModules(false);
	HINSTANCE hInstance = (HINSTANCE)Marshal::GetHINSTANCE(modules[0]).ToPointer();
	_tWinMain(hInstance, 0, 0, 0);
    return 0;
}
