# RIVERWORKS v0.9 아키텍처

## 범위

RIVERWORKS v0.9은 Unity 6000.5.5f1 기반 단일 플레이어 공유 도시 프로토타입입니다. 런타임은 21×21 도시, 42×42 미세 격자의 지상·2층·3층 공장, 단일 HUD와 카메라, 38개 자원, 37개 제조법, 23개 설비, 26개 기술과 35개 연구 간선, 도시 프로젝트, 조건 자동화와 도시 저장 v6·공장 저장 v3를 구성합니다.

공장은 별도 게임이나 화면이 아닙니다. 공장 전용 바닥·카메라·연습 상태·청사진 UI가 없고, 도시 지형·영토·건물·도로가 설비의 배치와 도시 인계에 대한 권위 있는 상태입니다. 데스크톱과 모바일 입력 모두 같은 `OrbitCamera`와 레이어 8 도시 필지를 사용합니다.

주민의 기본 일과와 단우 안내는 로컬 규칙으로 동작합니다. 별도의 `ResidentAiClient`와 `Server/` 게이트웨이는 OpenRouter 모델이 제안한 제한된 방문 계획을 선택적으로 적용합니다. 모델이 건설·경제·생산 상태를 직접 바꾸는 API는 없으며, 연결되지 않았거나 검증에 실패하면 `CitizenSimulation`의 기존 일과를 계속 사용합니다.

v0.8 복잡 산업을 포함한 v0.9 통합 Windows 네이티브 검사와 Windows x64 빌드를 완료했습니다. 빌드 GUID는 `029d32dac5eb498a901bde882a5aad97`이며 세부 결과는 [검증 기록](VERIFICATION.md)을 따릅니다. 주민 AI 검사는 실제 제공자 호출이 없는 `MOCK_CONTRACT`이고 `LIVE_OPENROUTER`와 Android 실제 기기 검증은 별도 경계입니다.

## 실행 객체 그래프

```text
Riverworks.unity
  └─ GameController.Awake
      ├─ SaveStore.TryLoad 또는 GameState.CreateNew
      ├─ Simulation
      │   └─ 도시 경제·연구·목표·도로 연결·도시 물류 버퍼·CityProjects
      ├─ BoardView
      │   ├─ 21×21 지형·도로·건물
      │   └─ FactoryView
      │       └─ 42×42 미세 좌표의 설비·고체 화물·유체·전선·매장지·미리보기
      ├─ CitizenSimulation + CitizenView
      ├─ OrbitCamera
      │   └─ CameraCinematics: 시청 줌·교육 초점·단우 추적·반응 줌
      ├─ FeelDirector
      │   └─ WorldFeelEffects + 실제 Feel 5.6.1 MMF_Player
      ├─ Hud
      │   ├─ 도시 건설 탭
      │   ├─ 설비·물류 탭과 3개 층 선택
      │   ├─ 공용 검사기
      │   ├─ 설비 구성 모달
      │   ├─ IndustryPanel: 제조법·자원·생산 도감
      │   ├─ CityProjectPanel
      │   ├─ AutomationRulePanel
      │   ├─ TutorialDialogue
      │   └─ ResidentAiPanel
      ├─ FactoryController
      │   └─ FactorySimulation(GameState.Factory, CityLogistics)
      │       ├─ FactoryLayers + 층간 링크
      │       ├─ FactoryAutomation
      │       └─ FactoryFluidSimulation
      ├─ CityProjectView
      ├─ TutorialDirector + TownHallIntro
      ├─ ResidentAiClient ─HTTP→ 선택형 Node.js/OpenRouter 게이트웨이
      ├─ MobileInput + Soundscape
      └─ 명령행 플래그에 따른 전용 런타임 스모크
```

`GameController.Update`는 1배속에서 현실 5초마다 `Simulation.Tick()`을 호출합니다. `FactoryController.Update`는 같은 속도를 사용해 0.1초 단위로 `FactorySimulation.Tick()`을 계속 실행합니다. 설비 팔레트가 닫혀도 설비는 멈추지 않습니다. 도움말, 연구, 확인 또는 설비 구성 모달이 열리거나 게임 속도가 0이면 두 흐름 모두 멈춥니다.

## 저장 상태

### `GameState`

[GameState.cs](../Assets/Scripts/Core/GameState.cs)의 현재 직렬화 필드는 다음 경계를 갖습니다.

- `Version = 6`, `Size = 21`
- `Stock`: `ResourceCatalog.Count = 38`인 도시 공유 재고
- `Cells`: 441개의 `Cell`
- `OwnedRegions`: 구매한 7×7 지역 ID
- 연구·시대·인구·목표·일일 진행 필드
- `Factory`: 현재 실행되는 42×42 공유 도시 설비 상태
- `ArchivedFactory`: v3에서 이전된 별도 공장 원본 또는 `null`
- `CityProjects`: 대교·중앙 발전소·연구 단지의 부지와 3단계 납품·공사·연료 날짜 상태
- `Tutorial`: 11단계 진행, 건너뛰기·완료, 19개 기능 안내 읽음 상태와 시청 인트로 재생 여부

`GameState.CreateNew()`는 `FactoryState.CreateCityGrid()`를 사용하므로 새 게임의 활성 설비 격자는 처음부터 42×42입니다.

### `Cell`

각 `Cell`은 도시 좌표와 `Terrain`, `Building`, `Connected`, 레벨·진행·상태 외에 다음 두 버퍼를 가집니다.

- `LogisticsInput`: 도시 가공 시설로 실제 전달된 원료
- `LogisticsOutput`: 도시 생산 시설에서 실제 물류로 꺼낼 생산물

두 필드는 자원 카탈로그와 같은 길이 38의 `List<float>`입니다. 코인 슬롯 0은 항상 0이고 각 버퍼 전체 용량은 `CityLogistics.BufferCapacity = 80f`입니다. `float`를 사용해 도시의 소수 생산량을 보존하고, 투입기 경계에서는 고체를 정수 1개씩 실제 화물로 변환합니다.

### `FactoryState`와 `FactoryEntity`

[FactoryState.cs](../Assets/Scripts/Core/FactoryState.cs)의 활성 런타임 상태는 다음 필드를 사용합니다.

- `Version = 3`
- `Width = 42`, `Height = 42`, `NextEntityId`
- `Entities`
- 길이 38의 자원별 `Produced`, `Exported`, `Recovered`
- `PowerBudget`, `ElapsedSeconds`
- `Platforms`: 2층·3층을 지지하는 2×2 미세 칸 플랫폼
- `AutomationRules`, `NextAutomationRuleId`: 조건 규칙과 증가 전용 ID

각 `FactoryEntity`는 기존 생산·화물·유체 필드와 함께 `Floor`, `LinkId`, `IsLinkSender`, `ControllerInstalled`를 저장합니다. 수동 `Paused`는 영속 상태지만 `AutomationBlocked`는 `NonSerialized` 계산 상태이며 `IsStopped`는 둘 중 하나라도 참일 때 동작을 멈춥니다.

