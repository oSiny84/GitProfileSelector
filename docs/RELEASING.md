# 공개 및 배포 안내

이 문서는 저장소 관리자가 소스와 Windows 실행 파일을 배포할 때 사용하는 절차입니다. 아래 명령은 프로젝트 루트의 PowerShell에서 실행합니다.

## 소스 공개

소스 코드, 프로젝트 파일, README, 문서, 테스트를 저장소에 포함합니다. `bin`, `obj`, `dist`, `.test-work`는 `.gitignore`로 제외됩니다. 개인 프로필과 Git 설정 백업이 들어 있는 `%LOCALAPPDATA%\GitOwnerSelector`는 배포에 포함하지 않습니다.

저장소를 Git으로 초기화한 뒤에는 커밋 전 `git status --short`로 실제 추적 대상을 확인하세요. `.gitignore`는 이미 추적 중인 파일을 자동으로 제거하지 않습니다.

현재 프로젝트에는 배포 라이선스가 지정되어 있지 않습니다. 라이선스를 제공하려면 관리자가 원하는 조건을 선택해 `LICENSE`를 추가하고 README에도 표시하세요. 라이선스 배지를 먼저 추가하지 마세요.

## 빌드와 검증

```powershell
dotnet build src/GitOwnerSelector/GitOwnerSelector.csproj -c Release
dotnet run --project tests/GitOwnerSelector.Tests/GitOwnerSelector.Tests.csproj -c Release
dotnet publish src/GitOwnerSelector/GitOwnerSelector.csproj -c Release --self-contained false -o dist/GitOwnerSelector
```

오류 없이 빌드되는지, 통합 검증이 통과하는지 확인합니다. 배포 폴더의 `GitOwnerSelector.exe`로 화면을 확인하고, 로그인·실제 push를 직접 검증했다면 확인 범위를 릴리스 설명에 기록합니다. 자동 검증만으로 실제 GitHub 인증까지 검증했다고 표시하지 않습니다.

## ZIP 만들기

현재 명령은 .NET 9 Desktop Runtime을 별도로 설치해야 하는 배포 방식입니다. 실행 파일 하나만 배포하지 말고 publish 출력 전체를 ZIP에 포함합니다.

```powershell
Copy-Item README.md dist/GitOwnerSelector/README.md
Copy-Item CONTRIBUTING.md dist/GitOwnerSelector/CONTRIBUTING.md
New-Item -ItemType Directory -Force dist/GitOwnerSelector/docs | Out-Null
Copy-Item docs/* dist/GitOwnerSelector/docs -Recurse -Force
Compress-Archive -Path dist/GitOwnerSelector/* -DestinationPath dist/GitOwnerSelector-windows.zip -Force
```

GitHub Release에는 생성한 ZIP을 첨부하고 다음 내용을 명시합니다.

- 버전과 주요 변경 사항
- 실행에 필요한 .NET 9 Desktop Runtime 및 Git/GCM
- 지원 환경: Windows Git, GitHub.com, HTTPS
- 실제로 검증한 운영체제와 아키텍처
- 알려진 제한과 검증하지 않은 인증 시나리오

배포 명령에는 아키텍처를 지정하지 않았으므로 빌드 환경에 따라 출력이 달라질 수 있습니다. 확인 없이 모든 아키텍처를 지원한다고 표시하지 마세요.

## README 화면 이미지 갱신

앱의 미리보기 모드는 실제 Git 설정을 읽거나 변경하지 않고 빈 초기 화면을 렌더링합니다.

```powershell
New-Item -ItemType Directory -Force docs/images | Out-Null
dotnet dist/GitOwnerSelector/GitOwnerSelector.dll --render-preview docs/images/app-preview.png
```

생성된 이미지에서 글자 잘림이나 레이아웃 문제를 확인한 뒤 커밋합니다. 사용자 실계정이나 회사 경로가 들어간 화면은 공개 이미지로 사용하지 않습니다.
