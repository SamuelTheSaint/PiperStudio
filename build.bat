@echo off
rem Builds PiperStudio.exe with the C# compiler that comes with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:PiperStudio.exe /win32icon:app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll src\*.cs
if errorlevel 1 (echo Build failed. & pause & exit /b 1)
echo Built PiperStudio.exe
pause
