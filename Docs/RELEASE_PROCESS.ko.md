# Riverworks 릴리스 절차

이 문서는 공개 GitHub 저장소의 소스 검증과, GitHub Releases에 올리는 플레이 가능한 Windows 바이너리의 생성 절차를 구분한다. GitHub Actions는 .NET 핵심 계약 테스트와 Node 게이트웨이 fixture 테스트를 실행한다. Unity Windows 빌드는 Unity와 상용 Feel 패키지 라이선스가 있는 로컬 환경에서 만든다. Feel 원본 패키지와 프로젝트의 실제 API 키는 공개 저장소나 릴리스에 올리지 않는다.

v0.9은 코어·Editor 저장 검사, Windows 빌드와 10종 네이티브 실행 검사를 완료했다. 각 릴리스는 아래 순서로 같은 실행 파일을 검증하고 패키징한다. 상세 결과와 범위는 [검증 기록](VERIFICATION.md)에 있다.

## 준비

- Windows PowerShell 5.1 이상
- 프로젝트에 지정된 Unity Editor 버전과 유효한 Unity 라이선스
- 로컬 프로젝트에 설치된 Feel 패키지
- .NET 8 SDK와 Node.js 22 이상
- GitHub CLI `gh` 로그인

버전은 `Assets/Editor/BuildAutomation.cs`의 현재 Player 버전과 일치시킨다. 아래 예시는 `0.9.0`이다.

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
.\Tools\Build.ps1 -OutputDirectory .\Builds\Windows-v0.9
```

빌드가 끝나면 다음 명령으로 실행 검사를 수행한다. `Tools/Build.ps1`은 기본적으로 작업 스레드 2개와 낮은 우선순위를 사용하며, 게임 실행 검사는 차례대로 진행한다.

```powershell
.\Tools\Test.ps1 -Runtime -FactoryRuntime -CompactUiRuntime -FeelRuntime -TutorialRuntime -ResidentActivityRuntime -ResearchTreeRuntime -IndustryRuntime -ExpansionRuntime -ExecutablePath .\Builds\Windows-v0.9\Riverworks.exe
```

`Build.ps1 -OutputDirectory`는 v0.9 실행 파일을 기존 `Builds\Windows`와 분리한다. 결과는 `Builds\Windows-v0.9\Riverworks.exe`, 빌드 로그와 요약은 `Artifacts\build.log`와 `Artifacts\build-result.txt`에 기록된다.

`Test.ps1`은 먼저 코어 검사를 다시 실행한 뒤 요청한 네이티브 검사를 차례로 실행하고, 모든 결과에서 같은 32자리 빌드 GUID와 `ERRORS 0`, 각 성공 마커를 요구한다. `-IndustryRuntime`은 38자원·37제조법·23설비의 복합 산업, `-ExpansionRuntime`은 3층·층간 운송·조건 자동화·도시 프로젝트를 다룬다.

주민 활동 검사는 `Artifacts\ResidentActivitySmoke\01-resident-activity-overview-1600x900.png`와 결과 파일을 만든다. 산업·확장 결과는 각각 `Artifacts\IndustrySmoke`, `Artifacts\ExpansionSmoke`에 기록된다. 성공 결과와 새 스크린샷을 직접 확인하고 검증 경계는 [VERIFICATION.md](VERIFICATION.md)에 기록한다.

v0.9 기준 코어 2,114개, Editor 저장 97개, Node 계약 22개가 통과했다. Windows 빌드는 108,347,651바이트이며 오류 0·경고 30이다. 9종 기본 실행 검사와 별도 주민 AI MOCK_CONTRACT 검사는 모두 같은 빌드 GUID `029d32dac5eb498a901bde882a5aad97`로 통과했다.

## 3. 릴리스 ZIP 생성

v0.9는 격리 빌드, 별도 릴리스 출력, 최신 확장 미리보기와 이번 v0.9 실행 화면으로 만든 영상을 모두 명시한다. 다음 명령의 MoviePath 파일은 패키징 전에 실제로 새로 만들어져 있어야 한다.

```powershell
.\Tools\Package-Release.ps1 -Version 0.9.0 -WindowsBuildPath .\Builds\Windows-v0.9 -OutputDirectory .\Builds\Release-v0.9 -PreviewPath .\Docs\Images\expansion-v0.9.png -MoviePath .\Artifacts\ExpansionSmoke\expansion-v0.9.mp4
```

`-MoviePath`를 생략하면 스크립트가 과거 주민 영상을 자동 선택할 수 있으므로 v0.9 릴리스에서는 생략하지 않는다. v0.7 연구 영상, v0.8 산업 영상, 기존 `factory-process.mp4`와 Windows 빌드 폴더 안의 영상을 재사용하지 않는다.

출력:

- `Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip`
- `Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip.sha256`
- `Builds\Release-v0.9\Riverworks-Resident-Gateway-v0.9.0.zip`
- `Builds\Release-v0.9\Riverworks-Resident-Gateway-v0.9.0.zip.sha256`

스크립트는 지정한 출력의 `Builds\Release-v0.9\.release-staging` 아래에 실행마다 새 검토 폴더를 만든다. Windows ZIP에는 Unity 실행 파일과 런타임 폴더, 사용자 문서, 원본 라이선스, provenance, 최신 미리보기와 새 v0.9 영상만 명시적으로 넣는다. 게이트웨이 ZIP에는 `Server/src`, `package.json`, `start.ps1`, `.env.example`, 주민 AI 안내만 넣는다. 실제 `.env`, `.data`, 로그, 테스트, `node_modules`, 공급자 키는 포함하지 않는다.

같은 이름의 결과물이 이미 있으면 패키징은 중단한다. 확인 후 교체할 때만 `-Force`를 지정한다.

## 4. 패키지 검증

```powershell
.\Tools\Verify-Release.ps1 -ZipPath .\Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip -ExpectedVersion 0.9.0
```

검증기는 필수 실행 파일, Unity 데이터, v0.9 확장 문서·이미지, 라이선스, PNG 헤더, Player 버전과 SHA-256 일치를 확인한다. 새 임시 폴더에 압축을 풀고 코드 DLL·리소스·Player 설정의 바이트 일치를 확인한 뒤 도시·확장 실행 검사를 별도로 수행한다.

```powershell
$verifyRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Riverworks-v0.9.0-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $verifyRoot | Out-Null
Expand-Archive -LiteralPath .\Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip -DestinationPath $verifyRoot
$packagedExe = Get-ChildItem -LiteralPath $verifyRoot -Recurse -File -Filter Riverworks.exe | Select-Object -First 1
.\Tools\Test.ps1 -Runtime -ExpansionRuntime -ExecutablePath $packagedExe.FullName
```

압축 해제 실행에서도 원본 빌드와 같은 GUID, 각 결과의 `ERRORS 0`와 성공 마커를 확인한다. 외부 LLM 기능은 유료 호출 없이 로컬 `MOCK_CONTRACT`까지만 릴리스 게이트로 사용하며, 실제 제공자 호출은 별도 실행을 했을 때만 기록한다.

## 5. 공개 GitHub Release 게시

테스트와 패키지 검증이 성공하고 변경 사항을 공개 저장소에 푸시한 뒤 실행한다.

```powershell
gh release create v0.9.0 `
  .\Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip `
  .\Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip.sha256 `
  .\Builds\Release-v0.9\Riverworks-Resident-Gateway-v0.9.0.zip `
  .\Builds\Release-v0.9\Riverworks-Resident-Gateway-v0.9.0.zip.sha256 `
  --repo atozuser0224/riverworks-city `
  --title "Riverworks v0.9.0" `
  --generate-notes
```

