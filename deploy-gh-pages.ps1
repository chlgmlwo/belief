# BELIEF WebGL 빌드를 GitHub Pages(gh-pages 브랜치)로 배포한다.
#
# 사용법:
#   1) 먼저 웹 빌드를 뽑는다 (Unity 메뉴 Belief > Build > WebGL 빌드)
#   2) .\deploy-gh-pages.ps1
#   3) 최초 1회만: GitHub 저장소 Settings > Pages > Source를 "Deploy from a branch",
#      Branch를 "gh-pages" / "/ (root)"로 지정
#   → https://chlgmlwo.github.io/belief/
#
# 왜 main의 docs/ 폴더가 아니라 별도 브랜치인가:
#   빌드 산출물이 49MB고 그중 39MB가 단일 파일(WebGL.data.unityweb)이다. main에 커밋하면
#   빌드할 때마다 49MB짜리 blob이 히스토리에 영구히 쌓여서, 몇 번만 배포해도 clone 용량이
#   수백 MB로 불어난다. 여기서는 배포마다 **완전히 새 저장소를 만들어 force push**하므로
#   gh-pages에는 항상 커밋 1개만 남고 히스토리가 누적되지 않는다(그래서 main의 히스토리는
#   전혀 건드리지 않는다).

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$build    = Join-Path $repoRoot "Build\WebGL"
$remote   = "https://github.com/chlgmlwo/belief.git"
$branch   = "gh-pages"
$staging  = Join-Path $env:TEMP "belief-ghpages"

if (-not (Test-Path (Join-Path $build "index.html"))) {
    Write-Error "웹 빌드가 없다: $build`n먼저 Unity에서 Belief > Build > WebGL 빌드를 실행해라."
}

# LFS로 추적되면 GitHub Pages가 실제 파일 대신 포인터 텍스트(133바이트)를 내려줘서
# 게임이 통째로 안 뜬다. 배포 저장소에는 LFS를 아예 쓰지 않는다.
$sizeMB = [math]::Round((Get-ChildItem $build -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
$biggest = Get-ChildItem $build -Recurse -File | Sort-Object Length -Descending | Select-Object -First 1
$biggestMB = [math]::Round($biggest.Length / 1MB, 1)
Write-Host "빌드 크기 $sizeMB MB / 최대 파일 $($biggest.Name) $biggestMB MB"
if ($biggest.Length -ge 100MB) {
    Write-Error "$($biggest.Name)이 100MB를 넘는다. GitHub은 100MB 초과 파일을 거부한다."
}

# 스테이징 디렉터리를 매번 새로 만든다 - 이전 배포에 있었지만 이번엔 없는 파일이
# 남아 있으면 안 되기 때문이다(빌드 파일명에 해시가 붙는 설정으로 바꾸면 특히 중요).
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

Copy-Item (Join-Path $build "*") $staging -Recurse -Force

# Jekyll이 밑줄로 시작하는 파일/폴더를 걸러내지 않도록 끈다(배포도 빨라진다).
New-Item -ItemType File -Path (Join-Path $staging ".nojekyll") | Out-Null

# 모든 파일을 바이너리로 취급해 줄바꿈 자동 변환을 막는다. wasm/data는 git이 알아서
# 바이너리로 판정하지만, 전역 core.autocrlf 설정에 따라 결과가 달라질 여지를 없앤다.
Set-Content -Path (Join-Path $staging ".gitattributes") -Value "* -text" -Encoding ascii

Push-Location $staging
try {
    git init -q -b $branch
    git config core.autocrlf false
    git config lfs.https://github.com/chlgmlwo/belief.git/info/lfs.access none
    git add -A
    git -c user.name="belief-deploy" -c user.email="deploy@local" commit -q -m "Deploy WebGL build ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
    git remote add origin $remote
    Write-Host "push 중... (49MB라 처음엔 몇 분 걸린다)"
    git push --force origin $branch
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "배포 완료. 반영까지 1~2분 걸린다:"
Write-Host "  https://chlgmlwo.github.io/belief/"