`FactoryState.CreateEmpty()`와 `CreateExample()`의 기본 24×16 상태는 순수 공장 단위 픽스처와 이전 형식 검증을 위해 남아 있습니다. 실제 게임의 새 상태나 활성 상태를 만드는 API는 `CreateCityGrid()`입니다. 24×16 예제를 선택하는 런타임 UI는 없습니다.

## 좌표와 점유

`CityLogistics.Resolution`은 2입니다. 설비 미세 좌표 `(x, z)`가 속한 도시 필지는 `(x / 2, z / 2)`이고, 도시 필지 하나가 2×2 미세 칸입니다.

`FactoryView.WorldPosition(int x, int z)`는 다음 관계로 도시 보드 위 위치를 계산합니다.

```text
BoardView.Position(0, 0)
  + ((x × 0.5 - 0.25), 0, (z × 0.5 - 0.25)) × BoardView.Spacing
```

2×2 설비 모델의 중심은 좌하단 기준점에서 각 축으로 `(size - 1) / 2` 미세 칸을 더합니다. `FactoryController.GridAt(Vector3)`는 레이어 8 도시 필지의 충돌점에서 같은 역변환으로 미세 좌표를 얻습니다. 전용 공장 콜라이더나 공장 바닥은 만들지 않습니다.

### 공장 층과 플랫폼

`FactoryLayers.MaxFloor = 2`이므로 `Floor` 0·1·2는 지상·2층·3층입니다. 플랫폼 하나는 짝수 미세 좌표에서 2×2 칸, 즉 도시 부지 한 칸을 차지합니다. 2층 플랫폼은 소유한 육지와 `MassProduction`, 3층 플랫폼은 같은 위치의 2층 플랫폼과 `AdvancedManufacturing`이 필요합니다. 상층 설비는 전체 점유 칸이 해당 층 플랫폼으로 지지되어야 합니다.

플랫폼은 기존 지상 설비 위에 세울 수 있지만 물이나 도로 외 도시 건물, 도시 프로젝트 부지와 겹칠 수 없습니다. 위 설비 또는 다음 층 플랫폼을 지지하는 동안에는 철거할 수 없습니다. 플랫폼당 비용은 30G·강철 보 2·모듈 프레임 1이며 안전한 철거는 자재 전량과 코인 35%를 돌려줍니다. 중력 붕괴 시뮬레이션은 없습니다.

채굴기, 물·원유 펌프, 반입·반출 부두와 전력 인입구는 지상 전용입니다. 같은 X/Z라도 서로 다른 층의 평면 운송은 분리되고 `ItemLift`·`FluidRiser` 또는 같은 좌표의 인접 층 전신주 쌍만 명시적으로 층을 잇습니다.

### 배치 권위

`FactorySimulation.CanPlace`가 설비 격자 경계와 설비 간 겹침을 검사한 뒤 `IFactoryEnvironment.CanPlace`를 호출합니다. 실제 게임의 구현은 `CityLogistics`입니다.

- 모든 점유 도시 필지가 구매한 지역인지 검사합니다.
- 2×2 설비는 도시 필지 경계에 맞춰야 합니다.
- 기존 철광석 채굴은 실제 `TerrainKind.Rock`을 사용합니다. 새 채굴 제조법은 `IndustryDeposits`가 지정한 구리 광석·석탄·보크사이트 매장지와 일치해야 하며 원유 펌프도 지정 유전 위에만 놓입니다.
- 물 펌프는 구매한 지역의 빈 수면에만 놓입니다. 파이프와 분기점은 구매한 수면을 지날 수 있지만 다른 고체 설비는 물에서 기존 교량 조건을 따릅니다.
- 벨트·투입기·전신주·분배기와 파이프·분기점은 경량 물류 설비로 분류되어 도시 도로와 공존할 수 있습니다.
- 그 밖의 설비는 물, 도시 건물과 도시 도로가 있는 필지에 놓을 수 없습니다.

반대 방향의 충돌 검사는 `CityLogistics.CanBuildCity(GameState, BuildingKind, int x, int z, out string reason)`가 담당합니다. 도시 건물은 설비 점유와 겹칠 수 없고 도로만 경량 물류 설비와 공존할 수 있습니다. `CanDemolishCity`는 물 위 경량 물류가 남은 동안 그 아래 도로 교량 철거를 막습니다.

## 도시 프로젝트

[CityProjects.cs](../Assets/Scripts/Core/CityProjects.cs)는 대교·중앙 발전소·연구 단지의 카탈로그, 부지 점유, 시작·납품·취소·날짜 진행과 보상을 담당합니다. `CityProjectState`는 종류, 부지, 현재 단계, 남은 공사일, 시작·완공일, `FuelDay`와 길이 38의 현재 단계 납품량을 저장합니다.

프로젝트마다 시작 코인을 한 번 지불하고 세 단계의 고체 자재를 도시 재고에서 수동 납품합니다. 현재 단계 요구량을 모두 채운 뒤에만 `DaysRemaining`이 시작되며 `CityProjects.Tick`은 도로 연결 중인 날에만 감소시킵니다. 취소는 현재 단계 납품분과 시작 코인의 35%만 돌려주고 완료 단계 자재는 되돌리지 않습니다.

- 4×1 대교는 양 끝 육지, 내부 수면, 소유권과 한쪽 끝의 기존 도로 연결을 요구합니다. 완공 시 네 필지를 실제 `Road`로 바꾸며 프로젝트가 보호하는 도로는 개별 철거할 수 없습니다.
- 2×2 중앙 발전소는 완공·도로 연결 상태에서 `ProducePower`가 그날 석탄 2개를 한 번만 소비하고 전력 80을 제공합니다. `FuelDay`가 같은 날짜의 중복 소비를 막고 `PreviewPower`는 재고를 변경하지 않습니다.
- 2×2 연구 단지는 완공·도로 연결 중 `ResearchBonus = 10`을 `Simulation.ResearchPerDay`에 더합니다.

도시 건물, 공장 설비, 플랫폼과 다른 프로젝트는 같은 부지를 차지할 수 없습니다. 세 프로젝트는 종류별 하나만 허용되고 완공 보상 외의 숨은 자원·경제 변경은 없습니다.

## 도시 Core와 점진적 자동화

