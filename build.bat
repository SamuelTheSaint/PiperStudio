@echo off
rem Builds PiperStudio.exe with the C# compiler that comes with Windows (.NET Framework 4.x).
rem No Visual Studio needed. Also used by the automatic GitHub build (CI=true: no pause).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:PiperStudio.exe /win32icon:app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.IO.Compression.dll /r:System.Security.dll /resource:fonts\Vazirmatn-Regular.ttf,Vazirmatn-Regular.ttf /resource:fonts\Vazirmatn-Bold.ttf,Vazirmatn-Bold.ttf src\*.cs
if errorlevel 1 goto failed
echo Built PiperStudio.exe
if not defined CI pause
exit /b 0
:failed
echo Build failed.
if not defined CI pause
exit /b 1
