@echo off
rem ---------------------------------------------------------------------------
rem  Sobiraet OBE versii programmy kompilyatorom, kotoryy uzhe est v Windows.
rem  Visual Studio i .NET SDK ne nuzhny. Prosto dvoynoy klik po etomu faylu.
rem
rem    provodnik.exe      - chistyy: tolko lokalnaya set (~60 KB)
rem    provodnik-pro.exe  - plyus publichnyy QR cherez internet (~52 MB)
rem
rem  Gotovye exe kladutsya v koren proekta (dve papki vverh).
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

rem koren proekta: iz .claude\provodnik\ podnimaemsya na dva urovnya
set OUTDIR=..\..
set FLAGS=/nologo /target:winexe /platform:anycpu /optimize+ /warn:1
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
set WEB=/resource:src\web\mobile.html,mobile.html /resource:src\web\style.css,style.css

echo.
echo === 1/2: provodnik.exe (chistyy, bez interneta) ===
"%CSC%" %FLAGS% /define:LOCAL_ONLY /win32icon:icons\provodnik.ico %REFS% %WEB% ^
  /out:"%OUTDIR%\provodnik.exe" src\*.cs
if errorlevel 1 goto oshibka

echo.
echo === 2/2: provodnik-pro.exe (s publichnym QR) ===
if not exist "cloudflared.exe" (
  echo [!] Net faila cloudflared.exe - bez nego pro-versiya ne sobiraetsya.
  echo     Skachayte ego zdes i polozhite ryadom s etim faylom:
  echo     https://github.com/cloudflare/cloudflared/releases/latest
  echo     nuzhen fayl cloudflared-windows-amd64.exe, pereimenuyte v cloudflared.exe
  echo.
  echo     provodnik.exe pri etom uzhe sobran i rabotaet.
  pause
  exit /b 1
)
"%CSC%" %FLAGS% /win32icon:icons\provodnik-pro.ico %REFS% %WEB% ^
  /resource:cloudflared.exe,cloudflared.exe ^
  /out:"%OUTDIR%\provodnik-pro.exe" src\*.cs
if errorlevel 1 goto oshibka

echo.
echo [OK] Gotovo:
echo      provodnik.exe      - chistyy
echo      provodnik-pro.exe  - s publichnym QR
echo.
echo Lyuboy iz nih mozhno prosto skopirovat na drugoy komputer i zapustit.
echo.
pause
exit /b 0

:oshibka
echo.
echo [!] Oshibka sborki. Smotrite soobshcheniya vyshe.
pause
exit /b 1