- [Catalog.cs](../Assets/Scripts/Core/Catalog.cs)는 시청을 포함한 도시 건물 18종의 이름, 비용, 인구 조건과 생산 레시피를 정의합니다.
- [ResourceCatalog.cs](../Assets/Scripts/Core/ResourceCatalog.cs)는 통화 1종·고체 30종·유체 7종의 이름, 단위, 색, 교역 가능 여부를 정의합니다.
- [Technology.cs](../Assets/Scripts/Core/Technology.cs)는 기술 26종의 선행 조건, 비용, 기간, 시대와 건물·설비·제조법 해금을 정의합니다. [ResearchGraph.cs](../Assets/Scripts/Core/ResearchGraph.cs)는 35개 직접 선행 연결을 검증하고 경로·계획을 계산합니다.
- [Simulation.cs](../Assets/Scripts/Core/Simulation.cs)는 건설·철거·증축, 도로 연결, 날짜별 생산·식량·세금·연구·영토·목표를 계산합니다.
- [CitizenSimulation.cs](../Assets/Scripts/Core/CitizenSimulation.cs)는 Unity와 독립된 주민 정체성, 주택·직장·목적지 배정, 도로 경로와 일과를 계산합니다.

`Simulation.Tick()`은 생산 시설마다 `CityLogistics.HasAutomatedInput`과 `HasAutomatedOutput`을 확인합니다. 두 함수는 투입기의 집는 칸과 놓는 칸이 해당 도시 필지에 속하는지 판정하며, 현재 전력 여부와 무관하게 구성된 물류 경계를 결정합니다.

### 자동화되지 않은 기준선

투입기가 연결되지 않은 도시 시설은 이전 버전과 같은 경제 흐름을 유지합니다. 연결된 생산 시설은 `GameState.Stock`에서 원료를 소비하고 결과를 같은 공유 재고에 더합니다. 따라서 기존 도시를 즉시 전면 재배치하지 않고 필지별로 자동화할 수 있습니다.

### 자동화된 출력

출력 쪽 투입기가 있으면 새 생산량을 `Cell.LogisticsOutput`에 더합니다. 출력 전체가 80을 넘게 되면 생산과 입력 소비를 모두 보류합니다. 투입기는 정수 한 개가 준비될 때 `CityLogistics.TryTake`로 화물을 꺼냅니다. 농장·벌목장 같은 원료 생산자와 제분소·제과점·제련소·공방 같은 가공 시설에 동일하게 적용됩니다.

### 자동화된 입력

입력 쪽 투입기가 있는 가공 시설은 `GameState.Stock` 대신 `Cell.LogisticsInput`의 레시피 원료를 소비합니다. 벨트나 투입기가 물자를 도시 필지에 놓을 때 `CityLogistics.TryGive`가 레시피 입력인지 확인하고 해당 버퍼에 정수 한 개를 추가합니다.

입력과 출력 자동화는 독립적으로 판정됩니다. 한쪽만 물리 물류로 바꾸고 다른 쪽은 공유 재고 경로로 남길 수 있습니다.

## `IFactoryEnvironment`와 도시 접점

[CityLogistics.cs](../Assets/Scripts/Core/CityLogistics.cs)는 Unity 참조 없는 `IFactoryEnvironment` 구현입니다. 공개 계약은 다음과 같습니다.

```text
CanPlace(FactoryKind, int x, int z, int direction, out string reason)
HasOre(int x, int z)
TryTake(int x, int z, Resource filter, out Resource item)
TryGive(int x, int z, Resource item)
CanSupplyPower(FactoryEntity)
CanExport(FactoryEntity)
```

복잡 산업의 지형 질의는 별도 `IIndustryEnvironment`의 `HasWater`와 `HasDeposit`으로 분리됩니다. 기존 `IFactoryEnvironment` 호출자는 그대로 유지되며 실제 게임에서는 `CityLogistics`가 두 인터페이스를 모두 구현합니다.

도시 인계 규칙은 다음과 같습니다.

| 대상 | `TryTake` | `TryGive` |
|---|---|---|
| 자동화된 도시 생산 시설 | `LogisticsOutput`에서 일치 자원 1개 | 레시피 입력이면 `LogisticsInput`에 1개 |
| 연결된 시청 | 도시 공유 재고에서 운반 가능한 고체 1개 | 운반 가능한 고체를 도시 공유 재고에 1개 |
| 연결된 도시 창고 | 도시 공유 재고에서 운반 가능한 고체 1개 | 운반 가능한 고체를 도시 공유 재고에 1개 |
| 연결된 시장 | 지원하지 않음 | 빵 또는 도구 1개 |

`TryGive`가 시청·도시 창고·시장에 성공하면 `FactoryState.Exported`도 증가합니다. 연결되지 않은 접점은 인계를 거부합니다.

`CanExport`는 반출 부두가 연결된 도시 도로나 시청에 닿을 때만 참입니다. `FactorySimulation.TakeExports`는 이 조건을 만족하는 부두에서만 고체를 꺼내 `GameState.Stock`에 더하며 유체는 아이템 부두로 반출하지 않습니다. `CanSupplyPower`도 전력 인입구가 연결 도로나 시청에 닿을 때만 참입니다.

도시 도로·건물·영토가 바뀌면 `GameController.RefreshWorld`가 `FactoryController.NotifyCityChanged()`를 호출합니다. 이 메서드는 `FactorySimulation.InvalidateEnvironment()`로 전력과 접점 캐시를 무효화하고 뷰와 HUD를 갱신합니다.

## 물리 설비 Core

- [FactoryCatalog.cs](../Assets/Scripts/Core/FactoryCatalog.cs)는 설비 23종과 제조법 37종의 크기·비용·전력·기술 조건, 호환 설비와 다중 입출력을 정의합니다.
- [FactorySimulation.cs](../Assets/Scripts/Core/FactorySimulation.cs)는 점유, 배치, 회전, 제조법, 필터, 정수 버퍼, 전력망, 고체 이동, 다중 입출력 생산과 상태 검증을 처리합니다.
- [FactoryFluidSimulation.cs](../Assets/Scripts/Core/FactoryFluidSimulation.cs)와 [FluidPorts.cs](../Assets/Scripts/Core/FluidPorts.cs)는 방향성 리터 단위 유체 이동과 회전된 기계 포트를 처리합니다.
- [FactoryState.cs](../Assets/Scripts/Core/FactoryState.cs)는 Unity와 독립된 직렬화 DTO를 제공합니다.

v0.8의 21종에 아이템 리프트와 유체 라이저를 더한 23종입니다. 두 층간 설비는 일반 배치가 아니라 같은 X/Z의 인접 층에 송신·수신 끝점 두 개를 함께 만드는 `TryPlaceLink`를 사용합니다. 두 끝점은 하나의 전역 ID 공간에서 서로를 참조하며 한쪽 철거 시 양쪽 재고·화물·설치 컨트롤러와 참조 규칙을 원자적으로 회수·삭제합니다.

### 물자 보존

벨트와 분배기는 `CargoResource` 하나를 보유합니다. 출구가 막히면 `CargoProgress = 1`에서 기다립니다. 투입기도 뒤에서 하나를 꺼낸 뒤 앞이 받을 때까지 같은 화물을 보유합니다. 서로 마주 보는 벨트는 같은 화물을 왕복시키지 않습니다.

