@echo off
dotnet fsi build.fsx -- -p build -p push
pause
