$processes = Get-CimInstance Win32_Process -Filter "Name = 'java.exe' OR Name = 'javaw.exe'" -ErrorAction SilentlyContinue
foreach ($process in $processes) {
    if ($process.CommandLine -match 'openGaussProxy') {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}
