param([string]$OutputPath = 'dist\release\Evacopy.exe')
$ErrorActionPreference = 'Stop'
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force (Split-Path -Parent $OutputPath) | Out-Null
    & "$fx\csc.exe" /nologo /target:winexe "/out:$OutputPath" /reference:"$fx\WPF\PresentationFramework.dll" /reference:"$fx\WPF\PresentationCore.dll" /reference:"$fx\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /win32icon:assets\Evacopy.ico /resource:assets\Evacopy.ico,Evacopy.ico /resource:assets\Theme.xaml,Theme.xaml /resource:assets\Evacopy.png,Evacopy.png src\SuperRobocopy.cs src\DesktopIntegration.cs src\FileSafety.cs src\Service.cs src\Worker.cs src\Tests.cs src\ProcessGuard.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    $test = Start-Process -FilePath (Join-Path $PSScriptRoot $OutputPath) -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'Self-test failed' }
    Write-Output "Built $OutputPath; CRC, path-boundary, compact layout, checkbox contrast and icon checks passed."
} finally { Pop-Location }







