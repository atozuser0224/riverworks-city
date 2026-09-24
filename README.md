# RIVERWORKS

정착지를 먹이며, 벨트와 파이프로 공장을 잇는 로우폴리 도시 게임입니다.

[지금 받기 · Windows v0.26.0](https://github.com/atozuser0224/riverworks-city/releases/download/v0.26.0/Riverworks-v0.26.0-Windows-x64.zip) · [소개 페이지](https://atozuser0224.github.io/riverworks-city/) · [릴리스 기록](https://github.com/atozuser0224/riverworks-city/releases/tag/v0.26.0)

![시작 마을. 서기 단우가 첫 길을 안내한다.](Docs/Images/riverworks-v0.6.png)

ZIP을 폴더째 풀고 `Riverworks.exe`를 실행합니다. Unity를 깔지 않아도 됩니다.

## 이 빌드에서 하는 일

- 야영지와 집, 채집으로 시작합니다. 주민은 도로를 걷고, 식량과 장작이 떨어지면 마을이 흔들립니다.
- 같은 지도 위에 벨트와 투입기, 파이프를 놓습니다. 고체와 유체를 나누고, 출구가 막히면 생산도 멈춥니다.
- 연구는 선행이 있는 트리입니다. 구리·석유·알루미늄을 부품으로 바꾸고, 층이 있는 공장과 조건 자동화까지 이어집니다.
- v0.26.0에는 원시 시대 시작, 연구 45종, 대륙 지도와 산업 자원 전쟁이 들어 있습니다.

![도로를 따라 늘어선 탱크와 3층 공장](Docs/Images/expansion-v0.9.png)

## 조작

`B` 건설, `G` 설비, `T` 연구, `WASD` 카메라, `Q` `E` 회전. 처음이면 [플레이 안내](Docs/PLAYER_GUIDE.ko.md)를 보세요.

## 소스에서 빌드

Unity **6000.5.5f1**. 유료 패키지 Feel은 저장소에 없고, 본인 라이선스로 [설치 안내](Docs/FEEL_SETUP.ko.md)를 따른 뒤 엽니다.

```powershell
dotnet run --project .\Tests\Riverworks.Tests.csproj -c Release
```

Windows 패키지를 만드는 순서는 [릴리스 절차](Docs/RELEASE_PROCESS.ko.md)에 있습니다. 공개된 v0.26.0은 그 절차로 검사한 빌드입니다.
