# Deploy-Robust.ps1 - Robust deployment with connection recovery

# EDIT THESE VALUES:
$SERVER = "lukas.openims.com"
$USERNAME = "root"      # Change this
$PASSWORD = "ck5ycesR3702ve17"      # Change this  
$REMOTE_PATH = "/data/faststorage/planapp"

Write-Host "Starting robust deployment..." -ForegroundColor Green

# Helper function to create SSH session with retry
function New-SSHSessionWithRetry {
    param($Server, $Credential, $MaxRetries = 3)
    
    for ($i = 1; $i -le $MaxRetries; $i++) {
        try {
            Write-Host "Connecting attempt $i..." -ForegroundColor Yellow
            $session = New-SSHSession -ComputerName $Server -Credential $Credential -ConnectionTimeout 30
            if ($session) {
                Write-Host "Connected successfully" -ForegroundColor Green
                return $session
            }
        }
        catch {
            Write-Host "Connection attempt $i failed: $_" -ForegroundColor Yellow
            if ($i -lt $MaxRetries) {
                Start-Sleep -Seconds 5
            }
        }
    }
    throw "Failed to connect after $MaxRetries attempts"
}

# Helper function to execute SSH command with retry
function Invoke-SSHCommandWithRetry {
    param($Session, $Command, $MaxRetries = 2)
    
    for ($i = 1; $i -le $MaxRetries; $i++) {
        try {
            $result = Invoke-SSHCommand -SSHSession $Session -Command $Command -TimeOut 30
            return $result
        }
        catch {
            Write-Host "Command failed (attempt $i): $_" -ForegroundColor Yellow
            if ($i -lt $MaxRetries) {
                Start-Sleep -Seconds 2
            }
        }
    }
    throw "Command failed after $MaxRetries attempts: $Command"
}

# Step 1: Build
Write-Host "Building app..." -ForegroundColor Yellow
if (Test-Path "publish") { 
    Remove-Item "publish" -Recurse -Force 
}

Push-Location "Planapp.Web"
dotnet clean | Out-Null
dotnet publish -c Release -o "../publish" | Out-Null
Pop-Location

if (-not (Test-Path "publish/wwwroot")) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build complete" -ForegroundColor Green

# Step 2: Install Posh-SSH
if (-not (Get-Module -ListAvailable -Name "Posh-SSH")) {
    Write-Host "Installing Posh-SSH..." -ForegroundColor Yellow
    Install-Module -Name Posh-SSH -Force -Scope CurrentUser
}
Import-Module Posh-SSH

# Step 3: Prepare credentials
$secPass = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($USERNAME, $secPass)

# Step 4: Initial connection and setup
$ssh = New-SSHSessionWithRetry -Server $SERVER -Credential $cred

