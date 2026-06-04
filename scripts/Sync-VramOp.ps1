param(
    [string]$RepoPath = "C:\git\vram-op"
)

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:APPDATA "VramOp"
$logPath = Join-Path $logDir "git-sync.log"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -Path $logPath -Value "[$timestamp] $Message"
}

try {
    if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
        throw "No git repository found at $RepoPath"
    }

    $status = git -C $RepoPath status --porcelain
    if ($status) {
        Write-Log "Skipped sync: working tree has uncommitted changes."
        exit 2
    }

    git -C $RepoPath fetch origin --prune | Out-Null

    $branch = (git -C $RepoPath rev-parse --abbrev-ref HEAD).Trim()
    $upstream = git -C $RepoPath rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null
    if (-not $upstream) {
        $remoteBranchExists = git -C $RepoPath ls-remote --heads origin $branch
        if ($remoteBranchExists) {
            git -C $RepoPath branch --set-upstream-to "origin/$branch" $branch | Out-Null
        }
    }

    $remoteRef = "origin/$branch"
    $remoteExists = git -C $RepoPath rev-parse --verify $remoteRef 2>$null
    if (-not $remoteExists) {
        Write-Log "Skipped sync: $remoteRef does not exist yet."
        exit 3
    }

    $local = (git -C $RepoPath rev-parse HEAD).Trim()
    $remote = (git -C $RepoPath rev-parse $remoteRef).Trim()
    $base = (git -C $RepoPath merge-base HEAD $remoteRef).Trim()

    if ($local -eq $remote) {
        Write-Log "Already synced at $local."
        exit 0
    }

    if ($local -eq $base) {
        git -C $RepoPath pull --ff-only | Out-Null
        Write-Log "Pulled fast-forward updates from $remoteRef."
        exit 0
    }

    if ($remote -eq $base) {
        git -C $RepoPath push origin $branch | Out-Null
        Write-Log "Pushed local commits to $remoteRef."
        exit 0
    }

    Write-Log "Skipped sync: local and remote have diverged."
    exit 4
}
catch {
    Write-Log "Sync failed: $($_.Exception.Message)"
    exit 1
}
