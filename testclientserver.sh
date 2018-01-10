#!/bin/bash
echo Test the console server and console client
workdir=$(pwd)
testresult=0

cd SampleApplications/Samples/NetCoreConsoleServer
echo build server
dotnet build NetCoreConsoleServer.csproj
echo start server
dotnet run --no-restore --no-build --project NetCoreConsoleServer.csproj -t 60 -a &
serverpid="$!"
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleClient
echo build client
dotnet build
echo start client
dotnet run --no-restore --no-build -t 20 &
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



