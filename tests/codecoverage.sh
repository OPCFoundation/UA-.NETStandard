#!/bin/bash
# SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
# SPDX-License-Identifier: MIT

BUILDROOT=$(pwd)/..
cd $BUILDROOT

rm -r ./CodeCoverage
rm -r ./TestResults
dotnet test "UA.slnx" -v n --configuration Release --framework net10.0 --collect:"XPlat Code Coverage"  --settings ./tests/coverlet.runsettings.xml --results-directory ./TestResults

# ensure latest report tool is installed
dotnet tool uninstall -g dotnet-reportgenerator-globaltool
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./CodeCoverage -reporttypes:"Badges;Html;HtmlSummary;Cobertura"

# Display result in browser (mac OS)
open ./CodeCoverage/index.html