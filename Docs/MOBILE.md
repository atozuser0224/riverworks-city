# RIVERWORKS Android 빌드

현재 v0.6 GitHub Release는 Windows용이다. 아래 APK 생성 기록은 이전 v0.5에 대한 것이며, 새 주민 모델이 포함된 v0.6 APK와 실제 Android 기기는 아직 검증하지 않았다.

프로젝트는 Android 8.0(API 26) 이상, ARM64, IL2CPP, 가로 화면으로 빌드한다. APK는 Unity의 기본 디버그 서명을 사용하므로 개발·테스트용이다. 스토어 배포 전에는 별도의 안전한 서명 및 릴리스 설정이 필요하다.

도시와 공장 설비는 같은 맵과 카메라를 사용한다. 하단의 설비·물류 탭에서 도구를 고르면 목록이 접히며 탭으로 설치한다. 선택 정보와 목표는 닫을 수 있고, 현황·영토·교역·메뉴는 필요할 때 연다. 주요 조작 버튼은 44 기준 픽셀 이상이다. 도구를 취소하면 한 손가락으로 카메라를 이동하고, 두 손가락 이동과 핀치 확대도 사용할 수 있다. 시스템 뒤로 가기는 열린 창을 먼저 닫고 그다음 도구를 취소한다.

## APK 만들기

프로젝트 루트에서 PowerShell 5 이상으로 실행한다.

```powershell
.\Tools\Build-Android.ps1
```

결과는 `Builds\Android\Riverworks.apk`, 전체 로그는 `Artifacts\android-build.log`, 요약은 `Artifacts\android-build-result.txt`에 생성된다.

## Android Studio용 Gradle 프로젝트 내보내기

```powershell
.\Tools\Build-Android.ps1 -ExportGradle
```

결과는 `Builds\Android\GradleProject`에 생성된다. 내보낸 프로젝트를 Android Studio에서 열어 추가 네이티브 설정이나 릴리스 서명을 적용할 수 있다.

빌드 스크립트는 `ProjectSettings\ProjectVersion.txt`의 정확한 Unity 버전을 사용한다. `%LOCALAPPDATA%\RiverworksBuildTools\Unity\<버전>\Editor\Unity.exe`에 사용자 전용 Android 에디터가 있으면 우선 사용하고, 없으면 Unity Hub 설치본을 사용한다. 같은 에디터에 Android Build Support, Android SDK/NDK Tools, OpenJDK가 모두 없으면 누락된 경로를 표시하고 중단한다. `UNITY_EDITOR_PATH`로 정확한 `Unity.exe` 경로를 덮어쓸 수도 있다.

2026-09-14에 v0.5.0(versionCode 5) APK 생성과 서명 검사, ARM64 라이브러리 및 min SDK 26/target SDK 36 구성을 확인했다. APK 크기는 22,912,606바이트다. `Tools/Inspect-Android.ps1`로 같은 검사를 재현할 수 있다. 실제 Android 기기 설치, 터치 사용성, 성능과 발열은 검사하지 않았다.