아이템 리프트 송신기는 같은 층의 고체를 받고 초당 2개 속도로 연결 수신기에 넘깁니다. 수신기는 수직 입력만 받고 앞 방향 벨트·설비로 배출되며, 틱 시작에 이미 들고 있던 화물만 이동하므로 새로 받은 물자가 같은 틱에 다시 전진하지 않습니다.

생산 설비는 제조법의 모든 입력과 모든 출력 공간을 확인한 뒤에만 한 배치를 원자적으로 변환합니다. 부산물 공간 하나라도 부족하면 입력을 소비하거나 일부 결과만 만들지 않고 진행 상태를 보존한 채 대기합니다. 기존 생산 설비의 입력·출력 용량은 24, 새 복잡 산업 생산 설비는 80, 설비 창고와 부두는 80입니다.

`FactoryController.PlaceAt`의 철거 경로는 `FactorySimulation.Remove`가 모은 `Recovered`를 도시 재고로 옮기고, 설비 기본 건설비의 코인·목재·석재를 각각 35% 환급합니다. 제조법 변경은 선택 설비의 입력·출력을 먼저 `Recovered`로 옮겨 도시 재고에 반환하고 `Progress`를 0으로 만든 뒤 새 제조법을 적용합니다.

### 전력

지상 전력 인입구는 `CityLogistics.CanSupplyPower`를 만족할 때만 전력망의 루트가 됩니다. 전신주는 같은 층에서 `FactorySimulation.GridDistance` 6 이내로 연결되고 동력 설비는 활성 노드에서 거리 4 이내일 때 전력을 받습니다. 서로 다른 층은 정확히 같은 X/Z에 있는 인접 층 전신주 쌍만 연결되며 그 밖의 층간 거리는 무한으로 취급합니다.

생산 설비는 `Paused`와 `ClockPercent`를 저장합니다. 클록은 50·100·150·200%만 허용하고 150·200%는 `AdvancedManufacturing` 연구가 필요합니다. 생산 속도는 `clock / 100`, 전력 수요는 `(clock / 100)^2`에 비례합니다. 일시정지한 설비는 진행률을 잃지 않으며, 정지한 인입구·전신주는 전력망을 중계하지 않습니다.

### 유체

유체 7종은 벨트·투입기·설비 창고가 운반하지 않습니다. 파이프와 분기점은 40L, 탱크는 240L의 `Input`에 한 종류만 저장하고 방향에 따라 출력합니다. 생산 기계는 회전 방향을 기준으로 뒤쪽 입력 포트 둘과 앞쪽 출력 포트 둘을 가지며 제조법의 유체 입출력 순서와 연결됩니다.

`FactoryFluidSimulation.Tick`은 층을 포함한 포트 키, 틱 시작 스냅샷과 목적지 용량 예약을 사용하므로 한 틱에 여러 구간을 건너뛰거나 순회 순서에 따라 복제되지 않습니다. 파이프·분기점·기계 출력과 유체 라이저는 초당 8L, 탱크는 초당 12L이며 소수 이송 크레딧을 `FluidProgress`에 보존합니다. 라이저 송신기는 같은 층 입력만 받고 연결된 인접 층 수신기로만 보내며 수신기는 그 층의 앞 방향으로만 배출합니다. 정지한 배관·라이저는 흐름을 막고 다른 유체 혼합을 거부합니다. 중력·압력·양정 모델은 포함하지 않습니다.

### 조건 자동화

[FactoryAutomation.cs](../Assets/Scripts/Core/FactoryAutomation.cs)는 공장 틱 시작에 모든 규칙을 같은 재고 스냅샷으로 평가합니다. 원본 ID 0은 도시 재고, 양수 ID는 원본 설비의 입력·출력과 일치 화물 하나를 측정합니다. 비교는 `AtLeast`·`AtMost`, 동작은 `AllowWhenTrue`·`StopWhenTrue`만 허용하며 코드나 임의 수식은 실행하지 않습니다.

대상에 연결된 모든 활성 규칙 중 하나라도 차단하면 `AutomationBlocked`가 참입니다. 따라서 허용 규칙은 모두 참이어야 하고 중지 규칙은 하나라도 참이면 멈추는 AND 허용·OR 차단 의미입니다. 원본이 사라지거나 측정에 실패하면 안전하게 차단합니다. 대상당 최대 4개, 공장 전체 최대 64개이고 임계값은 0~1,000,000입니다.

첫 규칙 저장은 `IndustrialControl` 연구와 도시 재고의 제어 장치 1개를 요구하고 대상의 `ControllerInstalled`를 켭니다. 이후 규칙 수정·삭제는 재청구·환급하지 않으며 설비 철거 때 제어 장치 1개를 회수합니다. 수동 `Paused`는 규칙이 바꾸지 않는 영속 명령이고 `AutomationBlocked`는 저장하지 않은 채 매 틱 지우고 재계산합니다. 생산·고체·유체·반출·전력은 모두 `IsStopped` 경계를 사용합니다.

`FactoryController.Configure`는 도시 생산 시설이 사용하고 남은 `Simulation.PowerCapacity - Simulation.PowerUsed`를 `FactoryState.PowerBudget`에 반영합니다. `ConfigureTechnology(GameState)`는 선택 연구를 직렬화하지 않은 런타임 배율로 계산합니다.

| 기술 | 런타임 배율 |
|---|---|
| 물류 | `BeltSpeedMultiplier = 1.5` |
| 자동화 | `InserterSpeedMultiplier = 1.5` |
| 대량 생산 | `MachineSpeedMultiplier = 1.25` |
| 전기화 | `PowerDemandMultiplier = 0.85` |

## Runtime과 UI

### `GameController`

[GameController.cs](../Assets/Scripts/Runtime/GameController.cs)는 도시와 설비 입력, 선택, 속도, 모달, 자동 저장과 공용 카메라의 수명 주기를 관리합니다.

- `SelectedFactory`는 `Factory.SelectedEntity`를 노출합니다.
- `FactoryToolActive`는 설비 배치 도구 또는 설비 철거 모드가 활성인지 나타냅니다.
- `Feel`, `Tutorial`, `ResidentAi`는 각각 피드백, 안내, 선택형 주민 계획 런타임의 단일 소유자를 노출합니다.
- `OpenFactory()`는 `FactoryController.IsOpen`만 토글합니다. 도시 보드·주민·HUD·카메라를 숨기지 않습니다.
- 도시 건설 도구를 선택하면 설비 도구를 닫고, 설비 도구를 선택하면 `ClearCityToolForFactory()`로 도시 도구를 해제합니다.
- 레이어 8 필지를 맞힌 포인터 위치는 설비 도구가 활성일 때 미세 좌표 배치로, 도구가 없을 때 설비 선택 또는 도시 필지 선택으로 전달됩니다.

### `FactoryController`

