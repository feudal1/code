@echo off
echo ===================================
echo 正在卸载 SolidWorks 插件...
echo ===================================

:: 以管理员身份运行 regasm 进行卸载
%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe "%~dp0sw_plugin.dll" /unregister


pause
