@echo off
dotnet publish "%~dp0Application\MORCA\MORCA.csproj" -f net10.0-windows10.0.19041.0 -p:PublishProfile=win-x64