[FactoryController.cs](../Assets/Scripts/Runtime/FactoryController.cs)는 항상 `GameState.Factory` 하나에 `CityLogistics`를 결합합니다. `IsOpen`은 설비 팔레트가 활성이라는 뜻이며 뷰나 시뮬레이션의 수명을 뜻하지 않습니다.

`OpenPractice`, `SelectBlueprint`, `IsPractice`, `PracticeBlueprintIndex`는 이전 호출자와의 소스 호환을 위해 남은 비활성 호환 표면입니다. 연습 상태를 만들거나 전환하지 않으며 런타임 UI에서도 호출하지 않습니다.

`Feed(Resource, int amount = 10)`는 고체를 설비 창고·반입 부두에, 유체를 탱크에만 넣습니다. `ChooseRecipe`는 카탈로그가 허용한 생산 설비·제조법 조합과 기술 조건을 검사하고, `ChooseFilter`는 투입기의 고체 또는 파이프·분기점·탱크의 유체 필터를 설정합니다. `SetPaused`와 `SetClock`은 선택 설비의 저장된 운전 상태를 바꿉니다.

`ActiveFloor`는 0~2의 현재 선택·배치 층이고 `SetFloor`가 선택과 미리보기를 해당 층으로 옮깁니다. `FoundationMode`는 상층 플랫폼을, `LinkTargetFloor`는 아이템 리프트·유체 라이저의 인접 연결층을 선택합니다. 링크는 끝점 두 개의 전체 비용과 두 위치를 먼저 검증한 뒤 함께 배치합니다. `SaveAutomationRule`·`RemoveAutomationRule`과 `GameController`의 프로젝트 메서드가 UI 변경을 코어 API로 전달하며 UI는 DTO를 직접 수정하지 않습니다.

### `FactoryView`

[FactoryView.cs](../Assets/Scripts/Presentation/FactoryView.cs)는 `BoardView` 아래에서 설비 모델, 실제 고체 화물, 유체 부피·흐름, 전선, 매장지 표식, 배치 미리보기와 선택 윤곽만 렌더링합니다. 공장 바닥, 조명이나 별도 카메라를 생성하지 않습니다. 새 산업 설비 모델은 주민 모델·작업·경로와 분리된 표현 계층입니다.

`FactoryView.FloorHeight = 1.65f`가 층별 월드 높이를 정합니다. 플랫폼은 2×2 상판과 아래층까지의 지지 기둥을 만들고, 활성 층과 아래층은 보이며 위층은 숨깁니다. 아이템 리프트는 수직 화물, 유체 라이저는 유체 종류와 흐름을 표시하고 전선·포트·선택 윤곽·피킹도 `Floor`를 포함합니다.

`Camera`는 `Game.CameraRig.Camera`를 반환하고 `PanScreen`, `Zoom`, `HomeCamera`도 같은 `OrbitCamera`에 위임합니다. 상층 배치·선택은 현재 층의 논리 평면을 사용하며 기존 441개 지상 타일 피킹은 유지합니다.

### `Hud`

[Hud.cs](../Assets/Scripts/UI/Hud.cs)는 도시와 설비의 유일한 활성 HUD입니다. 하단 분류는 `주거`, `생산`, `산업`, `도시`, `설비`, `물류`이며 기존 `Button_Factory`의 표시 문자열은 `산업 설비 G`입니다.

`설비`에는 추출·제련·가공·정유·화학·제조·전력 설비가, `물류`에는 벨트·투입기·분배기·부두와 파이프·분기점·탱크·아이템 리프트·유체 라이저가 나타납니다. 접을 수 있는 공장 막대에서 지상·2층·3층, 플랫폼과 링크 대상 층을 선택합니다. `R`과 화면의 회전 버튼은 현재 층 설비 도구 방향을 바꾸며, 선택 설비는 층·링크·수동 정지·조건 대기 상태와 함께 공용 검사기에 표시됩니다.

전체 HUD와 튜토리얼 대화는 `GameFont.Load()`를 통해 번들된 [Galmuri11](PIXEL_FONT.md)을 먼저 사용합니다. `GameFont`는 동적 아틀라스에 point filtering을 적용하고 아틀라스 재생성 뒤에도 다시 적용합니다. Galmuri11을 불러오지 못했을 때만 데스크톱 시스템 폰트, 번들 Nanum Gothic, Unity 기본 폰트 순서로 대체합니다.

검사기의 `설비 구성` 모달은 다음 항목을 카탈로그에서 동적으로 제공합니다.

- 선택 설비와 호환되는 37개 제조법, 기술 잠금 사유, 입력·출력과 기본 분당 생산량
- 투입기 고체 필터와 파이프·분기점·탱크 유체 필터
- 설비 창고·반입 부두의 고체 또는 유체 탱크에 도시 재고 10단위 투입
- 일시정지, 50·100·150·200% 클록과 내부 재고 회수
- 조건 규칙 편집 패널
- 38개 자원·37개 제조법·현재 생산 현황을 검색하는 `IndustryPanel` 도감
- 닫기

메뉴의 `CityProjectPanel`은 프로젝트 부지 선택·납품·취소·상태를, `AutomationRulePanel`은 선택 설비의 원본·자원·비교·동작·임계값과 최대 4개 규칙을 편집합니다. 모달 상태는 `GameController.ModalOpen`과 HUD의 로컬 상태를 함께 사용해 서로 겹치지 않게 합니다. 사용하지 않던 독립 공장 HUD는 삭제된 상태를 유지합니다.

### 모바일 입력

[MobileInput.cs](../Assets/Scripts/Runtime/MobileInput.cs)는 한 손가락 탭을 `GameController.InteractScreenPoint`에 전달합니다. 배치·철거 도구가 없을 때 한 손가락 드래그로 공용 카메라를 이동하고, 두 손가락은 도구 상태와 관계없이 이동·핀치를 처리합니다. 시스템 뒤로 가기는 도구, 설비 팔레트와 모달을 단계적으로 닫습니다. 모바일용 별도 공장 카메라 경로는 없습니다.

## Feel 5.6.1과 카메라 연출

제공된 Feel 5.6.1의 실제 `MMF_Player`가 런타임에 포함됩니다. 피드백은 표현 계층에만 있으며 코어 상태와 필지 선택 콜라이더를 바꾸지 않습니다.

