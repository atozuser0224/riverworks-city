using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Riverworks
{
    public enum ResidentAiNetworkMode
    {
        CouldNotConnect,
        Rule,
        LLM
    }

    [Serializable]
    internal sealed class ResidentAiCityFacts
    {
        public string era = "";
        public float happiness;
        public float grain;
        public float bread;
        public float coins;
    }

    [Serializable]
    internal sealed class ResidentAiPlanRequest
    {
        public int protocol = 1;
        public string sessionId = "";
        public string requestId = "";
        public int generation;
        public int simulationDay;
        public ResidentAiCityFacts city = new ResidentAiCityFacts();
        public CitizenDecisionFacts[] residents = Array.Empty<CitizenDecisionFacts>();
    }

    [Serializable]
    internal sealed class ResidentAiUsage
    {
        public int promptTokens = -1;
        public int completionTokens = -1;
        public float estimatedUsd = -1f;
    }

    [Serializable]
    internal sealed class ResidentAiPlanResponse
    {
        public int protocol = -1;
        public string requestId = "";
        public int generation = -1;
        public string source = "";
        public string model = "";
        public string generatedAt = "";
        public ResponseDecision[] decisions = Array.Empty<ResponseDecision>();
        public ResidentAiUsage usage = new ResidentAiUsage();
    }

    [Serializable]
    internal sealed class ResidentAiHealthResponse
    {
        public bool ready = false;
        public string provider = "";
        public string model = "";
    }

    /// <summary>
    /// Sends constrained, round-robin resident facts to the local Riverworks gateway.
    /// Provider credentials deliberately never enter this process; an optional gateway
    /// bearer token lives only in this component's memory.
    /// </summary>
    public sealed class ResidentAiClient : MonoBehaviour
    {
        public const string GatewayUrlPlayerPrefsKey = "Riverworks.ResidentAi.GatewayUrl.v1";
        public const float MinimumPlanCadenceSeconds = 30f;
        public const float PlanTimeToLiveSeconds = 90f;
        public const int PlanTimeoutSeconds = 35;
        const int HealthTimeoutSeconds = 10;
        const int MaximumResponseBytes = 128 * 1024;

        GameController controller;
        GameState boundState;
        CitizenSimulation boundSimulation;
        ResidentAiPanel panel;
        UnityWebRequest activeRequest;
        string gatewayUrl = "";
        string gatewayAccessToken = "";
        string sessionId = "";
        int generation;
        int roundRobinCursor;
        int healthFailureStreak;
        float nextHealthAt;
        float nextPlanAt;
        bool initialized;
        bool enabledForSession = true;
        bool requestInFlight;
        bool activePlanRequest;
        bool healthKnown;
        bool gatewayReady;
        bool rootTestEndpoint;

        public event Action Changed;

        public bool EnabledForSession => enabledForSession;
        public bool RequestInFlight => requestInFlight;
        public bool GatewayReady => healthKnown && gatewayReady;
        public string GatewayUrl => gatewayUrl;
        public string Status { get; private set; } = "규칙 기반 행동";
        public string StatusReason { get; private set; } = "게이트웨이 연결을 확인하기 전입니다.";
        public string Model { get; private set; } = "";
        public int AppliedDecisionCount { get; private set; }
        public int LastAppliedDecisionCount { get; private set; }
        public int FailedRequestCount { get; private set; }
        public string LastResponseSource { get; private set; } = "";
        public string LastResponseRequestId { get; private set; } = "";
        public float LastResponseLatency { get; private set; }
        public float LastResponseLatencyMilliseconds => LastResponseLatency;
        public int LastPromptTokens { get; private set; }
        public int LastCompletionTokens { get; private set; }
        public float LastEstimatedUsd { get; private set; }
        public ResidentAiNetworkMode CurrentNetworkMode { get; private set; } = ResidentAiNetworkMode.Rule;
        public bool RootTestEndpoint => rootTestEndpoint;

        public void Initialize(GameController game, bool allowRootTestEndpoint = false)
        {
            CancelActiveRequest();
            controller = game;
            initialized = controller != null;
            rootTestEndpoint = allowRootTestEndpoint || HasCommandLineFlag("-riverworks-resident-ai-smoke");
            sessionId = Guid.NewGuid().ToString("N");
            generation = 0;
            roundRobinCursor = 0;
            boundState = controller == null ? null : controller.State;
            boundSimulation = CurrentSimulation();
            gatewayUrl = LoadGatewayUrl();
            healthKnown = false;
            gatewayReady = false;
            healthFailureStreak = 0;
            nextHealthAt = 0f;
            nextPlanAt = 0f;
            AppliedDecisionCount = 0;
            LastAppliedDecisionCount = 0;
            FailedRequestCount = 0;
            ResetLastResponseTelemetry();
            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", InitialReason());
        }

        void Update()
        {
            if (!initialized || controller == null) return;
            if (ObserveCityIdentityChange()) return;
            if (requestInFlight)
            {
                string interruptionReason;
                if (activePlanRequest && PlanningBlocked(out interruptionReason))
                {
                    AdvanceGeneration();
                    CancelActiveRequest();
                    ClearLlmPlans();
                    SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", interruptionReason);
                }
                return;
            }

            if (!enabledForSession)
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "주민 AI가 이 실행 세션에서 꺼져 있습니다.");
                return;
            }
            if (!NetworkUseAllowed())
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "일반 스모크 테스트에서는 네트워크 요청을 보내지 않습니다.");
                return;
            }
            if (string.IsNullOrEmpty(gatewayUrl))
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "게이트웨이 주소를 설정하면 연결 상태를 확인할 수 있습니다.");
                return;
            }

            float now = Time.unscaledTime;
            if ((!healthKnown || !gatewayReady) && now >= nextHealthAt)
            {
                StartCoroutine(SendHealthRequest());
                return;
            }
            if (!gatewayReady) return;

            string blockedReason;
            if (PlanningBlocked(out blockedReason))
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", blockedReason);
                return;
            }
            if (now >= nextPlanAt) StartCoroutine(SendPlanRequest());
        }

        void OnDisable()
        {
            if (!initialized) return;
            AdvanceGeneration();
            CancelActiveRequest();
            ClearLlmPlans();
            ResetLastResponseTelemetry();
            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "주민 AI 클라이언트가 비활성화되었습니다.");
        }

        void OnDestroy()
        {
            CancelActiveRequest();
            gatewayAccessToken = "";
        }

        public void OpenSettings()
        {
            if (controller == null || controller.CityHud == null) return;
            if (panel == null)
            {
                GameObject host = new GameObject("Resident AI panel", typeof(RectTransform));
                host.transform.SetParent(controller.CityHud.transform, false);
                RectTransform hostRect = (RectTransform)host.transform;
                hostRect.anchorMin = Vector2.zero;
                hostRect.anchorMax = Vector2.one;
                hostRect.offsetMin = Vector2.zero;
                hostRect.offsetMax = Vector2.zero;
                panel = host.AddComponent<ResidentAiPanel>();
                panel.Initialize(controller, this, host.transform);
            }
            panel.Show();
        }

        public void CloseSettings()
        {
            if (panel != null) panel.Hide();
        }

        public void SetEnabled(bool value)
        {
            if (enabledForSession == value) return;
            enabledForSession = value;
            AdvanceGeneration();
            CancelActiveRequest();
            ClearLlmPlans();
            ResetLastResponseTelemetry();
            if (value)
            {
                healthKnown = false;
                gatewayReady = false;
                healthFailureStreak = 0;
                nextHealthAt = 0f;
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "주민 AI 연결을 다시 확인합니다.");
            }
            else SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "주민 AI가 이 실행 세션에서 꺼져 있습니다.");
        }

        public bool TrySetGatewayUrl(string value, out string reason)
        {
            string normalized;
            if (!TryNormalizeGatewayUrl(value, out normalized, out reason))
            {
                SetStatus(ResidentAiNetworkMode.CouldNotConnect, "연결할 수 없음", reason);
                return false;
            }
            if (string.Equals(gatewayUrl, normalized, StringComparison.Ordinal))
            {
                reason = string.IsNullOrEmpty(normalized) ? "게이트웨이 주소가 비어 있습니다." : "게이트웨이 주소를 사용합니다.";
                return true;
            }

            gatewayUrl = normalized;
            PlayerPrefs.SetString(GatewayUrlPlayerPrefsKey, gatewayUrl);
            PlayerPrefs.Save();
            AdvanceGeneration();
            CancelActiveRequest();
            ClearLlmPlans();
            ResetLastResponseTelemetry();
            Model = "";
            healthKnown = false;
            gatewayReady = false;
            healthFailureStreak = 0;
            nextHealthAt = 0f;
            nextPlanAt = 0f;
            reason = string.IsNullOrEmpty(gatewayUrl) ? "게이트웨이 주소를 비웠습니다." : "게이트웨이 주소를 저장했습니다.";
            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", reason);
            return true;
        }

        public bool SetGatewayAccessToken(string value)
        {
            value = value ?? "";
            if(value.StartsWith("sk-or-",StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(ResidentAiNetworkMode.CouldNotConnect,"연결할 수 없음","OpenRouter API 키는 게임이 아니라 서버의 환경 설정에 넣어 주세요.");
                return false;
            }
            if (value.Length > 512 || ContainsHeaderControlCharacter(value))
            {
                gatewayAccessToken = "";
                AdvanceGeneration();
                CancelActiveRequest();
                ClearLlmPlans();
                ResetLastResponseTelemetry();
                SetStatus(ResidentAiNetworkMode.CouldNotConnect, "연결할 수 없음", "게이트웨이 접근 토큰 형식이 올바르지 않습니다.");
                return false;
            }
            if (string.Equals(gatewayAccessToken, value, StringComparison.Ordinal)) return true;
            gatewayAccessToken = value;
            AdvanceGeneration();
            CancelActiveRequest();
            ClearLlmPlans();
            ResetLastResponseTelemetry();
            healthKnown = false;
            gatewayReady = false;
            healthFailureStreak = 0;
            nextHealthAt = 0f;
            return true;
        }

        public void RequestHealthNow()
        {
            if (!CanBeginNetworkRequest("연결 확인")) return;
            StartCoroutine(SendHealthRequest());
        }

        /// <summary>
        /// Explicit planning hook for the central runtime check. Call with StartCoroutine.
        /// It still observes the 30-second cadence and every gameplay/lifecycle fence.
        /// </summary>
        public IEnumerator RequestPlanNow()
        {
            if (!CanBeginNetworkRequest("주민 계획 요청")) yield break;
            if (!healthKnown || !gatewayReady)
            {
                yield return SendHealthRequest();
                if (!gatewayReady) yield break;
            }
            if (requestInFlight) yield break;
            if (Time.unscaledTime < nextPlanAt)
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "다음 요청 주기까지 기다리는 중입니다.");
                yield break;
            }
            string blockedReason;
            if (PlanningBlocked(out blockedReason))
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", blockedReason);
                yield break;
            }
            yield return SendPlanRequest();
        }

        public void ClearSessionMemory()
        {
            AdvanceGeneration();
            CancelActiveRequest();
            ClearLlmPlans();
            AppliedDecisionCount = 0;
            LastAppliedDecisionCount = 0;
            ResetLastResponseTelemetry();
            nextPlanAt = Time.unscaledTime + MinimumPlanCadenceSeconds;
            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "세션의 주민 AI 계획과 게임 속 기억을 지웠습니다.");
        }

        IEnumerator SendHealthRequest()
        {
            if (requestInFlight || string.IsNullOrEmpty(gatewayUrl)) yield break;
            requestInFlight = true;
            activePlanRequest = false;
            UnityWebRequest request = UnityWebRequest.Get(gatewayUrl + "/health");
            request.timeout = HealthTimeoutSeconds;
            request.redirectLimit = 0;
            AddGatewayAuthorization(request);
            activeRequest = request;
            SetStatus(ResidentAiNetworkMode.Rule, "연결 확인 중", "게이트웨이 준비 상태만 확인합니다.");

            yield return request.SendWebRequest();
            bool stillCurrent = ReferenceEquals(request, activeRequest);
            if (stillCurrent)
            {
                activeRequest = null;
                requestInFlight = false;
                activePlanRequest = false;
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                {
                    RecordHealthFailure(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError,
                        request.responseCode);
                }
                else
                {
                    string body = request.downloadHandler == null ? "" : request.downloadHandler.text;
                    ResidentAiHealthResponse health;
                    if (!WithinResponseLimit(body) || !TryParseHealth(body, out health))
                    {
                        RecordHealthFailure(false, 0);
                    }
                    else
                    {
                        healthKnown = true;
                        gatewayReady = health.ready;
                        Model = health.model;
                        healthFailureStreak = 0;
                        if (gatewayReady)
                        {
                            nextHealthAt = float.PositiveInfinity;
                            SetStatus(ResidentAiNetworkMode.Rule, "연결됨 · 규칙 기반 행동", "게이트웨이가 준비되었습니다. 다음 주민 계획을 기다립니다.");
                        }
                        else
                        {
                            nextHealthAt = Time.unscaledTime + 60f;
                            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "게이트웨이에 클라우드 제공자 키가 설정되지 않았습니다.");
                        }
                    }
                }
                NotifyChanged();
            }
            request.Dispose();
        }

        IEnumerator SendPlanRequest()
        {
            if (requestInFlight || controller == null || controller.Citizens == null) yield break;
            CitizenSimulation simulation = CurrentSimulation();
            GameState state = controller.State;
            if (simulation == null || state == null) yield break;

            CitizenDecisionBatch snapshot = BuildNextBatch(simulation);
            if (snapshot.residents == null || snapshot.residents.Count == 0)
            {
                nextPlanAt = Time.unscaledTime + MinimumPlanCadenceSeconds;
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "현재 이동 가능한 주민 계획이 없어 기본 행동을 사용합니다.");
                yield break;
            }

            int requestGeneration = AdvanceGeneration();
            string requestId = Guid.NewGuid().ToString("N");
            ResidentAiPlanRequest envelope = BuildRequest(state, snapshot, requestId, requestGeneration);
            string payload = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            float sentAt = Time.realtimeSinceStartup;
            nextPlanAt = Time.unscaledTime + MinimumPlanCadenceSeconds;

            UnityWebRequest request = new UnityWebRequest(gatewayUrl + "/v1/citizens/plan", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            AddGatewayAuthorization(request);
            request.timeout = PlanTimeoutSeconds;
            request.redirectLimit = 0;
            requestInFlight = true;
            activePlanRequest = true;
            activeRequest = request;
            SetStatus(ResidentAiNetworkMode.Rule, "주민 계획 요청 중", "응답이 검증될 때까지 주민은 규칙 기반 행동을 계속합니다.");

            yield return request.SendWebRequest();
            bool stillCurrent = ReferenceEquals(request, activeRequest);
            if (stillCurrent)
            {
                activeRequest = null;
                requestInFlight = false;
                activePlanRequest = false;
                float latencyMs = Mathf.Max(0f, (Time.realtimeSinceStartup - sentAt) * 1000f);
                bool identityMatches = ReferenceEquals(state, controller.State) && requestGeneration == generation &&
                    ReferenceEquals(simulation, CurrentSimulation());
                if (!identityMatches)
                {
                    ClearLlmPlans();
                    SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "도시가 바뀌어 이전 응답을 폐기했습니다.");
                }
                else if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                {
                    HandlePlanFailure(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError,
                        request.responseCode);
                }
                else
                {
                    string body = request.downloadHandler == null ? "" : request.downloadHandler.text;
                    ResidentAiPlanResponse response;
                    string validationReason;
                    if (!WithinResponseLimit(body) || !TryParseAndValidateResponse(body, envelope, simulation, out response, out validationReason))
                    {
                        FailedRequestCount++;
                        ClearLlmPlans();
                        SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "게이트웨이 응답이 게임 규칙 검증을 통과하지 못했습니다.");
                    }
                    else if (!ApplyValidatedBatch(response, simulation, out validationReason))
                    {
                        FailedRequestCount++;
                        simulation.ClearLlmPlans();
                        SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "도시 상태가 달라져 주민 계획 배치 전체를 폐기했습니다.");
                    }
                    else
                    {
                        AppliedDecisionCount += response.decisions.Length;
                        LastAppliedDecisionCount = response.decisions.Length;
                        LastResponseSource = response.source;
                        LastResponseRequestId = response.requestId;
                        LastResponseLatency = latencyMs;
                        LastPromptTokens = response.usage.promptTokens;
                        LastCompletionTokens = response.usage.completionTokens;
                        LastEstimatedUsd = response.usage.estimatedUsd;
                        Model = response.model;
                        healthKnown = true;
                        gatewayReady = true;
                        healthFailureStreak = 0;
                        SetStatus(ResidentAiNetworkMode.LLM, "LLM 주민 계획 적용", response.decisions.Length + "명의 게임 속 계획을 검증해 적용했습니다.");
                    }
                }
                NotifyChanged();
            }
            request.Dispose();
        }

        CitizenDecisionBatch BuildNextBatch(CitizenSimulation simulation)
        {
            IReadOnlyList<Citizen> residents = simulation.Residents;
            if (residents == null || residents.Count == 0) return new CitizenDecisionBatch();
            int take = Math.Min(CitizenSimulation.MaxDecisionBatchSize, residents.Count);
            var ids = new List<int>(take);
            for (int i = 0; i < take; i++) ids.Add(residents[(roundRobinCursor + i) % residents.Count].Id);
            roundRobinCursor = (roundRobinCursor + take) % residents.Count;
            return simulation.BuildDecisionSnapshot(ids);
        }

        ResidentAiPlanRequest BuildRequest(GameState state, CitizenDecisionBatch snapshot, string requestId, int requestGeneration)
        {
            return new ResidentAiPlanRequest
            {
                sessionId = sessionId,
                requestId = requestId,
                generation = requestGeneration,
                simulationDay = Math.Max(0, state.Day),
                city = new ResidentAiCityFacts
                {
                    era = state.Era.ToString().ToLowerInvariant(),
                    happiness = Mathf.Clamp(state.Happiness, 0, 100),
                    grain = SafeStock(state, Resource.Grain),
                    bread = SafeStock(state, Resource.Bread),
                    coins = Mathf.Max(0f, state.Coins)
                },
                residents = snapshot.residents.ToArray()
            };
        }

        bool TryParseAndValidateResponse(string json, ResidentAiPlanRequest request, CitizenSimulation simulation,
            out ResidentAiPlanResponse response, out string reason)
        {
            response = null;
            reason = "invalid response";
            JsonShape root;
            if (!JsonShapeParser.TryParse(json, out root) || !ResidentAiProtocolShape.IsPlanResponse(root)) return false;
            try { response = JsonUtility.FromJson<ResidentAiPlanResponse>(json); }
            catch { return false; }
            if (response == null || response.protocol != 1 || response.generation != request.generation ||
                !string.Equals(response.requestId, request.requestId, StringComparison.Ordinal) ||
                !string.Equals(response.source, "openrouter", StringComparison.Ordinal) ||
                !ValidText(response.model, 1, 80) || !ValidText(response.generatedAt, 1, 64) || response.usage == null ||
                response.usage.promptTokens < 0 || response.usage.completionTokens < 0 ||
                !Finite(response.usage.estimatedUsd) || response.usage.estimatedUsd < 0f)
                return false;

            DateTimeOffset generated;
            if (!DateTimeOffset.TryParse(response.generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out generated)) return false;
            if (response.decisions == null || response.decisions.Length != request.residents.Length) return false;

            var requested = new Dictionary<string, CitizenDecisionFacts>(StringComparer.Ordinal);
            for (int i = 0; i < request.residents.Length; i++)
            {
                CitizenDecisionFacts facts = request.residents[i];
                if (facts == null || string.IsNullOrEmpty(facts.id) || requested.ContainsKey(facts.id)) return false;
                requested.Add(facts.id, facts);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < response.decisions.Length; i++)
            {
                ResponseDecision decision = response.decisions[i];
                CitizenDecisionFacts facts;
                if (decision == null || !seen.Add(decision.id) || !requested.TryGetValue(decision.id, out facts) ||
                    !ValidateDecisionAgainstFacts(decision, facts)) return false;
            }
            if (seen.Count != requested.Count) return false;

            // Rebuild the legal choices immediately before mutation. This catches road,
            // building and population changes that did not replace GameState itself.
            var integerIds = new List<int>(request.residents.Length);
            for (int i = 0; i < request.residents.Length; i++)
            {
                int id;
                if (!int.TryParse(request.residents[i].id, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
                integerIds.Add(id);
            }
            CitizenDecisionBatch fresh = simulation.BuildDecisionSnapshot(integerIds);
            if (fresh.residents == null || fresh.residents.Count != request.residents.Length) return false;
            var freshById = new Dictionary<string, CitizenDecisionFacts>(StringComparer.Ordinal);
            for (int i = 0; i < fresh.residents.Count; i++) freshById[fresh.residents[i].id] = fresh.residents[i];
            for (int i = 0; i < response.decisions.Length; i++)
            {
                CitizenDecisionFacts facts;
                if (!freshById.TryGetValue(response.decisions[i].id, out facts) || !ValidateChoice(response.decisions[i], facts.allowed)) return false;
            }
            reason = "valid response";
            return true;
        }

        static bool ApplyValidatedBatch(ResidentAiPlanResponse response, CitizenSimulation simulation, out string reason)
        {
            for (int i = 0; i < response.decisions.Length; i++)
            {
                if (!simulation.TryApplyDecision(response.decisions[i], response.model, PlanTimeToLiveSeconds, out reason))
                    return false;
            }
            reason = "batch applied";
            return true;
        }

        static bool ValidateDecisionAgainstFacts(ResponseDecision decision, CitizenDecisionFacts facts)
        {
            if (!ValidText(decision.id, 1, 64) || decision.target < 0 || decision.target > 1000000 ||
                !Finite(decision.dwellSeconds) || decision.dwellSeconds < 3f || decision.dwellSeconds > 30f ||
                decision.dwellSeconds != Mathf.Floor(decision.dwellSeconds) || !ValidText(decision.thought, 0, 100) ||
                !ValidText(decision.memory, 0, 160) || !ValidMood(decision.mood)) return false;
            return ValidateChoice(decision, facts.allowed);
        }

        static bool ValidateChoice(ResponseDecision decision, List<CitizenAllowedDecision> allowed)
        {
            if (allowed == null) return false;
            for (int i = 0; i < allowed.Count; i++)
            {
                CitizenAllowedDecision choice = allowed[i];
                if (choice != null && string.Equals(choice.intent, decision.intent, StringComparison.Ordinal) && choice.target == decision.target)
                    return true;
            }
            return false;
        }

        static bool TryParseHealth(string json, out ResidentAiHealthResponse health)
        {
            health = null;
            JsonShape root;
            if (!JsonShapeParser.TryParse(json, out root) || !ResidentAiProtocolShape.IsHealth(root)) return false;
            try { health = JsonUtility.FromJson<ResidentAiHealthResponse>(json); }
            catch { return false; }
            return health != null && string.Equals(health.provider, "openrouter", StringComparison.Ordinal) && ValidText(health.model, 1, 80);
        }

        void HandlePlanFailure(bool connectionFailure, long statusCode)
        {
            FailedRequestCount++;
            if (connectionFailure)
            {
                healthKnown = false;
                gatewayReady = false;
                ScheduleHealthBackoff();
                SetStatus(ResidentAiNetworkMode.CouldNotConnect, "연결할 수 없음", "게이트웨이에 연결하지 못했습니다. 주민은 규칙 기반 행동을 계속합니다.");
                return;
            }
            if (statusCode == 503)
            {
                healthKnown = true;
                gatewayReady = false;
                nextHealthAt = Time.unscaledTime + 60f;
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "게이트웨이에 클라우드 제공자 키가 설정되지 않았습니다.");
            }
            else if (statusCode == 429)
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "게이트웨이 요청 제한 또는 비용 한도 때문에 이번 계획을 건너뛰었습니다.");
            else
                SetStatus(ResidentAiNetworkMode.CouldNotConnect, "연결할 수 없음", "게이트웨이가 주민 계획 요청을 처리하지 못했습니다 (HTTP " + statusCode + ").");
        }

        void RecordHealthFailure(bool connectionFailure, long statusCode)
        {
            FailedRequestCount++;
            healthKnown = false;
            gatewayReady = false;
            ScheduleHealthBackoff();
            string reason = connectionFailure
                ? "게이트웨이에 연결하지 못했습니다. 주민은 규칙 기반 행동을 계속합니다."
                : statusCode > 0 ? "게이트웨이 준비 상태를 확인하지 못했습니다 (HTTP " + statusCode + ")."
                    : "게이트웨이 상태 응답 형식이 올바르지 않습니다.";
            SetStatus(ResidentAiNetworkMode.CouldNotConnect, "연결할 수 없음", reason);
        }

        void ScheduleHealthBackoff()
        {
            healthFailureStreak = Math.Min(healthFailureStreak + 1, 6);
            float[] delays = { 5f, 15f, 30f, 60f, 120f, 120f };
            nextHealthAt = Time.unscaledTime + delays[healthFailureStreak - 1];
        }

        bool PlanningBlocked(out string reason)
        {
            if (controller.GameSpeed <= 0f) { reason = "게임이 일시정지되어 규칙 기반 행동을 사용합니다."; return true; }
            if (controller.ModalOpen || controller.HelpOpen || controller.ResearchOpen)
            { reason = "창을 읽거나 설정하는 동안 주민 계획 요청을 쉬고 있습니다."; return true; }
            TutorialProgress tutorial = controller.State == null ? null : controller.State.Tutorial;
            if (tutorial != null && tutorial.Enabled && !tutorial.Completed)
            { reason = "튜토리얼을 진행하는 동안 주민은 규칙 기반 행동을 사용합니다."; return true; }
            reason = "";
            return false;
        }

        bool CanBeginNetworkRequest(string action)
        {
            if (!initialized || controller == null) return false;
            if (requestInFlight)
            {
                SetStatus(CurrentNetworkMode, Status, "이미 게이트웨이 요청을 처리하고 있습니다.");
                return false;
            }
            if (!enabledForSession)
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "주민 AI를 켠 뒤 " + action + "을 실행하세요.");
                return false;
            }
            if (!NetworkUseAllowed())
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "일반 스모크 테스트에서는 네트워크 요청을 보내지 않습니다.");
                return false;
            }
            if (string.IsNullOrEmpty(gatewayUrl))
            {
                SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "먼저 게이트웨이 주소를 설정하세요.");
                return false;
            }
            return true;
        }

        bool ObserveCityIdentityChange()
        {
            GameState currentState = controller.State;
            CitizenSimulation currentSimulation = CurrentSimulation();
            if (ReferenceEquals(boundState, currentState) && ReferenceEquals(boundSimulation, currentSimulation)) return false;
            AdvanceGeneration();
            CancelActiveRequest();
            if (boundSimulation != null) boundSimulation.ClearLlmPlans();
            if (currentSimulation != null) currentSimulation.ClearLlmPlans();
            boundState = currentState;
            boundSimulation = currentSimulation;
            roundRobinCursor = 0;
            ResetLastResponseTelemetry();
            nextPlanAt = Time.unscaledTime + MinimumPlanCadenceSeconds;
            SetStatus(ResidentAiNetworkMode.Rule, "규칙 기반 행동", "새 도시 상태에 맞춰 주민 계획을 초기화했습니다.");
            return true;
        }

        int AdvanceGeneration()
        {
            if (generation == int.MaxValue)
            {
                sessionId = Guid.NewGuid().ToString("N");
                generation = 0;
            }
            else generation++;
            return generation;
        }

        void CancelActiveRequest()
        {
            StopAllCoroutines();
            if (activeRequest != null)
            {
                activeRequest.Abort();
                activeRequest.Dispose();
                activeRequest = null;
            }
            requestInFlight = false;
            activePlanRequest = false;
        }

        void ClearLlmPlans()
        {
            CitizenSimulation simulation = CurrentSimulation();
            if (simulation != null) simulation.ClearLlmPlans();
        }

        void ResetLastResponseTelemetry()
        {
            LastResponseSource = "";
            LastResponseRequestId = "";
            LastResponseLatency = 0f;
            LastAppliedDecisionCount = 0;
            LastPromptTokens = 0;
            LastCompletionTokens = 0;
            LastEstimatedUsd = 0f;
        }

        CitizenSimulation CurrentSimulation()
        {
            return controller == null || controller.Citizens == null ? null : controller.Citizens.Simulation;
        }

        void AddGatewayAuthorization(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(gatewayAccessToken)) request.SetRequestHeader("Authorization", "Bearer " + gatewayAccessToken);
        }

        bool NetworkUseAllowed()
        {
            return controller != null && (!controller.SmokeMode || rootTestEndpoint);
        }

        string InitialReason()
        {
            if (!NetworkUseAllowed()) return "일반 스모크 테스트에서는 네트워크 요청을 보내지 않습니다.";
            return string.IsNullOrEmpty(gatewayUrl)
                ? "게이트웨이 주소를 설정하면 연결 상태를 확인할 수 있습니다."
                : "게이트웨이 연결을 확인하는 중입니다.";
        }

        string LoadGatewayUrl()
        {
            string fallback = Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer
                ? "http://127.0.0.1:47841" : "";
            string stored = PlayerPrefs.GetString(GatewayUrlPlayerPrefsKey, fallback);
            string normalized;
            string ignored;
            return TryNormalizeGatewayUrl(stored, out normalized, out ignored) ? normalized : fallback;
        }

        public static bool TryNormalizeGatewayUrl(string value, out string normalized, out string reason)
        {
            normalized = "";
            value = (value ?? "").Trim();
            if (value.Length == 0) { reason = "게이트웨이 주소가 비어 있습니다."; return true; }
            if (value.Length > 300) { reason = "게이트웨이 주소가 너무 깁니다."; return false; }
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { reason = "http 또는 https 전체 주소를 입력하세요."; return false; }
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            { reason = "주소에는 사용자 정보, 쿼리 또는 조각을 넣을 수 없습니다."; return false; }
            if (string.IsNullOrEmpty(uri.Host) || uri.Port <= 0)
            { reason = "게이트웨이 호스트와 포트를 확인하세요."; return false; }
            if (uri.Scheme == Uri.UriSchemeHttp && !IsPrivateOrLoopbackHost(uri.Host))
            { reason = "공개 네트워크 주소는 https만 사용할 수 있습니다."; return false; }
            normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            reason = "게이트웨이 주소가 올바릅니다.";
            return true;
        }

        static bool IsPrivateOrLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            if (!IPAddress.TryParse(host, out address)) return false;
            if (IPAddress.IsLoopback(address)) return true;
            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
        }

        static float SafeStock(GameState state, Resource resource)
        {
            int index = (int)resource;
            return state.Stock == null || index < 0 || index >= state.Stock.Count ? 0f : Mathf.Max(0f, state.Stock[index]);
        }

        static bool HasCommandLineFlag(string flag)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++) if (string.Equals(arguments[i], flag, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool WithinResponseLimit(string value)
        {
            return value != null && Encoding.UTF8.GetByteCount(value) <= MaximumResponseBytes;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool ValidMood(string value)
        {
            return value == "calm" || value == "happy" || value == "worried" || value == "curious" || value == "tired";
        }

        static bool ValidText(string value, int minimum, int maximum)
        {
            if (value == null || ContainsControlCharacter(value)) return false;
            int count = UnicodeScalarCount(value);
            return count >= minimum && count <= maximum;
        }

        static bool ContainsControlCharacter(string value)
        {
            if (value == null) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c <= '\u0008') || c == '\u000b' || c == '\u000c' || (c >= '\u000e' && c <= '\u001f') || c == '\u007f') return true;
            }
            return false;
        }

        static bool ContainsHeaderControlCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++) if (value[i] <= '\u001f' || value[i] == '\u007f') return true;
            return false;
        }

        static int UnicodeScalarCount(string value)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++, count++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return int.MaxValue;
                    i++;
                }
                else if (char.IsLowSurrogate(value[i])) return int.MaxValue;
            }
            return count;
        }

        void SetStatus(ResidentAiNetworkMode mode, string status, string reason)
        {
            if (CurrentNetworkMode == mode && string.Equals(Status, status, StringComparison.Ordinal) &&
                string.Equals(StatusReason, reason, StringComparison.Ordinal)) return;
            CurrentNetworkMode = mode;
            Status = status ?? "";
            StatusReason = reason ?? "";
            NotifyChanged();
        }

        void NotifyChanged()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }
    }

    internal enum JsonShapeKind { Object, Array, String, Number, Boolean, Null }

    internal sealed class JsonShape
    {
        public JsonShapeKind Kind;
        public Dictionary<string, JsonShape> Members;
        public List<JsonShape> Items;
    }

    /// <summary>A small shape-only parser used before JsonUtility so missing, duplicate, or extra wire keys fail closed.</summary>
    internal sealed class JsonShapeParser
    {
        readonly string input;
        int index;

        JsonShapeParser(string value) { input = value ?? ""; }

        public static bool TryParse(string value, out JsonShape result)
        {
            result = null;
            try
            {
                var parser = new JsonShapeParser(value);
                parser.SkipWhite();
                result = parser.ReadValue();
                parser.SkipWhite();
                return result != null && parser.index == parser.input.Length;
            }
            catch { result = null; return false; }
        }

        JsonShape ReadValue()
        {
            if (index >= input.Length) throw new FormatException();
            char c = input[index];
            if (c == '{') return ReadObject();
            if (c == '[') return ReadArray();
            if (c == '"') { ReadString(); return new JsonShape { Kind = JsonShapeKind.String }; }
            if (c == 't') { ReadLiteral("true"); return new JsonShape { Kind = JsonShapeKind.Boolean }; }
            if (c == 'f') { ReadLiteral("false"); return new JsonShape { Kind = JsonShapeKind.Boolean }; }
            if (c == 'n') { ReadLiteral("null"); return new JsonShape { Kind = JsonShapeKind.Null }; }
            ReadNumber();
            return new JsonShape { Kind = JsonShapeKind.Number };
        }

        JsonShape ReadObject()
        {
            index++;
            var members = new Dictionary<string, JsonShape>(StringComparer.Ordinal);
            SkipWhite();
            if (Take('}')) return new JsonShape { Kind = JsonShapeKind.Object, Members = members };
            while (true)
            {
                SkipWhite();
                string key = ReadString();
                if (members.ContainsKey(key)) throw new FormatException();
                SkipWhite();
                Require(':');
                SkipWhite();
                members.Add(key, ReadValue());
                SkipWhite();
                if (Take('}')) break;
                Require(',');
            }
            return new JsonShape { Kind = JsonShapeKind.Object, Members = members };
        }

        JsonShape ReadArray()
        {
            index++;
            var items = new List<JsonShape>();
            SkipWhite();
            if (Take(']')) return new JsonShape { Kind = JsonShapeKind.Array, Items = items };
            while (true)
            {
                SkipWhite();
                items.Add(ReadValue());
                SkipWhite();
                if (Take(']')) break;
                Require(',');
            }
            return new JsonShape { Kind = JsonShapeKind.Array, Items = items };
        }

        string ReadString()
        {
            Require('"');
            var builder = new StringBuilder();
            while (index < input.Length)
            {
                char c = input[index++];
                if (c == '"') return builder.ToString();
                if (c < 0x20) throw new FormatException();
                if (c != '\\') { builder.Append(c); continue; }
                if (index >= input.Length) throw new FormatException();
                char escaped = input[index++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u': builder.Append(ReadUnicodeEscape()); break;
                    default: throw new FormatException();
                }
            }
            throw new FormatException();
        }

        char ReadUnicodeEscape()
        {
            if (index + 4 > input.Length) throw new FormatException();
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char c = input[index++];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0) throw new FormatException();
                value = value * 16 + digit;
            }
            return (char)value;
        }

        void ReadNumber()
        {
            int start = index;
            if (Take('-')) { }
            if (Take('0')) { }
            else
            {
                if (index >= input.Length || input[index] < '1' || input[index] > '9') throw new FormatException();
                while (index < input.Length && char.IsDigit(input[index])) index++;
            }
            if (Take('.'))
            {
                int decimals = index;
                while (index < input.Length && char.IsDigit(input[index])) index++;
                if (decimals == index) throw new FormatException();
            }
            if (index < input.Length && (input[index] == 'e' || input[index] == 'E'))
            {
                index++;
                if (index < input.Length && (input[index] == '+' || input[index] == '-')) index++;
                int exponent = index;
                while (index < input.Length && char.IsDigit(input[index])) index++;
                if (exponent == index) throw new FormatException();
            }
            if (index == start) throw new FormatException();
        }

        void ReadLiteral(string value)
        {
            if (index + value.Length > input.Length || !string.Equals(input.Substring(index, value.Length), value, StringComparison.Ordinal))
                throw new FormatException();
            index += value.Length;
        }

        void SkipWhite()
        {
            while (index < input.Length && (input[index] == ' ' || input[index] == '\t' || input[index] == '\r' || input[index] == '\n')) index++;
        }

        bool Take(char value)
        {
            if (index >= input.Length || input[index] != value) return false;
            index++;
            return true;
        }

        void Require(char value) { if (!Take(value)) throw new FormatException(); }
    }

    internal static class ResidentAiProtocolShape
    {
        static readonly string[] PlanKeys = { "protocol", "requestId", "generation", "source", "model", "generatedAt", "decisions", "usage" };
        static readonly string[] DecisionKeys = { "id", "intent", "target", "dwellSeconds", "thought", "memory", "mood" };
        static readonly string[] UsageKeys = { "promptTokens", "completionTokens", "estimatedUsd" };
        static readonly string[] HealthKeys = { "ready", "provider", "model" };

        public static bool IsPlanResponse(JsonShape root)
        {
            if (!ExactObject(root, PlanKeys)) return false;
            JsonShape decisions = root.Members["decisions"];
            JsonShape usage = root.Members["usage"];
            if (decisions.Kind != JsonShapeKind.Array || !ExactObject(usage, UsageKeys)) return false;
            for (int i = 0; i < decisions.Items.Count; i++) if (!ExactObject(decisions.Items[i], DecisionKeys)) return false;
            return true;
        }

        public static bool IsHealth(JsonShape root)
        {
            return ExactObject(root, HealthKeys) && root.Members["ready"].Kind == JsonShapeKind.Boolean;
        }

        static bool ExactObject(JsonShape value, string[] keys)
        {
            if (value == null || value.Kind != JsonShapeKind.Object || value.Members == null || value.Members.Count != keys.Length) return false;
            for (int i = 0; i < keys.Length; i++) if (!value.Members.ContainsKey(keys[i])) return false;
            return true;
        }
    }
}
