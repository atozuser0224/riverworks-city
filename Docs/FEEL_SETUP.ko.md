# Feel 5.6.1 로컬 설치

Riverworks의 공개 소스 저장소에는 상용 에셋인 Feel 원본을 포함하지 않는다. 소스에서 Unity 프로젝트를 열거나 빌드하려는 개발자는 Feel 5.6.1 라이선스를 직접 보유하고, 본인의 합법적인 다운로드를 사용해 필요한 파일을 로컬에 설치해야 한다. 이 도구는 구매, 계정 인증, 다운로드를 대신하거나 우회하지 않는다.

## 고정된 요구 사항

- Feel `5.6.1` (`02 Jul 2025`) 원본 `.unitypackage`
- 원본 패키지 SHA-256: `36f6c61c45478216353d73d0f46e34f5089ccab6a879186dc59879ea13f77dd1`
- Python 3
- Unity `6000.5.5f1`(Unity 6.5.5)과 이 저장소의 프로젝트

Feel은 본인이 라이선스를 가진 공식 Unity Asset Store 계정과 Unity의 정상 다운로드 절차로 받아야 한다. 이 저장소는 Feel 다운로드 링크나 패키지 복사본을 제공하지 않는다.

## 먼저 검사하기

프로젝트 루트의 PowerShell에서 패키지 경로를 지정해 dry run을 실행한다.

```powershell
python .\Tools\Import-Feel.py "D:\LicensedAssets\Feel v5.6.1 (02 Jul 2025).unitypackage" --dry-run
```

dry run은 패키지를 수정하거나 프로젝트에 파일을 쓰지 않는다. 다음을 확인한 뒤 종료한다.

- Unity 패키지 형식인지(파일명은 바꿀 수 있으며 버전은 SHA-256으로 확인)
- 패키지 SHA-256이 위 고정값과 같은지
- 선별된 에셋이 1,031개인지
- 각 선별 파일의 경로와 설치될 내용의 SHA-256
- 함께 보존할 Unity `.meta` 파일 개수

패키지의 GUID 디렉터리, `pathname`, 절대 경로와 `..` 경로를 엄격하게 검사한다. `pathname` 뒤의 Unity 패키지 표식(`00`)은 첫 줄을 기준으로 정규화한다.

## 설치하기

dry run이 성공한 같은 원본으로 실행한다.

```powershell
python .\Tools\Import-Feel.py "D:\LicensedAssets\Feel v5.6.1 (02 Jul 2025).unitypackage"
```

도구가 설치하는 범위는 다음과 같다.

- `Assets/Feel/MMFeedbacks`
- `Assets/Feel/MMTools`
- `Assets/Feel/license.txt`와 `Assets/Feel/readme.txt`
- 위 파일과 부모 폴더의 원본 `.meta`

Feel의 `FeelDemos`, `MMTools/Demos`, 최상위 `NiceVibrations` 원본과 네이티브 플러그인은 설치하지 않는다. `MMFeedbacks` 안에서 선택된 런타임 연동 소스는 원래 선별 범위에 포함된다. 원본 파일의 Unity GUID와 Feel 라이선스 파일은 변경하지 않는다.

대부분의 파일은 패키지에서 그대로 복사한다. Unity 6000.5.5f1 호환을 위해 다음 두 곳만 좁게 변환한다.

- `MoreMountains.Tools.asmdef`의 참조를 `UnityEngine.UI`, `Unity.TextMeshPro`로 지정
- `MMMonoBehaviourUITKEditor.cs`의 단일 `target.GetInstanceID()` 호출을 `target.GetEntityId()`로 변경

기존 파일의 내용이 설치 결과와 같으면 건너뛴다. 내용이 다르면 사용자 변경을 덮어쓰지 않고 중단한다. 차이를 직접 검토하고 교체하려는 경우에만 `--force`를 붙인다.

```powershell
python .\Tools\Import-Feel.py "D:\LicensedAssets\Feel v5.6.1 (02 Jul 2025).unitypackage" --force
```

설치가 끝난 뒤 Unity에서 프로젝트를 열고 [검증 절차](VERIFICATION.md)에 따라 컴파일과 Feel 런타임 검사를 수행한다. `.unitypackage` 자체와 `Assets/Feel` 원본은 공개 Git 커밋에 추가하지 않는다.