- [FeelDirector.cs](../Assets/Scripts/Runtime/FeelDirector.cs)는 건설·철거·증축·영토·교역·저장·연구·시대·목표·설비 이벤트를 `FeelCue`로 정규화하고 중복 상태 전이를 막습니다.
- [WorldFeelEffects.cs](../Assets/Scripts/Presentation/WorldFeelEffects.cs)는 고정 풀의 `MMF_Player`로 대상 위치·크기, 색·투명도, 파티클과 카메라 오프셋을 재생하고 종료 시 기준 변환을 복원합니다.
- [FeelUiFeedback.cs](../Assets/Scripts/UI/FeelUiFeedback.cs)는 버튼 누름·놓기와 패널 열림을 unscaled time으로 재생하므로 모달로 도시가 멈춰도 UI 피드백은 끝까지 진행됩니다.
- [CameraCinematics.cs](../Assets/Scripts/Runtime/CameraCinematics.cs)는 `OrbitCamera`가 권위 있는 실제 카메라로 남도록 보이지 않는 pose를 Feel로 움직입니다. 시청 근접 줌, 교육 대상 초점, 단우 추적과 주요 이벤트의 짧은 줌 반응을 담당합니다.
- [TownHallIntro.cs](../Assets/Scripts/Runtime/TownHallIntro.cs)는 새 도시에서 실제 시청 모델을 위에서 떨어뜨리고 반동·충격 피드백과 적극적인 카메라 줌을 재생한 뒤 모델과 카메라를 기준 상태로 복원합니다.

마우스·휠·터치로 카메라를 직접 조작하면 진행 중인 자동 이동이나 줌을 취소하고 그 순간 표시된 구도를 `OrbitCamera`에 반영합니다. 이후 약 3초 동안 낮은 우선순위 카메라 반응을 억제합니다. `FeelDirector.ReducedMotion`은 큰 대상 변형, 카메라 흔들림·자동 이동을 끄고 색·투명도 중심 피드백을 유지합니다. 자세한 사용자 동작은 [Feel 연출 안내](FEEL_GUIDE.ko.md)에 있습니다.

## 단우 튜토리얼과 기능 안내

[TutorialDirector.cs](../Assets/Scripts/Runtime/TutorialDirector.cs)는 저장된 `TutorialProgress`, 코어의 `TutorialCatalog`·`GuidanceLessonCatalog`와 런타임 UI를 결합합니다. 새 도시는 시청 도착이 실제로 끝난 뒤 단우의 첫 대화를 시작합니다.

기본 안내는 환영부터 시청 선택, 도로·주택·벌목장·서재 건설, 윤작 연구 시작·완료, 기계 동력과 생산망 설명, 명시적 저장, 완료까지 11단계입니다. 단계는 플레이어의 실제 선택·연결된 건설·연구 완료·저장 성공을 검사하며 시작 배치를 새로 지은 것으로 세지 않습니다. 그 뒤 `GuidanceLessonCatalog.Count = 19`인 현재 기능 안내가 기술과 인구 상태에 맞춰 하나씩 나타납니다. 목록에서는 잠긴 기능도 설명만 미리 읽을 수 있습니다.

단우는 새 인구나 별도 프리팹이 아닙니다. `CitizenSimulation.EnsureGuide()`가 기존 주민 ID 1에 이름과 안내자 표시를 부여하고, `GuideToNear`가 목표 필지 옆의 도달 가능한 도로를 찾아 일반 주민과 같은 4방향 도로 경로로 이동시킵니다. 안내 중에는 unscaled time으로 걷고 `CameraCinematics.FollowTarget`이 실제 3D 변환을 따라갑니다. 플레이어가 카메라를 조작하면 추적이 해제되며 `단우 보기`가 명시적으로 다시 연결합니다. `TutorialDialogue`는 초상화를 만들지 않습니다.

`TutorialAdvisor`의 주제별 조언은 네트워크를 사용하지 않는 결정적 로컬 문구입니다. 아래의 주민 LLM 계획과 별개이며, 안내자가 튜토리얼 제어 중일 때는 주민 AI 배치 대상에서 단우를 제외합니다. 단계, 완료·건너뛰기, 시청 인트로 재생 여부와 19비트 읽음 마스크는 도시 저장에 남지만 현재 페이지의 타자 위치와 단우의 일시적인 안내 이동은 저장하지 않습니다. 자세한 흐름은 [단우 안내서](TUTORIAL_GUIDE.ko.md)에 있습니다.

## 선택형 주민 AI

주민 AI 경로는 Unity 클라이언트, 순수 주민 적용 경계, 로컬 Node.js 게이트웨이의 세 부분으로 나뉩니다.

### Unity 클라이언트와 적용 경계

[ResidentAiClient.cs](../Assets/Scripts/Runtime/ResidentAiClient.cs)는 최대 16명의 주민 사실과 도시 요약을 `protocol`, 세션·요청 ID, 세대 번호와 함께 게이트웨이에 보냅니다. 기본 요청 간격은 30초 이상이며 응답을 기다리는 동안 기존 일과를 계속합니다. 게이트웨이 URL만 PlayerPrefs에 저장하고 선택적 접근 토큰은 메모리에만 둡니다. OpenRouter 제공자 키는 Unity 프로세스, APK, URL과 로그에 들어가지 않습니다.

응답은 JSON 구조, 요청 ID·세대, 주민 ID 중복, 응답 크기와 각 필드를 먼저 검증합니다. [CitizenSimulation.cs](../Assets/Scripts/Core/CitizenSimulation.cs)의 `TryApplyDecision`은 주민별 허용 목적지와 현재 도로 도달성을 다시 검사한 뒤 집·직장·시장·공원·시청 방문 계획을 활성화합니다. 도시 교체, 연결 해제, 만료, 출발점 변경이나 경로 무효화 시 계획을 폐기하고 로컬 일과로 돌아갑니다. 생각·기분·최근 기억·모델 출처는 세션 표현 상태이며 `GameState`에는 직렬화되지 않습니다.

### Node.js 게이트웨이

`Server/`는 Node.js 22 이상에서 실행되는 OpenRouter 전용 게이트웨이입니다.

```text
Unity ResidentAiClient
  ├─ GET  /health
  └─ POST /v1/citizens/plan
          ├─ 엄격한 요청 검증과 idempotency/generation fencing
          ├─ 클라이언트별 round-robin, 전체 동시 제공자 호출 1개
          ├─ 분당 최대 2회, 시간당 $0.50, 일일 $2 상한
          ├─ OpenRouter structured JSON 요청
          └─ 사용량·비용과 검증된 주민 계획 응답
```

기본 바인딩은 `127.0.0.1:47841`입니다. 루프백 밖에 바인딩하면 길이가 검증된 Bearer 토큰이 필수이고, 외부 클라이언트 주소는 HTTPS만 허용합니다. `OPENROUTER_API_KEY`는 배포되지 않는 `Server/.env`에서만 읽습니다. 현재 가격 검증 카탈로그는 `google/gemini-2.5-flash-lite` 하나이며, 알 수 없는 모델은 서버 시작 단계에서 거부합니다. 비용 장부를 읽거나 쓸 수 없을 때도 제공자 호출을 막습니다. 운영과 보안 경계는 [주민 AI 연결 안내](RESIDENT_AI.ko.md)에 있습니다.

