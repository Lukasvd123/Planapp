# get.ps1
# Generate a filtered tree with both folders and files, excluding unwanted directories

# List of folders to exclude
$excluded = @(
    ".git", ".github", ".vs", "bin", "obj", "Debug", "Release",
    "www-root", ".idea", ".vscode", "packages", "node_modules"
)

# Install PSTree if missing
if (-not (Get-Module -ListAvailable -Name PSTree)) {
    Write-Host "PSTree module not found. Installing..."
    Install-Module -Name PSTree -Scope CurrentUser -Force
}

Import-Module PSTree

# Get tree excluding unwanted folders (both files and folders)
$treeItems = Get-PSTree -Path "." -Exclude $excluded

# Write output to file
$OutputPath = "tree_filtered.txt"
$treeItems | Out-File -Encoding utf8 $OutputPath

Write-Host "Filtered tree (folders + files) written to $OutputPath"
