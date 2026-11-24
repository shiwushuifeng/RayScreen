@echo off
cd /d "C:\DSH NEXT\develop\RayScreen"
call uninstall.cmd < nul > "tests\uninstall3.log" 2>&1