구현과 모의 계약 검사는 완료됐지만 실제 제공자 성공은 별도 상태입니다. 최신 주민 AI 런타임 결과는 `MODE MOCK_CONTRACT`, `actualProviderCall=false`이며 로컬 계약 서버를 상대로 UnityWebRequest, 응답 검증, 실제 주민 도로 이동과 실패 차단을 확인했습니다. 유효한 키가 없어 `LIVE_OPENROUTER` 실제 호출은 아직 검증하지 않았습니다.

## 주민 경로와 경제 경계

주민은 집에서 직장으로, 직장에서 시장 또는 공원으로, 다시 집으로 이동하고 각 목적지에서 실내 대기합니다. 경로 탐색은 시청과 4방향 연속 도로만 사용합니다. 물 필지는 도로 교량일 때만 통과하며 건물은 경로의 시작·끝점입니다.

`CitizenSimulation`은 모든 주민의 일과와 계획을 진행하며, 표시 예산 때문에 주민을 실내로 바꾸지 않습니다. 거리 모델 표시는 PC 128명·모바일 64명, 별도의 작업·여가 표시는 PC 32명·모바일 16명으로 제한합니다. `VisibleCount`는 거리 주민 수를 유지하고 `ActiveWorksiteCount`가 건물에서 활동하는 주민을 셉니다.

`CitizenWorksites`는 `Indoors`, `Activity`, `CurrentIndex`와 실제 할당을 읽어 회전된 건물의 마당에 최대 두 명을 표시합니다. `BoardView.TryGetBuildingTransform`으로 건물 교체 뒤 새 기준 위치를 받습니다. `ResidentActor`는 Quaternius 모델, 수동 Playables 그래프, 작업 도구와 활동 글자를 관리합니다. 화면 밖 모션 평가는 10Hz이며 일시정지 시 관절과 재생 시간이 멈춥니다. 모델 교체와 시대 변화에서도 주민 루트는 유지합니다. 사용하지 않는 모델 캐시도 표시 예산에 맞춰 정리합니다.

원본 모델은 Generic으로 가져오고 생성 과정에서 발의 계층을 교정해 별도 Humanoid Avatar를 만듭니다. 재질별 정점 색을 하나의 스킨드 메시로 합치고, 실제 정점 범위에서 가져오기 배율을 보정해 런타임 높이를 맞춥니다. 애니메이션은 UAL1/UAL2의 정확한 클립 이름으로 연결하며, 독서·반죽 등은 기본 대기 위에 보조 동작을 적용합니다. 자세한 출처와 동작 구분은 [주민 시각 표현](RESIDENT_VISUALS.ko.md)에 있습니다.

주민의 작업 소품은 생산·물류 재고와 분리된 표시입니다. 도시 생산·식량·세금은 `Simulation`이 한 번만 계산하며 실제 설비 이동은 `FactorySimulation`과 `CityLogistics` 경계에서만 재고를 바꿉니다.

## 날짜 틱

1. 도로 연결과 연구·전력 미리보기 계산
2. 날짜 증가, 연구 단지 보너스를 포함한 연구 점수와 진행 중 연구의 남은 날짜 처리
3. 연결된 도시 프로젝트의 공사일 감소, 단계 전환과 대교 실제 도로 완공
4. 중앙 발전소의 `FuelDay`를 확인해 그날 석탄 2개를 한 번만 소비하고 전력 80 반영
5. 풍차 동력과 목재 연료를 쓰는 증기 동력 계산
6. 각 생산 시설의 자동화 입력·출력 경계, 원료·동력·버퍼 공간 판정과 생산
7. 시장의 저재고 빵 수입과 주택 식량 처리
8. 선택적 생활 도구, 행복, 세금·유지비와 목표 보상 판정

설비의 0.1초 틱은 먼저 조건 자동화를 평가한 뒤 층별 전력, 생산, 고체·유체와 수직 링크를 갱신합니다. 날짜 틱에서 생긴 도시 출력 버퍼의 정수 물자는 이후 투입기 틱에서 꺼낼 수 있습니다.

## v6 저장과 마이그레이션

파일명은 호환성을 위해 `city-v1.json`으로 유지하지만 현재 `GameState.Version`은 6이고 활성·보관 `FactoryState.Version`은 3입니다.

```text
%USERPROFILE%\AppData\LocalLow\Riverworks Studio\Riverworks\city-v1.json
```

`SaveStore.TryLoad`는 v1~v4 데이터를 변경하기 전에 이전 형식 그대로 검증합니다. 도시 재고와 각 도시 물류 버퍼는 정확히 9칸이어야 하고, 옛 공장은 공장 v1의 설비·제조법·자원 ID 범위와 9칸 배열을 만족해야 합니다. 이 경로는 먼저 동결된 도시 v5·공장 v2를 만든 뒤 그 중간 결과를 다시 검증합니다.

1. v1은 기존 호환 규칙에 따라 원래 18개 기술과 산업 시대를 부여해 v2가 됩니다. v0.8의 새 기술 8개를 자동 부여하지 않습니다.
2. v2는 이전 단계의 빈 24×16 공장 상태를 추가해 v3가 됩니다.
3. `CityLogistics.Migrate`가 v3의 `Factory` 참조를 그대로 `ArchivedFactory`에 보관합니다.
4. 새 활성 `Factory`는 `CreateCityGrid()`의 빈 42×42 상태가 됩니다.
5. 옛 `Recovered`, 각 설비의 `Input`, `Output`, 운반 중 `CargoResource`를 도시 재고로 한 번 반환합니다.
6. 옛 설비 각각의 전체 `CoinCost`, `TimberCost`, `StoneCost`를 한 번 환급합니다.
7. 옛 `Produced`, `Exported`, `ElapsedSeconds`는 새 활성 상태에 이어집니다.
8. 도시 v4를 만든 뒤 `IndustryMigration`이 도시 재고, 441개 도시 입출력 버퍼, 활성·보관 공장의 집계와 각 설비 입출력을 38칸으로 확장합니다.
9. 새 29개 자원 수량은 0으로 추가하고 기존 값·화물·진행률·소수 버퍼를 보존합니다. 옛 설비에는 `Paused = false`, `ClockPercent = 100`, 유체 진행·분기 기본값을 적용합니다.
10. 공장을 v2, 도시를 v5로 올리고 `ValidateVersion5`로 정확한 38칸 배열, 설비 ID≤21, 확장 필드 부재와 기존 플래그를 검사합니다. 디스크에서 직접 읽은 v5도 같은 검사를 먼저 통과해야 합니다.
11. `ExpansionMigration`이 활성·보관 공장을 v3로 올리며 기존 설비를 `Floor=0`, 링크 없음, 컨트롤러 없음으로 두고 플랫폼·규칙·프로젝트를 빈 상태로 추가합니다. 기존 수동 `Paused`와 모든 v0.8 값은 보존합니다.
12. 비영속 `AutomationBlocked`를 false로 두고 공장 v3, 도시 v6 버전을 마지막에 설정합니다. 마이그레이션은 제어 장치나 프로젝트 보상을 자동 지급하지 않습니다.

