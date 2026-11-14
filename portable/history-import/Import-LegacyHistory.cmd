@echo off
setlocal
chcp 65001 >nul
echo Wuwa Tappo 自动助手 - 旧版历史导入 by Phrolova2405
echo.
echo 运行前请先停止自动助手后台并关闭控制面板。
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Import-LegacyHistory.ps1"
set RESULT=%ERRORLEVEL%
echo.
if not "%RESULT%"=="0" (
  echo 导入失败，请查看上方错误信息。
) else (
  echo 可以关闭此窗口。
)
pause
exit /b %RESULT%
