@echo off
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
pause
