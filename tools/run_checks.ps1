$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
& python (Join-Path $RepoRoot "scripts/run_checks.py") @args
exit $LASTEXITCODE
