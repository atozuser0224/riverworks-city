using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class CitizenChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("시민 검증 실패: " + name); passed++; Console.WriteLine("✓ " + name); }

    public static int Run()
    {
        passed = 0;
        ConnectedCardinalPath(); DisconnectedAndBridgeRules(); PauseAndStableIdentity(); AssignmentsAndRoutine(); PopulationResizeAndTopologyReplan();
        Console.WriteLine($"시민 시뮬레이션 {passed}개 검증 통과"); return passed;
    }

    static void ConnectedCardinalPath()
    {
        var state = GameState.CreateNew(); state.Population = 4;
        int home = At(state,9,9), farm = At(state,9,8);
        var citizens = new CitizenSimulation(state); var path = citizens.FindPath(home,farm);
        True(path.Count >= 3, "주택과 일터가 실제 도로로 연결됨");
        True(path[0] == home && path[path.Count-1] == farm && citizens.IsValidPath(path), "경로의 건물 끝점과 직교 도로 유효성");
        True(path.Zip(path.Skip(1),(a,b) => Math.Abs(a%state.Size-b%state.Size)+Math.Abs(a/state.Size-b/state.Size)).All(d => d == 1), "대각선 없는 직교 보행");
    }

    static void DisconnectedAndBridgeRules()
    {
        var state = Blank();
        Place(state,0,10,BuildingKind.House); Place(state,5,10,BuildingKind.Farm);
        for (int x=1;x<=4;x++) Place(state,x,10,BuildingKind.Road);
        state.Cells[At(state,2,10)].Terrain=TerrainKind.Water; state.Cells[At(state,3,10)].Terrain=TerrainKind.Water;
        var citizens = new CitizenSimulation(state); var bridge = citizens.FindPath(At(state,0,10),At(state,5,10));
        True(citizens.IsValidPath(bridge), "도로가 놓인 물 타일은 다리로 통행");
        state.Cells[At(state,2,10)].Building=BuildingKind.None; citizens.Synchronize();
        True(citizens.FindPath(At(state,0,10),At(state,5,10)).Count==0, "다리 철거 시 강을 건서 이동하지 않음");
        True(!citizens.IsValidPath(new[]{At(state,1,10),At(state,2,10),At(state,3,10)}), "일반 물 타일 보행 경로 거부");
    }

    static void PauseAndStableIdentity()
    {
        var state=GameState.CreateNew(); state.Population=5; var citizens=new CitizenSimulation(state);
        var ids=citizens.Residents.Select(r=>r.Id).ToArray(); var before=citizens.Residents.Select(r=>(r.X,r.Z,r.Activity)).ToArray();
        citizens.Advance(0); True(before.SequenceEqual(citizens.Residents.Select(r=>(r.X,r.Z,r.Activity))), "정지 delta에서 위치와 루틴 불변");
        state.Population=8; citizens.Synchronize(); True(citizens.Residents.Take(5).Select(r=>r.Id).SequenceEqual(ids), "인구 증가에도 기존 주민 ID 유지");
        state.Population=3; citizens.Synchronize(); True(citizens.Residents.Select(r=>r.Id).SequenceEqual(ids.Take(3)), "인구 감소 시 남은 주민 ID 유지");
        state.Population=5; citizens.Synchronize(); True(citizens.Residents.Skip(3).All(r=>!ids.Contains(r.Id)), "재증가 주민은 새 ID 부여");
    }

    static void AssignmentsAndRoutine()
    {
        var state=GameState.CreateNew(); state.Population=6; Place(state,12,9,BuildingKind.Market);
        var citizens=new CitizenSimulation(state);
        True(citizens.Residents.All(r=>r.HomeIndex>=0), "전체 실제 인구에 주택 배정");
        True(citizens.Residents.All(r=>r.JobIndex>=0 && citizens.FindPath(r.HomeIndex,r.JobIndex).Count>0), "연결된 일자리 배정");
        True(citizens.Residents.All(r=>r.LeisureIndex>=0 && citizens.FindPath(r.HomeIndex,r.LeisureIndex).Count>0), "연결된 시장 또는 공원 배정");
        var seen=new HashSet<CitizenActivity>();
        bool correctDestination=true,arriveAtWork=true,arriveAtLeisure=true,arriveHome=true;
        for(int i=0;i<240;i++)
        {
            citizens.Advance(.25f);
            foreach(var r in citizens.Residents)
            {
                seen.Add(r.Activity);
                if(r.Activity==CitizenActivity.GoingToWork)correctDestination &= r.DestinationIndex==r.JobIndex;
                if(r.Activity==CitizenActivity.GoingToLeisure)correctDestination &= r.DestinationIndex==r.LeisureIndex;
                if(r.Activity==CitizenActivity.GoingHome)correctDestination &= r.DestinationIndex==r.HomeIndex;
                if(r.Activity==CitizenActivity.Working)arriveAtWork &= r.X==r.JobIndex%state.Size && r.Z==r.JobIndex/state.Size;
                if(r.Activity==CitizenActivity.Leisure)arriveAtLeisure &= r.X==r.LeisureIndex%state.Size && r.Z==r.LeisureIndex/state.Size;
                if(r.Activity==CitizenActivity.Home)arriveHome &= r.X==r.HomeIndex%state.Size && r.Z==r.HomeIndex/state.Size;
            }
        }
        True(seen.Contains(CitizenActivity.GoingToWork)&&seen.Contains(CitizenActivity.Working), "시차 출근과 실내 근무 루틴");
        True(seen.Contains(CitizenActivity.GoingToLeisure)&&seen.Contains(CitizenActivity.Leisure)&&seen.Contains(CitizenActivity.GoingHome)&&seen.Contains(CitizenActivity.Home), "일터-시장-귀가 루틴");
        True(citizens.Residents.All(r=>r.Indoors || citizens.IsValidPath(r.Path)), "거리 주민은 항상 유효한 현재 경로 보유");
        True(correctDestination,"출발 위치가 실제 이동 목적지를 덮어쓰지 않음");
        True(arriveAtWork,"근무 상태는 실제 일터 좌표에서 발생");
        True(arriveAtLeisure,"여가 상태는 실제 시장 또는 공원 좌표에서 발생");
        True(arriveHome,"귀가 후 실제 주택 좌표에 머무름");
    }

    static void PopulationResizeAndTopologyReplan()
    {
        var state=GameState.CreateNew(); state.Population=2; var citizens=new CitizenSimulation(state);
        for(int i=0;i<20;i++) citizens.Advance(.25f);
        state.Cells[At(state,10,8)].Building=BuildingKind.None; citizens.Synchronize();
        True(citizens.Residents.All(r=>r.Indoors&&r.Activity==CitizenActivity.Home), "도로 철거 즉시 안전한 집 상태로 재계획");
        True(citizens.Residents.All(r=>r.JobIndex<0 || citizens.FindPath(r.HomeIndex,r.JobIndex).Count>0), "철거 후 끊어진 일자리 배정 제거");
        var replacement=GameState.CreateNew(); replacement.Population=7; citizens.Reset(replacement);
        True(citizens.ResidentCount==7&&citizens.Residents.Select(r=>r.Id).Distinct().Count()==7, "새 게임 또는 불러오기에서 인구와 ID 재구성");
    }

    static GameState Blank()
    {
        var state=GameState.CreateNew(); state.Population=1;
        foreach(var c in state.Cells){ c.Building=BuildingKind.None; c.Level=0; c.Terrain=TerrainKind.Grass; }
        return state;
    }
    static int At(GameState state,int x,int z)=>z*state.Size+x;
    static void Place(GameState state,int x,int z,BuildingKind kind){ var c=state.Cells[At(state,x,z)]; c.Building=kind; c.Level=1; }
}
