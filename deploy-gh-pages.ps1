# BELIEF WebGL 빌드를 GitHub Pages(gh-pages 브랜치)로 배포한다.
#
# 사용법:
#   1) 먼저 웹 빌드를 뽑는다 (Unity 메뉴 Belief > Build > WebGL 빌드)
#   2) .\deploy-gh-pages.ps1
#   3) 최초 1회만: GitHub 저장소 Settings > Pages > Source를 "Deploy from a branch",
#      Branch를 "gh-pages" / "/ (root)"로 지정
#   → https://chlgmlwo.github.io/belief/
#
# 이 파일은 반드시 UTF-8 **with BOM**으로 저장해야 한다. Windows PowerShell 5.1은 BOM이 없으면
# .ps1을 UTF-8이 아니라 시스템 ANSI 코드페이지로 읽어서 위 주석과 아래 한글 메시지가 전부 깨진다.
#
# 왜 main의 docs/ 폴더가 아니라 별도 브랜치인가:
#   빌드 산출물이 49MB고 그중 39MB가 단일 파일(WebGL.data.unityweb)이다. main에 커밋하면
#   빌드할 때마다 49MB짜리 blob이 히스토리에 영구히 쌓여서, 몇 번만 배포해도 clone 용량이
#   수백 MB로 불어난다. 여기서는 배포마다 완전히 새 저장소를 만들어 force push 하므로
#   gh-pages에는 항상 커밋 1개만 남고, main의 히스토리는 전혀 건드리지 않는다.

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$build    = Join-Path $repoRoot "Build\WebGL"
$remote   = "https://github.com/chlgmlwo/belief.git"
$branch   = "gh-pages"
$staging  = Join-Path $env:TEMP "belief-ghpages"

# ── git 찾기 ───────────────────────────────────────────────────────────────
# 이 PC에는 Git for Windows가 따로 설치돼 있지 않고 GitHub Desktop에 번들된 git만 있다.
# 번들 경로에는 앱 버전(app-3.6.3)이 들어가서 GitHub Desktop이 업데이트되면 바뀌므로,
# 폴더를 훑어 가장 최신 것을 고른다.
function Resolve-Git {
    $onPath = Get-Command git -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    foreach ($base in @("$env:LOCALAPPDATA\GitHubDesktop", "$env:ProgramFiles\GitHub Desktop")) {
        if (-not (Test-Path $base)) { continue }
        $found = Get-ChildItem $base -Directory -Filter "app-*" -ErrorAction SilentlyContinue |
                 Sort-Object Name -Descending |
                 ForEach-Object { Join-Path $_.FullName "resources\app\git\cmd\git.exe" } |
                 Where-Object { Test-Path $_ } |
                 Select-Object -First 1
        if ($found) { return $found }
    }

    foreach ($c in @("$env:ProgramFiles\Git\cmd\git.exe",
                     "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
                     "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe")) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$git = Resolve-Git
if (-not $git) {
    Write-Error "git을 찾을 수 없다. https://git-scm.com/download/win 에서 Git for Windows를 설치해라."
}
Write-Host "git: $git"

# GitHub Desktop 번들 git을 직접 부르면 자격증명 헬퍼가 설정돼 있지 않아 push에서 인증이 막힌다.
# 같은 번들 안의 git-credential-manager를 명시적으로 지정한다(자격증명은 이미 Windows
# 자격증명 관리자에 저장돼 있으므로 재로그인은 필요 없다).
$gitArgs = @()
$gcm = Join-Path (Split-Path (Split-Path $git -Parent) -Parent) "mingw64\bin\git-credential-manager.exe"
if (Test-Path $gcm) { $gitArgs += @("-c", "credential.helper=$gcm") }

# 전역 .gitconfig에 LFS 필터가 required=true로 걸려 있다. 배포 저장소는 LFS를 쓰지 않지만
# (LFS로 추적되면 GitHub Pages가 실제 파일 대신 포인터 텍스트를 내려줘 게임이 통째로 안 뜬다),
# 필터가 끼어들 여지를 아예 없앤다.
$gitArgs += @("-c", "filter.lfs.required=false", "-c", "core.autocrlf=false")

function Invoke-Git { & $git @gitArgs @args; if ($LASTEXITCODE -ne 0) { throw "git 실패: $args" } }

# ── 빌드 확인 ──────────────────────────────────────────────────────────────
if (-not (Test-Path (Join-Path $build "index.html"))) {
    Write-Error "웹 빌드가 없다: $build`n먼저 Unity에서 Belief > Build > WebGL 빌드를 실행해라."
}

$sizeMB    = [math]::Round((Get-ChildItem $build -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
$biggest   = Get-ChildItem $build -Recurse -File | Sort-Object Length -Descending | Select-Object -First 1
$biggestMB = [math]::Round($biggest.Length / 1MB, 1)
Write-Host "빌드 크기 $sizeMB MB / 최대 파일 $($biggest.Name) $biggestMB MB"
if ($biggest.Length -ge 100MB) {
    Write-Error "$($biggest.Name)이 100MB를 넘는다. GitHub은 100MB 초과 파일을 거부한다."
}

# ── 스테이징 ───────────────────────────────────────────────────────────────
# 매번 새로 만든다. 이전 배포에는 있었지만 이번엔 없는 파일이 남아 있으면 안 되기 때문이다.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

Copy-Item (Join-Path $build "*") $staging -Recurse -Force

# Jekyll이 밑줄로 시작하는 파일/폴더를 걸러내지 않도록 끈다(배포도 빨라진다).
New-Item -ItemType File -Path (Join-Path $staging ".nojekyll") | Out-Null

# 모든 파일을 바이너리로 취급해 줄바꿈 자동 변환을 막는다. 동시에 filter=lfs가 걸릴 여지도 없앤다.
Set-Content -Path (Join-Path $staging ".gitattributes") -Value "* -text" -Encoding ascii

# ── push ──────────────────────────────────────────────────────────────────
Push-Location $staging
try {
    Invoke-Git init -q
    # `git init -b`는 git 2.28 미만에서 안 먹는다. symbolic-ref는 모든 버전에서 동작한다.
    Invoke-Git symbolic-ref HEAD "refs/heads/$branch"
    Invoke-Git add -A
    Invoke-Git -c user.name=chlgmlwo -c user.email=128338976+chlgmlwo@users.noreply.github.com `
               commit -q -m "Deploy WebGL build ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
    Invoke-Git remote add origin $remote
    Write-Host "push 중... 49MB라 회선에 따라 몇 분 걸린다."
    Invoke-Git push --force origin $branch
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "배포 완료. 반영까지 1~2분 걸린다:"
Write-Host "  https://chlgmlwo.github.io/belief/"
Write-Host ""
Write-Host "최초 1회만: GitHub 저장소 Settings > Pages 에서"
Write-Host "  Source = Deploy from a branch / Branch = gh-pages / (root) 로 지정할 것."
