# 주민 모델과 건물별 활동

주민은 Quaternius의 **Ultimate Animated Character Pack**에서 가져온 로우폴리 모델을 사용한다. 일반 주민·작업복 주민·제빵사와 청록색 옷의 단우를 같은 주민 ID, 도로 이동, 선택, AI 계획에 연결한다. 단우의 모델을 바꿔도 카메라가 따라가는 주민 루트는 유지한다.

주민이 실제 목적지에 도착하면 건물의 앞마당에서 작업이나 여가 동작을 보여준다. 건물마다 최대 두 명만 표시한다. `Indoors`는 시뮬레이션의 체류 상태로 유지하며, 보여주기 위해 출퇴근·생산량·인구·LLM 결정을 변경하지 않는다. 상자와 도구는 활동을 설명하는 소품이며 실제 물류 재고로 계산하지 않는다.

| 장소 | 보이는 활동 | 동작 구성 |
|---|---|---|
| 농장 | 밭 경작과 수확 | UAL2 `Farm_Harvest`, 호미와 밭고랑 |
| 벌목장 | 통나무 다듬기 | UAL2 `TreeChopping_Loop`, 도끼와 통나무 |
| 채석장·광산 | 돌과 광석 캐기 | 도끼질 클립을 곡괭이 작업에 재사용 |
| 제분소·풍차·창고 | 자루와 화물 운반 | UAL2 `Walk_Carry_Loop`, 작업용 상자와 적재 소품 |
| 제과점 | 반죽하고 굽기 | 기본 대기 클립과 양팔·손 보조 동작, 반죽대 |
| 제련소·공방 | 쇠 두드리기와 도구 제작 | 기본 대기 클립과 망치질 보조 동작, 모루·작업대 |
| 서재·아카데미 | 독서와 연구 | 기본 대기 클립과 독서 자세, 책과 독서대 |
| 증기 동력소 | 보일러 돌보기 | 기본 대기 클립과 조작 자세, 렌치·연료함 |
| 시장·시청 광장 | 장보기·흥정·대화 | UAL1 `Idle_Talking_Loop` |
| 공원 | 앉아 쉬기 | UAL1 `Sitting_Idle_Loop` |
| 도로 | 출퇴근·이동 | UAL1 `Walk_Loop` |

무료 애니메이션 팩에 정확한 독서·반죽·망치질·곡괭이 클립은 없다. 해당 작업은 위 표처럼 기존 클립과 절차적 보조 동작을 조합한다. 휴머노이드 리타게팅과 루트 모션 차단으로 이동 위치는 계속 시뮬레이션이 소유한다. 가까이 확대하거나 주민을 선택하면 짧은 한글 활동 설명을 보여준다.

## 성능과 일시정지

여러 색의 원본 서브메시를 정점 색이 있는 단일 스킨드 메시와 공유 재질로 변환한다. 원본 FBX는 수정하지 않는다. 거리 주민은 PC 최대 128명, 모바일 최대 64명이며 작업자는 별도로 PC 32명, 모바일 16명까지 표시한다. 화면 밖 애니메이션 평가는 10Hz로 제한하고, 사용하지 않는 주민 모델 캐시를 표시 예산 근처로 정리한다. 화면에 없는 주민도 시뮬레이션에서는 계속 살아 있다.

일반 주민은 게임 일시정지를 따르며, 안내 중인 단우만 튜토리얼의 정지와 관계없이 움직인다. 자동 카메라 이동은 기존 효과 줄이기 설정을 따른다. 모바일의 작은 모델과 도구는 그림자를 만들지 않는다.

## 출처와 재생성

- [Ultimate Animated Character Pack](https://quaternius.com/packs/ultimatedanimatedcharacter.html): `Worker_Male`, `Worker_Female`, `Casual_Male`, `Chef_Female`.
- [Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html) 무료 Standard.
- [Universal Animation Library 2](https://quaternius.com/packs/universalanimationlibrary2.html) 무료 Standard.

세 팩은 제작자가 CC0로 공개한 에셋이다. 원본·라이선스·출처·SHA-256은 `Assets/ThirdParty/Quaternius/Characters`에 있다. 다운로드 ZIP과 검사 자료는 배포 대상이 아닌 `Artifacts/ResidentAssets`에 있다.

`Riverworks.Editor.ResidentAssetBuild.Prepare()`가 `Assets/Resources/Residents`에 프리팹과 라이브러리를 생성한다. 기본 빌드 준비 과정에 이 단계가 포함되어 있다. 실제 임포트 결과는 `Artifacts/ResidentAssets/import-report.json`, 실행 검사 결과는 `Artifacts/ResidentActivitySmoke/resident-activity-results.txt`에 기록한다. 빌드·실행 검증 상태는 해당 최신 결과 파일로 확인한다.
