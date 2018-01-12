#!/bin/bash
echo Test the Mono console server and console client
workdir=$(pwd)
testresult=0

cd SampleApplications/Samples/NetCoreConsoleServer
echo build server
msbuild /p:configuration=Debug /t:restore,compile MonoConsoleServer.csproj
echo start server
cd SampleApplications/Samples/NetCoreConsoleServer/bin/Debug/net46
mono MonoConsoleServer.exe -t 60 -a &
serverpid="$!"
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleClient
echo build client
msbuild /p:configuration=Debug /t:restore,compile MonoConsoleClient.csproj
echo start client
cd SampleApplications/Samples/NetCoreConsoleClient/bin/Debug/net46
mono MonoConsoleClient.exe -t 20 &
clientpid="$!"
cd $workdir

echo wait for client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresult=$?
	echo "FAILED - Client test failed with a status of $testresult"
fi

echo wait for server
wait $serverpid
serverresult=$?

if [ $? -eq 0 ]; then
	echo "SUCCESS - Server test passed"
else
	testresult=$?
	echo "FAILED - Server test failed with a status of $testresult"
fi

exit $testresult



