@echo off
cd /d "C:\DSH NEXT\develop\RayScreen"
call install.cmd < nul > "tests\install2.log" 2>&1
exit /b %errorlevel%