마이그레이션은 디스크 원본을 직접 고치기 전에 각 버전의 메모리 후보를 검증합니다. `ArchivedFactory`는 저장 검증 대상이지만 생산·렌더링·자동 반출 대상이 아닙니다. v3→v4의 원본 보관과 일회 환급은 반복하지 않으며 v4→v5→v6은 기존 활성 공장 배치를 비우거나 보관 공장을 버리지 않습니다.

`SaveStore.Validate`는 현재 v6의 모든 배열이 정확히 38칸인지, 설비의 제조법·연구·층·플랫폼 지지·상호 링크·컨트롤러와 규칙, 도시 프로젝트의 단계·납품·부지·날짜·`FuelDay`가 유효한지 확인합니다. 현재 형식의 잘못된 길이, 고ID, 중복·범위 밖 상태는 자동으로 자르거나 보정하지 않고 거부합니다.

정상 저장은 임시 파일을 같은 디렉터리에 쓰고 `File.Replace`로 교체하며 이전 정식 파일을 `.bak`으로 남깁니다. 동일한 정규 JSON이면 불필요한 교체와 백업 회전을 생략합니다. 새 도시는 현재 상태의 `.previous-city.json`과 새 정식 저장이 모두 성공한 뒤 메모리 상태를 바꿉니다.

## 검증 경계

순수 C# 검사는 다음 명령으로 실행합니다.

```powershell
dotnet run --project .\Tests\Riverworks.Tests.csproj -c Release
```

`IndustryCatalogChecks`, `IndustryChainChecks`, `IndustryFluidChecks`, `IndustrySaveChecks`는 현재 38개 자원, 37개 제조법, 23개 설비, 26개 기술·35개 간선, 다중 입출력 원자성, 부산물 역압, 클록 전력식, 유체 방향·분기·스냅샷 이송과 v1~v4→v5 저장 경계를 대상으로 합니다.

`FactoryLayerChecks`, `LayerFluidChecks`, `FactoryAutomationChecks`, `CityProjectChecks`, `ExpansionSaveChecks`, `ExpansionFlowChecks`와 `ExpansionCatalogChecks`는 층별 분리·플랫폼 지지, 아이템/유체 층간 보존, 층별 전력, 규칙 AND 허용·OR 차단과 수동 정지 우선, 컨트롤러 일회 비용, 프로젝트 납품·공사·연료·도로/연구 보상, 동결 v5→v6 저장 경계를 대상으로 합니다.

코어 풀체인은 1차 원료만 있는 테스트 장부에서 제조법별 `FactorySimulation` 배치를 실행하고 모든 산출물과 부산물을 다음 단계 장부로 옮겨 제어 장치까지 도달시키는 구조입니다. 이는 카탈로그 도달성과 재고 보존을 검사하는 단계 이송 경계이며 하나의 네이티브 도시에서 전 공정을 연결한 증거가 아닙니다.

`IndustrySmokeTest`의 네이티브 대상은 석유화학, 알루미늄 회수, 제어 장치 조립의 세 예제입니다. 제어 장치 예제는 컴퓨터·모터·배터리·모듈 프레임·알루미늄 케이싱이 준비된 창고에서 시작해 제조기와 반출까지 확인하도록 구성되어 있으므로 원료부터 시작하는 네이티브 풀체인이 아닙니다.

`ExpansionSmokeTest`는 실제 3층 플랫폼·아이템 리프트·유체 라이저, 조건 규칙 UI와 동작, 도시 프로젝트 하나의 실제 날짜 진행·완공을 네이티브 대상으로 구성합니다. v0.8을 포함한 v0.9 Windows 네이티브 검사 9종과 주민 AI `MOCK_CONTRACT` 1종, 총 10종을 같은 빌드 GUID에서 완료했으며, 전체 수치와 산출물은 [검증 기록](VERIFICATION.md)을 따릅니다.

이전에 확정된 v0.7.0 기준선 결과는 다음과 같습니다.

| 검증 계층 | 결과 |
|---|---:|
| 순수 C# 코어 | 715개 PASS |
| `RuntimeSmokeTest` 도시 | 155개 PASS |
| `SharedCitySmokeTest` | 106개 PASS |
| `CompactHudSmokeTest` | 1,087개 PASS |
| ResearchTreeSmokeTest | 1,081개 PASS |
| TutorialSmokeTest | 2,465개 PASS |
| `FeelSmokeTest` | 180개 PASS |
| `ResidentAiSmokeTest` | 160개 PASS, `MOCK_CONTRACT`, 실제 제공자 호출 없음 |

이 표의 Windows 런타임 결과는 v0.7 빌드 GUID `36c52483354e4e2f90acc0c0422bcb64`에서 종료된 과거 기준선입니다. v0.8·v0.9 통합 결과는 최신 [검증 기록](VERIFICATION.md)을 따릅니다.

v0.9의 코어·저장·Node 검사와 Windows 네이티브 실행·빌드는 완료했습니다. Android 실제 기기와 외부 제공자 `LIVE_OPENROUTER`, GitHub 공개 게시는 별도이며 최신 [검증 기록](VERIFICATION.md)의 실제 산출물 보고를 기준으로 상태를 판단해야 합니다.

## 현재 범위

주민은 개별 경제 주체가 아니며 LLM 계획도 방문 목적과 짧은 생각·기분·세션 기억만 제안합니다. v0.8·v0.9의 새 장비와 층간 구조물은 산업 표현이며 주민 모델·작업·경로를 바꾸지 않습니다. 공장은 최대 3개 층이고 플랫폼 중력 붕괴, 임의 높이, 지하 벨트는 없습니다. 조건 자동화는 정해진 재고 비교와 허용·중지 동작만 제공하며 스크립트·임의 식·재고 직접 변경을 실행하지 않습니다. 교통 혼잡, 주민별 자산·거래, 장기 AI 기억, 차량별 화물, 유체 압력·중력, 기차·전투, 멀티플레이와 모딩은 포함하지 않습니다. 예전 24×16 공장 보관본은 기록과 가치 보존을 위한 마이그레이션 자료이며 플레이 공간이 아닙니다.


## v0.7 연결형 연구 지도 (이전 버전)

ResearchGraph는 카탈로그의 18개 기술과 19개 선행 관계를 검증하고 조상·후속·잔여 연구 계획을 계산합니다. ResearchTreeLayout은 위상 깊이에 맞는 고정 노드와 카드 사이 통로의 연결선을 배치합니다. ResearchTreeView와 ResearchConnectionGraphic은 지도·선택·검색·이동·정수 확대를, ResearchDetailPanel은 실제 효과와 비용·해금·선행 경로를 보여 줍니다. 게임 시뮬레이션과 v4 저장 필드는 그대로입니다.
