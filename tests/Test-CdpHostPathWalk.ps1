[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$cscCandidates = @(
    (Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:SystemRoot "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($csc)) {
    throw "找不到 .NET Framework C# 编译器 csc.exe。"
}

$frameworkDirectory = Split-Path $csc -Parent
$testExe = Join-Path $env:TEMP (
    "wuwatappo-cdp-path-test-{0}.exe" -f [guid]::NewGuid().ToString("N")
)
try {
    $compilerArguments = @(
        "/nologo",
        "/target:exe",
        "/platform:x86",
        "/optimize+",
        "/codepage:65001",
        "/main:WuwaTappoCdpHostTests.CdpHostSelfTestProgram",
        "/out:$testExe",
        "/reference:$(Join-Path $frameworkDirectory 'System.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Core.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Drawing.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Management.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Windows.Forms.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Web.Extensions.dll')",
        (Join-Path $projectRoot "portable\CdpHost.cs"),
        (Join-Path $projectRoot "tests\CdpHostSelfTest.cs")
    )
    & $csc @compilerArguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $testExe -PathType Leaf)) {
        throw "CDP Host 路径自检编译失败，csc 退出码：$LASTEXITCODE"
    }

    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "CDP Host 路径自检失败，退出码：$LASTEXITCODE"
    }
}
finally {
    if (Test-Path -LiteralPath $testExe -PathType Leaf) {
        [System.IO.File]::Delete($testExe)
    }
}