Write-Host "Preparing remote directory..." -ForegroundColor Yellow
Invoke-SSHCommandWithRetry -Session $ssh -Command "mkdir -p `"$REMOTE_PATH`""
Invoke-SSHCommandWithRetry -Session $ssh -Command "rm -rf `"$REMOTE_PATH`"/*"

# Step 5: Create deployment strategy
Write-Host "Analyzing files for deployment..." -ForegroundColor Yellow

$files = Get-ChildItem "publish/wwwroot" -Recurse -File
$totalFiles = $files.Count

# Categorize files by size and type
$smallFiles = @()      # < 10KB
$mediumFiles = @()     # 10KB - 100KB  
$largeFiles = @()      # > 100KB

foreach ($file in $files) {
    $size = $file.Length
    if ($size -lt 10KB) {
        $smallFiles += $file
    }
    elseif ($size -lt 100KB) {
        $mediumFiles += $file
    }
    else {
        $largeFiles += $file
    }
}

Write-Host "Files categorized: Small($($smallFiles.Count)) Medium($($mediumFiles.Count)) Large($($largeFiles.Count))" -ForegroundColor Cyan

# Step 6: Upload small files first (most likely to succeed)
Write-Host "Uploading small files..." -ForegroundColor Yellow
$uploadedCount = 0
$failedFiles = @()

foreach ($file in $smallFiles) {
    $uploadedCount++
    
    try {
        $relativePath = $file.FullName.Replace((Resolve-Path "publish/wwwroot").Path, "").Replace("\", "/")
        if (-not $relativePath.StartsWith("/")) {
            $relativePath = "/" + $relativePath
        }
        $remotePath = "$REMOTE_PATH$relativePath"
        $remoteDir = Split-Path $remotePath -Parent
        
        # Create directory
        Invoke-SSHCommandWithRetry -Session $ssh -Command "mkdir -p `"$remoteDir`""
        
        # Upload file
        $content = Get-Content $file.FullName -Raw -Encoding UTF8
        if ($content) {
            $command = @"
cat > "$remotePath" << 'SMALLFILE_EOF'
$content
SMALLFILE_EOF
"@
            Invoke-SSHCommandWithRetry -Session $ssh -Command $command
        }
        else {
            Invoke-SSHCommandWithRetry -Session $ssh -Command "touch `"$remotePath`""
        }
        
        Write-Progress -Activity "Uploading small files" -Status "$uploadedCount of $($smallFiles.Count)" -PercentComplete (($uploadedCount / $smallFiles.Count) * 100)
    }
    catch {
        $failedFiles += $file
        Write-Host "Failed: $($file.Name)" -ForegroundColor Yellow
    }
}

Write-Progress -Activity "Uploading small files" -Completed
Write-Host "Small files completed: $($smallFiles.Count - $failedFiles.Count) successful, $($failedFiles.Count) failed" -ForegroundColor Green

# Step 7: Upload medium files with base64 encoding
Write-Host "Uploading medium files..." -ForegroundColor Yellow

foreach ($file in $mediumFiles) {
    $uploadedCount++
    
    try {
        # Reconnect if session is dead
        if (-not $ssh -or $ssh.IsConnected -eq $false) {
            Write-Host "Reconnecting..." -ForegroundColor Yellow
            if ($ssh) { Remove-SSHSession -SSHSession $ssh }
            $ssh = New-SSHSessionWithRetry -Server $SERVER -Credential $cred
        }
        
        $relativePath = $file.FullName.Replace((Resolve-Path "publish/wwwroot").Path, "").Replace("\", "/")
        if (-not $relativePath.StartsWith("/")) {
            $relativePath = "/" + $relativePath
        }
        $remotePath = "$REMOTE_PATH$relativePath"
        $remoteDir = Split-Path $remotePath -Parent
        
        Invoke-SSHCommandWithRetry -Session $ssh -Command "mkdir -p `"$remoteDir`""
        
        # Use base64 for medium files
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $base64 = [System.Convert]::ToBase64String($bytes)
        
        # Upload via temporary file to avoid command line limits
        $tempFile = "/tmp/deploy_$(Get-Random).b64"
        Invoke-SSHCommandWithRetry -Session $ssh -Command "echo '$base64' > $tempFile"
        Invoke-SSHCommandWithRetry -Session $ssh -Command "base64 -d $tempFile > `"$remotePath`" && rm $tempFile"
        
        Write-Progress -Activity "Uploading medium files" -Status "$($uploadedCount - $smallFiles.Count) of $($mediumFiles.Count) - $($file.Name)" -PercentComplete ((($uploadedCount - $smallFiles.Count) / $mediumFiles.Count) * 100)
    }
    catch {
        $failedFiles += $file
        Write-Host "Failed: $($file.Name) - $_" -ForegroundColor Yellow
    }
}

Write-Progress -Activity "Uploading medium files" -Completed

# Step 8: Handle large files with chunked upload
Write-Host "Uploading large files..." -ForegroundColor Yellow

foreach ($file in $largeFiles) {
    $uploadedCount++
    
    try {
        # Reconnect if needed
        if (-not $ssh -or $ssh.IsConnected -eq $false) {
            Write-Host "Reconnecting for large file..." -ForegroundColor Yellow
            if ($ssh) { Remove-SSHSession -SSHSession $ssh }
            $ssh = New-SSHSessionWithRetry -Server $SERVER -Credential $cred
        }
        
        $relativePath = $file.FullName.Replace((Resolve-Path "publish/wwwroot").Path, "").Replace("\", "/")
        if (-not $relativePath.StartsWith("/")) {
            $relativePath = "/" + $relativePath
        }
        $remotePath = "$REMOTE_PATH$relativePath"
        $remoteDir = Split-Path $remotePath -Parent
        
        Invoke-SSHCommandWithRetry -Session $ssh -Command "mkdir -p `"$remoteDir`""
        
        Write-Host "Uploading large file: $($file.Name) ($([math]::Round($file.Length/1KB, 1))KB)" -ForegroundColor Cyan
        
        # For very large files, upload in chunks
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $base64 = [System.Convert]::ToBase64String($bytes)
        
        $chunkSize = 30000  # Smaller chunks for large files
        $chunks = [math]::Ceiling($base64.Length / $chunkSize)
        
        $tempFile = "/tmp/deploy_large_$(Get-Random).b64"
        Invoke-SSHCommandWithRetry -Session $ssh -Command "rm -f $tempFile"
        
        for ($i = 0; $i -lt $chunks; $i++) {
            $start = $i * $chunkSize
            $length = [math]::Min($chunkSize, $base64.Length - $start)
            $chunk = $base64.Substring($start, $length)
            
            Invoke-SSHCommandWithRetry -Session $ssh -Command "echo -n '$chunk' >> $tempFile"
            Write-Progress -Activity "Uploading large file chunks" -Status "Chunk $($i+1) of $chunks" -PercentComplete (($i+1)/$chunks*100)
        }
        
        Invoke-SSHCommandWithRetry -Session $ssh -Command "base64 -d $tempFile > `"$remotePath`" && rm $tempFile"
        Write-Progress -Activity "Uploading large file chunks" -Completed
        
        Write-Host "Large file completed: $($file.Name)" -ForegroundColor Green
    }
    catch {
        $failedFiles += $file
        Write-Host "Failed large file: $($file.Name) - $_" -ForegroundColor Red
    }
}

# Step 9: Set permissions
Write-Host "Setting permissions..." -ForegroundColor Yellow
try {
    Invoke-SSHCommandWithRetry -Session $ssh -Command "find `"$REMOTE_PATH`" -type f -exec chmod 644 {} \;"
    Invoke-SSHCommandWithRetry -Session $ssh -Command "find `"$REMOTE_PATH`" -type d -exec chmod 755 {} \;"
    Invoke-SSHCommandWithRetry -Session $ssh -Command "chown -R apache:apache `"$REMOTE_PATH`""
    Write-Host "Permissions set" -ForegroundColor Green
}
catch {
    Write-Host "Permission setting failed: $_" -ForegroundColor Yellow
}

# Step 10: Restart Apache
Write-Host "Reloading Apache..." -ForegroundColor Yellow
try {
    $apacheResult = Invoke-SSHCommandWithRetry -Session $ssh -Command "sudo systemctl reload httpd"
    Write-Host "Apache reloaded" -ForegroundColor Green
}
catch {
    Write-Host "Apache reload failed - you may need to do this manually" -ForegroundColor Yellow
}

# Step 11: Summary
Remove-SSHSession -SSHSession $ssh

$successCount = $totalFiles - $failedFiles.Count
Write-Host "`nDeployment Summary:" -ForegroundColor Green
Write-Host "===================" -ForegroundColor Green
Write-Host "Total files: $totalFiles" -ForegroundColor Cyan
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Failed: $($failedFiles.Count)" -ForegroundColor Yellow

if ($failedFiles.Count -gt 0) {
    Write-Host "`nFailed files:" -ForegroundColor Yellow
    foreach ($file in $failedFiles) {
        Write-Host "  - $($file.Name)" -ForegroundColor Gray
    }
}

if ($successCount -gt ($totalFiles * 0.8)) {
    Write-Host "`nDeployment mostly successful!" -ForegroundColor Green
    Write-Host "Your app should be available at: http://$SERVER/planapp" -ForegroundColor Cyan
}
else {
    Write-Host "`nDeployment had significant issues. Check failed files above." -ForegroundColor Yellow
}