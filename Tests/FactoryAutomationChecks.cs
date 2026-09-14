using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Riverworks;

public static class FactoryAutomationChecks
{
    static int passed;
    static void True(bool value, string name)
    {
        if (!value) throw new Exception("공장 자동화 검증 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }

    public static int Run()
    {
        passed = 0;
        ReadsTypedSnapshotValues();
        EvaluatesComparisonsAndManualPause();
        MissingSourceFailsClosed();
        SavesControllerAtomicallyAndOnlyOnce();
        EnforcesRuleLimitsAndReferences();
        RemovesRulesWithoutRefundingHardware();
        StrictValidationIsReadOnly();
        Console.WriteLine($"공장 조건식 자동화 {passed}개 검증 통과");
        return passed;
    }

    static void ReadsTypedSnapshotValues()
    {
        GameState game = GameWith(Entity(1, FactoryKind.Assembler), Entity(2, FactoryKind.Storage));
        FactoryEntity source = game.Factory.Entities[1];
        game.Stock[(int)Resource.Steel] = 4.9f;
        AutomationRule city = Rule(0, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 4);
        True(FactoryAutomation.ReadValue(game.Factory, game, city) == 4, "도시 재고를 온전한 정수 단위로 측정");

        source.Input[(int)Resource.Steel] = 2;
        source.Output[(int)Resource.Steel] = 3;
        source.CargoResource = Resource.Steel;
        source.CargoProgress = .4f;
        AutomationRule entity = Rule(0, source.Id, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 6);
        True(FactoryAutomation.ReadValue(game.Factory, game, entity) == 6, "설비 입력·출력·일치 운반 화물 1개 합산");
        source.CargoResource = Resource.Copper;
        True(FactoryAutomation.ReadValue(game.Factory, game, entity) == 5, "다른 종류 운반 화물은 측정에서 제외");
        source.Input = null;
        True(FactoryAutomation.ReadValue(game.Factory, game, entity) == -1, "손상된 설비 측정값을 실패 폐쇄 센티널로 반환");
        game.Stock[(int)Resource.Steel] = float.NaN;
        True(FactoryAutomation.ReadValue(game.Factory, game, city) == -1, "비유한 도시 재고를 실패 폐쇄 센티널로 반환");
        True(FactoryAutomation.ReadValue(game.Factory, game, Rule(0, 0, 1, Resource.Coins, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 0)) == -1,
            "코인은 자동화 측정에서 제외");
    }

    static void EvaluatesComparisonsAndManualPause()
    {
        FactoryEntity target = Entity(1, FactoryKind.Assembler);
        FactoryEntity source = Entity(2, FactoryKind.Storage);
        target.ControllerInstalled = true;
        source.Input[(int)Resource.Steel] = 5;
        GameState game = GameWith(target, source);

        target.Paused = true;
        game.Factory.AutomationRules.Add(Rule(1, 2, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 5));
        game.Factory.NextAutomationRuleId = 2;
        string inventory = InventorySignature(source);
        FactoryAutomation.Evaluate(game.Factory, game);
        True(!target.AutomationBlocked && target.Paused && target.IsStopped, "참인 허용 조건은 자동 정지를 해제하되 수동 일시정지 유지");
        True(InventorySignature(source) == inventory, "조건 평가는 측정 설비 재고와 화물을 변경하지 않음");

        target.Paused = false;
        game.Factory.AutomationRules[0].Action = AutomationAction.StopWhenTrue;
        FactoryAutomation.Evaluate(game.Factory, game);
        True(target.AutomationBlocked && !target.Paused && target.IsStopped, "참인 정지 조건이 자동 정지 플래그 설정");

        game.Factory.AutomationRules[0] = Rule(1, 2, 1, Resource.Steel, AutomationComparison.AtMost, AutomationAction.AllowWhenTrue, 5);
        FactoryAutomation.Evaluate(game.Factory, game);
        True(!target.AutomationBlocked, "AtMost 경계값 포함 비교");

        game.Factory.AutomationRules.Add(Rule(2, 2, 1, Resource.Steel, AutomationComparison.AtMost, AutomationAction.AllowWhenTrue, 4));
        game.Factory.NextAutomationRuleId = 3;
        FactoryAutomation.Evaluate(game.Factory, game);
        True(target.AutomationBlocked, "여러 규칙 중 하나라도 막으면 대상 정지");
        game.Factory.AutomationRules[1].Enabled = false;
        FactoryAutomation.Evaluate(game.Factory, game);
        True(!target.AutomationBlocked, "비활성 규칙은 자동화를 막지 않음");

        game.Factory.AutomationRules.Clear();
        FactoryAutomation.Evaluate(game.Factory, game);
        True(!target.AutomationBlocked && !target.Paused, "규칙이 없으면 자동 정지 플래그 초기화");
    }

    static void MissingSourceFailsClosed()
    {
        FactoryEntity target = Entity(1, FactoryKind.Belt);
        target.ControllerInstalled = true;
        GameState game = GameWith(target);
        game.Factory.AutomationRules.Add(Rule(1, 999, 1, Resource.Steel, AutomationComparison.AtMost, AutomationAction.StopWhenTrue, 10));
        game.Factory.NextAutomationRuleId = 2;
        FactoryAutomation.Evaluate(game.Factory, game);
        True(target.AutomationBlocked && target.Status.Contains("원본 측정 실패"), "실행 중 원본 누락은 액션과 무관하게 실패 폐쇄 정지와 상태 표시");

        game.Factory.AutomationRules[0].Enabled = false;
        FactoryAutomation.Evaluate(game.Factory, game);
        True(!target.AutomationBlocked && !target.Status.StartsWith("자동화 조건", StringComparison.Ordinal), "비활성 누락 규칙은 막지 않고 오래된 자동화 상태 제거");
    }

    static void SavesControllerAtomicallyAndOnlyOnce()
    {
        FactoryEntity first = Entity(1, FactoryKind.Assembler);
        FactoryEntity second = Entity(2, FactoryKind.Storage);
        GameState game = GameWith(first, second);
        game.Technologies.Remove(TechId.IndustrialControl);
        game.Stock[(int)Resource.ControlUnit] = 2;
        AutomationRule proposed = Rule(0, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 8);
        string before = StateSignature(game);
        True(!FactoryAutomation.SaveRule(game, proposed, out _) && StateSignature(game) == before, "산업 제어 연구 전 저장 원자적 거부");

        game.Technologies.Add(TechId.IndustrialControl);
        game.Stock[(int)Resource.ControlUnit] = 0;
        before = StateSignature(game);
        True(!FactoryAutomation.SaveRule(game, proposed, out _) && StateSignature(game) == before, "제어 장치 부족 시 저장 원자적 거부");

        game.Stock[(int)Resource.ControlUnit] = 2;
        True(FactoryAutomation.SaveRule(game, proposed, out _) && proposed.Id == 1 && game.Factory.NextAutomationRuleId == 2,
            "새 규칙에 단조 증가 ID 할당");
        True(first.ControllerInstalled && game.Stock[(int)Resource.ControlUnit] == 1 && game.Factory.AutomationRules.Count == 1,
            "대상 첫 규칙이 제어 장치 1개를 한 번만 설치·소비");
        proposed.Threshold = 999;
        True(game.Factory.AutomationRules[0].Threshold == 8, "저장 후 호출자 DTO 변경이 저장 규칙에 영향 없음");

        AutomationRule later = Rule(0, 0, 1, Resource.Steel, AutomationComparison.AtMost, AutomationAction.StopWhenTrue, 20);
        True(FactoryAutomation.SaveRule(game, later, out _) && later.Id == 2 && game.Stock[(int)Resource.ControlUnit] == 1,
            "같은 대상의 후속 규칙은 제어 장치를 다시 소비하지 않음");
        int next = game.Factory.NextAutomationRuleId;
        AutomationRule update = Rule(1, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 12);
        True(FactoryAutomation.SaveRule(game, update, out _) && game.Factory.NextAutomationRuleId == next && game.Stock[(int)Resource.ControlUnit] == 1,
            "기존 규칙 수정은 ID와 하드웨어 비용을 유지");
        True(game.Factory.AutomationRules.Single(rule => rule.Id == 1).Threshold == 12, "기존 ID 규칙을 정확히 교체");

        AutomationRule retarget = Rule(1, 0, 2, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 3);
        True(FactoryAutomation.SaveRule(game, retarget, out _) && second.ControllerInstalled && first.ControllerInstalled && game.Stock[(int)Resource.ControlUnit] == 0,
            "규칙을 새 대상으로 옮기면 새 대상 하드웨어만 한 번 설치");
        before = StateSignature(game);
        True(!FactoryAutomation.SaveRule(game, Rule(999, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _)
            && StateSignature(game) == before, "존재하지 않는 업데이트 ID 거부");
    }

    static void EnforcesRuleLimitsAndReferences()
    {
        FactoryEntity target = Entity(1, FactoryKind.Assembler);
        target.ControllerInstalled = true;
        GameState game = GameWith(target, Entity(2, FactoryKind.Storage));
        game.Stock[(int)Resource.ControlUnit] = 10;
        for (int id = 1; id <= 4; id++) game.Factory.AutomationRules.Add(Rule(id, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, id));
        game.Factory.NextAutomationRuleId = 5;
        string before = StateSignature(game);
        True(!FactoryAutomation.SaveRule(game, Rule(0, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 5), out _)
            && StateSignature(game) == before, "대상당 다섯 번째 규칙 원자적 거부");

        before = StateSignature(game);
        True(!FactoryAutomation.SaveRule(game, Rule(0, 999, 2, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _)
            && StateSignature(game) == before, "없는 원본 설비 참조 원자적 거부");
        True(!FactoryAutomation.SaveRule(game, Rule(0, 0, 999, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _)
            && StateSignature(game) == before, "없는 대상 설비 참조 원자적 거부");
        True(!FactoryAutomation.SaveRule(game, Rule(0, 0, 2, Resource.Coins, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _)
            && StateSignature(game) == before, "코인 조건 원자적 거부");
        True(!FactoryAutomation.SaveRule(game, Rule(0, 0, 2, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, FactoryAutomation.MaximumThreshold + 1), out _)
            && StateSignature(game) == before, "조건 임계값 상한 초과 원자적 거부");

        FactoryEntity sender = Entity(3, FactoryKind.ItemLift); sender.IsLinkSender = true;
        FactoryEntity receiver = Entity(4, FactoryKind.ItemLift); receiver.IsLinkSender = false;
        GameState links = GameWith(sender, receiver);
        links.Stock[(int)Resource.ControlUnit] = 2;
        True(FactoryAutomation.CanSave(links, Rule(0, 0, sender.Id, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _),
            "수직 링크 송신 끝점 제어 허용");
        True(!FactoryAutomation.CanSave(links, Rule(0, 0, receiver.Id, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _),
            "수직 링크 수신 끝점 제어 거부");

        var many = new List<FactoryEntity>();
        for (int id = 1; id <= 16; id++) { FactoryEntity entity = Entity(id, FactoryKind.Belt); entity.ControllerInstalled = true; many.Add(entity); }
        GameState full = GameWith(many.ToArray());
        int ruleId = 1;
        foreach (FactoryEntity entity in many)
            for (int n = 0; n < 4; n++) full.Factory.AutomationRules.Add(Rule(ruleId++, 0, entity.Id, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, n));
        full.Factory.NextAutomationRuleId = ruleId;
        before = StateSignature(full);
        True(!FactoryAutomation.SaveRule(full, Rule(0, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1), out _)
            && StateSignature(full) == before, "공장 전체 65번째 규칙 원자적 거부");
    }

    static void RemovesRulesWithoutRefundingHardware()
    {
        FactoryEntity a = Entity(1, FactoryKind.Assembler); a.ControllerInstalled = true;
        FactoryEntity b = Entity(2, FactoryKind.Storage); b.ControllerInstalled = true;
        FactoryEntity c = Entity(3, FactoryKind.Belt); c.ControllerInstalled = true;
        GameState game = GameWith(a, b, c);
        game.Stock[(int)Resource.ControlUnit] = 0;
        game.Factory.AutomationRules.Add(Rule(1, 2, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        game.Factory.AutomationRules.Add(Rule(2, 0, 2, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        game.Factory.AutomationRules.Add(Rule(3, 1, 3, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        game.Factory.NextAutomationRuleId = 4;

        True(FactoryAutomation.RemoveRule(game, 2, out _) && game.Factory.AutomationRules.Count == 2 && game.Stock[(int)Resource.ControlUnit] == 0 && b.ControllerInstalled,
            "규칙 삭제는 설치 하드웨어를 유지하고 환불하지 않음");
        int next = game.Factory.NextAutomationRuleId;
        string before = StateSignature(game);
        True(!FactoryAutomation.RemoveRule(game, 999, out _) && StateSignature(game) == before, "없는 규칙 삭제 원자적 거부");

        FactoryAutomation.RemoveReferences(game.Factory, 1);
        True(game.Factory.AutomationRules.Count == 0 && a.ControllerInstalled && b.ControllerInstalled && c.ControllerInstalled,
            "원본 또는 대상 철거 참조 규칙만 제거하고 하드웨어 플래그 유지");
        True(game.Factory.NextAutomationRuleId == next, "규칙 제거 후 ID를 재사용하지 않음");
        game.Stock[(int)Resource.ControlUnit] = 1;
        AutomationRule replacement = Rule(0, 0, 3, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1);
        True(FactoryAutomation.SaveRule(game, replacement, out _) && replacement.Id == next && game.Factory.NextAutomationRuleId == next + 1,
            "삭제 뒤 새 규칙은 과거 ID가 아닌 다음 ID 사용");
    }

    static void StrictValidationIsReadOnly()
    {
        FactoryEntity target = Entity(1, FactoryKind.Assembler); target.ControllerInstalled = true; target.AutomationBlocked = true;
        FactoryEntity source = Entity(2, FactoryKind.Storage);
        FactoryState valid = FactoryState.CreateEmpty();
        valid.Entities.Add(target); valid.Entities.Add(source); valid.NextEntityId = 3;
        valid.AutomationRules.Add(Rule(1, 2, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 3));
        valid.NextAutomationRuleId = 2;
        string before = FactorySignature(valid);
        FactoryAutomation.Validate(valid);
        True(FactorySignature(valid) == before && target.AutomationBlocked, "엄격 검증은 transient 플래그를 포함한 상태를 변경하지 않음");

        FactoryState bad = ValidFactory(); bad.AutomationRules[0].Id = bad.NextAutomationRuleId;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "다음 ID 이상 규칙 ID 거부");
        bad = ValidFactory(); bad.AutomationRules[0].SourceEntityId = 999;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "저장 데이터의 누락 원본 참조 거부");
        bad = ValidFactory(); bad.AutomationRules[0].TargetEntityId = 999;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "저장 데이터의 누락 대상 참조 거부");
        bad = ValidFactory(); bad.AutomationRules[0].Resource = Resource.Coins;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "저장 데이터의 코인 자원 거부");
        bad = ValidFactory(); bad.AutomationRules[0].Comparison = (AutomationComparison)99;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "알 수 없는 비교 enum 거부");
        bad = ValidFactory(); bad.AutomationRules[0].Action = (AutomationAction)99;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "알 수 없는 액션 enum 거부");
        bad = ValidFactory(); bad.AutomationRules[0].Threshold = -1;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "음수 임계값 거부");
        bad = ValidFactory(); bad.Entities[0].ControllerInstalled = false;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "규칙 대상의 미설치 제어 장치 거부");
        bad = ValidFactory(); bad.AutomationRules.Add(Rule(2, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        bad.AutomationRules.Add(Rule(3, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        bad.AutomationRules.Add(Rule(4, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        bad.AutomationRules.Add(Rule(5, 0, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        bad.NextAutomationRuleId = 6;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "대상당 규칙 4개 초과 저장 데이터 거부");
        bad = FactoryState.CreateEmpty(); FactoryEntity receiver = Entity(1, FactoryKind.FluidRiser); receiver.ControllerInstalled = true; bad.Entities.Add(receiver); bad.NextEntityId = 2;
        True(ThrowsInvalid(() => FactoryAutomation.Validate(bad)), "수직 링크 수신 끝점의 제어 장치 저장 거부");
    }

    static GameState GameWith(params FactoryEntity[] entities)
    {
        GameState game = GameState.CreateNew();
        game.Factory = FactoryState.CreateEmpty();
        game.Factory.Entities.AddRange(entities);
        game.Factory.NextEntityId = entities.Length == 0 ? 1 : entities.Max(entity => entity.Id) + 1;
        if (!game.Technologies.Contains(TechId.IndustrialControl)) game.Technologies.Add(TechId.IndustrialControl);
        return game;
    }

    static FactoryState ValidFactory()
    {
        FactoryEntity target = Entity(1, FactoryKind.Assembler); target.ControllerInstalled = true;
        FactoryEntity source = Entity(2, FactoryKind.Storage);
        FactoryState factory = FactoryState.CreateEmpty();
        factory.Entities.Add(target); factory.Entities.Add(source); factory.NextEntityId = 3;
        factory.AutomationRules.Add(Rule(1, 2, 1, Resource.Steel, AutomationComparison.AtLeast, AutomationAction.AllowWhenTrue, 1));
        factory.NextAutomationRuleId = 2;
        return factory;
    }

    static FactoryEntity Entity(int id, FactoryKind kind) => new FactoryEntity { Id = id, Kind = kind, ClockPercent = 100 };
    static AutomationRule Rule(int id, int source, int target, Resource resource, AutomationComparison comparison, AutomationAction action, int threshold) =>
        new AutomationRule { Id = id, SourceEntityId = source, TargetEntityId = target, Resource = resource, Comparison = comparison, Action = action, Threshold = threshold, Enabled = true };
    static bool ThrowsInvalid(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    static string InventorySignature(FactoryEntity entity) => string.Join(",", entity.Input ?? new List<int>()) + "|" + string.Join(",", entity.Output ?? new List<int>()) + "|" + entity.CargoResource + "|" + entity.CargoProgress;
    static string StateSignature(GameState game) => string.Join(",", game.Stock) + "|" + FactorySignature(game.Factory);
    static string FactorySignature(FactoryState factory) => factory.NextAutomationRuleId + "|" +
        string.Join(";", factory.Entities.Select(entity => $"{entity.Id},{entity.ControllerInstalled},{entity.AutomationBlocked},{entity.Paused},{entity.ClockPercent},{entity.Filter}")) + "|" +
        string.Join(";", factory.AutomationRules.Select(rule => $"{rule.Id},{rule.SourceEntityId},{rule.TargetEntityId},{(int)rule.Resource},{(int)rule.Comparison},{(int)rule.Action},{rule.Threshold},{rule.Enabled}"));
}
