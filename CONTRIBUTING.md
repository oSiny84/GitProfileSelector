# 개발 및 기여 안내

버그 수정, 문서 개선, 재현 가능한 문제 제보를 환영합니다. 기능을 변경할 때는 폴더별 기본 계정 선택과 기존 Git 설정 보존이라는 프로젝트의 목적을 유지해 주세요.

## 개발 환경

- Windows 및 .NET 9 SDK
- Git for Windows와 Git Credential Manager
- PowerShell 또는 Windows 터미널

외부 NuGet 패키지는 사용하지 않습니다. WPF 프로젝트의 빌드와 화면 확인은 Windows에서 진행합니다.

```powershell
dotnet build src/GitOwnerSelector/GitOwnerSelector.csproj -c Release
dotnet run --project tests/GitOwnerSelector.Tests/GitOwnerSelector.Tests.csproj -c Release
```

## 변경 시 확인할 내용

- Git 설정 처리는 `src/GitOwnerSelector.Core`에, UI 처리는 `src/GitOwnerSelector`에 둡니다.
- 외부 명령 인자는 문자열 셸 명령을 조합하지 않고 `ProcessStartInfo.ArgumentList`로 전달합니다.
- 저장소 설정 및 다른 프로그램의 설정을 임의로 지우지 않습니다.
- 설정 동작을 변경한다면 테스트 전용 `GIT_CONFIG_GLOBAL`을 사용해 실제 Git으로 검증합니다.
- 테스트에서 사용자 실제 계정으로 로그인하거나 원격 저장소에 push하지 않습니다.
- UI나 지원 범위가 바뀌면 README도 함께 수정합니다.

## 테스트

`GitOwnerSelector.Tests`는 테스트 프레임워크 대신 콘솔 검증 프로그램을 사용합니다. 실패하면 예외와 함께 종료되고, 성공하면 통과 항목 수를 출력합니다. 결과물은 `.test-work`에 남으며 Git 추적 대상에서 제외됩니다.

현재 자동 검증에는 GitHub의 실제 브라우저 인증, 조직 SSO, 원격 쓰기 권한이 포함되지 않습니다. 이러한 동작을 검증했다면 테스트 환경과 확인 범위를 PR 설명에 별도로 적어 주세요.

## Pull Request

문제와 변경 후 동작을 설명하고, 실행한 검증과 결과를 적어 주세요. 화면이 바뀌면 실제 개인정보가 없는 스크린샷을 포함하면 좋습니다.

토큰, 비밀번호, 사용자 설정 백업, 개인 이메일, 회사 저장소 주소는 커밋하거나 공개 댓글에 올리지 마세요.
