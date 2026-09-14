using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public class Simulation
    {
        public GameState State;
        public event Action<string> OnNotice;
        public float LastIncome;
        public int ConnectedBuildings, PowerCapacity, PowerUsed;
        public string ObjectiveTitle = "", ObjectiveDescription = "";
        public float ObjectiveProgress;
        public string ResearchStatus
        {
            get
            {
                if (State.ActiveResearch == TechId.None) return "진행 중인 연구 없음";
                TechSpec spec = TechCatalog.Get(State.ActiveResearch);
                return spec == null ? "진행 중인 연구 없음" : $"{spec.Name} · {State.ResearchDaysRemaining}일 남음";
            }
        }
        public float ResearchProgress
        {
            get
            {
                TechSpec spec = TechCatalog.Get(State.ActiveResearch);
                return spec == null || spec.DurationDays <= 0 ? 0 : Math.Max(0, Math.Min(1, 1f - State.ResearchDaysRemaining / (float)spec.DurationDays));
            }
        }
        public float ResearchPerDay => .8f + State.Population * .04f
            + State.Cells.Where(c => c.Connected && c.Building == BuildingKind.StudyHouse).Sum(c => c.Level * (TechCatalog.Has(State, TechId.Education) ? 2.5f : 1.5f))
            + State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Academy).Sum(c => c.Level * 3.5f)
            + CityProjects.ResearchBonus(State);
        public int UpgradeLevelCap => TechCatalog.Has(State, TechId.UrbanPlanning) ? 3 : TechCatalog.Has(State, TechId.Stonecraft) ? 2 : 1;

        private static readonly HashSet<BuildingKind> Powered = new HashSet<BuildingKind> { BuildingKind.Mill, BuildingKind.Bakery, BuildingKind.Mine, BuildingKind.Smelter, BuildingKind.Workshop };
        private static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };

        public Simulation(GameState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Normalize();
            Recalculate();
        }

        public Cell GetCell(int x, int z) => x >= 0 && z >= 0 && x < State.Size && z < State.Size ? State.Cells[z * State.Size + x] : null;
        public float Get(Resource resource) => resource == Resource.Coins ? State.Coins : State.Stock[(int)resource];
        public bool IsOwned(int x, int z) => GetCell(x, z) != null && State.OwnedRegions.Contains((z / 7) * 3 + x / 7);

        public bool CanResearch(TechId id, out string reason)
        {
            TechSpec spec = TechCatalog.Get(id);
            if (spec == null) return Fail("알 수 없는 기술입니다.", out reason);
            if (TechCatalog.Has(State, id)) return Fail("이미 연구한 기술입니다.", out reason);
            if (State.ActiveResearch != TechId.None) return Fail("다른 연구가 진행 중입니다.", out reason);
            TechId missing = spec.Prerequisites.FirstOrDefault(t => !TechCatalog.Has(State, t));
            if (missing != TechId.None) return Fail($"선행 기술 '{TechCatalog.Get(missing).Name}'이 필요합니다.", out reason);
            if (State.ResearchPoints + .001f < spec.ResearchCost) return Fail($"연구 점수가 {spec.ResearchCost - State.ResearchPoints:0.0} 부족합니다.", out reason);
            if (State.Coins + .001f < spec.CoinCost) return Fail($"코인이 {spec.CoinCost - State.Coins:0} 부족합니다.", out reason);
            reason = "연구 가능";
            return true;
        }

        public bool StartResearch(TechId id, out string reason)
        {
            if (!CanResearch(id, out reason)) return false;
            TechSpec spec = TechCatalog.Get(id);
            State.ResearchPoints -= spec.ResearchCost;
            State.Coins -= spec.CoinCost;
            SyncCoins();
            State.ActiveResearch = id;
            State.ResearchDaysRemaining = spec.DurationDays;
            reason = $"{spec.Name} 연구 시작 · {spec.DurationDays}일";
            Notice(reason);
            return true;
        }

        public bool IsBuildingUnlocked(BuildingKind kind, out string reason)
        {
            if (!Enum.IsDefined(typeof(BuildingKind), kind)) return Fail("알 수 없는 건물입니다.", out reason);
            TechId required = TechCatalog.RequiredTechnology(kind);
            if (!TechCatalog.Has(State, required)) return Fail($"기술 '{TechCatalog.Get(required).Name}' 연구가 필요합니다.", out reason);
            BuildingSpec spec = Catalog.Get(kind);
            if (State.Population < spec.UnlockPopulation) return Fail($"인구 {spec.UnlockPopulation}명부터 해금됩니다.", out reason);
            reason = "해금됨";
            return true;
        }

        public bool CanBuild(BuildingKind kind, int x, int z, out string reason)
        {
            Cell cell = GetCell(x, z);
            if (cell == null) return Fail("지도 밖에는 건설할 수 없습니다.", out reason);
            if (!Enum.IsDefined(typeof(BuildingKind), kind)) return Fail("알 수 없는 건물입니다.", out reason);
            if (kind == BuildingKind.None || kind == BuildingKind.TownHall) return Fail("이 건물은 직접 건설할 수 없습니다.", out reason);
            if (!IsBuildingUnlocked(kind, out reason)) return false;
            if (!IsOwned(x, z)) return Fail("먼저 이 지역을 매입하세요.", out reason);
            if (CityProjects.Occupies(State, x, z)) return Fail("대형 프로젝트 부지는 개별 도시 건설에 사용할 수 없습니다.", out reason);
            if (cell.Building != BuildingKind.None) return Fail("이미 건물이 있습니다.", out reason);
            if (!CityLogistics.CanBuildCity(State, kind, x, z, out reason)) return false;
            if (cell.Terrain == TerrainKind.Water && kind != BuildingKind.Road) return Fail("물 위에는 도로 다리만 건설할 수 있습니다.", out reason);
            BuildingSpec spec = Catalog.Get(kind);
            if (kind == BuildingKind.Farm && cell.Terrain != TerrainKind.Grass) return Fail("농장은 평지에만 지을 수 있습니다.", out reason);
            if (kind == BuildingKind.Lumberyard && !NearTerrain(x, z, TerrainKind.Forest)) return Fail("벌목장은 숲 위나 숲 옆에 지어야 합니다.", out reason);
            if ((kind == BuildingKind.Quarry || kind == BuildingKind.Mine) && !NearTerrain(x, z, TerrainKind.Rock)) return Fail("이 시설은 바위 위나 바위 옆에 지어야 합니다.", out reason);
            if (State.Coins + .001f < spec.Cost) return Fail($"코인이 {spec.Cost - State.Coins:0} 부족합니다.", out reason);
            if (Get(Resource.Timber) + .001f < spec.TimberCost) return Fail($"목재가 {spec.TimberCost - Get(Resource.Timber):0} 부족합니다.", out reason);
            if (Get(Resource.Stone) + .001f < spec.StoneCost) return Fail($"석재가 {spec.StoneCost - Get(Resource.Stone):0} 부족합니다.", out reason);
            reason = "건설 가능";
            return true;
        }

        public bool Build(BuildingKind kind, int x, int z, out string reason)
        {
            if (!CanBuild(kind, x, z, out reason)) return false;
            var spec = Catalog.Get(kind);
            Spend(spec.Cost, spec.TimberCost, spec.StoneCost);
            Cell cell = GetCell(x, z);
            cell.Building = kind; cell.Level = 1; cell.Progress = 0;
            Recalculate();
            reason = $"{spec.Name} 건설 완료";
            Notice(reason);
            return true;
        }

        public bool Demolish(int x, int z, out string reason)
        {
            Cell cell = GetCell(x, z);
            if (cell == null || cell.Building == BuildingKind.None) return Fail("철거할 건물이 없습니다.", out reason);
            if (cell.Building == BuildingKind.TownHall) return Fail("시청은 철거할 수 없습니다.", out reason);
            if (CityProjects.Occupies(State, x, z)) return Fail("대형 프로젝트 부지는 프로젝트 취소 절차로만 정리할 수 있습니다.", out reason);
            if (!CityLogistics.CanDemolishCity(State, cell, out reason)) return false;
            BuildingSpec spec = Catalog.Get(cell.Building);
            // Refund a conservative portion of the exact base and upgrade investment.
            float coinInvestment = cell.Level == 1 ? 1f : cell.Level == 2 ? 1.9f : 3.05f;
            float materialInvestment = cell.Level == 1 ? 1f : cell.Level == 2 ? 1.5f : 2.5f;
            State.Coins += spec.Cost * coinInvestment * .35f;
            Add(Resource.Timber, spec.TimberCost * materialInvestment * .25f);
            Add(Resource.Stone, spec.StoneCost * materialInvestment * .25f);
            CityLogistics.EnsureCellBuffers(cell);
            for (int i = 1; i < ResourceCatalog.Count; i++)
            {
                State.Stock[i] += cell.LogisticsInput[i] + cell.LogisticsOutput[i];
                cell.LogisticsInput[i] = 0;
                cell.LogisticsOutput[i] = 0;
            }
            string name = spec.Name;
            cell.Building = BuildingKind.None; cell.Level = 0; cell.Progress = 0; cell.Connected = false; cell.Status = "";
            Recalculate();
            reason = $"{name} 철거 완료 · 일부 자재 환급";
            Notice(reason);
            return true;
        }

        public bool Upgrade(int x, int z, out string reason)
        {
            Cell cell = GetCell(x, z);
            if (cell == null || cell.Building == BuildingKind.None || cell.Building == BuildingKind.Road || cell.Building == BuildingKind.TownHall) return Fail("이 건물은 업그레이드할 수 없습니다.", out reason);
            if (cell.Level >= UpgradeLevelCap) return Fail($"현재 기술로는 레벨 {UpgradeLevelCap}까지 증축할 수 있습니다.", out reason);
            BuildingSpec spec = Catalog.Get(cell.Building);
            int next = cell.Level + 1;
            int coins = (int)Math.Ceiling(spec.Cost * (.65f + cell.Level * .25f));
            int timber = (int)Math.Ceiling(spec.TimberCost * .5f * cell.Level);
            int stone = (int)Math.Ceiling(spec.StoneCost * .5f * cell.Level);
            if (State.Coins < coins || Get(Resource.Timber) < timber || Get(Resource.Stone) < stone)
                return Fail($"레벨 {next} 비용: 코인 {coins}, 목재 {timber}, 석재 {stone}", out reason);
            Spend(coins, timber, stone);
            cell.Level = next;
            Recalculate();
            reason = $"{spec.Name} 레벨 {next} 업그레이드 완료";
            Notice(reason);
            return true;
        }

        public int RegionCost(int regionId) => regionId < 0 || regionId > 8 || State.OwnedRegions.Contains(regionId) ? 0 : 320 + Math.Max(0, State.OwnedRegions.Count - 1) * 140;

        public bool BuyRegion(int regionId, out string reason)
        {
            if (regionId < 0 || regionId > 8) return Fail("존재하지 않는 지역입니다.", out reason);
            if (State.OwnedRegions.Contains(regionId)) return Fail("이미 소유한 지역입니다.", out reason);
            int rx = regionId % 3, rz = regionId / 3;
            bool adjacent = State.OwnedRegions.Any(id => Math.Abs(id % 3 - rx) + Math.Abs(id / 3 - rz) == 1);
            if (!adjacent) return Fail("소유 지역과 맞닿은 지역부터 확장할 수 있습니다.", out reason);
            int cost = RegionCost(regionId);
            if (State.Coins < cost) return Fail($"지역 매입에 코인 {cost}이 필요합니다.", out reason);
            State.Coins -= cost; SyncCoins(); State.OwnedRegions.Add(regionId); State.OwnedRegions.Sort();
            reason = $"새 지역을 코인 {cost}에 매입했습니다.";
            Notice(reason); UpdateObjective();
            return true;
        }

        public void Tick()
        {
            Recalculate();
            State.Day++;
            State.ResearchPoints += ResearchPerDay;
            AdvanceResearch();
            CityProjects.Tick(State);
            Recalculate();
            PowerUsed = 0;
            float produced = 0;
            int warehouseLevels = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Warehouse).Sum(c => c.Level);
            float logistics = 1 + Math.Min(.25f, warehouseLevels * .03f);
            PowerCapacity = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Windmill).Sum(c => c.Level * 8)
                + CityProjects.ProducePower(State);
            foreach (Cell steam in State.Cells.Where(c => c.Connected && c.Building == BuildingKind.SteamPlant))
            {
                float fuel = steam.Level * .6f;
                if (Get(Resource.Timber) + .001f >= fuel) { Add(Resource.Timber, -fuel); PowerCapacity += steam.Level * 18; steam.Status = $"증기 가동 · 동력 +{steam.Level * 18}"; }
                else steam.Status = "연료 부족: 목재";
            }

            foreach (Cell cell in State.Cells)
            {
                BuildingSpec spec = Catalog.Get(cell.Building);
                if (spec.OutputAmount <= 0) continue;
                if (!cell.Connected) { cell.Status = "도로가 시청과 연결되지 않았습니다."; continue; }
                int power = Powered.Contains(cell.Building) ? cell.Level : 0;
                if (PowerUsed + power > PowerCapacity) { cell.Status = "동력이 부족합니다."; continue; }
                float scale = .75f + cell.Level * .25f;
                if (cell.Building == BuildingKind.Farm && TechCatalog.Has(State, TechId.CropRotation)) scale *= 1.25f;
                if ((cell.Building == BuildingKind.Lumberyard && NearTerrain(cell.X, cell.Z, TerrainKind.Forest)) ||
                    ((cell.Building == BuildingKind.Quarry || cell.Building == BuildingKind.Mine) && NearTerrain(cell.X, cell.Z, TerrainKind.Rock))) scale *= 1.25f;
                CityLogistics.EnsureCellBuffers(cell);
                bool automatedInput = spec.Inputs.Count > 0 && CityLogistics.HasAutomatedInput(State, cell);
                bool automatedOutput = CityLogistics.HasAutomatedOutput(State, cell);
                float amount = spec.OutputAmount * scale * logistics * TechnologyOutputMultiplier(cell.Building);
                if (automatedOutput && CityLogistics.BufferTotal(cell.LogisticsOutput) + amount > CityLogistics.BufferCapacity + .0001f)
                {
                    cell.Status = "물리 출력 버퍼가 가득 찼습니다.";
                    continue;
                }
                bool hasInputs = spec.Inputs.All(p => (automatedInput ? cell.LogisticsInput[(int)p.Key] : Get(p.Key)) + .001f >= p.Value * scale);
                if (!hasInputs) { cell.Status = "원료 부족: " + string.Join(", ", spec.Inputs.Where(p => (automatedInput?cell.LogisticsInput[(int)p.Key]:Get(p.Key)) < p.Value * scale).Select(p => Catalog.ResourceName(p.Key))); continue; }
                foreach (var input in spec.Inputs)
                {
                    if (automatedInput) cell.LogisticsInput[(int)input.Key] = Math.Max(0,cell.LogisticsInput[(int)input.Key]-input.Value*scale);
                    else Add(input.Key, -input.Value * scale);
                }
                if (automatedOutput)
                {
                    float before = cell.LogisticsOutput[(int)spec.Output];
                    cell.LogisticsOutput[(int)spec.Output] += amount;
                    int newlyPhysical = (int)Math.Floor(cell.LogisticsOutput[(int)spec.Output] + .0001f) - (int)Math.Floor(before + .0001f);
                    if (newlyPhysical > 0) State.Factory.Produced[(int)spec.Output] += newlyPhysical;
                }
                else Add(spec.Output, amount);
                produced += amount; cell.Progress += amount; PowerUsed += power;
                if (cell.Building == BuildingKind.Workshop) State.TotalToolsProduced += amount;
                cell.Status = $"가동 중 · {Catalog.ResourceName(spec.Output)} +{amount:0.0}";
            }

            var markets = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Market).ToList();
            int marketLevels = markets.Sum(c => c.Level);
            if (marketLevels > 0 && Get(Resource.Bread) < 3)
            {
                if (State.Coins >= 8)
                {
                    State.Coins -= 8; Add(Resource.Bread, 2 + marketLevels);
                    foreach (Cell market in markets) market.Status = "빵 비상 수입";
                }
                else foreach (Cell market in markets) market.Status = "빵 비상 수입 자금 부족";
            }
            ProcessHomes();
            bool toolsSupplied = ProcessComfortTools();

            int parks = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Park).Sum(c => c.Level);
            int working = State.Cells.Count(c => c.Connected && c.Building != BuildingKind.None && c.Building != BuildingKind.Road && c.Building != BuildingKind.TownHall);
            float tax = State.Population * (.10f + Math.Min(.04f, parks * .005f) + (toolsSupplied ? .06f : 0));
            float upkeep = working * .32f + PowerCapacity * .025f;
            LastIncome = tax - upkeep;
            State.Coins = Math.Max(0, State.Coins + LastIncome);
            State.TotalProduced += produced;
            SyncCoins();
            CheckMilestones();
        }

        public void Recalculate()
        {
            foreach (Cell c in State.Cells) { c.Connected = false; if (c.Building == BuildingKind.None) c.Status = ""; }
            Cell town = State.Cells.FirstOrDefault(c => c.Building == BuildingKind.TownHall);
            var queue = new Queue<Cell>();
            if (town != null) { town.Connected = true; town.Status = "도시 행정 중심"; queue.Enqueue(town); }
            while (queue.Count > 0)
            {
                Cell current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    Cell next = GetCell(current.X + Dx[i], current.Z + Dz[i]);
                    if (next != null && !next.Connected && next.Building == BuildingKind.Road) { next.Connected = true; next.Status = "시청과 연결됨"; queue.Enqueue(next); }
                }
            }
            foreach (Cell c in State.Cells)
            {
                if (c.Building == BuildingKind.None || c.Connected) continue;
                if (Adjacent(c.X, c.Z).Any(n => n.Connected && (n.Building == BuildingKind.Road || n.Building == BuildingKind.TownHall))) { c.Connected = true; c.Status = "도로망 연결됨"; }
                else c.Status = "도로가 시청과 연결되지 않았습니다.";
            }
            ConnectedBuildings = State.Cells.Count(c => c.Connected && c.Building != BuildingKind.None);
            PowerCapacity = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.Windmill).Sum(c => c.Level * 8)
                + PreviewSteamPowerCapacity()
                + CityProjects.PreviewPower(State);
            int housingCapacity = State.Cells.Where(c => c.Building == BuildingKind.House).Sum(c => c.Level * 6);
            State.Population = Math.Min(State.Population, housingCapacity);
            SyncCoins();
            UpdateObjective();
        }

        private void ProcessHomes()
        {
            var homes = State.Cells.Where(c => c.Connected && c.Building == BuildingKind.House).ToList();
            int capacity = homes.Sum(c => c.Level * 6);
            float needed = Math.Max(.15f, State.Population * .055f);
            State.LastBreadDemand = homes.Count > 0 ? needed : 0;
            State.LastGrainConsumed = 0;
            State.LastFoodSatisfied = false;
            float breadUsed = Math.Min(Get(Resource.Bread), needed);
            float grainNeeded = (needed - breadUsed) * 2f;
            bool grainFallback = homes.Count > 0 && breadUsed + .001f < needed && Get(Resource.Grain) + .001f >= grainNeeded;
            bool fed = homes.Count > 0 && (breadUsed + .001f >= needed || grainFallback);
            if (fed)
            {
                Add(Resource.Bread, -breadUsed);
                if (grainFallback) { Add(Resource.Grain, -grainNeeded); State.LastGrainConsumed = grainNeeded; }
                State.LastFoodSatisfied = true;
                int parks = State.Cells.Count(c => c.Connected && c.Building == BuildingKind.Park);
                State.Happiness = Math.Min(100, State.Happiness + (grainFallback ? 0 : 1 + parks));
                foreach (Cell home in homes) { home.Progress += (grainFallback ? .12f : .22f) * home.Level; home.Status = grainFallback ? "곡물 식사 · 더 나은 음식이 필요합니다." : "주민 만족 · 성장 중"; }
                float growth = homes.Sum(h => h.Progress >= 1 ? 1 : 0);
                foreach (Cell home in homes.Where(h => h.Progress >= 1)) home.Progress -= 1;
                State.Population = Math.Min(capacity, State.Population + (int)growth);
            }
            else
            {
                State.Happiness = Math.Max(0, State.Happiness - (homes.Count == 0 ? 2 : 4));
                foreach (Cell home in homes) home.Status = "식량 부족 · 성장이 멈췄습니다.";
                if (State.Happiness < 25 && State.Population > 1) State.Population--;
            }
        }

        private bool ProcessComfortTools()
        {
            State.LastToolsDemand = State.Population >= 24 ? State.Population * .004f : 0;
            State.LastToolsDelivered = 0;
            if (State.LastToolsDemand <= 0 || Get(Resource.Tools) + .001f < State.LastToolsDemand) return false;
            Add(Resource.Tools, -State.LastToolsDemand);
            State.LastToolsDelivered = State.LastToolsDemand;
            State.Happiness = Math.Min(100, State.Happiness + 1);
            return true;
        }

        private void CheckMilestones()
        {
            bool changed;
            do
            {
                changed = false;
                if (State.Milestone == 0 && ConnectedBuildings >= 12) changed = Advance("도로망이 살아났습니다!");
                else if (State.Milestone == 1 && State.Population >= 16 && Get(Resource.Bread) >= 4) changed = Advance("자급 도시가 되었습니다!");
                else if (State.Milestone == 2 && State.OwnedRegions.Count >= 2) changed = Advance("도시 경계가 넓어졌습니다!");
                else if (State.Milestone == 3 && State.Population >= 30 && State.TotalToolsProduced >= 8 && ((HasOperatedConnected(BuildingKind.Mine) && HasOperatedConnected(BuildingKind.Smelter) && HasOperatedConnected(BuildingKind.Workshop)) || HasPhysicalFactoryChain())) changed = Advance("산업 도시가 완성되었습니다!");
                else if (State.Milestone == 4 && State.Population >= 45 && State.OwnedRegions.Count >= 4 && TechCatalog.Has(State, TechId.UrbanPlanning))
                {
                    State.Milestone++; State.Won = true; changed = true; Notice("리버웍스가 대도시로 성장했습니다! 자유 건설을 계속할 수 있습니다.");
                }
            } while (changed);
            UpdateObjective();
        }

        private bool Advance(string message) { State.Milestone++; State.Coins += 180 + State.Milestone * 40; Add(Resource.Timber, 20); Add(Resource.Stone, 12); SyncCoins(); Notice(message + " 보상이 지급되었습니다."); return true; }

        private void UpdateObjective()
        {
            switch (State.Milestone)
            {
                case 0: ObjectiveTitle = "첫 도로망"; ObjectiveDescription = "시청에 건물 12개를 연결하세요."; ObjectiveProgress = Math.Min(1, ConnectedBuildings / 12f); break;
                case 1: ObjectiveTitle = "먹고사는 도시"; ObjectiveDescription = "인구 16명과 빵 4개를 확보하세요."; ObjectiveProgress = Math.Min(1, Math.Min(State.Population / 16f, Get(Resource.Bread) / 4f)); break;
                case 2: ObjectiveTitle = "새로운 땅"; ObjectiveDescription = "인접 지역 하나를 매입하세요."; ObjectiveProgress = Math.Min(1, (State.OwnedRegions.Count - 1)); break;
                case 3: ObjectiveTitle = "산업 혁명"; ObjectiveDescription = "인구 30명과 도시 광산·제련소·공방의 도구 8개 생산 또는 공장의 광석·강철·도구 생산 및 도구 8개 반출을 달성하세요."; ObjectiveProgress = IndustrialObjectiveProgress(); break;
                case 4: ObjectiveTitle = "대도시의 꿈"; ObjectiveDescription = "도시 계획을 연구하고 인구 45명과 소유 지역 4곳을 달성하세요."; ObjectiveProgress = Math.Min(1, Math.Min(TechCatalog.Has(State, TechId.UrbanPlanning) ? 1 : 0, Math.Min(State.Population / 45f, State.OwnedRegions.Count / 4f))); break;
                default: ObjectiveTitle = "자유 건설"; ObjectiveDescription = "승리했습니다. 원하는 도시를 계속 건설하세요."; ObjectiveProgress = 1; break;
            }
        }

        private void AdvanceResearch()
        {
            if (State.ActiveResearch == TechId.None) return;
            if (State.ResearchDaysRemaining > 0) State.ResearchDaysRemaining--;
            if (State.ResearchDaysRemaining > 0) return;
            TechSpec spec = TechCatalog.Get(State.ActiveResearch);
            if (spec == null) { State.ActiveResearch = TechId.None; return; }
            if (!State.Technologies.Contains(spec.Id)) State.Technologies.Add(spec.Id);
            State.ActiveResearch = TechId.None;
            State.ResearchDaysRemaining = 0;
            if (spec.Id == TechId.Guilds) State.Era = Era.Renaissance;
            if (spec.Id == TechId.SteamPower) State.Era = Era.Industrial;
            Notice($"{spec.Name} 연구 완료 · {spec.Benefit}");
        }
        private bool HasPhysicalFactoryChain() => State.Factory!=null && State.Factory.Produced[(int)Resource.Ore]>0 && State.Factory.Produced[(int)Resource.Steel]>0 && State.Factory.Produced[(int)Resource.Tools]>=8 && State.Factory.Exported[(int)Resource.Tools]>=8;

        private int PreviewSteamPowerCapacity()
        {
            float fuelBudget = Get(Resource.Timber);
            int capacity = 0;
            foreach (Cell steam in State.Cells.Where(c => c.Connected && c.Building == BuildingKind.SteamPlant))
            {
                float fuel = steam.Level * .6f;
                if (fuelBudget + .001f < fuel) continue;
                fuelBudget -= fuel;
                capacity += steam.Level * 18;
            }
            return capacity;
        }

        private float IndustrialObjectiveProgress()
        {
            float population = Math.Min(1, State.Population / 30f);
            float totalTools = Math.Min(1, State.TotalToolsProduced / 8f);
            float cityChain = HasOperatedConnected(BuildingKind.Mine) && HasOperatedConnected(BuildingKind.Smelter) && HasOperatedConnected(BuildingKind.Workshop) ? totalTools : 0;
            float factoryChain = 0;
            if (State.Factory != null)
            {
                float ore = State.Factory.Produced[(int)Resource.Ore] > 0 ? 1 : 0;
                float steel = State.Factory.Produced[(int)Resource.Steel] > 0 ? 1 : 0;
                float tools = Math.Min(1, State.Factory.Produced[(int)Resource.Tools] / 8f);
                float exported = Math.Min(1, State.Factory.Exported[(int)Resource.Tools] / 8f);
                factoryChain = Math.Min(totalTools, Math.Min(Math.Min(ore, steel), Math.Min(tools, exported)));
            }
            return Math.Min(population, Math.Max(cityChain, factoryChain));
        }

        private void Normalize()
        {
            if (State.Version >= 1 && State.Version <= 4) IndustryMigration.UpgradeLegacy(State);
            if (State.Version == 5) ExpansionMigration.Upgrade(State);
            int count = ResourceCatalog.Count;
            State.Stock ??= new List<float>();
            while (State.Stock.Count < count) State.Stock.Add(0);
            if (State.Cells == null || State.Cells.Count != State.Size * State.Size) throw new ArgumentException("맵 셀 수가 올바르지 않습니다.");
            State.OwnedRegions ??= new List<int>();
            SyncCoins();
        }
        private bool NearTerrain(int x, int z, TerrainKind terrain) => GetCell(x, z)?.Terrain == terrain || Adjacent(x, z).Any(c => c.Terrain == terrain);
        private float TechnologyOutputMultiplier(BuildingKind kind)
        {
            float multiplier = 1f;
            if (kind == BuildingKind.Lumberyard && TechCatalog.Has(State, TechId.Forestry)) multiplier *= 1.25f;
            if (kind == BuildingKind.Farm && TechCatalog.Has(State, TechId.Irrigation)) multiplier *= 1.2f;
            if (kind == BuildingKind.Quarry && TechCatalog.Has(State, TechId.Masonry)) multiplier *= 1.25f;
            if ((kind == BuildingKind.Mine || kind == BuildingKind.Smelter) && TechCatalog.Has(State, TechId.MetallurgicalEfficiency)) multiplier *= 1.2f;
            return multiplier;
        }
        private bool HasOperatedConnected(BuildingKind kind) => State.Cells.Any(c => c.Connected && c.Building == kind && c.Progress > 0);
        private IEnumerable<Cell> Adjacent(int x, int z) { for (int i = 0; i < 4; i++) { Cell c = GetCell(x + Dx[i], z + Dz[i]); if (c != null) yield return c; } }
        private void Spend(float coins, float timber, float stone) { State.Coins -= coins; Add(Resource.Timber, -timber); Add(Resource.Stone, -stone); SyncCoins(); }
        private void Add(Resource resource, float amount) { if (resource == Resource.Coins) State.Coins = Math.Max(0, State.Coins + amount); else State.Stock[(int)resource] = Math.Max(0, State.Stock[(int)resource] + amount); }
        private void SyncCoins() => State.Stock[(int)Resource.Coins] = State.Coins;
        private static bool Fail(string message, out string reason) { reason = message; return false; }
        private void Notice(string message) => OnNotice?.Invoke(message);
    }
}