게시 후 공개 릴리스 페이지에서 네 파일의 이름과 크기를 확인하고, Windows ZIP을 다시 내려받아 SHA-256과 실행을 확인한다. 릴리스 사용자는 소스를 빌드할 필요 없이 Windows ZIP을 풀고 `Riverworks.exe`를 실행한다. 소스에서 재빌드하려는 개발자는 별도로 Unity 및 Feel 라이선스 환경을 준비해야 한다.

## 유지 관리

- 모든 버전은 같은 검사, 빌드, 런타임 스모크, 패키지 검증 순서를 거친다.
- 릴리스 태그, Player 버전, ZIP 파일명, `START-HERE.ko.txt`의 버전을 일치시킨다.
- v0.9 패키지는 격리 `WindowsBuildPath`, 별도 `OutputDirectory`와 이번 실행에서 만든 새 `MoviePath`를 명시한다.
- 공개 저장소에는 Feel 원본 파일, Unity 캐시와 빌드 중간물, 비밀 설정, 런타임 데이터와 로그를 커밋하지 않는다.
- 릴리스 설명에는 실제로 수행한 검사만 적고, 코어·Editor·빌드·네이티브·압축 해제 실행·로컬 계약·외부 서비스를 구분한다. 한 단계라도 실패하거나 결과가 미완료면 GitHub Release를 게시하지 않는다.
