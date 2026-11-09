@echo off
rem publish server (includes the PayrollEngine.Mcp.Tools assembly)
echo Publishing server...
dotnet publish ..\Server\PayrollEngine.Mcp.Server.csproj -c Release -o ..\publish

rem build static site
echo.
echo Building static HTML reference...
echo.
docfx docfx.json
echo.
