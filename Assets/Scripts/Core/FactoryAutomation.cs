using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Riverworks
{
    public enum AutomationComparison { AtLeast = 0, AtMost = 1 }
    public enum AutomationAction { AllowWhenTrue = 0, StopWhenTrue = 1 }

    [Serializable]
    public sealed class AutomationRule
    {
        public int Id;
        public int SourceEntityId;
        public int TargetEntityId;
        public Resource Resource;
        public AutomationComparison Comparison;
        public AutomationAction Action;
        public int Threshold;
        public bool Enabled = true;
    }

    /// <summary>Pure, bounded conditional control for factory entities.</summary>
    public static class FactoryAutomation
    {
        public const int MaximumRulesPerTarget = 4;
        public const int MaximumRulesPerFactory = 64;
        public const int MaximumThreshold = 1000000;

        sealed class Measurement
        {
            public AutomationRule Rule;
            public FactoryEntity Target;
            public int Value;
            public bool Invalid;
        }

        /// <summary>Clears and recomputes transient automation flags from one tick-start inventory snapshot.</summary>
        public static void Evaluate(FactoryState factory, GameState game)
        {
            if (factory?.Entities == null) return;
            foreach (FactoryEntity entity in factory.Entities)
            {
                if (entity == null) continue;
                entity.AutomationBlocked = false;
                if (entity.Status != null && (entity.Status.StartsWith("조건 자동화", StringComparison.Ordinal) ||
                    entity.Status.StartsWith("자동화 조건", StringComparison.Ordinal))) entity.Status = "";
            }
            if (factory.AutomationRules == null || factory.AutomationRules.Count == 0) return;

            var targets = UniqueEntities(factory.Entities);
            var snapshot = new List<Measurement>();
            foreach (AutomationRule rule in factory.AutomationRules)
            {
                if (rule == null || !rule.Enabled) continue;
                FactoryEntity target;
                targets.TryGetValue(rule.TargetEntityId, out target);
                int value = ReadValue(factory, game, rule);
                snapshot.Add(new Measurement
                {
                    Rule = rule,
                    Target = target,
                    Value = value,
                    Invalid = target == null || !ValidRuleShape(rule) || !IsControllable(target) || !target.ControllerInstalled || value < 0
                });
            }

            var missingMeasurementTargets = new HashSet<int>();
            foreach (Measurement measurement in snapshot)
            {
                FactoryEntity target = measurement.Target;
                if (target == null) continue;
                bool blocked;
                if (measurement.Invalid)
                {
                    blocked = true;
                    missingMeasurementTargets.Add(target.Id);
                }
                else
                {
                    bool comparisonTrue = measurement.Rule.Comparison == AutomationComparison.AtLeast
                        ? measurement.Value >= measurement.Rule.Threshold
                        : measurement.Value <= measurement.Rule.Threshold;
                    blocked = measurement.Rule.Action == AutomationAction.AllowWhenTrue ? !comparisonTrue : comparisonTrue;
                }
                if (blocked) target.AutomationBlocked = true;
            }

            foreach (FactoryEntity target in factory.Entities)
            {
                if (target == null || !target.AutomationBlocked) continue;
                target.Status = missingMeasurementTargets.Contains(target.Id)
                    ? "자동화 조건 오류: 원본 측정 실패"
                    : "자동화 조건 대기";
            }
        }

        public static bool CanSave(GameState game, AutomationRule rule, out string reason)
        {
            if (game?.Factory == null) return Fail("공장 상태가 없습니다.", out reason);
            if (!TechCatalog.Has(game, TechId.IndustrialControl)) return Fail("산업 제어 연구가 필요합니다.", out reason);
            FactoryState factory = game.Factory;
            try { Validate(factory); }
            catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentNullException || exception is OverflowException)
            { return Fail("기존 자동화 규칙이 손상되었습니다.", out reason); }
            if (rule == null || !ValidRuleShape(rule)) return Fail("자동화 규칙 값이 올바르지 않습니다.", out reason);

            var entities = UniqueEntities(factory.Entities);
            FactoryEntity target;
            if (!entities.TryGetValue(rule.TargetEntityId, out target) || !IsControllable(target))
                return Fail("제어할 수 있는 대상 설비가 아닙니다.", out reason);
            if (rule.SourceEntityId > 0 && !entities.ContainsKey(rule.SourceEntityId))
                return Fail("측정할 원본 설비를 찾을 수 없습니다.", out reason);

            AutomationRule existing = null;
            if (rule.Id == 0)
            {
                if (factory.AutomationRules.Count >= MaximumRulesPerFactory) return Fail("공장 자동화 규칙은 최대 64개입니다.", out reason);
                if (factory.NextAutomationRuleId <= 0 || factory.NextAutomationRuleId == int.MaxValue)
                    return Fail("새 자동화 규칙 ID를 만들 수 없습니다.", out reason);
            }
            else
            {
                existing = factory.AutomationRules.SingleOrDefault(value => value.Id == rule.Id);
                if (existing == null) return Fail("수정할 자동화 규칙을 찾을 수 없습니다.", out reason);
            }

            int targetRuleCount = factory.AutomationRules.Count(value => value.Id != rule.Id && value.TargetEntityId == rule.TargetEntityId) + 1;
            if (targetRuleCount > MaximumRulesPerTarget) return Fail("대상 설비에는 규칙을 최대 4개 저장할 수 있습니다.", out reason);
            if (!target.ControllerInstalled && !AvailableControlUnit(game)) return Fail("제어 장치가 1개 필요합니다.", out reason);
            reason = existing == null ? "새 자동화 규칙 저장 가능" : "자동화 규칙 수정 가능";
            return true;
        }

        public static bool SaveRule(GameState game, AutomationRule rule, out string reason)
        {
            if (!CanSave(game, rule, out reason)) return false;
            FactoryState factory = game.Factory;
            FactoryEntity target = factory.Entities.Single(entity => entity.Id == rule.TargetEntityId);
            bool installController = !target.ControllerInstalled;
            bool create = rule.Id == 0;
            int assignedId = create ? factory.NextAutomationRuleId : rule.Id;
            var saved = Copy(rule, assignedId);

            if (installController)
            {
                game.Stock[(int)Resource.ControlUnit] -= 1f;
                target.ControllerInstalled = true;
            }
            if (create)
            {
                factory.AutomationRules.Add(saved);
                factory.NextAutomationRuleId++;
                rule.Id = assignedId;
            }
            else
            {
                int index = factory.AutomationRules.FindIndex(value => value.Id == assignedId);
                factory.AutomationRules[index] = saved;
            }
            reason = create ? "자동화 규칙을 저장했습니다." : "자동화 규칙을 수정했습니다.";
            return true;
        }

        public static bool RemoveRule(GameState game, int id, out string reason)
        {
            if (game?.Factory?.AutomationRules == null) return Fail("공장 자동화 규칙이 없습니다.", out reason);
            int index = game.Factory.AutomationRules.FindIndex(rule => rule != null && rule.Id == id);
            int matches = game.Factory.AutomationRules.Count(rule => rule != null && rule.Id == id);
            if (id <= 0 || index < 0 || matches != 1) return Fail("삭제할 자동화 규칙을 찾을 수 없습니다.", out reason);
            game.Factory.AutomationRules.RemoveAt(index);
            reason = "자동화 규칙을 삭제했습니다.";
            return true;
        }

        public static void RemoveReferences(FactoryState factory, int entityId)
        {
            if (factory?.AutomationRules == null || entityId <= 0) return;
            factory.AutomationRules.RemoveAll(rule => rule != null && (rule.SourceEntityId == entityId || rule.TargetEntityId == entityId));
        }

        /// <summary>Strict, read-only validation for the serialized automation portion of a factory.</summary>
        public static void Validate(FactoryState factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (factory.Entities == null || factory.AutomationRules == null)
                throw new InvalidDataException("Automation save data is incomplete.");
            if (factory.AutomationRules.Count > MaximumRulesPerFactory || factory.NextAutomationRuleId <= 0)
                throw new InvalidDataException("Automation rule count or next ID is invalid.");

            Dictionary<int, FactoryEntity> entities = UniqueEntities(factory.Entities);
            if (entities.Count != factory.Entities.Count || factory.Entities.Any(entity => entity == null || entity.Id <= 0))
                throw new InvalidDataException("Factory entity references are invalid.");
            foreach (FactoryEntity entity in factory.Entities)
                if (entity == null || (entity.ControllerInstalled && !IsControllable(entity)))
                    throw new InvalidDataException("Factory controller hardware is invalid.");

            var ruleIds = new HashSet<int>();
            var perTarget = new Dictionary<int, int>();
            foreach (AutomationRule rule in factory.AutomationRules)
            {
                if (rule == null || rule.Id <= 0 || rule.Id >= factory.NextAutomationRuleId || !ruleIds.Add(rule.Id) || !ValidRuleShape(rule))
                    throw new InvalidDataException("Automation rule data is invalid.");
                FactoryEntity target;
                if (!entities.TryGetValue(rule.TargetEntityId, out target) || !IsControllable(target) || !target.ControllerInstalled)
                    throw new InvalidDataException("Automation rule target or controller is invalid.");
                if (rule.SourceEntityId > 0 && !entities.ContainsKey(rule.SourceEntityId))
                    throw new InvalidDataException("Automation rule source is missing.");
                int count;
                perTarget.TryGetValue(rule.TargetEntityId, out count);
                if (++count > MaximumRulesPerTarget) throw new InvalidDataException("Too many automation rules target one entity.");
                perTarget[rule.TargetEntityId] = count;
            }
        }

        /// <summary>Reads whole units from city stock or one entity's input, output, and matching carried cargo.</summary>
        public static int ReadValue(FactoryState factory, GameState game, AutomationRule rule)
        {
            if (factory == null || rule == null || !ValidResource(rule.Resource) || rule.SourceEntityId < 0) return -1;
            int resourceIndex = (int)rule.Resource;
            if (rule.SourceEntityId == 0)
            {
                if (game?.Stock == null || game.Stock.Count != ResourceCatalog.InventoryCount || game.Stock.Any(value => !Finite(value) || value < 0)) return -1;
                float value = game.Stock[resourceIndex];
                if (value > int.MaxValue) return -1;
                return (int)Math.Floor(value);
            }
            if (factory.Entities == null) return -1;
            FactoryEntity source = null;
            foreach (FactoryEntity entity in factory.Entities)
            {
                if (entity == null || entity.Id != rule.SourceEntityId) continue;
                if (source != null) return -1;
                source = entity;
            }
            if (source == null || !ValidInventory(source.Input) || !ValidInventory(source.Output) ||
                !ResourceCatalog.IsValid(source.CargoResource) || !Finite(source.CargoProgress) || source.CargoProgress < 0 || source.CargoProgress > 1)
                return -1;
            bool hasCargo = ResourceCatalog.IsTransportable(source.CargoResource);
            if ((source.CargoResource == Resource.Coins && source.CargoProgress != 0) ||
                (source.CargoResource != Resource.Coins && !hasCargo)) return -1;
            long total = (long)source.Input[resourceIndex] + source.Output[resourceIndex];
            if (source.CargoResource == rule.Resource) total++;
            return total > int.MaxValue ? -1 : (int)total;
        }

        public static bool ValidRuleShape(AutomationRule rule)
        {
            return rule != null && rule.Id >= 0 && rule.SourceEntityId >= 0 && rule.TargetEntityId > 0 &&
                   ValidResource(rule.Resource) && Enum.IsDefined(typeof(AutomationComparison), rule.Comparison) &&
                   Enum.IsDefined(typeof(AutomationAction), rule.Action) && rule.Threshold >= 0 && rule.Threshold <= MaximumThreshold;
        }

        public static bool ValidResource(Resource resource) => ResourceCatalog.IsValid(resource) && resource != Resource.Coins;

        public static bool IsControllable(FactoryEntity entity)
        {
            if (entity == null || entity.Kind == FactoryKind.None || FactoryCatalog.Get(entity.Kind) == null) return false;
            return (entity.Kind != FactoryKind.ItemLift && entity.Kind != FactoryKind.FluidRiser) || entity.IsLinkSender;
        }

        static Dictionary<int, FactoryEntity> UniqueEntities(IEnumerable<FactoryEntity> entities)
        {
            var result = new Dictionary<int, FactoryEntity>();
            if (entities == null) return result;
            foreach (FactoryEntity entity in entities)
                if (entity != null && entity.Id > 0 && !result.ContainsKey(entity.Id)) result.Add(entity.Id, entity);
            return result;
        }

        static bool AvailableControlUnit(GameState game)
        {
            if (game?.Stock == null || game.Stock.Count != ResourceCatalog.InventoryCount) return false;
            float value = game.Stock[(int)Resource.ControlUnit];
            return Finite(value) && value >= 1f;
        }

        public static bool ValidInventory(List<int> inventory) => inventory != null && inventory.Count == ResourceCatalog.InventoryCount && inventory[0] == 0 && inventory.All(value => value >= 0);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static AutomationRule Copy(AutomationRule source, int id) => new AutomationRule
        {
            Id = id,
            SourceEntityId = source.SourceEntityId,
            TargetEntityId = source.TargetEntityId,
            Resource = source.Resource,
            Comparison = source.Comparison,
            Action = source.Action,
            Threshold = source.Threshold,
            Enabled = source.Enabled
        };
        static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
