using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
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
        int uiProbeCaptureCount;
        int suppressedErrors;
        UiSmokeViewport smokeViewport;
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
            smokeViewport?.Dispose();
            smokeViewport = null;
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
            yield return VerifyResidentAiUiAcrossViewports();
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

        IEnumerator VerifyResidentAiUiAcrossViewports()
        {
            Require(!client.RequestInFlight && !client.EnabledForSession,
                "final resident AI UI probe starts disabled and cannot issue another provider request");
            Require(game.CityHud != null && game.CameraRig != null && game.CameraRig.Camera != null,
                "final resident AI UI probe has the live HUD canvas and main camera");
            Canvas canvas = game.CityHud.GetComponent<Canvas>();
            Require(canvas != null && EventSystem.current != null,
                "resident AI UI probe has a Canvas and EventSystem for projected raycasts");

            client.OpenSettings();
            yield return new WaitForSecondsRealtime(.3f);
            Require(game.ModalOpen && FindPanelTransform("ResidentAiOverlay")?.gameObject.activeInHierarchy == true,
                "resident AI UI probe opens the existing modal without a network request");
            results.Add("CAPTURE_MODE OFFSCREEN_RENDER_TARGET");
            results.Add("DEVICE_VALIDATION NOT_RUN - exact render targets do not prove a physical window or Android layout");

            int[,] resolutions = { { 1600, 900 }, { 1280, 720 }, { 1024, 768 }, { 2048, 1536 } };
            for (int index = 0; index < resolutions.GetLength(0); index++)
            {
                int width = resolutions[index, 0];
                int height = resolutions[index, 1];
                string size = width + "x" + height;
                smokeViewport?.Dispose();
                smokeViewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, width, height);
                yield return null;
                Canvas.ForceUpdateCanvases();
                ScrollRect scroll = FindPanelTransform("ResidentAiContentViewport")?.GetComponent<ScrollRect>();
                if (scroll != null)
                {
                    scroll.StopMovement();
                    scroll.verticalNormalizedPosition = 1f;
                }
                Canvas.ForceUpdateCanvases();

                VerifyResidentAiPanelContracts(canvas, width, height, size);
                results.Add("VIEWPORT " + size + " CANVAS " +
                    Mathf.RoundToInt(canvas.pixelRect.width) + "x" + Mathf.RoundToInt(canvas.pixelRect.height) +
                    " RENDER_TEXTURE " + smokeViewport.Width + "x" + smokeViewport.Height);
                yield return CaptureUiViewport("resident-ai-ui-" + size + ".png");
            }
            Require(uiProbeCaptureCount == 4,
                "resident AI UI probe writes exactly four requested offscreen resolution captures");

            smokeViewport?.Dispose();
            smokeViewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, 640, 360);
            yield return null;
            Canvas.ForceUpdateCanvases();
            VerifySmallScrollablePanel(canvas);

            InputField token = FindPanelTransform("ResidentAiGatewayToken")?.GetComponent<InputField>();
            Button close = FindPanelButton("Button_ResidentAiClose");
            Require(token != null && close != null, "small UI probe retains the token field and stable close action");
            token.text = "ui-probe-placeholder";
            ExecuteProjectedClick(close, canvas, 640, 360);
            yield return null;
            Check(!game.ModalOpen && !FindPanelTransform("ResidentAiOverlay").gameObject.activeInHierarchy &&
                  string.IsNullOrEmpty(token.text),
                "rendered close action clears the token field, hides the panel, and releases ModalOpen");

            client.OpenSettings();
            yield return null;
            Check(game.ModalOpen && string.IsNullOrEmpty(token.text),
                "reopening resident AI settings never restores the cleared session token text");
            ExecuteProjectedClick(close, canvas, 640, 360);
            yield return null;
            Check(!game.ModalOpen && !client.RequestInFlight,
                "final UI probe closes without starting health, planning, or provider traffic");
            results.Add("COVERAGE NOT_APPLICABLE - resident AI is an intentional modal; the 20 percent idle-world limit does not apply");
            smokeViewport.Dispose();
            smokeViewport = null;
        }

        void VerifyResidentAiPanelContracts(Canvas canvas, int width, int height, string size)
        {
            RectTransform overlay = FindPanelTransform("ResidentAiOverlay");
            RectTransform card = FindPanelTransform("ResidentAiCard");
            RectTransform contentViewport = FindPanelTransform("ResidentAiContentViewport");
            RectTransform content = FindPanelTransform("ResidentAiContent");
            Require(overlay != null && card != null && contentViewport != null && content != null,
                "resident AI overlay, card, fixed header, and scrolling body exist at " + size);

            Rect screen = new Rect(0f, 0f, width, height);
            Rect cardPixels = ProjectedRect(card, canvas);
            Check(ContainsRect(screen, cardPixels, 1f) && cardPixels.width <= 680.5f && cardPixels.height <= 520.5f,
                "resident AI card remains within the exact viewport bounds at " + size);
            Check(!contentViewport.IsChildOf(content) && content.IsChildOf(contentViewport),
                "resident AI body content is clipped by its own viewport at " + size);
            ScrollRect bodyScroll = contentViewport.GetComponent<ScrollRect>();
            Check(bodyScroll != null && bodyScroll.vertical && !bodyScroll.horizontal &&
                  ReferenceEquals(bodyScroll.viewport, contentViewport) && ReferenceEquals(bodyScroll.content, content),
                "resident AI body uses the expected vertical ScrollRect at " + size);

            Text[] labels = card.GetComponentsInChildren<Text>(true);
            Text title = labels.FirstOrDefault(label => string.Equals(label.text, "주민 AI", StringComparison.Ordinal));
            Text status = labels.FirstOrDefault(label => label.text != null && label.text.StartsWith("규칙 기반 행동", StringComparison.Ordinal));
            Require(title != null && status != null, "resident AI title and rule status render at " + size);
            Check(status.text.IndexOf("규칙 기반 · 규칙 기반 행동", StringComparison.Ordinal) < 0 &&
                  CountOccurrences(status.text, "규칙 기반") == 1,
                "resident AI status avoids the duplicated rule-mode phrase at " + size);
            Check(title.fontSize == HudStyle.TitleSize && labels.Where(label => !ReferenceEquals(label, title))
                      .All(label => label.fontSize == HudStyle.BodySize) &&
                  labels.All(label => label.fontStyle == FontStyle.Normal),
                "resident AI typography uses only 22px title, 11px body, and Normal style at " + size);

            Button close = FindPanelButton("Button_ResidentAiClose");
            string[] stableNames =
            {
                "Button_ResidentAiClose", "Button_ResidentAiConnect", "Button_ResidentAiTest",
                "Button_ResidentAiPause", "Button_ResidentAiClearMemory"
            };
            Require(close != null && stableNames.All(name => FindPanelButton(name) != null),
                "all existing resident AI button names remain stable at " + size);
            RectTransform closeRect = close.transform as RectTransform;
            Image closeIcon = close.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(image => image.gameObject.name == "Icon");
            Check(closeRect != null && Mathf.Abs(closeRect.rect.width - HudStyle.TouchSize) <= .5f &&
                  Mathf.Abs(closeRect.rect.height - HudStyle.TouchSize) <= .5f && closeIcon != null &&
                  closeIcon.sprite == HudAssets.Icon(HudAssets.CloseIcon),
                "resident AI close action is a 44px semantic close-icon button at " + size);
            Check(stableNames.Skip(1).Select(FindPanelButton).All(button =>
                      button != null && Mathf.Abs(((RectTransform)button.transform).rect.height - HudStyle.TouchSize) <= .5f),
                "resident AI actions retain the 44px touch height at " + size);
            Check(IsFirstProjectedRaycast(close, canvas, width, height) &&
                  IsFirstProjectedRaycast(FindPanelButton("Button_ResidentAiTest"), canvas, width, height),
                "close and connection-test actions are unobscured EventSystem targets at " + size);

            InputField address = FindPanelTransform("ResidentAiGatewayAddress")?.GetComponent<InputField>();
            InputField token = FindPanelTransform("ResidentAiGatewayToken")?.GetComponent<InputField>();
            Text addressLabel = labels.FirstOrDefault(label => string.Equals(label.text, "게이트웨이", StringComparison.Ordinal));
            Text tokenLabel = labels.FirstOrDefault(label => string.Equals(label.text, "접근 토큰", StringComparison.Ordinal));
            Require(address != null && token != null && addressLabel != null && tokenLabel != null,
                "resident AI inputs and their labels exist at " + size);
            Check(!ProjectedRect(addressLabel.rectTransform, canvas).Overlaps(ProjectedRect((RectTransform)address.transform, canvas)) &&
                  !ProjectedRect(tokenLabel.rectTransform, canvas).Overlaps(ProjectedRect((RectTransform)token.transform, canvas)) &&
                  !ProjectedRect((RectTransform)address.transform, canvas).Overlaps(ProjectedRect((RectTransform)token.transform, canvas)),
                "gateway and token labels do not overlap either input at " + size);
            Check(token.contentType == InputField.ContentType.Password && string.IsNullOrEmpty(token.text),
                "token field remains masked and empty at " + size);
        }

        void VerifySmallScrollablePanel(Canvas canvas)
        {
            const string size = "640x360 logical probe";
            RectTransform card = FindPanelTransform("ResidentAiCard");
            RectTransform contentViewport = FindPanelTransform("ResidentAiContentViewport");
            RectTransform content = FindPanelTransform("ResidentAiContent");
            Button close = FindPanelButton("Button_ResidentAiClose");
            Button clear = FindPanelButton("Button_ResidentAiClearMemory");
            ScrollRect scroll = contentViewport == null ? null : contentViewport.GetComponent<ScrollRect>();
            Require(card != null && contentViewport != null && content != null && close != null && clear != null && scroll != null,
                "small logical resident AI panel retains its fixed header and scrolling body");
            Rect screen = new Rect(0f, 0f, 640f, 360f);
            Rect cardPixels = ProjectedRect(card, canvas);
            Rect closeBefore = ProjectedRect((RectTransform)close.transform, canvas);
            Check(ContainsRect(screen, cardPixels, 1f) && closeBefore.width >= 43.5f && closeBefore.height >= 43.5f &&
                  !close.transform.IsChildOf(contentViewport),
                "small logical panel keeps the 44px close header fixed inside card bounds");
            Check(content.rect.height > contentViewport.rect.height && scroll.vertical,
                "small logical panel exposes overflow through its vertical body ScrollRect");

            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            Rect closeAfter = ProjectedRect((RectTransform)close.transform, canvas);
            Rect clearPixels = ProjectedRect((RectTransform)clear.transform, canvas);
            Rect viewportPixels = ProjectedRect(contentViewport, canvas);
            Check(Vector2.Distance(closeBefore.center, closeAfter.center) <= .5f,
                "small logical panel header does not move when the body scrolls");
            Check(ContainsRect(viewportPixels, clearPixels, 1.5f) && IsFirstProjectedRaycast(clear, canvas, 640, 360),
                "scrolling to the bottom exposes the lower session-memory action as the first raycast target");
            Check(IsFirstProjectedRaycast(close, canvas, 640, 360),
                "fixed close action remains raycastable after the small panel body scrolls");
            results.Add("VIEWPORT " + size + " CANVAS " + Mathf.RoundToInt(canvas.pixelRect.width) + "x" +
                Mathf.RoundToInt(canvas.pixelRect.height) + " RENDER_TEXTURE 640x360 CAPTURE_NOT_REQUESTED");
        }

        IEnumerator CaptureUiViewport(string name)
        {
            yield return new WaitForSecondsRealtime(.3f);
            Texture2D texture = null;
            try
            {
                texture = smokeViewport.Capture();
                Require(texture != null && texture.width == smokeViewport.Width && texture.height == smokeViewport.Height,
                    "resident AI offscreen capture has the requested exact size: " + name);
                Require(VisibleFrame(texture), "resident AI offscreen capture contains visible UI and world pixels: " + name);
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                uiProbeCaptureCount++;
                results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height + " OFFSCREEN_RENDER_TARGET");
            }
            finally
            {
                if (texture != null) Destroy(texture);
            }
        }

        void ExecuteProjectedClick(Button button, Canvas canvas, int width, int height)
        {
            Require(button != null && button.gameObject.activeInHierarchy && button.interactable,
                "projected UI click has an active interactable button");
            PointerEventData pointer = PointerAtProjectedCenter(button, canvas, width, height);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && IsButtonHit(button, hits[0].gameObject),
                "projected button is the first EventSystem raycast target before click");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        bool IsFirstProjectedRaycast(Button button, Canvas canvas, int width, int height)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable || EventSystem.current == null)
                return false;
            try
            {
                PointerEventData pointer = PointerAtProjectedCenter(button, canvas, width, height);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                return hits.Count > 0 && IsButtonHit(button, hits[0].gameObject);
            }
            catch { return false; }
        }

        static PointerEventData PointerAtProjectedCenter(Button button, Canvas canvas, int width, int height)
        {
            Rect projected = ProjectedRect((RectTransform)button.transform, canvas);
            Vector2 center = projected.center;
            if (projected.width < 1f || projected.height < 1f || center.x < 0f || center.x > width ||
                center.y < 0f || center.y > height)
                throw new InvalidOperationException("Button has no visible projected rect: " + button.name);
            return new PointerEventData(EventSystem.current)
            {
                position = center,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
        }

        static Rect ProjectedRect(RectTransform rect, Canvas canvas)
        {
            if (rect == null || canvas == null) return default;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 first = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            for (int index = 1; index < corners.Length; index++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[index]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        static bool ContainsRect(Rect outer, Rect inner, float tolerance)
        {
            return inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;
        }

        static bool IsButtonHit(Button button, GameObject hit)
        {
            return hit == button.gameObject || hit.transform.IsChildOf(button.transform);
        }

        Button FindPanelButton(string name)
        {
            return game?.CityHud == null ? null : game.CityHud.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button.name == name);
        }

        RectTransform FindPanelTransform(string name)
        {
            return game?.CityHud == null ? null : game.CityHud.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        static int CountOccurrences(string value, string search)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(search)) return 0;
            int count = 0;
            for (int at = 0; (at = value.IndexOf(search, at, StringComparison.Ordinal)) >= 0; at += search.Length)
                count++;
            return count;
        }

        static bool VisibleFrame(Texture2D texture)
        {
            if (texture == null || texture.width < 320 || texture.height < 180) return false;
            Color32[] pixels = texture.GetPixels32();
            int stride = Mathf.Max(1, pixels.Length / 4096);
            int samples = 0, lit = 0;
            var colors = new HashSet<int>();
            for (int index = 0; index < pixels.Length; index += stride)
            {
                Color32 pixel = pixels[index];
                samples++;
                if (pixel.r + pixel.g + pixel.b >= 24) lit++;
                colors.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4));
            }
            return lit >= samples / 20 && colors.Count >= 16;
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
            smokeViewport?.Dispose();
            smokeViewport = null;
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
