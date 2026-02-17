param(
    [string]$PythonExe = "python",
    [string]$OutputZip = "funasr-runtime-bundle.zip",
    [string]$WorkDir = ".funasr_bundle_build",
    [string]$RuntimeDeps = "modelscope torch torchaudio numpy scipy librosa soundfile kaldiio torch-complex sentencepiece jieba pytorch-wpe oss2 tqdm umap-learn jaconv hydra-core tensorboardX requests pyyaml jamo"
)

$ErrorActionPreference = "Stop"

function Run-Step([string]$cmd, [string]$arguments) {
    Write-Host "[bundle] $cmd $arguments"
    $p = Start-Process -FilePath $cmd -ArgumentList $arguments -PassThru -Wait -NoNewWindow
    if ($p.ExitCode -ne 0) {
        throw "Command failed ($($p.ExitCode)): $cmd $arguments"
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$buildRoot = Join-Path $root $WorkDir
$venvRoot = Join-Path $buildRoot "venv"
$venvPython = Join-Path $venvRoot "Scripts\python.exe"

if (Test-Path $buildRoot) {
    Remove-Item $buildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $buildRoot | Out-Null

Run-Step $PythonExe "-m venv `"$venvRoot`""
Run-Step $venvPython "-m pip install --upgrade pip setuptools wheel"
Run-Step $venvPython "-m pip install --prefer-binary $RuntimeDeps"
Run-Step $venvPython "-m pip install --prefer-binary --no-deps funasr"
Run-Step $venvPython "-c `"import funasr,modelscope,torch,numpy,scipy;print('ok')`""

$outputPath = Join-Path $root $OutputZip
if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

Compress-Archive -Path (Join-Path $venvRoot "*") -DestinationPath $outputPath -CompressionLevel Optimal
Write-Host "[bundle] done: $outputPath"
