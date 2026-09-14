using Riverworks;

public static class IndustryCatalogChecks
{
    static int passed;
    public static int Run()
    {
        passed=0;
        IdsAndCountsAreStable();
        ResourceCapabilitiesAreTyped();
        RecipesAreCompleteAndReachable();
        TechnologyUnlockMetadataIsConsistent();
        DepositsAreDeterministic();
        return passed;
    }

    static void IdsAndCountsAreStable()
    {
        True(ResourceCatalog.Count==38&&ResourceCatalog.All.Count==38,"자원 38종 등록");
        True((int)Resource.Tools==8&&(int)Resource.CopperOre==9&&(int)Resource.Fuel==37,"자원 ID 호환성");
        True(Enum.GetValues<FactoryKind>().Length==24&&FactoryCatalog.All.Count()==23&&
             (int)FactoryKind.Splitter==11&&(int)FactoryKind.Manufacturer==21&&
             (int)FactoryKind.ItemLift==22&&(int)FactoryKind.FluidRiser==23,"공장 종류 ID 호환성");
        True(FactoryCatalog.Recipes.Count==37&&(int)FactoryRecipe.Bread==4&&(int)FactoryRecipe.BatteryAssembly==37,"제조법 ID 호환성");
        True(TechCatalog.All.Count()==26&&(int)TechId.Automation==18&&(int)TechId.IndustrialControl==26,"기술 ID 호환성");
        True(FactoryState.NewInventory().Count==38&&Cell.NewLogisticsBuffer().Count==38,"동적 자원 버퍼 폭");
        True(new[]{FactoryKind.Storage,FactoryKind.ImportDock,FactoryKind.ExportDock}.All(k=>FactoryCatalog.Get(k).InputCapacity==80&&FactoryCatalog.Get(k).OutputCapacity==80),"기존 저장 설비 3종 용량 80 보존");
        True(FactoryCatalog.GetRecipe(FactoryRecipe.BauxiteMining).RequiredTech==TechId.AluminumProcessing,"보크사이트 채굴 후반 기술 잠금");
        True(FactoryCatalog.IsClockable(FactoryKind.Drill)&&FactoryCatalog.IsClockable(FactoryKind.Refinery)&&!FactoryCatalog.IsClockable(FactoryKind.Belt)&&!FactoryCatalog.IsClockable(FactoryKind.Inserter)&&!FactoryCatalog.IsClockable(FactoryKind.Splitter),"생산 설비만 클럭 조절 가능");
    }

    static void ResourceCapabilitiesAreTyped()
    {
        True(ResourceCatalog.TradeableResources.Count==11&&ResourceCatalog.TradeableResources.All(s=>s.TradePrice>0),"교역 가능 자원 11종");
        True(ResourceCatalog.FluidResources.Count==7&&ResourceCatalog.FluidResources.All(s=>ResourceCatalog.IsFluid(s.Id)),"유체 7종");
        True(ResourceCatalog.SolidResources.Count==30&&ResourceCatalog.SolidResources.All(s=>ResourceCatalog.IsTransportable(s.Id)),"운송 가능 고체 30종");
        True(!ResourceCatalog.IsTransportable(Resource.Coins)&&!ResourceCatalog.IsTransportable(Resource.Water)&&ResourceCatalog.CanFeed(Resource.Water),"코인·유체 운송 능력 분리");
    }

    static void RecipesAreCompleteAndReachable()
    {
        RecipeSpec[] recipes=FactoryCatalog.Recipes.ToArray();
        True(recipes.All(r=>r.Duration>=1&&r.Duration<=14&&r.Outputs.Length>0&&r.Outputs.All(a=>a.Amount>0)),"모든 제조법의 시간과 산출량 유효");
        True(recipes.All(r=>r.IsExtraction||r.Inputs.Length>0),"비채굴 제조법은 원료 소비");
        True(recipes.All(r=>r.Machines.Length>0&&r.Machines.All(m=>FactoryCatalog.IsRecipeCompatible(m,r.Id))),"제조법과 기계 호환성 단일 정의");
        True(FactoryCatalog.GetRecipe(FactoryRecipe.PlasticRecycling).Inputs.Any(a=>a.Resource==Resource.Fuel)&&FactoryCatalog.GetRecipe(FactoryRecipe.RubberRecycling).Inputs.Any(a=>a.Resource==Resource.Fuel),"재생 공정의 무료 순환 방지");

        var reachable=new HashSet<Resource>{Resource.Timber,Resource.Stone,Resource.Grain};
        foreach(var extraction in recipes.Where(r=>r.IsExtraction)) foreach(var output in extraction.Outputs) reachable.Add(output.Resource);
        bool changed;
        do { changed=false; foreach(var recipe in recipes.Where(r=>!r.IsExtraction&&r.Inputs.All(a=>reachable.Contains(a.Resource)))) foreach(var output in recipe.Outputs) changed|=reachable.Add(output.Resource); } while(changed);
        Resource[] produced=recipes.SelectMany(r=>r.Outputs).Select(a=>a.Resource).Distinct().ToArray();
        True(produced.All(reachable.Contains)&&reachable.Contains(Resource.ControlUnit),"원료와 채굴부터 모든 신소재 도달 가능");
    }

    static void TechnologyUnlockMetadataIsConsistent()
    {
        True(FactoryCatalog.All.All(f=>TechCatalog.RequiredTechnology(f.Kind)==f.RequiredTech),"공장 기술 역조회 일치");
        True(FactoryCatalog.Recipes.All(r=>TechCatalog.RequiredTechnology(r.Id)==r.RequiredTech),"제조법 기술 역조회 일치");
        True(TechCatalog.All.All(t=>t.UnlockFactories.All(k=>FactoryCatalog.Get(k).RequiredTech==t.Id)&&t.UnlockRecipes.All(id=>FactoryCatalog.GetRecipe(id).RequiredTech==t.Id)),"기술 잠금 메타데이터 일치");
    }

    static void DepositsAreDeterministic()
    {
        True(IndustryDeposits.At(16,10)==Resource.CopperOre&&IndustryDeposits.At(10,16)==Resource.Coal&&IndustryDeposits.At(16,16)==Resource.Bauxite&&IndustryDeposits.At(16,4)==Resource.CrudeOil,"산업 매장지 좌표");
        True(IndustryDeposits.At(0,0)==Resource.Coins&&!IndustryDeposits.IsSpecial(Resource.Coal,0,0),"일반 지형은 특수 매장지 아님");
    }

    static void True(bool value,string name){if(!value)throw new Exception("산업 카탈로그 검사 실패: "+name);passed++;Console.WriteLine("✓ "+name);}
}
