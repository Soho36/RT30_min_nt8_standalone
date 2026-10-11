param([string]$RoutedSource = 'I:\PycharmProjects\Accounts_staggering\nt8\RR_m_w_r_stoplimits_and_tplimit_InstanceID_WindowRR_Offsets_SafeExits_Routed_MaxRedRun.cs')
$ErrorActionPreference = 'Stop'
$sources = @(
    (Join-Path $PSScriptRoot 'RR_m_w_r_stoplimits_and_tplimit_InstanceID_WindowRR_Offsets_SafeExits_MaxRedRun.cs'),
    $RoutedSource
)
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'stop_timeout_tests.cs.txt'))
for ($index = 0; $index -lt $sources.Count; $index++) {
    $source = [IO.File]::ReadAllText($sources[$index])
    $fieldsStart = $source.IndexOf('        // Stop-limit escape')
    $fieldsEnd = $source.IndexOf("`n", $source.IndexOf('private string StopFallbackName', $fieldsStart))
    $methodsStart = $source.IndexOf('        private void StartStopFallbackTimer()')
    $methodsEnd = $source.IndexOf('        private bool ExceedsMaxRedRun()', $methodsStart)
    if ($fieldsStart -lt 0 -or $fieldsEnd -lt 0 -or $methodsStart -lt 0 -or $methodsEnd -lt 0) {
        throw "Missing timeout implementation in $($sources[$index])"
    }
    $code = $template.Replace('/* FIELDS */', $source.Substring($fieldsStart, $fieldsEnd - $fieldsStart))
    $code = $code.Replace('/* METHODS */', $source.Substring($methodsStart, $methodsEnd - $methodsStart))
    $code = $code.Replace('TimeoutTests', "TimeoutTests$index").Replace('TimeoutHarness', "TimeoutHarness$index")
    $code = $code.Replace('namespace Core', "namespace TestCore$index").Replace('Core.Globals', "TestCore$index.Globals")
    $code = $code.Replace('namespace TimeoutModel', "namespace TimeoutModel$index").Replace('using TimeoutModel;', "using TimeoutModel$index;")
    Add-Type -TypeDefinition $code
    $testType = "TimeoutTests$index" -as [type]
    $testType::Run()
    Write-Host "Timeout scenarios passed: $($sources[$index])"
}

# Compile the complete strategies and real router against installed NT8 assemblies.
# This checks API signatures without loading NinjaTrader or starting a strategy.
$ntBin = 'C:\Program Files\NinjaTrader 8\bin'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$referencePaths = @(
    "$framework\mscorlib.dll", "$framework\System.dll", "$framework\System.Core.dll",
    "$framework\System.Xml.dll", "$framework\System.ComponentModel.DataAnnotations.dll",
    "$framework\WPF\WindowsBase.dll", "$framework\WPF\PresentationCore.dll",
    "$framework\WPF\PresentationFramework.dll", "$framework\System.Xaml.dll",
    "$ntBin\NinjaTrader.Core.dll", "$ntBin\NinjaTrader.Gui.dll",
    "$ntBin\Custom\Backup\NinjaTrader.Custom.dll",
    "$ntBin\SharpDX.dll", "$ntBin\SharpDX.Direct2D1.dll"
)
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@(
    foreach ($path in $referencePaths) {
        [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path)
    }
)
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default
$trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@(
    foreach ($path in ($sources + 'I:\PycharmProjects\Accounts_staggering\nt8\PropRouter.cs')) {
        [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($path), $parseOptions, $path)
    }
)
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('StopTimeoutVerification', $trees, $references, $options)
$stream = [IO.MemoryStream]::new()
try {
    $result = $compilation.Emit($stream)
    $errors = @($result.Diagnostics | Where-Object Severity -eq 'Error')
    if (-not $result.Success) {
        $errors | Select-Object -First 15 | ForEach-Object { Write-Host $_.ToString() }
        throw 'Complete NT8 strategy compilation failed.'
    }
    Write-Host 'Both complete strategies compiled against installed NT8 assemblies.'
}
finally { $stream.Dispose() }
