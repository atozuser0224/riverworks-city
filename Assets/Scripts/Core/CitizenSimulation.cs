using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Riverworks
{
    public enum CitizenActivity { Home, GoingToWork, Working, GoingToLeisure, Leisure, GoingHome }

    public sealed class Citizen
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; } = "";
        public int HomeIndex { get; internal set; } = -1;
        public int JobIndex { get; internal set; } = -1;
        public int LeisureIndex { get; internal set; } = -1;
        public CitizenActivity Activity { get; internal set; }
        public bool Indoors { get; internal set; } = true;
        public float X { get; internal set; }
        public float Z { get; internal set; }
        public int DestinationIndex { get; internal set; } = -1;
        public int CurrentIndex { get; internal set; } = -1;
        public bool IsGuide { get; internal set; }
        public string Persona { get; internal set; } = "";
        public string LastDecisionSource { get; internal set; } = "routine";
        public string LastThought { get; internal set; } = "";
        public string LastMood { get; internal set; } = "calm";
        public string RecentMemory { get; internal set; } = "";
        public string LastSourceModel { get; internal set; } = "";
        public string SourceModel => LastSourceModel;
        public bool HasPendingPlan => pendingPlan != null;
        public bool HasActivePlan => activePlan != null;
        public string PendingIntent => pendingPlan == null ? "" : pendingPlan.Intent;
        public int PendingTargetIndex => pendingPlan == null ? -1 : pendingPlan.Target;
        public string ActiveIntent => activePlan == null ? "" : activePlan.Intent;
        public int ActiveTargetIndex => activePlan == null ? -1 : activePlan.Target;
        public IReadOnlyList<int> Path => path;
        internal readonly List<int> path = new List<int>();
        internal int pathStep;
        internal float segment;
        internal float wait;
        internal CitizenDecisionPlan pendingPlan;
        internal CitizenDecisionPlan activePlan;
    }

    /// <summary>Pure resident identity, assignment, road routing and routine simulation.</summary>
    public sealed class CitizenSimulation
    {
        public const int StreetCapacity = 128;
        public const int MaxDecisionBatchSize = 16;
        const float WalkCellsPerSecond = .72f;
        static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };
        static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임" };
        static readonly string[] Given = { "도윤", "서연", "하준", "지우", "민준", "수아", "예준", "하은", "현우", "유진", "지민", "시우" };
        static readonly string[] PersonaTemperaments = { "차분하고 신중한", "호기심 많고 다정한", "실용적이고 부지런한", "낙천적이고 사교적인", "조용하고 관찰력 있는" };
        static readonly string[] PersonaInterests = { "동네 소식과 산책", "일의 숙련과 도구", "장터와 새로운 음식", "공원과 자연", "도시의 역사와 광장" };
        static readonly HashSet<string> Moods = new HashSet<string>(StringComparer.Ordinal) { "calm", "happy", "worried", "curious", "tired" };
        readonly List<Citizen> residents = new List<Citizen>();
        GameState state;
        int nextId = 1;
        int topologySignature = int.MinValue;
        Citizen guideCitizen;
        bool guideControlled;
        int pendingGuideDestination = -1;

        public CitizenSimulation(GameState state) { Reset(state); }
        public GameState State => state;
        public IReadOnlyList<Citizen> Residents => residents;
        public int ResidentCount => residents.Count;
        public Citizen GuideCitizen => guideCitizen;
        public bool GuideControlled => guideControlled && guideCitizen != null;
        public bool GuideWalking => GuideControlled && guideCitizen.path.Count > 1;

        public void Reset(GameState newState)
        {
            state = newState ?? throw new ArgumentNullException(nameof(newState));
            guideCitizen = null; guideControlled = false; pendingGuideDestination = -1;
            residents.Clear(); nextId = 1; topologySignature = int.MinValue;
            Synchronize();
        }

        public void Synchronize()
        {
            int wanted = Math.Max(0, state.Population);
            while (residents.Count > wanted) residents.RemoveAt(residents.Count - 1);
            if (guideCitizen != null && !residents.Contains(guideCitizen))
            {
                guideCitizen = null; guideControlled = false; pendingGuideDestination = -1;
            }
            while (residents.Count < wanted)
            {
                int id = nextId++;
                residents.Add(new Citizen
                {
                    Id = id,
                    Name = Surnames[(id * 7) % Surnames.Length] + Given[(id * 11) % Given.Length],
                    Persona = PersonaFor(id),
                    wait = (id % 7) * .23f
                });
            }
            int signature = TopologySignature();
            if (signature != topologySignature) { topologySignature = signature; ReassignAll(); }
            else if (residents.Any(r => r.HomeIndex < 0)) ReassignAll();
        }

        public void Advance(float deltaSeconds)
        {
            // The presentation calls Synchronize after world mutations. Population can
            // also change on a simulation tick, so keep that cheap case self-healing.
            if (residents.Count != Math.Max(0, state.Population)) Synchronize();
            int signature = TopologySignature();
            if (signature != topologySignature) { topologySignature = signature; ReassignAll(); }
            if (deltaSeconds <= 0) return;
            // StreetCapacity is a presentation budget. Every resident still advances
            // so a round-robin decision client can cover the aggregate population.
            for (int i = 0; i < residents.Count; i++)
                if (!guideControlled || !ReferenceEquals(residents[i], guideCitizen)) Advance(residents[i], deltaSeconds);
        }

        /// <summary>Designates resident 1 as Danwoo without creating population or changing the city.</summary>
        public bool EnsureGuide()
        {
            if (guideCitizen == null) guideCitizen = FindResident(1);
            if (guideCitizen == null) return false;
            guideCitizen.IsGuide = true;
            guideCitizen.Name = "단우";
            guideCitizen.Persona = "친절한 서기";
            return true;
        }

        /// <summary>Gives tutorial presentation exclusive movement control of the existing guide resident.</summary>
        public bool SetGuideControl(bool enabled)
        {
            if (enabled)
            {
                if (!EnsureGuide()) return false;
                if (!guideControlled)
                {
                    ClearModelState(guideCitizen, false);
                    pendingGuideDestination = -1;
                    guideControlled = true;
                }
                return true;
            }
            if (guideCitizen == null) { guideControlled = false; pendingGuideDestination = -1; return false; }
            guideControlled = false;
            pendingGuideDestination = -1;
            if (!guideCitizen.Indoors && guideCitizen.path.Count < 2 && guideCitizen.HomeIndex >= 0)
                BeginTrip(guideCitizen, guideCitizen.HomeIndex, CitizenActivity.GoingHome);
            return true;
        }

        /// <summary>Walks the guide to a reachable road beside a teaching site, or queues it until the current walk ends.</summary>
        public bool GuideToNear(int x, int z)
        {
            if (!GuideControlled || x < 0 || z < 0 || x >= state.Size || z >= state.Size) return false;
            int origin = GuideWalking ? guideCitizen.DestinationIndex : guideCitizen.CurrentIndex;
            int destination = BestAdjacentGuideRoad(origin, x, z);
            if (destination < 0) return false;
            if (GuideWalking)
            {
                pendingGuideDestination = destination == guideCitizen.DestinationIndex ? -1 : destination;
                return true;
            }
            pendingGuideDestination = -1;
            if (destination == guideCitizen.CurrentIndex)
            {
                StandGuideAt(destination);
                return true;
            }
            return BeginGuideTrip(destination);
        }

        /// <summary>Advances only a controlled guide using presentation-provided unscaled time.</summary>
        public void AdvanceGuide(float unscaledDeltaSeconds)
        {
            if (!GuideControlled || unscaledDeltaSeconds <= 0 || float.IsNaN(unscaledDeltaSeconds) || float.IsInfinity(unscaledDeltaSeconds)) return;
            if (GuideWalking) WalkGuide(unscaledDeltaSeconds);
        }

        /// <summary>Builds at most sixteen deterministic, mutation-free resident facts for a gateway request.</summary>
        public CitizenDecisionBatch BuildDecisionSnapshot(IEnumerable<int> citizenIds)
        {
            if (citizenIds == null) throw new ArgumentNullException(nameof(citizenIds));
            var batch = new CitizenDecisionBatch();
            var included = new HashSet<int>();
            foreach (int id in citizenIds)
            {
                if (batch.residents.Count >= MaxDecisionBatchSize) break;
                if (!included.Add(id)) continue;
                Citizen resident = FindResident(id);
                if (resident == null || (guideControlled && resident.IsGuide)) continue;
                List<CitizenAllowedDecision> allowed = AllowedDecisions(resident);
                // A resident with no legal trip stays on the local routine and must
                // not make the gateway's one-or-more allowed-choice contract invalid.
                if (allowed.Count == 0) continue;
                batch.residents.Add(new CitizenDecisionFacts
                {
                    id = resident.Id.ToString(CultureInfo.InvariantCulture),
                    name = resident.Name,
                    persona = resident.Persona,
                    activity = ActivityName(resident.Activity),
                    home = resident.HomeIndex,
                    job = resident.JobIndex,
                    current = DecisionOrigin(resident),
                    allowed = allowed,
                    recentMemory = resident.RecentMemory
                });
            }
            return batch;
        }

        /// <summary>Validates a single constrained response completely before changing the resident.</summary>
        public bool TryApplyDecision(ResponseDecision decision, string sourceModel, float expiresAfterSeconds, out string reason)
        {
            Citizen resident;
            CitizenDecisionPlan plan;
            if (!TryValidateDecision(decision, sourceModel, expiresAfterSeconds, out resident, out plan, out reason)) return false;

            bool startNow = resident.Indoors;
            if (startNow)
            {
                if (!ActivatePlan(resident, plan)) return FailDecision("decision route changed", out reason);
                resident.pendingPlan = null;
            }
            else resident.pendingPlan = plan;
            resident.LastThought = decision.thought;
            resident.LastMood = decision.mood;
            resident.RecentMemory = decision.memory;
            resident.LastSourceModel = sourceModel;
            resident.LastDecisionSource = "model";
            reason = startNow ? "decision started" : "decision queued";
            return true;
        }

        /// <summary>Clears session-only model state and returns every resident to legacy routine control.</summary>
        public void ClearLlmPlans()
        {
            for (int i = 0; i < residents.Count; i++) ClearModelState(residents[i], true);
        }

        public IReadOnlyList<int> FindPath(int startIndex, int endIndex)
        {
            if (!ValidIndex(startIndex) || !ValidIndex(endIndex)) return Array.Empty<int>();
            if (startIndex == endIndex) return new[] { startIndex };
            var starts = RoadAccess(startIndex); var goals = new HashSet<int>(RoadAccess(endIndex));
            if (starts.Count == 0 || goals.Count == 0) return Array.Empty<int>();
            var previous = new Dictionary<int, int>(); var queue = new Queue<int>();
            foreach (int start in starts) if (!previous.ContainsKey(start)) { previous[start] = -1; queue.Enqueue(start); }
            int found = -1;
            while (queue.Count > 0 && found < 0)
            {
                int current = queue.Dequeue();
                if (goals.Contains(current)) { found = current; break; }
                int x = current % state.Size, z = current / state.Size;
                for (int d = 0; d < 4; d++)
                {
                    int next = Index(x + Dx[d], z + Dz[d]);
                    if (next < 0 || previous.ContainsKey(next) || !IsRoad(next)) continue;
                    previous[next] = current; queue.Enqueue(next);
                }
            }
            if (found < 0) return Array.Empty<int>();
            var reversed = new List<int>();
            for (int at = found; at >= 0; at = previous[at]) reversed.Add(at);
            reversed.Reverse();
            if (reversed[0] != startIndex) reversed.Insert(0, startIndex);
            if (reversed[reversed.Count - 1] != endIndex) reversed.Add(endIndex);
            return reversed;
        }

        public bool IsValidPath(IReadOnlyList<int> path)
        {
            if (path == null || path.Count == 0) return false;
            for (int i = 0; i < path.Count; i++)
            {
                if (!ValidIndex(path[i])) return false;
                if (i > 0)
                {
                    int a = path[i - 1], b = path[i];
                    if (Math.Abs(a % state.Size - b % state.Size) + Math.Abs(a / state.Size - b / state.Size) != 1) return false;
                }
                if (i > 0 && i < path.Count - 1 && !IsRoad(path[i])) return false;
                Cell cell = state.Cells[path[i]];
                if (cell.Terrain == TerrainKind.Water && cell.Building != BuildingKind.Road) return false;
            }
            return true;
        }

        void ReassignAll()
        {
            var homes = state.Cells.Select((c, i) => new { c, i }).Where(v => v.c.Building == BuildingKind.House).SelectMany(v => Enumerable.Repeat(v.i, Math.Max(1, v.c.Level) * 6)).ToList();
            var jobs = state.Cells.Select((c, i) => new { c, i }).Where(v => IsJob(v.c.Building)).Select(v => v.i).ToList();
            var leisure = state.Cells.Select((c, i) => new { c, i }).Where(v => v.c.Building == BuildingKind.Market || v.c.Building == BuildingKind.Park).Select(v => v.i).ToList();
            for (int i = 0; i < residents.Count; i++)
            {
                Citizen r = residents[i]; r.HomeIndex = homes.Count == 0 ? -1 : homes[i % homes.Count];
                r.JobIndex = PickReachable(r.HomeIndex, jobs, r.Id); r.LeisureIndex = PickReachable(r.HomeIndex, leisure, r.Id * 3);
                ClearModelState(r, false);
                if (guideControlled && ReferenceEquals(r, guideCitizen) && CanPreserveControlledGuide(r)) continue;
                PutAt(r, r.HomeIndex); r.CurrentIndex = r.HomeIndex; r.DestinationIndex=r.HomeIndex; r.Activity = CitizenActivity.Home; r.Indoors = true; r.wait = .8f + (r.Id % 9) * .21f; r.path.Clear();
            }
        }

        bool CanPreserveControlledGuide(Citizen resident)
        {
            if (resident.Indoors) return ValidIndex(resident.CurrentIndex) && state.Cells[resident.CurrentIndex].Building != BuildingKind.None;
            if (resident.path.Count > 1) return IsValidPath(resident.path);
            return ValidIndex(resident.CurrentIndex) && state.Cells[resident.CurrentIndex].Building == BuildingKind.Road;
        }

        int PickReachable(int home, List<int> choices, int seed)
        {
            if (home < 0) return -1;
            for (int n = 0; n < choices.Count; n++)
            {
                int value = choices[(seed + n) % choices.Count];
                if (FindPath(home, value).Count > 0) return value;
            }
            return -1;
        }

        void Advance(Citizen r, float dt)
        {
            AgePlans(r, dt);
            if (r.HomeIndex < 0) { r.Indoors = true; return; }
            if (r.activePlan != null && !ValidPlanTarget(r, r.activePlan.Intent, r.activePlan.Target))
            {
                r.activePlan = null;
                r.LastDecisionSource = "routine";
                if (r.Indoors) r.wait = 0;
            }
            if (r.Indoors)
            {
                if (r.activePlan != null)
                {
                    r.wait -= dt;
                    if (r.wait > 0) return;
                    r.activePlan = null;
                    if (TryActivatePending(r)) return;
                    r.LastDecisionSource = "routine";
                }
                else if (TryActivatePending(r)) return;
                r.wait -= dt;
                if (r.wait > 0) return;
                if (r.Activity == CitizenActivity.Home)
                {
                    if (r.JobIndex >= 0 && BeginTrip(r, r.JobIndex, CitizenActivity.GoingToWork)) return;
                    r.wait = 2f;
                }
                else if (r.Activity == CitizenActivity.Working)
                {
                    if (r.LeisureIndex >= 0 && BeginTrip(r, r.LeisureIndex, CitizenActivity.GoingToLeisure)) return;
                    BeginTrip(r, r.HomeIndex, CitizenActivity.GoingHome);
                }
                else if (r.Activity == CitizenActivity.Leisure) BeginTrip(r, r.HomeIndex, CitizenActivity.GoingHome);
                return;
            }
            if (r.path.Count < 2)
            {
                if (r.HomeIndex >= 0) BeginTrip(r, r.HomeIndex, CitizenActivity.GoingHome);
                return;
            }
            Walk(r, dt);
        }

        bool BeginTrip(Citizen r, int destination, CitizenActivity activity)
        {
            int origin = r.CurrentIndex;
            var route = FindPath(origin, destination);
            if (route.Count < 2) return false;
            r.path.Clear(); r.path.AddRange(route); r.pathStep = 0; r.segment = 0; r.DestinationIndex = destination; r.Activity = activity; r.Indoors = false; PutAt(r, route[0]);
            return true;
        }

        void Walk(Citizen r, float dt)
        {
            float remaining = dt * WalkCellsPerSecond;
            while (remaining > 0 && !r.Indoors)
            {
                float take = Math.Min(remaining, 1f - r.segment); r.segment += take; remaining -= take;
                int a = r.path[r.pathStep], b = r.path[r.pathStep + 1];
                r.X = Lerp(a % state.Size, b % state.Size, r.segment); r.Z = Lerp(a / state.Size, b / state.Size, r.segment);
                if (r.segment < .9999f) continue;
                r.pathStep++; r.segment = 0;
                if (r.pathStep < r.path.Count - 1) continue;
                PutAt(r, r.DestinationIndex); r.CurrentIndex = r.DestinationIndex; r.Indoors = true; r.path.Clear();
                CitizenDecisionPlan arrivedPlan = r.activePlan;
                if (arrivedPlan != null && arrivedPlan.Target == r.CurrentIndex)
                {
                    SetArrivedActivity(r, arrivedPlan.Intent);
                    r.wait = arrivedPlan.DwellSeconds;
                }
                else
                {
                    if (r.Activity == CitizenActivity.GoingToWork) { r.Activity = CitizenActivity.Working; r.wait = 3.2f + (r.Id % 5) * .3f; }
                    else if (r.Activity == CitizenActivity.GoingToLeisure) { r.Activity = CitizenActivity.Leisure; r.wait = 1.7f + (r.Id % 4) * .25f; }
                    else { r.Activity = CitizenActivity.Home; r.wait = 2.2f + (r.Id % 8) * .2f; }
                    if (TryActivatePending(r)) return;
                }
            }
        }

        int BestAdjacentGuideRoad(int origin, int siteX, int siteZ)
        {
            if (!ValidIndex(origin)) return -1;
            int best = -1, bestLength = int.MaxValue;
            for (int d = 0; d < 4; d++)
            {
                int candidate = Index(siteX + Dx[d], siteZ + Dz[d]);
                if (candidate < 0 || state.Cells[candidate].Building != BuildingKind.Road || !IsOwned(candidate)) continue;
                IReadOnlyList<int> route = FindPath(origin, candidate);
                if (route.Count == 0 || !IsValidPath(route)) continue;
                if (route.Count < bestLength || (route.Count == bestLength && candidate < best))
                {
                    best = candidate;
                    bestLength = route.Count;
                }
            }
            return best;
        }

        bool BeginGuideTrip(int destination)
        {
            if (!GuideControlled || !ValidIndex(destination) || state.Cells[destination].Building != BuildingKind.Road) return false;
            IReadOnlyList<int> route = FindPath(guideCitizen.CurrentIndex, destination);
            if (route.Count < 2 || !IsValidPath(route)) return false;
            guideCitizen.path.Clear(); guideCitizen.path.AddRange(route); guideCitizen.pathStep = 0; guideCitizen.segment = 0;
            guideCitizen.DestinationIndex = destination; guideCitizen.Activity = CitizenActivity.GoingToLeisure; guideCitizen.Indoors = false;
            guideCitizen.LastDecisionSource = "routine";
            PutAt(guideCitizen, route[0]);
            return true;
        }

        void WalkGuide(float dt)
        {
            float remaining = dt * WalkCellsPerSecond;
            while (remaining > 0 && GuideWalking)
            {
                float take = Math.Min(remaining, 1f - guideCitizen.segment);
                guideCitizen.segment += take;
                remaining -= take;
                int a = guideCitizen.path[guideCitizen.pathStep], b = guideCitizen.path[guideCitizen.pathStep + 1];
                guideCitizen.X = Lerp(a % state.Size, b % state.Size, guideCitizen.segment);
                guideCitizen.Z = Lerp(a / state.Size, b / state.Size, guideCitizen.segment);
                if (guideCitizen.segment < .9999f) continue;
                guideCitizen.pathStep++;
                guideCitizen.segment = 0;
                if (guideCitizen.pathStep < guideCitizen.path.Count - 1) continue;

                int arrived = guideCitizen.DestinationIndex;
                StandGuideAt(arrived);
                int queued = pendingGuideDestination;
                pendingGuideDestination = -1;
                if (queued >= 0 && queued != arrived && BeginGuideTrip(queued)) continue;
                break;
            }
        }

        void StandGuideAt(int index)
        {
            PutAt(guideCitizen, index);
            guideCitizen.CurrentIndex = index;
            guideCitizen.DestinationIndex = index;
            guideCitizen.Indoors = false;
            guideCitizen.Activity = CitizenActivity.Leisure;
            guideCitizen.path.Clear();
            guideCitizen.pathStep = 0;
            guideCitizen.segment = 0;
        }

        bool TryValidateDecision(ResponseDecision decision, string sourceModel, float expiresAfterSeconds,
            out Citizen resident, out CitizenDecisionPlan plan, out string reason)
        {
            resident = null;
            plan = null;
            if (decision == null) return FailDecision("decision is required", out reason);
            int citizenId;
            if (!int.TryParse(decision.id, NumberStyles.None, CultureInfo.InvariantCulture, out citizenId) || citizenId <= 0 ||
                !string.Equals(decision.id, citizenId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return FailDecision("citizen id is invalid", out reason);
            resident = FindResident(citizenId);
            if (resident == null) return FailDecision("unknown citizen", out reason);
            if (guideControlled && resident.IsGuide) return FailDecision("guide is under tutorial control", out reason);
            if (!Finite(decision.dwellSeconds) || decision.dwellSeconds < 3f || decision.dwellSeconds > 30f || decision.dwellSeconds != (float)Math.Floor(decision.dwellSeconds))
                return FailDecision("dwellSeconds must be an integer between 3 and 30", out reason);
            if (!Finite(expiresAfterSeconds) || expiresAfterSeconds <= 0)
                return FailDecision("expiresAfterSeconds must be positive and finite", out reason);
            if (!ValidText(decision.thought, 100)) return FailDecision("thought is invalid", out reason);
            if (!ValidText(decision.memory, 160)) return FailDecision("memory is invalid", out reason);
            if (!ValidText(sourceModel, 80) || sourceModel.Length == 0) return FailDecision("sourceModel is invalid", out reason);
            if (decision.mood == null || !Moods.Contains(decision.mood)) return FailDecision("mood is invalid", out reason);

            List<CitizenAllowedDecision> allowed = AllowedDecisions(resident);
            bool exactAllowed = allowed.Any(a => string.Equals(a.intent, decision.intent, StringComparison.Ordinal) && a.target == decision.target);
            if (!exactAllowed) return FailDecision("intent and target are not allowed", out reason);
            int origin = DecisionOrigin(resident);
            IReadOnlyList<int> route = FindPath(origin, decision.target);
            if (route.Count < 2 || !IsValidPath(route)) return FailDecision("target is not reachable", out reason);

            plan = new CitizenDecisionPlan
            {
                Intent = decision.intent,
                Target = decision.target,
                Origin = origin,
                DwellSeconds = decision.dwellSeconds,
                ExpiresAfterSeconds = expiresAfterSeconds
            };
            plan.Route.AddRange(route);
            reason = "decision valid";
            return true;
        }

        List<CitizenAllowedDecision> AllowedDecisions(Citizen resident)
        {
            var result = new List<CitizenAllowedDecision>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int origin = DecisionOrigin(resident);
            if (!ValidIndex(origin) || !IsOwned(origin)) return result;
            AddAllowed(result, keys, resident, "home", resident.HomeIndex);
            AddAllowed(result, keys, resident, "work", resident.JobIndex);
            for (int i = 0; i < state.Cells.Count; i++)
            {
                BuildingKind kind = state.Cells[i].Building;
                if (kind == BuildingKind.Market) AddAllowed(result, keys, resident, "market", i);
                else if (kind == BuildingKind.Park) AddAllowed(result, keys, resident, "park", i);
                else if (kind == BuildingKind.TownHall) AddAllowed(result, keys, resident, "square", i);
            }
            return result;
        }

        void AddAllowed(List<CitizenAllowedDecision> result, HashSet<string> keys, Citizen resident, string intent, int target)
        {
            if (result.Count >= 16) return;
            int origin = DecisionOrigin(resident);
            if (target == origin || !ValidPlanTarget(resident, intent, target)) return;
            IReadOnlyList<int> route = FindPath(origin, target);
            if (route.Count < 2 || !IsValidPath(route)) return;
            string key = intent + ":" + target;
            if (keys.Add(key)) result.Add(new CitizenAllowedDecision(intent, target));
        }

        bool ValidPlanTarget(Citizen resident, string intent, int target)
        {
            if (!ValidIndex(target) || !IsOwned(target)) return false;
            BuildingKind kind = state.Cells[target].Building;
            if (string.Equals(intent, "home", StringComparison.Ordinal)) return target == resident.HomeIndex && kind == BuildingKind.House;
            if (string.Equals(intent, "work", StringComparison.Ordinal)) return target == resident.JobIndex && IsJob(kind);
            if (string.Equals(intent, "market", StringComparison.Ordinal)) return kind == BuildingKind.Market;
            if (string.Equals(intent, "park", StringComparison.Ordinal)) return kind == BuildingKind.Park;
            if (string.Equals(intent, "square", StringComparison.Ordinal)) return kind == BuildingKind.TownHall;
            return false;
        }

        bool ActivatePlan(Citizen resident, CitizenDecisionPlan plan)
        {
            if (plan == null || resident.CurrentIndex != plan.Origin || !ValidPlanTarget(resident, plan.Intent, plan.Target) ||
                plan.Route.Count < 2 || plan.Route[0] != resident.CurrentIndex || plan.Route[plan.Route.Count - 1] != plan.Target || !IsValidPath(plan.Route))
                return false;
            resident.activePlan = plan;
            resident.path.Clear(); resident.path.AddRange(plan.Route); resident.pathStep = 0; resident.segment = 0;
            resident.DestinationIndex = plan.Target; resident.Activity = TravelActivity(plan.Intent); resident.Indoors = false; PutAt(resident, plan.Route[0]);
            resident.LastDecisionSource = "model";
            return true;
        }

        bool TryActivatePending(Citizen resident)
        {
            CitizenDecisionPlan plan = resident.pendingPlan;
            if (plan == null) return false;
            resident.pendingPlan = null;
            if (plan.AgeSeconds >= plan.ExpiresAfterSeconds || resident.CurrentIndex != plan.Origin || !ActivatePlan(resident, plan))
            {
                resident.LastDecisionSource = "routine";
                return false;
            }
            return true;
        }

        void AgePlans(Citizen resident, float dt)
        {
            if (resident.activePlan != null)
            {
                resident.activePlan.AgeSeconds += dt;
                if (resident.activePlan.AgeSeconds >= resident.activePlan.ExpiresAfterSeconds)
                {
                    resident.activePlan = null;
                    resident.LastDecisionSource = "routine";
                    if (resident.Indoors) resident.wait = 0;
                }
            }
            if (resident.pendingPlan != null)
            {
                resident.pendingPlan.AgeSeconds += dt;
                if (resident.pendingPlan.AgeSeconds >= resident.pendingPlan.ExpiresAfterSeconds) resident.pendingPlan = null;
            }
        }

        void ClearModelState(Citizen resident, bool clearDiagnostics)
        {
            resident.pendingPlan = null;
            resident.activePlan = null;
            resident.LastDecisionSource = "routine";
            if (resident.Indoors) resident.wait = 0;
            if (!clearDiagnostics) return;
            resident.LastThought = "";
            resident.LastMood = "calm";
            resident.RecentMemory = "";
            resident.LastSourceModel = "";
        }

        Citizen FindResident(int id)
        {
            for (int i = 0; i < residents.Count; i++) if (residents[i].Id == id) return residents[i];
            return null;
        }

        int DecisionOrigin(Citizen resident) => resident.Indoors ? resident.CurrentIndex : resident.DestinationIndex;
        bool IsOwned(int index)
        {
            if (!ValidIndex(index) || state.OwnedRegions == null) return false;
            int x = index % state.Size, z = index / state.Size;
            return state.OwnedRegions.Contains((z / 7) * 3 + x / 7);
        }
        static CitizenActivity TravelActivity(string intent) => string.Equals(intent, "work", StringComparison.Ordinal) ? CitizenActivity.GoingToWork
            : string.Equals(intent, "home", StringComparison.Ordinal) ? CitizenActivity.GoingHome : CitizenActivity.GoingToLeisure;
        static void SetArrivedActivity(Citizen resident, string intent)
        {
            resident.Activity = string.Equals(intent, "work", StringComparison.Ordinal) ? CitizenActivity.Working
                : string.Equals(intent, "home", StringComparison.Ordinal) ? CitizenActivity.Home : CitizenActivity.Leisure;
        }
        static string ActivityName(CitizenActivity activity)
        {
            switch (activity)
            {
                case CitizenActivity.GoingToWork: return "going_to_work";
                case CitizenActivity.Working: return "working";
                case CitizenActivity.GoingToLeisure: return "going_to_leisure";
                case CitizenActivity.Leisure: return "leisure";
                case CitizenActivity.GoingHome: return "going_home";
                default: return "home";
            }
        }
        static string PersonaFor(int id) => PersonaTemperaments[(id * 7) % PersonaTemperaments.Length] + " 주민. 관심사는 " + PersonaInterests[(id * 11) % PersonaInterests.Length] + ".";
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool ValidText(string value, int maximumLength)
        {
            if (value == null || value.Length > maximumLength) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
        static bool FailDecision(string message, out string reason) { reason = message; return false; }

        void PutAt(Citizen r, int index) { if (!ValidIndex(index)) return; r.X = index % state.Size; r.Z = index / state.Size; }
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        bool ValidIndex(int index) => index >= 0 && index < state.Cells.Count;
        int Index(int x, int z) => x < 0 || z < 0 || x >= state.Size || z >= state.Size ? -1 : z * state.Size + x;
        bool IsRoad(int index) { BuildingKind k = state.Cells[index].Building; return k == BuildingKind.Road || k == BuildingKind.TownHall; }
        List<int> RoadAccess(int index)
        {
            var result = new List<int>(); if (IsRoad(index)) { result.Add(index); return result; }
            int x = index % state.Size, z = index / state.Size;
            for (int d = 0; d < 4; d++) { int adjacent = Index(x + Dx[d], z + Dz[d]); if (adjacent >= 0 && IsRoad(adjacent)) result.Add(adjacent); }
            return result;
        }
        static bool IsJob(BuildingKind kind) => kind != BuildingKind.None && kind != BuildingKind.Road && kind != BuildingKind.House && kind != BuildingKind.TownHall && kind != BuildingKind.Park && kind != BuildingKind.Market;
        int TopologySignature()
        {
            unchecked
            {
                int hash = state.Size * 397;
                for (int i = 0; i < state.Cells.Count; i++) { Cell c = state.Cells[i]; hash = hash * 31 + (int)c.Building; hash = hash * 31 + c.Level; hash = hash * 31 + (int)c.Terrain; }
                return hash;
            }
        }
    }
}
