# Riverworks 릴리스 절차

이 문서는 공개 GitHub 저장소의 소스 검증과, GitHub Releases에 올리는 플레이 가능한 Windows 바이너리의 생성 절차를 구분한다. GitHub Actions는 .NET 핵심 계약 테스트와 Node 게이트웨이 fixture 테스트를 실행한다. Unity Windows 빌드는 Unity와 상용 Feel 패키지 라이선스가 있는 로컬 환경에서 만든다. Feel 원본 패키지와 프로젝트의 실제 API 키는 공개 저장소나 릴리스에 올리지 않는다.

## 준비

- Windows PowerShell 5.1 이상
- 프로젝트에 지정된 Unity Editor 버전과 유효한 Unity 라이선스
- 로컬 프로젝트에 설치된 Feel 패키지
- .NET 8 SDK와 Node.js 22 이상
- GitHub CLI `gh` 로그인

버전은 `Assets/Editor/BuildAutomation.cs`의 현재 Player 버전과 일치시킨다. 아래 예시는 `0.7.0`이다.

## 1. 소스 계약 검사

프로젝트 루트에서 실행한다.

```powershell
dotnet run --project Tests/Riverworks.Tests.csproj -c Release
npm test --prefix Server
```

이 검사는 코드 계약과 외부 API를 호출하지 않는 fixture를 확인한다. 플레이 가능한 Unity 빌드나 실제 외부 LLM 연결 성공을 뜻하지 않는다.

## 2. Windows 빌드와 런타임 검사

Unity가 설치된 Windows 환경에서 빌드한다.

```powershell
.\Tools\Build.ps1
```

빌드가 끝나면 다음 명령으로 실행 검사를 수행한다. `Tools/Build.ps1`은 기본적으로 작업 스레드 2개와 낮은 우선순위를 사용하며, 게임 실행 검사는 차례대로 진행한다.

```powershell
.\Tools\Test.ps1 -Runtime -FactoryRuntime -CompactUiRuntime -FeelRuntime -TutorialRuntime -ResidentActivityRuntime -ResearchTreeRuntime
```

주민 활동 검사는 `Artifacts\ResidentActivitySmoke\01-resident-activity-overview-1600x900.png`와 결과 파일을 만든다. 성공 결과와 새 스크린샷을 직접 확인한다. 검증 경계는 [VERIFICATION.md](VERIFICATION.md)에 기록한다.

## 3. 릴리스 ZIP 생성

기본 명령은 Windows 게임 ZIP과 선택형 주민 AI 게이트웨이 ZIP을 함께 만든다.

```powershell
.\Tools\Package-Release.ps1 -Version 0.7.0
```

미리보기와 이번 빌드에서 새로 녹화한 영상 경로를 명시할 수도 있다.

```powershell
.\Tools\Package-Release.ps1 -Version 0.7.0 `
  -PreviewPath .\Docs\Images\riverworks-v0.6.png `
  -MoviePath .\Artifacts\ResearchTree\research-tree.mp4
```

출력:

- `Builds\Riverworks-v0.7.0-Windows-x64.zip`
- `Builds\Riverworks-v0.7.0-Windows-x64.zip.sha256`
- `Builds\Riverworks-Resident-Gateway-v0.7.0.zip`
- `Builds\Riverworks-Resident-Gateway-v0.7.0.zip.sha256`

스크립트는 `Builds\.release-staging` 아래에 실행마다 새 검토 폴더를 만든다. Windows ZIP에는 Unity 실행 파일과 런타임 폴더, 사용자 문서, 원본 라이선스, provenance, 최신 미리보기만 명시적으로 넣는다. 게이트웨이 ZIP에는 `Server/src`, `package.json`, `start.ps1`, `.env.example`, 주민 AI 안내만 넣는다. 실제 `.env`, `.data`, 로그, 테스트, `node_modules`, 공급자 키는 포함하지 않는다.

같은 이름의 결과물이 이미 있으면 패키징은 중단한다. 확인 후 교체할 때만 `-Force`를 지정한다.

## 4. 패키지 검증

```powershell
.\Tools\Verify-Release.ps1 `
  -ZipPath .\Builds\Riverworks-v0.7.0-Windows-x64.zip `
  -ExpectedVersion 0.7.0
```

검증기는 필수 실행 파일, Unity 데이터, 문서, 라이선스, PNG 헤더, Player 버전, SHA-256 일치를 확인한다. 이어서 ZIP을 새 폴더에 풀고 `Riverworks.exe`를 직접 실행해 새 게임, 저장/불러오기, 튜토리얼, 주민 활동과 공장 작업을 확인한다. 외부 LLM 기능은 키를 사용자가 별도로 설정했을 때만 실제 연결 검증으로 기록한다.

## 5. 공개 GitHub Release 게시

테스트와 패키지 검증이 성공하고 변경 사항을 공개 저장소에 푸시한 뒤 실행한다.

```powershell
gh release create v0.7.0 `
  .\Builds\Riverworks-v0.7.0-Windows-x64.zip `
  .\Builds\Riverworks-v0.7.0-Windows-x64.zip.sha256 `
  .\Builds\Riverworks-Resident-Gateway-v0.7.0.zip `
  .\Builds\Riverworks-Resident-Gateway-v0.7.0.zip.sha256 `
  --repo atozuser0224/riverworks-city `
  --title "Riverworks v0.7.0" `
  --generate-notes
```

게시 후 공개 릴리스 페이지에서 네 파일의 이름과 크기를 확인하고, Windows ZIP을 다시 내려받아 SHA-256과 실행을 확인한다. 릴리스 사용자는 소스를 빌드할 필요 없이 Windows ZIP을 풀고 `Riverworks.exe`를 실행한다. 소스에서 재빌드하려는 개발자는 별도로 Unity 및 Feel 라이선스 환경을 준비해야 한다.

## 유지 관리

- 모든 버전은 같은 검사, 빌드, 런타임 스모크, 패키지 검증 순서를 거친다.
- 릴리스 태그, Player 버전, ZIP 파일명, `START-HERE.ko.txt`의 버전을 일치시킨다.
- 공개 저장소에는 Feel 원본 파일, Unity 캐시와 빌드 중간물, 비밀 설정, 런타임 데이터와 로그를 커밋하지 않는다.
- 릴리스 설명에는 실제로 수행한 검사만 적고, 로컬 계약 테스트와 실제 게임 실행 또는 외부 서비스 연결을 구분한다.
