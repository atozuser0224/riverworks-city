# RIVERWORKS v0.6.1 Windows 검증 기록

검증일: 2026-09-14. Unity 6000.5.5f1. 저장 형식은 v4를 유지한다.

| 범위 | 결과 | 근거 |
|---|---|---|
| 도시·기술·물류·주민 핵심 검사 | 695개 통과 | Artifacts/v061-tests.log |
| Unity 저장·복원·이전 버전 호환 | 64개 통과 | Artifacts/save-validation-results.txt |
| Windows x64 빌드 | 오류 0, 경고 30, 107,960,691바이트 | Artifacts/build-result.txt |
| 도시 실행 | 59개 통과 | Artifacts/Smoke/runtime-results.txt |
| 같은 도시의 물류 실행 | 106개 통과 | Artifacts/SharedCitySmoke/shared-city-results.txt |
| 컴팩트 UI 실행 | 248개 통과 | Artifacts/CompactUiSmoke/compact-ui-results.txt |
| Feel 연출·카메라 | 180개 통과 | Artifacts/FeelSmoke/feel-results.txt |
| 단우·기본/후속 튜토리얼 | 886개 통과 | Artifacts/TutorialSmoke/tutorial-results.txt |
| 주민 모델·건물별 작업 | 306개 통과 | Artifacts/ResidentActivitySmoke/resident-activity-results.txt |
| 주민 AI HTTP 계약 | 77개 통과, MOCK_CONTRACT | Artifacts/ResidentAiSmoke/resident-ai-results.txt |
| Node 게이트웨이 fixture 검사 | 22개 통과 | Artifacts/resident-release-server-tests.log |

Windows 실행 검사 전체의 빌드 GUID는 **6305bd1d1d9c485c90bd4c7e4aeef363**이며 런타임 오류는 모두 0이다. 빌드 경고 수와 런타임 오류 수를 구분해 기록한다. 엔진 런처 날짜 대신 빌드 결과와 실행 GUID로 최신 여부를 확인했다.

## 주민 모델과 작업

공식 Quaternius 원본 모델 4종과 단우 변형 1종은 유효한 Humanoid Avatar, 단일 스킨드 메시·서브메시와 정점 색을 가진다. 원본 FBX는 보존하고 생성 프리팹에서 발 관절 계층을 교정했다. 가져오기 배율을 실측해 원본 높이 약 3.16~3.37을 런타임 0.36으로 정규화했다.

UAL1/UAL2의 대기·걷기·운반·벌목·수확·대화·앉기 클립 이름을 정확히 대조했다. 실제 Playables 시간과 관절 변화, 일시정지, 루트 모션 누적 방지를 검사했다. 독서·반죽·망치질·보일러 작업은 전용 클립이 없는 보조 동작임을 구분한다.

실제 CitizenSimulation의 출근·작업장 도착을 관찰했다. 별도의 제어된 표현용 도시에서는 16개 작업·여가 건물, 건물당 두 명, 작업자 32명과 거리 주민 128명 상한, 모델 캐시, 선택과 생각 문자열 정제, 모델 교체 및 건물·시대·도시 교체 후 재연결을 검사했다. 제어된 캡처 배치를 일반 플레이의 자연 성장 결과로 주장하지 않는다.

overview와 6개 작업 클로즈업, 관절 변화가 확인되는 24프레임을 캡처했다. 단우 안내·시청 도착 캡처와 합친 짧은 MP4는 실제 런타임 프레임으로 만든 미리보기이며 성능 벤치마크가 아니다. 출처·라이선스·동작 매핑은 [주민 시각 표현](RESIDENT_VISUALS.ko.md)에 있다.

## 튜토리얼·도시·물류·UI

튜토리얼은 시청 도착, 실제 주민 단우의 도로 이동과 카메라 추적, 수동 카메라 인계, 도트 한글과 초상화 제거, 글자 출력·페이지 높이, 11개의 실제 행동 목표, 19개 기능 안내와 읽음 저장을 검사한다. 윤작 완료는 1배속 실제 시간으로 확인하고 저장 실패·성공을 구분한다.

도시·통합 물류 검사는 18개 연구와 시대 진행, 주민 보행·선택·정지, 농장과 벌목장의 출고 버퍼, 투입기·벨트·조립기·도시 창고 인계, 원료 보존과 저장 복원을 포함한다. 물류는 실제 운송 중 도시 재고에 중복 반영되지 않는다.

UI 검사는 실제 EventSystem 입력으로 도구 분류 전환, 선택 창·모달·연구·설비 구성과 월드 입력 복원을 확인한다. 1600×900과 1280×720에서 기본 화면의 UI 차단 지점은 168/960(17.50%), 선택 화면은 240/960(25.00%)이다. 화면당 40×24개 지점의 실제 입력 차단 검사이며 Android 기기 측정이 아니다.

## 외부 AI와 Android의 경계

주민 AI 검사는 명시적인 로컬 fixture 서버를 이용한 **MOCK_CONTRACT**다. Unity의 HTTP 응답 검증·실제 도로 이동·체류·오류 차단을 확인했으나 OpenRouter 모델을 호출하지 않았다. 실제 모델 호출은 사용자 키가 필요한 별도 LIVE_OPENROUTER 검사이며 아직 미검증이다.

이번 GitHub Release는 Windows용이다. Android 설정과 빌드 도구는 제공하지만 최신 주민 모델이 들어간 v0.6 APK, 실제 기기 설치·화면·터치·성능은 검사하지 않았다. 이전 v0.5 APK를 최신 릴리스 산출물로 포함하지 않는다.

## 재현

v0.6.1은 가까운 줌에서 수동 이동·반복 확대·Feel 반응이 같은 줌 범위를 사용하는지 확인하는 추가 회귀 검사 6개를 포함한다.

~~~powershell
.\Tools\Build.ps1
.\Tools\Test.ps1 -Runtime -FactoryRuntime -CompactUiRuntime -FeelRuntime -TutorialRuntime -ResidentActivityRuntime
npm test --prefix Server
.\Tools\Package-Release.ps1 -Version 0.6.1 -PreviewPath .\Docs\Images\riverworks-v0.6.png
.\Tools\Verify-Release.ps1 -ZipPath .\Builds\Riverworks-v0.6.1-Windows-x64.zip
~~~

상용 Feel 원본은 공개 소스에서 제외한다. 재빌드하려는 개발자는 [Feel 설치 안내](FEEL_SETUP.ko.md)를 따른다. 배포는 [릴리스 절차](RELEASE_PROCESS.ko.md)에 따라 새 폴더에 압축 해제한 실행 파일과 SHA-256을 확인한 뒤 진행한다. 로그·테스트 저장·실제 키·실행 상태는 배포에 포함하지 않는다.
