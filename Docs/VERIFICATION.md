# RIVERWORKS v0.9 검증 현황

기준일: 2026-09-15 KST. Unity 6000.5.5f1. 현재 저장 형식은 도시 v6·공장 v3이며 Windows 출력은 이전 실행 파일과 분리된 `Builds/Windows-v0.9`를 사용한다.

| 범위 | 결과 | 근거 |
|---|---|---|
| 순수 C# 코어 | **2,114개 통과** | `Artifacts/Expansion/core-tests.log` |
| Unity Editor 저장·복원·이전 버전 호환 | **97개 통과** | `Artifacts/save-validation-results.txt` |
| Windows x64 빌드 | **성공**, 오류 0·경고 30·108,347,651바이트 | `Artifacts/build-result.txt`, `Artifacts/build.log`, `Builds/Windows-v0.9` |
| 도시 런타임 | **203개 통과** | `Artifacts/Smoke/runtime-results.txt` |
| 같은 도시의 물류 | **107개 통과** | `Artifacts/SharedCitySmoke/shared-city-results.txt` |
| 컴팩트 UI | **1,263개 통과** | `Artifacts/CompactUiSmoke/compact-ui-results.txt` |
| Feel·카메라 | **180개 통과** | `Artifacts/FeelSmoke/feel-results.txt` |
| 단우·기본/후속 안내 | **2,473개 통과** | `Artifacts/TutorialSmoke/tutorial-results.txt` |
| 주민 모델·건물별 작업 | **306개 통과** | `Artifacts/ResidentActivitySmoke/resident-activity-results.txt` |
| 연결형 연구 지도 | **3,028개 통과** | `Artifacts/ResearchTreeSmoke/research-tree-results.txt` |
| 복합 산업 네이티브 (`-IndustryRuntime`) | **127개 통과** | `Artifacts/IndustrySmoke/industry-results.txt` |
| 복층·프로젝트 네이티브 (`-ExpansionRuntime`) | **112개 통과** | `Artifacts/ExpansionSmoke/expansion-results.txt` |
| 주민 AI HTTP 계약·UI | **160개 통과**, `MOCK_CONTRACT` | `Artifacts/ResidentAiSmoke-v0.9/resident-ai-results.txt` |
| Node 게이트웨이 fixture | **22개 통과** | `Artifacts/Expansion/server-tests.log` |
| 배포 파일 검증 | 릴리스별 별도 기록 | ZIP·SHA-256·압축 해제 실행·게시 확인은 아래 배포 절차에 따라 기록 |

9개 `Tools/Test.ps1` 런타임은 모두 빌드 GUID **029d32dac5eb498a901bde882a5aad97**와 `ERRORS 0`를 기록했고 `Artifacts/Expansion/final-tests.log`는 전체 요청 성공으로 끝났다. 별도 주민 AI `MOCK_CONTRACT`도 같은 GUID, `ERRORS 0`, `EXIT 0`다. 배포 ZIP의 SHA-256은 동봉된 사이드카와 GitHub Release에서 확인한다.

## v0.9 코어와 저장 범위

2,114개 코어 검사는 기존 도시·주민·38자원·37제조법·26기술에 더해 다음 경계를 포함한다.

- 지상·2층·3층 플랫폼 지지, 같은 좌표의 층 분리, 아이템 리프트·유체 라이저 쌍과 층별 전력
- 규칙당 비교·동작, AND 허용·OR 차단, 대상당 4개·전체 64개 제한, 첫 제어 장치 1개 비용과 수동 일시정지 우선
- 대교·중앙 발전소·연구 단지의 3단계 납품·날짜 진행·취소·도로/전력/연구 보상
- 원료와 유체 보존, 같은 틱 다중 이동 금지, 조건과 프로젝트가 기존 경제를 우회하지 않는 경계

Editor의 97개 저장 검사는 v1~v4의 정확한 9칸 형식을 먼저 검증해 동결된 v5·공장 v2로 만들고, 그 38칸 상태를 다시 검증한 뒤 v6·공장 v3으로 이관하는 경로를 포함한다. 기존 수동 `Paused`는 보존하고 플랫폼·링크·컨트롤러·규칙·프로젝트는 빈 기본값으로 추가하며, 비영속 `AutomationBlocked`는 저장하지 않는다. 잘못된 v5/v6 배열·층·링크·규칙·프로젝트는 자동 보정하지 않고 거부한다.

## v0.9 네이티브 실제 범위

도시 런타임은 저장 직전과 재로드 직후의 `SaveStore` 영속 필드 전체가 정확히 같음을 확인했다. 로드 뒤 런타임 재결합은 현재 도시 전력에 맞춘 `FactoryState.PowerBudget` 캐시만 다시 계산하며 연구·시대·재고·공장·프로젝트 등 다른 영속 상태를 바꾸지 않는다.

복합 산업 네이티브는 다음 세 시나리오를 실제 `FactoryController.Update` 흐름으로 실행했다.

- 석유화학: 원유 펌프와 물 펌프가 실제 원료를 추출하고 정유소의 중유·석유 가스 출력, 중유 탱크와 플라스틱 중합·반출까지 진행한다.
- 알루미늄 회수: 보크사이트·석탄·석재는 명시적으로 준비된 원료이며, 물 펌프와 회수 물 배관 루프를 실제로 거쳐 알루미나 용액→스크랩→알루미늄을 생산·반출한다. 이 시나리오를 매장지 채굴부터 시작한 원료 E2E로 해석하지 않는다.
- 제어 장치: 컴퓨터·모터·배터리·모듈 프레임·알루미늄 케이싱 5종이 준비된 상태에서 제조기 최종 조립과 반출을 확인한다. 1차 원료부터의 전체 체인 증거는 코어 장부 검사와 구분한다.

