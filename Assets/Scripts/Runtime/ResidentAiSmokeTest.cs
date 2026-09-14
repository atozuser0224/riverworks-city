using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>
    /// Explicit, executable proof of the native resident-AI pipeline. The default mode talks only
    /// to the deterministic local contract fixture. Real provider traffic is possible only with
    /// -riverworks-ai-live and is capped here at two plan requests.
    /// </summary>
    public sealed class ResidentAiSmokeTest : MonoBehaviour
    {
        const string SmokeArgument = "-riverworks-resident-ai-smoke";
        const string LiveArgument = "-riverworks-ai-live";
        const string TestUrlArgument = "-riverworks-ai-test-url";
        const string NoKeyUrlArgument = "-riverworks-ai-no-key-url";
        const string OutputArgument = "-riverworks-output";
        const string LiveGatewayUrl = "http://127.0.0.1:47841";
        const int MaximumErrors = 80;
        const int MaximumErrorCharacters = 4096;
        const float NetworkDeadlineSeconds = 30f;
        const float WholeRunDeadlineSeconds = 150f;

        [Serializable]
        sealed class FixtureStats
        {
            public int healthRequests;
            public int planRequests;
            public bool validRequestShapes;
            public bool idsAreStrings;
            public bool contentTypeJson;
            public bool secretFreeRequestUrls;
            public int authorizationHeaders;
            public int duplicateRequestIds;
            public string lastRequestId = "";
        }

        GameController game;
        ResidentAiClient client;
        string output = "";
        string testBaseUrl = "";
        string providerMode = "MOCK_CONTRACT";
        string originalGatewayPreference = "";
        bool hadOriginalGatewayPreference;
        bool live;
        bool finished;
        int livePlanCalls;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller;
            client = controller == null ? null : controller.ResidentAi;
            string[] arguments = Environment.GetCommandLineArgs();
            live = HasFlag(arguments, LiveArgument);
            providerMode = live ? "LIVE_OPENROUTER" : "MOCK_CONTRACT";
            testBaseUrl = ArgumentValue(arguments, TestUrlArgument);
            string requestedOutput = ArgumentValue(arguments, OutputArgument);
            output = string.IsNullOrWhiteSpace(requestedOutput)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "ResidentAiSmoke"))
                : Path.GetFullPath(requestedOutput);
            Directory.CreateDirectory(output);

            hadOriginalGatewayPreference = PlayerPrefs.HasKey(ResidentAiClient.GatewayUrlPlayerPrefsKey);
            originalGatewayPreference = PlayerPrefs.GetString(ResidentAiClient.GatewayUrlPlayerPrefsKey, "");
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
            StartCoroutine(DeadlineGuard());
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            RestoreGatewayPreference();
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordError((message ?? "") + "\n" + (trace ?? ""));
        }

        IEnumerator DeadlineGuard()
        {
            float deadline = Time.realtimeSinceStartup + WholeRunDeadlineSeconds;
            while (!finished && Time.realtimeSinceStartup < deadline) yield return null;
            if (!finished)
            {
                RecordError("Resident AI smoke exceeded the bounded 150 second runtime.");
                if (client != null) client.SetEnabled(false);
                Finish();
            }
        }

        IEnumerator GuardedRun()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => RecordError(exception.ToString()));
            Finish();
        }

        IEnumerator RunSteps()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            Require(HasFlag(arguments, SmokeArgument),
                "resident AI runtime smoke only runs behind its explicit command flag");
            Require(game != null && game.SmokeMode && client != null && game.Citizens != null,
                "GameController supplies the isolated smoke-mode resident AI runtime");
            Require(client.RootTestEndpoint,
                "the explicit resident AI smoke flag alone unlocks the otherwise blocked smoke network path");

            if (live)
            {
                testBaseUrl = LiveGatewayUrl;
                Require(string.Equals(testBaseUrl, LiveGatewayUrl, StringComparison.Ordinal),
                    "live mode is pinned to the real loopback OpenRouter gateway and cannot be redirected");
                results.Add("EVIDENCE providerMode=LIVE_OPENROUTER actualProviderCall=true gateway=127.0.0.1:47841 maxPlanCalls=2");
            }
            else
            {
                Require(!string.IsNullOrWhiteSpace(testBaseUrl),
                    "mock mode requires -riverworks-ai-test-url with the deterministic local fixture endpoint");
                Require(IsSafeMockEndpoint(testBaseUrl),
                    "mock fixture endpoint is loopback HTTP and is not the live 47841 gateway");
                results.Add("EVIDENCE providerMode=MOCK_CONTRACT actualProviderCall=false protocolSourceLabel=openrouter fixtureModel=contract-fixture");
            }

            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            game.SetSpeed(0f);
            client.SetEnabled(false);
            yield return new WaitForSecondsRealtime(.2f);

            GameState fixture = SharedCityScenario.Create();
            if (fixture.Tutorial != null) fixture.Tutorial.Skip();
            Require(fixture.Population >= 20,
                "SharedCityScenario supplies a rich connected resident population");
            Require(SaveStore.TrySave(game.SavePath, fixture, out string fixtureError),
                "shared resident fixture saves to the isolated smoke slot: " + fixtureError);
            game.LoadGame();
            yield return null;
            game.SetSpeed(0f);
            Require(game.Citizens.Simulation != null && game.Citizens.Simulation.ResidentCount == fixture.Population,
                "CitizenView binds the loaded shared-city simulation");
            CitizenDecisionBatch fixtureBatch = game.Citizens.Simulation.BuildDecisionSnapshot(
                game.Citizens.Simulation.Residents.Select(resident => resident.Id));
            Require(fixtureBatch.residents.Count >= 2,
                "shared resident fixture supplies multiple protocol-eligible residents for duplicate-ID fencing");
            Check(game.Citizens.Simulation.Residents.All(resident => resident.LastDecisionSource == "routine"),
                "legacy residents are explicitly tagged as rule-driven routine decisions before AI planning");

            ConfigureClient(testBaseUrl, false);
            yield return ExerciseSettingsHealth();
            Require(client.GatewayReady, "native settings connection test reports a ready gateway");
            if (live) Require(!string.Equals(client.Model, "contract-fixture", StringComparison.Ordinal),
                "live health reports a real configured OpenRouter model rather than the contract fixture");
            else Require(string.Equals(client.Model, "contract-fixture", StringComparison.Ordinal),
                "mock health identifies the response as the contract fixture");

            client.CloseSettings();
            yield return null;
            Require(!game.ModalOpen, "native settings close button returns control to the city");

            CitizenSimulation firstSimulation = game.Citizens.Simulation;
            GameState firstState = game.State;
            int appliedBefore = client.AppliedDecisionCount;
            yield return RequestPlan("first validated resident plan");
            game.SetSpeed(0f);
            Require(client.AppliedDecisionCount > appliedBefore && client.LastAppliedDecisionCount > 0,
                "gateway response applies a non-empty validated batch to the actual CitizenSimulation");
            Require(client.CurrentNetworkMode == ResidentAiNetworkMode.LLM &&
                    string.Equals(client.LastResponseSource, "openrouter", StringComparison.Ordinal),
                "client reports the protocol source and LLM-applied state only after validation");
            Require(IsHexRequestId(client.LastResponseRequestId),
                "response correlation retains the echoed 32-character request ID");
            VerifyUsageTelemetry();

            Citizen modelResident = FindModelResident(firstSimulation);
            Require(modelResident != null, "an applied gateway decision is visible on a real resident");
            Require(!string.IsNullOrWhiteSpace(modelResident.LastThought) &&
                    !string.IsNullOrWhiteSpace(modelResident.RecentMemory),
                "the applied plan carries a resident thought and session-only memory");
            yield return WaitForActiveModelWalk(firstSimulation, modelResident);
            Require(modelResident.HasActivePlan && !modelResident.Indoors,
                "the returned decision becomes an active model-directed road trip");
            int target = modelResident.ActiveTargetIndex;
            string intent = modelResident.ActiveIntent;
            float beforeX = modelResident.X;
            float beforeZ = modelResident.Z;
            firstSimulation.Advance(.5f);
            Check(Mathf.Abs(modelResident.X - beforeX) + Mathf.Abs(modelResident.Z - beforeZ) > .01f,
                "the applied decision produces real road motion in CitizenSimulation");
            Check(firstSimulation.IsValidPath(modelResident.Path),
                "the active model-directed resident remains on a valid connected road path");

            yield return SelectAndCaptureModelResident(modelResident);
            yield return AdvanceToDwell(firstSimulation, modelResident, target, intent);

            Require(game.TrySaveGame(out string saveError),
                "the city saves while model session data is active: " + saveError);
            string savedJson = File.ReadAllText(game.SavePath);
            Check(!savedJson.Contains(modelResident.LastThought) && !savedJson.Contains(modelResident.RecentMemory) &&
                  !savedJson.Contains(modelResident.LastSourceModel),
                "model thought, memory, and source model are absent from the durable city save");

            client.SetEnabled(false);
            yield return null;
            Check(NoModelState(firstSimulation),
                "disabling resident AI clears active and pending plans plus session diagnostics");

            ConfigureClient(testBaseUrl);
            game.SetSpeed(1f);
            int secondAppliedBefore = client.AppliedDecisionCount;
            yield return RequestPlan("second validated resident plan");
            Require(client.AppliedDecisionCount > secondAppliedBefore && !NoModelState(firstSimulation),
                "a second bounded request applies plans before reload fencing is exercised");
            game.LoadGame();
            yield return null;
            game.SetSpeed(0f);
            CitizenSimulation reloadedSimulation = game.Citizens.Simulation;
            Check(!ReferenceEquals(firstState, game.State) && ReferenceEquals(firstSimulation, reloadedSimulation) &&
                  ReferenceEquals(reloadedSimulation.State, game.State) && NoModelState(reloadedSimulation),
                "city reload invalidates the generation, rebinds the resident simulation, and clears every model plan");
            client.SetEnabled(false);

            if (!live)
            {
                yield return VerifyRejectedFixtureMode("malformed", "malformed JSON response");
                yield return VerifyRejectedFixtureMode("disallowed", "all decisions outside the supplied allowlists");
                yield return VerifyRejectedFixtureMode("duplicate", "duplicate resident response ID");
                yield return VerifyRejectedFixtureMode("stale", "stale response generation");
                yield return VerifyNoKeyHealth(arguments);
                yield return VerifyFixtureStats();
            }

            client.ClearSessionMemory();
            Check(client.AppliedDecisionCount == 0 && client.LastAppliedDecisionCount == 0 &&
                  string.IsNullOrEmpty(client.LastResponseSource) && string.IsNullOrEmpty(client.LastResponseRequestId) &&
                  client.LastPromptTokens == 0 && client.LastCompletionTokens == 0 && client.LastEstimatedUsd == 0f,
                "clear-session removes all response telemetry and leaves no durable LLM session memory");
            Check(!GatewayPreferenceContainsSecret(),
                "gateway preference contains only a safe URL and no key, token, query, or user-info secret");
            Check(!live || livePlanCalls <= 2,
                "live OpenRouter mode never exceeds two paid plan calls");
        }

        IEnumerator ExerciseSettingsHealth()
        {
            client.OpenSettings();
            yield return null;
            GameObject overlay = GameObject.Find("ResidentAiOverlay");
            Require(overlay != null && overlay.activeInHierarchy, "resident AI settings opens as a native Unity panel");
            Button testButton = FindActive("Button_ResidentAiTest")?.GetComponent<Button>();
            Button closeButton = FindActive("Button_ResidentAiClose")?.GetComponent<Button>();
            Toggle enabledToggle = FindActive("Toggle_ResidentAiEnabled")?.GetComponent<Toggle>();
            InputField address = FindActive("ResidentAiGatewayAddress")?.GetComponent<InputField>();
            InputField token = FindActive("ResidentAiGatewayToken")?.GetComponent<InputField>();
            Require(testButton != null && closeButton != null && enabledToggle != null,
                "settings exposes native connection-test, close, and enable controls");
            Require(address != null && token != null && token.contentType == InputField.ContentType.Password,
                "settings uses a plain URL field and a masked, session-only access-token field");
            Check(string.Equals(address.text, client.GatewayUrl, StringComparison.Ordinal) && string.IsNullOrEmpty(token.text),
                "settings starts with the configured safe endpoint and no client-side provider key");
            Check(overlay.GetComponentsInChildren<Text>(true).Any(text => text.text.Contains("규칙 기반")),
                "settings clearly labels fallback behavior as rule based before any plan is applied");

            enabledToggle.isOn = true;
            testButton.onClick.Invoke();
            yield return WaitForNetwork("native settings health request");
            Check(client.CurrentNetworkMode == ResidentAiNetworkMode.Rule,
                "a successful health check alone truthfully remains in rule-based behavior mode");
            Check(overlay.GetComponentsInChildren<Text>(true).Any(text => text.text.Contains("준비됨")),
                "native settings renders the ready connection status");
            yield return Capture("resident-ai-settings-health.png");
            closeButton.onClick.Invoke();
        }

        IEnumerator RequestPlan(string label)
        {
            if (live)
            {
                livePlanCalls++;
                Require(livePlanCalls <= 2, "live request budget remains capped at two plan calls");
            }
            game.SetSpeed(1f);
            client.StartCoroutine(client.RequestPlanNow());
            // Keep the request snapshot stable while a real provider thinks. GameSpeed remains
            // positive so the production planning gate is still exercised; only the two city
            // update components are held until the correlated response has been validated.
            bool gameWasEnabled = game.enabled;
            bool citizensWereEnabled = game.Citizens.enabled;
            game.enabled = false;
            game.Citizens.enabled = false;
            yield return WaitForNetwork(label);
            game.enabled = gameWasEnabled;
            game.Citizens.enabled = citizensWereEnabled;
            game.Citizens.Refresh();
            results.Add("EVIDENCE requestSnapshotStable=true planningGameSpeed=" +
                game.GameSpeed.ToString("0", CultureInfo.InvariantCulture));
        }

        IEnumerator WaitForNetwork(string label)
        {
            float started = Time.realtimeSinceStartup;
            float deadline = started + NetworkDeadlineSeconds;
            bool observed = client.RequestInFlight;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (client.RequestInFlight) observed = true;
                if (observed && !client.RequestInFlight) break;
                if (!observed && Time.realtimeSinceStartup - started > 1f) break;
                yield return null;
            }
            if (client.RequestInFlight)
            {
                client.SetEnabled(false);
                RecordError(label + " exceeded the bounded 30 second network wait.");
            }
            Check(observed, label + " traversed a real UnityWebRequest");
        }

        IEnumerator WaitForActiveModelWalk(CitizenSimulation simulation, Citizen resident)
        {
            if (resident.HasPendingPlan)
                results.Add("EVIDENCE pendingPlan=true waitingForRoutineDestinationBeforeModelActivation");
            float advanced = 0f;
            while ((!resident.HasActivePlan || resident.Indoors) && advanced < 60f)
            {
                simulation.Advance(.1f);
                advanced += .1f;
                if (((int)(advanced * 10f) % 10) == 0) yield return null;
            }
            Check(!resident.HasPendingPlan && resident.HasActivePlan && !resident.Indoors,
                "pending model plan activates only after the resident reaches its fenced origin");
        }

        IEnumerator SelectAndCaptureModelResident(Citizen resident)
        {
            yield return null;
            Physics.SyncTransforms();
            Vector3 world;
            Require(game.Citizens.TryGetVisibleResident(resident.Id, out world),
                "model-directed resident is rendered in the live city view");
            Vector3 screen = game.CameraRig.Camera.WorldToScreenPoint(world + Vector3.up * .15f);
            Require(screen.z > 0f && game.Citizens.TrySelect(new Vector2(screen.x, screen.y)),
                "native world pointer selection opens the model-directed NPC inspection");
            string details = game.ResidentDetails;
            Check(details.Contains("판단: LLM") && details.Contains("생각:"),
                "selected NPC inspection shows the LLM decision source and resident thought");
            Check(!details.Contains("<") && !details.Contains(">"),
                "selected NPC thought is rendered as safe plain UI text without rich-text injection");
            yield return Capture("resident-ai-npc-inspection.png");
            game.Citizens.ClearSelection();
        }

        IEnumerator AdvanceToDwell(CitizenSimulation simulation, Citizen resident, int target, string intent)
        {
            float advanced = 0f;
            while (!(resident.Indoors && resident.CurrentIndex == target && resident.HasActivePlan) && advanced < 60f)
            {
                simulation.Advance(.1f);
                advanced += .1f;
                if (((int)(advanced * 10f) % 10) == 0) yield return null;
            }
            Require(resident.Indoors && resident.CurrentIndex == target && resident.HasActivePlan,
                "resident reaches the validated intent-target destination with the model plan active");
            Check(string.Equals(resident.ActiveIntent, intent, StringComparison.Ordinal),
                "arrival preserves the exact validated model intent");
            simulation.Advance(2.5f);
            Check(resident.HasActivePlan && resident.Indoors,
                "returned dwell time keeps the resident at the destination beyond the minimum pre-dwell interval");
        }

        IEnumerator VerifyRejectedFixtureMode(string mode, string label)
        {
            CitizenSimulation simulation = game.Citizens.Simulation;
            simulation.ClearLlmPlans();
            string url = ModeUrl(testBaseUrl, mode);
            ConfigureClient(url);
            int failuresBefore = client.FailedRequestCount;
            int appliedBefore = client.AppliedDecisionCount;
            yield return RequestPlan("fixture rejection: " + mode);
            game.SetSpeed(0f);
            Check(client.FailedRequestCount == failuresBefore + 1 && client.AppliedDecisionCount == appliedBefore &&
                  client.CurrentNetworkMode == ResidentAiNetworkMode.Rule && NoModelState(simulation),
                "client fails closed for " + label);
            client.SetEnabled(false);
        }

        IEnumerator VerifyNoKeyHealth(string[] arguments)
        {
            string supplied = ArgumentValue(arguments, NoKeyUrlArgument);
            bool realGateway = !string.IsNullOrWhiteSpace(supplied);
            string url = realGateway ? supplied : ModeUrl(testBaseUrl, "not-ready");
            Require(realGateway ? IsDefaultGatewayUrl(url) : IsSafeMockEndpoint(testBaseUrl),
                "no-key health check uses a safe loopback endpoint");
            ConfigureClient(url);
            client.RequestHealthNow();
            yield return WaitForNetwork("no-key health request");
            Check(!client.GatewayReady && client.CurrentNetworkMode == ResidentAiNetworkMode.Rule &&
                  client.LastAppliedDecisionCount == 0,
                (realGateway ? "real default gateway" : "mock contract") +
                " reports no-key readiness false and keeps residents on rules");
            results.Add("EVIDENCE noKeyHealthMode=" + (realGateway ? "REAL_GATEWAY" : "MOCK_CONTRACT") +
                " ready=false behavior=RULE");
            client.SetEnabled(false);
        }

        IEnumerator VerifyFixtureStats()
        {
            string statsUrl = ModeUrl(testBaseUrl, "stats");
            using (UnityWebRequest request = UnityWebRequest.Get(statsUrl))
            {
                request.timeout = 5;
                request.redirectLimit = 0;
                yield return request.SendWebRequest();
                Require(request.result == UnityWebRequest.Result.Success && request.responseCode == 200,
                    "contract fixture exposes native HTTP request observations");
                FixtureStats stats = JsonUtility.FromJson<FixtureStats>(request.downloadHandler.text);
                Check(stats != null && stats.validRequestShapes && stats.idsAreStrings && stats.contentTypeJson &&
                      stats.secretFreeRequestUrls && stats.authorizationHeaders == 0 && stats.planRequests >= 6 &&
                      stats.duplicateRequestIds == 0 && IsHexRequestId(stats.lastRequestId),
                    "fixture observed JSON content type, string resident IDs, secret-free URLs, and unique request IDs");
            }
        }

        void VerifyUsageTelemetry()
        {
            if (live)
            {
                Check(client.LastPromptTokens > 0 && client.LastCompletionTokens > 0 &&
                      client.LastEstimatedUsd >= 0f && IsFinite(client.LastEstimatedUsd),
                    "live gateway retains provider token usage and finite estimated cost telemetry");
                results.Add("EVIDENCE usage promptTokens=" + client.LastPromptTokens +
                    " completionTokens=" + client.LastCompletionTokens + " estimatedUsd=" +
                    client.LastEstimatedUsd.ToString("0.000000", CultureInfo.InvariantCulture));
            }
            else
            {
                Check(client.LastPromptTokens == 0 && client.LastCompletionTokens == 0 && client.LastEstimatedUsd == 0f,
                    "contract fixture usage is explicitly zero and cannot be mistaken for a real provider call");
            }
        }

        void ConfigureClient(string url, bool enable = true)
        {
            client.SetEnabled(false);
            client.Initialize(game, true);
            Require(client.TrySetGatewayUrl(url, out string reason),
                "resident AI client accepts the bounded gateway endpoint: " + reason);
            Require(client.SetGatewayAccessToken(""),
                "runtime smoke does not place a provider or gateway secret in the Unity client");
            if (enable) client.SetEnabled(true);
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                Require(texture != null && texture.width >= 320 && texture.height >= 180,
                    "native resident AI capture is available: " + name);
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
            }
            finally
            {
                if (texture != null) Destroy(texture);
            }
        }

        static Citizen FindModelResident(CitizenSimulation simulation)
        {
            if (simulation == null) return null;
            for (int i = 0; i < simulation.Residents.Count; i++)
                if (simulation.Residents[i].LastDecisionSource == "model") return simulation.Residents[i];
            return null;
        }

        static bool NoModelState(CitizenSimulation simulation)
        {
            return simulation != null && simulation.Residents.All(resident =>
                !resident.HasActivePlan && !resident.HasPendingPlan && resident.LastDecisionSource == "routine" &&
                string.IsNullOrEmpty(resident.LastThought) && string.IsNullOrEmpty(resident.RecentMemory) &&
                string.IsNullOrEmpty(resident.LastSourceModel));
        }

        bool GatewayPreferenceContainsSecret()
        {
            string stored = PlayerPrefs.GetString(ResidentAiClient.GatewayUrlPlayerPrefsKey, "");
            Uri uri;
            if (!Uri.TryCreate(stored, UriKind.Absolute, out uri)) return true;
            string lower = stored.ToLowerInvariant();
            return !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                   lower.Contains("key=") || lower.Contains("token=") || lower.Contains("authorization");
        }

        static GameObject FindActive(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null && found.activeInHierarchy ? found : null;
        }

        static bool IsSafeMockEndpoint(string value)
        {
            Uri uri;
            return IsSafeLoopbackUrl(value, out uri) && uri.Scheme == Uri.UriSchemeHttp && uri.Port != 47841 &&
                   string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal);
        }

        static bool IsDefaultGatewayUrl(string value)
        {
            Uri uri;
            return IsSafeLoopbackUrl(value, out uri) && uri.Port == 47841 &&
                   string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal);
        }

        static bool IsSafeLoopbackUrl(string value)
        {
            Uri ignored;
            return IsSafeLoopbackUrl(value, out ignored);
        }

        static bool IsSafeLoopbackUrl(string value, out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return false;
            return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        static string ModeUrl(string baseUrl, string mode)
        {
            return (baseUrl ?? "").TrimEnd('/') + "/" + mode;
        }

        static bool IsHexRequestId(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static bool HasFlag(string[] arguments, string flag)
        {
            return Array.IndexOf(arguments, flag) >= 0;
        }

        static string ArgumentValue(string[] arguments, string name)
        {
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : "";
        }

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                return;
            }
            RecordError("FAIL " + message);
        }

        void Require(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
            Check(true, message);
        }

        void RecordError(string message)
        {
            if (errors.Count >= MaximumErrors)
            {
                suppressedErrors++;
                return;
            }
            message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            if (message.Length > MaximumErrorCharacters)
                message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
            errors.Add(message);
        }

        void RestoreGatewayPreference()
        {
            if (hadOriginalGatewayPreference)
                PlayerPrefs.SetString(ResidentAiClient.GatewayUrlPlayerPrefsKey, originalGatewayPreference);
            else
                PlayerPrefs.DeleteKey(ResidentAiClient.GatewayUrlPlayerPrefsKey);
            PlayerPrefs.Save();
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            game?.SetSpeed(0f);
            client?.SetEnabled(false);
            Application.logMessageReceived -= OnLog;
            RestoreGatewayPreference();
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILDGUID " + Application.buildGUID +
                " MODE " + providerMode + " UNITY " + Application.unityVersion);
            results.Add("PROVIDER_MODE " + providerMode);
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, "resident-ai-results.txt"), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write resident-ai-results.txt: " + exception.Message);
                exitCode = 1;
            }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_RESIDENT_AI_SMOKE_PASS" : "RIVERWORKS_RESIDENT_AI_SMOKE_FAIL");
            Application.Quit(exitCode);
        }
    }
}
