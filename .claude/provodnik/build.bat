@echo off
rem ---------------------------------------------------------------------------
rem  Sobiraet provodnik.exe kompilyatorom, kotoryy uzhe est v Windows.
rem  Visual Studio i .NET SDK ne nuzhny. Prosto dvoynoy klik po etomu faylu.
rem  Gotovyy exe kladyotsya v koren proekta (dve papki vverh).
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [!] Ne nayden kompilyator C# ^(.NET Framework 4^).
  pause
  exit /b 1
)

set OUT=..\..\provodnik.exe
set FLAGS=/nologo /target:winexe /platform:anycpu /optimize+ /warn:1
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
set WEB=/resource:src\web\admin.html,admin.html /resource:src\web\mobile.html,mobile.html /resource:src\web\style.css,style.css

echo.
echo Kompiliruyu...
"%CSC%" %FLAGS% /win32icon:icons\provodnik.ico %REFS% %WEB% /out:"%OUT%" src\*.cs
if errorlevel 1 (
  echo.
  echo [!] Oshibka sborki. Smotrite soobshcheniya vyshe.
  pause
  exit /b 1
)

echo.
echo [OK] Gotovo: provodnik.exe
echo      Etot fayl mozhno prosto skopirovat na drugoy komputer i zapustit.
echo.
pause
