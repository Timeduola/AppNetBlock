# AppNetBlock 一键构建脚本
# 依赖: Windows 自带的 .NET Framework 4.x 编译器 (csc.exe), 无需安装任何 SDK
# 用法: 在仓库根目录执行  .\build.ps1
#      产物输出至 dist\AppNetBlock.exe

param(
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Push-Location $root
try {
    # 定位系统自带编译器（优先 64 位）
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path -LiteralPath $csc)) {
        $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }
    if (-not (Test-Path -LiteralPath $csc)) {
        Write-Error "未找到 .NET Framework 编译器 csc.exe，无法构建（系统被精简?）"
    }

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $out = Join-Path $OutputDir "AppNetBlock.exe"

    $cscArgs = @(
        "/nologo",
        "/target:exe",
        "/platform:anycpu",
        "/codepage:65001",
        "/optimize+",
        "/out:$out",
        "/win32icon:src\app.ico",
        "/win32manifest:src\app.manifest",
        "/r:Microsoft.CSharp.dll",
        "/r:System.dll",
        "/r:System.Core.dll",
        "src\Program.cs"
    )
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ""
    Write-Host "构建完成: $out"
    Write-Host ("文件大小: {0:N1} KB" -f ((Get-Item $out).Length / 1KB))
}
finally {
    Pop-Location
}
