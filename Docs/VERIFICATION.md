# RIVERWORKS v0.7.0 Windows 검증 기록

검증일: 2026-09-14 UTC. Unity 6000.5.5f1. 저장 형식은 v4를 유지한다.

| 범위 | 결과 | 근거 |
|---|---|---|
| 도시·기술·물류·주민 핵심 검사 | 715개 통과 | Artifacts/ResearchTree/final-tests.log |
| Unity 저장·복원·이전 버전 호환 | 64개 통과 | Artifacts/save-validation-results.txt |
| Windows x64 빌드 | 오류 0, 경고 30, 108,097,235바이트 | Artifacts/build-result.txt |
| 도시 실행·연구 접근 | 155개 통과 | Artifacts/Smoke/runtime-results.txt |
| 같은 도시의 물류 실행 | 106개 통과 | Artifacts/SharedCitySmoke/shared-city-results.txt |
| 컴팩트 UI 실행 | 1,087개 통과 | Artifacts/CompactUiSmoke/compact-ui-results.txt |
| 연결형 연구 지도 | 1,081개 통과 | Artifacts/ResearchTreeSmoke/research-tree-results.txt |
| Feel 연출·카메라 | 180개 통과 | Artifacts/FeelSmoke/feel-results.txt |
| 단우·기본/후속 튜토리얼 | 2,465개 통과 | Artifacts/TutorialSmoke/tutorial-results.txt |
| 주민 모델·건물별 작업 | 306개 통과 | Artifacts/ResidentActivitySmoke/resident-activity-results.txt |
| 주민 AI HTTP 계약·UI | 160개 통과, MOCK_CONTRACT | Artifacts/ResidentAiSmoke/resident-ai-results.txt |
| Node 게이트웨이 fixture 검사 | 22개 통과 | Artifacts/ResearchTree/server-tests.log |

Windows 실행 검사 8개의 빌드 GUID는 모두 **36c52483354e4e2f90acc0c0422bcb64**이며 런타임 오류는 모두 0이다. 엔진 런처 날짜 대신 빌드 결과와 실행 GUID로 최신 여부를 확인했다.

## 연결형 연구 지도

실제 카탈로그의 18개 기술·19개 간선, 8단계 위상 깊이와 세 시대 구역을 검증했다. 모든 연결선은 콘텐츠 안에 있고 다른 기술 카드 내부를 가로지르지 않는다. 길드·증기력·자동화의 다중 선행 조건을 보존하며, 자동화의 공통 조상을 한 번만 계산한다. 기존 기술 ID·비용·효과와 연구 시작 시 지식·코인을 한 번 차감하는 규칙은 유지한다.

한글·영문 검색, 검색 결과 없음, 필터의 문맥 유지, 모든 노드의 이동·레이캐스트 접근, 시대·현재 연구·초기화 버튼, 100/200% 확대와 경계 제한, 확대 후 선택한 기술 유지, 실제 작은 지도 클릭을 검사했다. 상세 패널에는 도시 건물과 공장 설비 해금이 모두 표시되며 모든 활성 텍스트의 실제 선호 높이가 표시 영역 안에 들어간다. 검색 입력 포커스를 게임 단축키 차단 경로가 인식하는지도 확인했다.

1600×900, 1280×720, 1024×768, 2048×1536의 각 RenderTexture에서 개요·길드 경로·자동화·확대 검색을 렌더링해 총 16장의 정확한 크기 이미지를 남겼다. 물리 모니터·Android 기기·실제 키보드 하드웨어 입력 검사와는 구분한다. 캡처를 직접 검토했으며 기술 지도 구현과 사용법은 [연구 트리 가이드](RESEARCH_TREE.ko.md)에 있다.

## 기존 동작과 화면 가림

도시·물류의 실제 생산·운반·도로 연결·자원 보존·저장 복원, 11단계 단우 행동 목표와 19개 안내, 실제 주민 이동과 작업 애니메이션, 카메라·Feel·축소 모션 검사를 함께 수행했다. 주민 생성 프리팹 5개와 시각 라이브러리는 재생성 전후 의미 있는 직렬화 속성이 같음을 확인해 소스 변경에서 제외했다.

| 렌더 크기 | 기본 화면 차단 | 선택 화면 차단 |
|---|---|---|
| 1600×900 | 126/960 · 13.13% | 161/960 · 16.77% |
| 1280×720 | 168/960 · 17.50% | 231/960 · 24.06% |
| 1024×768 | 170/960 · 17.71% | 236/960 · 24.58% |
| 2048×1536 | 85/960 · 8.85% | 103/960 · 10.73% |

기본 화면 20%, 선택 화면 30% 기준을 모두 만족한다. 연구 모달은 열려 있는 동안 월드 입력과 도시 시간을 의도적으로 막는다. v0.6.2 당시 화면 개편 기록과 전후 캡처 범위는 [UI 다듬기](UI_POLISH.ko.md)에 보존한다.

## 외부 AI와 Android

주민 AI 검사는 명시적인 로컬 fixture 서버를 이용한 MOCK_CONTRACT다. 실제 OpenRouter 모델 호출은 수행하지 않았다. 이번 릴리스는 Windows용이며 최신 Android APK·실기기 설치·화면·터치·성능 검사는 포함하지 않는다.

## 재현

~~~powershell
.\Tools\Build.ps1
.\Tools\Test.ps1 -Runtime -FactoryRuntime -CompactUiRuntime -FeelRuntime -TutorialRuntime -ResidentActivityRuntime -ResearchTreeRuntime
npm test --prefix Server
.\Tools\Package-Release.ps1 -Version 0.7.0 -PreviewPath .\Docs\Images\research-tree-v0.7.png -MoviePath .\Artifacts\ResearchTree\research-tree.mp4
.\Tools\Verify-Release.ps1 -ZipPath .\Builds\Riverworks-v0.7.0-Windows-x64.zip
~~~

주민 AI는 별도 로컬 계약 서버와 -riverworks-resident-ai-smoke 인수로 검사한다. Feel 원본은 공개 소스에서 제외하며 재빌드는 [Feel 설치 안내](FEEL_SETUP.ko.md)를 따른다. 릴리스는 [배포 절차](RELEASE_PROCESS.ko.md)에 따라 새 폴더에서의 실행과 SHA-256을 확인한다.

## v0.7.0 패키지 확인

Windows ZIP은 39,137,490바이트이며 필수 파일 31개·전체 엔트리 184개와 SHA-256 검사를 통과했다. 새 폴더에 압축을 푼 뒤 코드 DLL·리소스·Player 설정의 바이트 일치와 연구 지도 검사 1,081개를 같은 빌드 GUID로 확인했다. 선택형 게이트웨이 ZIP은 19,069바이트이며 14개 파일의 CRC·SHA·금지 경로 검사를 통과했다.

- Windows SHA-256: `6aa815c71aeb6cbf5539787a726b1c129568d2b375585c2946450041ab7c5dc3`
- Gateway SHA-256: `39d06046e52ee16f0153d156a2b201e2ce8cb8ddf4c8fd722c62904a664551e3`

8초 미리보기는 이번 실행에서 캡처한 개요·길드 경로·자동화·확대 검색 화면 4장을 이어 만든 영상이다. 실시간 성능 벤치마크가 아니다. 공개 소스의 텍스트 파일 325개에서 비밀값·개인 사용자 경로를 검사했으며 금지 경로는 추적하지 않는다.
