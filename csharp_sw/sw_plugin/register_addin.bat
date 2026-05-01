@echo off
:: 1. 自动检测并请求管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 正在请求管理员权限...
    powershell -Command "Start-Process cmd -ArgumentList '/c', '%~s0' -Verb RunAs"
    exit /b
)

:: 2. 下面是正式的执行代码
echo ===================================
echo 正在注册 SolidWorks 插件...
echo ===================================

:: 定义 DLL 路径 (使用双引号包裹，防止路径有空格)
set "dll_path=%~dp0sw_plugin.dll"

:: 检查文件是否存在
if not exist "%dll_path%" (
    echo 错误：找不到文件 "%dll_path%"
    echo 请确保 sw_plugin.dll 和脚本在同一个文件夹内
    pause
    exit /b
)

:: 3. 执行注册命令
:: 注意：这里使用 call 是为了让 regasm 的错误信息能正确传递给批处理
call %windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe "%dll_path%" /codebase

:: 4. 检查执行结果
if %errorLevel% neq 0 (
    echo.
    echo [失败] 注册出错，请检查上方信息。
) else (
    echo.
    echo [成功] 插件注册成功！请打开 SolidWorks 查看。
)

pause