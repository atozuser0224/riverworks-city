using Riverworks;

public static class ExpansionCatalogChecks
{
    static int passed;

    public static int Run()
    {
        passed=0;
        ExistingCatalogIsPreserved();
        VerticalEndpointSpecsMatchContract();
        VerticalEndpointsHaveTypedCapabilities();
        return passed;
    }

    static void ExistingCatalogIsPreserved()
    {
        True(ResourceCatalog.Count==38&&FactoryCatalog.Recipes.Count==37&&TechCatalog.All.Count()==26,"v0.8 자원·제조법·기술 수 보존");
        True((int)FactoryKind.Manufacturer==21&&(int)FactoryKind.ItemLift==22&&(int)FactoryKind.FluidRiser==23,"수직 운송 설비 ID 후행 추가");
        True(FactoryCatalog.All.Count()==23,"None 제외 공장 설비 23종 등록");
    }

    static void VerticalEndpointSpecsMatchContract()
    {
        FactorySpec lift=FactoryCatalog.Get(FactoryKind.ItemLift);
        FactorySpec riser=FactoryCatalog.Get(FactoryKind.FluidRiser);
        True(lift!=null&&lift.Width==1&&lift.Height==1&&lift.CoinCost==45&&lift.TimberCost==4&&lift.StoneCost==4&&lift.RequiredTech==TechId.Logistics&&lift.PowerDemand==1,"화물 승강기 끝점 가격·크기·전력·기술");
        True(lift.InputCapacity==0&&lift.OutputCapacity==0,"화물 승강기는 재고 없이 단일 Cargo 사용");
        True(riser!=null&&riser.Width==1&&riser.Height==1&&riser.CoinCost==35&&riser.TimberCost==2&&riser.StoneCost==4&&riser.RequiredTech==TechId.FluidHandling&&riser.PowerDemand==1,"유체 라이저 끝점 가격·크기·전력·기술");
        True(riser.InputCapacity==40&&riser.OutputCapacity==0,"유체 라이저 입력 40L·출력 재고 없음");
        True(lift.Description.Contains("한 쌍")&&lift.Description.Contains("끝점 하나")&&riser.Description.Contains("한 쌍")&&riser.Description.Contains("끝점 하나"),"두 끝점 원자 설치와 개별 가격 안내");
    }

    static void VerticalEndpointsHaveTypedCapabilities()
    {
        True(!FactoryCatalog.IsProduction(FactoryKind.ItemLift)&&!FactoryCatalog.IsProduction(FactoryKind.FluidRiser),"수직 운송 설비는 생산 설비 아님");
        True(!FactoryCatalog.IsClockable(FactoryKind.ItemLift)&&!FactoryCatalog.IsClockable(FactoryKind.FluidRiser),"수직 운송 설비는 클럭 조절 불가");
        True(!FactoryCatalog.IsFluidTransport(FactoryKind.ItemLift)&&FactoryCatalog.IsFluidTransport(FactoryKind.FluidRiser),"고체 승강기와 유체 라이저 능력 분리");
        True(ResourceCatalog.IsTransportable(Resource.SteelBeam)&&!ResourceCatalog.IsTransportable(Resource.Water)&&ResourceCatalog.IsFluid(Resource.Water),"승강기 고체·라이저 유체 필터 기반 타입");
        True(FactoryCatalog.RecipesFor(FactoryKind.ItemLift).Count==0&&FactoryCatalog.RecipesFor(FactoryKind.FluidRiser).Count==0,"수직 운송 설비 제조법 없음");
    }

    static void True(bool value,string name){if(!value)throw new Exception("확장 카탈로그 검사 실패: "+name);passed++;Console.WriteLine("✓ "+name);}
}