복층·프로젝트 네이티브는 원료 곡물이 아이템 리프트로 세 층을 지나 실제 제분·제빵 뒤 지상 도시 재고로 반출되는 흐름을 확인했다. 물은 준비된 40L에서 시작해 전력이 공급된 두 유체 라이저를 거쳐 3층 탱크에 도달한다. 설치된 조건 규칙은 도시 빵 재고와 상층 물 수치를 읽어 설비를 차단하고, 수동 일시정지와 저장 전 편집 초안을 보존한다.

연구 단지는 실제 도시 부지 선택, 각 단계 자재 납품, `GameController`의 날짜 진행을 통한 세 공사 단계와 완공 후 일일 연구 +10까지 네이티브로 확인했다. 대교의 실제 도로 전환과 중앙 발전소의 날짜당 석탄 2개·전력 +80은 이번 네이티브 시나리오가 아니라 코어 검사 범위다.

UI 캡처의 1600×900, 1280×720, 1024×768, 2048×1536은 RenderTexture 결과다. 정확한 크기·비검정 화면·레이캐스트·44픽셀 동작 영역을 확인하지만 물리 모니터, Android 기기, 실제 키보드·터치 하드웨어 검증은 아니다.

## 외부 AI와 Android

주민 AI 검사는 명시적인 로컬 fixture 서버를 이용한 `MOCK_CONTRACT`이며 같은 최종 빌드 GUID에서 160개를 통과했다. `actualProviderCall=false`이므로 유료 OpenRouter 모델 호출 성공 증거가 아니다. 이번 릴리스는 Windows용이며 최신 Android APK·실기기 설치·화면·터치·성능 검사는 포함하지 않는다.

## 재현

~~~powershell
.\Tools\Build.ps1 -OutputDirectory .\Builds\Windows-v0.9
.\Tools\Test.ps1 -Runtime -FactoryRuntime -CompactUiRuntime -FeelRuntime -TutorialRuntime -ResidentActivityRuntime -ResearchTreeRuntime -IndustryRuntime -ExpansionRuntime -ExecutablePath .\Builds\Windows-v0.9\Riverworks.exe
npm test --prefix Server
.\Tools\Package-Release.ps1 -Version 0.9.0 -WindowsBuildPath .\Builds\Windows-v0.9 -OutputDirectory .\Builds\Release-v0.9 -PreviewPath .\Docs\Images\expansion-v0.9.png -MoviePath .\Artifacts\ExpansionSmoke\expansion-v0.9.mp4
.\Tools\Verify-Release.ps1 -ZipPath .\Builds\Release-v0.9\Riverworks-v0.9.0-Windows-x64.zip -ExpectedVersion 0.9.0
~~~

현재 9개 런타임과 주민 AI 로컬 계약은 통과했으며 위 패키징·검증은 다음 단계다. `-MoviePath`의 `Artifacts/ExpansionSmoke/expansion-v0.9.mp4`는 이번 v0.9 최종 네이티브 결과 이미지 8장을 사용해 새로 만든 930,425바이트 H.264, 1600×900, 192프레임, 16초 영상이다. 실제 플레이를 연속 녹화한 영상이나 성능 벤치마크가 아니며, v0.7 연구 영상·v0.8 산업 영상·예전 `factory-process.mp4`를 재사용하지 않았다.

패키지에 포함된 이 문서는 빌드와 실행 검사 결과를 기록한다. ZIP 자체의 크기·SHA-256과 압축 해제 실행 결과는 공개 저장소의 최신 검증 기록과 GitHub Release에 별도로 추가한다.

주민 AI는 별도 로컬 계약 서버와 `-riverworks-resident-ai-smoke` 인수로 검사한다. Feel 원본은 공개 소스에서 제외하며 재빌드는 [Feel 설치 안내](FEEL_SETUP.ko.md)를 따른다. 릴리스는 [배포 절차](RELEASE_PROCESS.ko.md)에 따라 새 폴더에서의 실행과 SHA-256을 확인한다.

## v0.9.0 배포 파일 확인

Windows ZIP은 41,831,467바이트이며 필수 파일 35개·전체 188개 엔트리, CRC와 SHA-256 검사를 통과했다. 새 `Artifacts/ReleaseCheck-v0.9.0` 폴더에서 도시 실행 203개와 확장 실행 112개 검사를 같은 GUID로 다시 통과했다. 코드 DLL·리소스·Player 설정 파일은 원본 빌드와 바이트가 같다.

- Windows SHA-256: `6ddb2c00fa02d970b1755d52c385140f8987657e80517a18540086dbf79160fb`
- 선택형 게이트웨이: 20,187바이트·14개 엔트리, CRC·허용 경로·SHA-256 통과
- Gateway SHA-256: `b15f00eb7b5f95986546f53db9b3eca771f4a5b8950290ad3f814da9799c48a3`

배포 파일은 [v0.9.0 Release](https://github.com/atozuser0224/riverworks-city/releases/tag/v0.9.0)에서 제공한다. ZIP에 포함된 검증 문서는 패키징 전에 동결했으며 이 배포 파일 확인 절은 소스의 검증 기록에 추가했다.