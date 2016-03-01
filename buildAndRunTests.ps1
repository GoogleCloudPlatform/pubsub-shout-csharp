# Copyright(c) 2015 Google Inc.
#
# Licensed under the Apache License, Version 2.0 (the "License"); you may not
# use this file except in compliance with the License. You may obtain a copy of
# the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
# WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
# License for the specific language governing permissions and limitations under
# the License.

function GetScriptDirectory
{
    $Invocation = (Get-Variable MyInvocation -Scope 1).Value
    Split-Path $Invocation.MyCommand.Path
}

##############################################################################
#.SYNOPSIS
# Runs code formatter on a project or solution.
#
#.INPUTS
# .sln and .csproj files.
#
#.EXAMPLE
# "myproject.sln" | Format-Code
##############################################################################
filter Format-Code {
    codeformatter.exe /rule:BraceNewLine /rule:ExplicitThis /rule:ExplicitVisibility /rule:FieldNames /rule:FormatDocument /rule:ReadonlyFields /rule:UsingLocation /nocopyright $_
    if ($LASTEXITCODE) {
        $_.FullName
        throw "codeformatter failed with exit code $LASTEXITCODE."
    }
}

##############################################################################
#.SYNOPSIS
# Runs code formatter on a project or solution.
#
#.DESCRIPTION Throws an exception if code formatter actually changed something.
#
#.INPUTS
# .sln and .csproj files.
#
#.EXAMPLE
# dir *.sln | Lint-Code
##############################################################################
filter Lint-Code {
    $_ | Format-Code
    # If git reports a diff, codeformatter changed something, and that's bad.
    $diff = git diff
    if ($diff) {
        $diff
        throw "Lint failed for $_"
    }
}

##############################################################################
# main
# Leave the user in the same directory as they started.
$originalDir = Get-Location
Try
{
    Set-Location (Join-Path (GetScriptDirectory) "windows-csharp")
    "ShoutService.sln" | Lint-Code 
    nuget restore
    msbuild /p:Configuration=Debug
    .\packages\xunit.runner.console.2.1.0\tools\xunit.console.exe .\ShoutLibTest\bin\Debug\ShoutLibTest.dll
    if ($LASTEXITCODE) {
        throw "TEST FAILED!"
    }
}
Finally
{
    Set-Location $originalDir
}
