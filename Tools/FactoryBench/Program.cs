using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Riverworks;

const int TickCount = 3_000;
const float SecondsPerTick = 0.1f;

BenchmarkCase[] cases =
{
    new("example-baseline", "example-production-line", false, FactoryState.CreateExample),
    new("example-upgraded", "example-production-line", true, FactoryState.CreateExample),
    new("factory-max-baseline", "max-entities-power-network-and-belt-cycles", false, CreateFactoryMaxState),
    new("factory-max-upgraded", "max-entities-power-network-and-belt-cycles", true, CreateFactoryMaxState),
};

var results = new List<BenchmarkResult>(cases.Length);
foreach (BenchmarkCase benchmarkCase in cases)
{
    WarmUp(benchmarkCase);
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

    BenchmarkResult result = Measure(benchmarkCase);
    results.Add(result);
    Console.WriteLine(
        $"{result.Name,-24} {result.ElapsedMilliseconds,10:F3} ms  " +
        $"{result.MillisecondsPer1000Ticks,9:F3} ms/1000 ticks  " +
        $"{result.AllocatedBytesPer1000Ticks,12:F0} B/1000 ticks  checksum {result.FinalState.Checksum}");
}

var report = new BenchmarkReport(
    SchemaVersion: 1,
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    Runtime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    TicksPerScenario: TickCount,
    SecondsPerTick: SecondsPerTick,
    Results: results);

string workspaceRoot = FindWorkspaceRoot();
string artifactDirectory = Path.Combine(workspaceRoot, "Artifacts");
Directory.CreateDirectory(artifactDirectory);
string artifactPath = Path.Combine(artifactDirectory, "factory-benchmark.json");

await using (FileStream stream = File.Create(artifactPath))
{
    await JsonSerializer.SerializeAsync(stream, report, BenchmarkJsonContext.Default.BenchmarkReport);
}

Console.WriteLine($"Artifact: {artifactPath}");
return 0;

static void WarmUp(BenchmarkCase benchmarkCase)
{
    FactorySimulation simulation = CreateSimulation(benchmarkCase);
    for (int tick = 0; tick < 32; tick++)
        simulation.Tick(SecondsPerTick);

    _ = ObserveState(simulation);
}

static BenchmarkResult Measure(BenchmarkCase benchmarkCase)
{
    FactorySimulation simulation = CreateSimulation(benchmarkCase);
    int initialEntityCount = simulation.State.Entities.Count;

    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    for (int tick = 0; tick < TickCount; tick++)
        simulation.Tick(SecondsPerTick);
    long stopped = Stopwatch.GetTimestamp();
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    StateObservation observation = ObserveState(simulation);
    if (observation.EntityCount != initialEntityCount || observation.ElapsedSeconds < TickCount * SecondsPerTick - 0.5f)
        throw new InvalidOperationException($"Benchmark workload did not complete: {benchmarkCase.Name}.");

    double elapsedMilliseconds = (stopped - started) * 1000d / Stopwatch.Frequency;
    GC.KeepAlive(simulation);
    return new BenchmarkResult(
        benchmarkCase.Name,
        benchmarkCase.Workload,
        benchmarkCase.OptionalFactoryTechnologyUpgrades,
        observation.EntityCount,
        TickCount,
        SecondsPerTick,
        elapsedMilliseconds,
        elapsedMilliseconds * 1000d / TickCount,
        allocatedBytes,
        allocatedBytes * 1000d / TickCount,
        observation);
}

static FactorySimulation CreateSimulation(BenchmarkCase benchmarkCase)
{
    FactoryState state = benchmarkCase.CreateState();
    FactorySimulation.ValidateState(state);
    var simulation = new FactorySimulation(state);

    GameState technologyState = GameState.CreateNew();
    if (benchmarkCase.OptionalFactoryTechnologyUpgrades)
    {
        technologyState.Technologies.AddRange(new[]
        {
            TechId.Logistics,
            TechId.Automation,
            TechId.MassProduction,
            TechId.Electrification,
        });
    }

    simulation.ConfigureTechnology(technologyState);
    return simulation;
}

static FactoryState CreateFactoryMaxState()
{
    var state = FactoryState.CreateEmpty();
    state.PowerBudget = 64;

    var powerPoles = new HashSet<(int X, int Z)>
    {
        (2, 2), (7, 2), (12, 2), (17, 2), (22, 2),
        (2, 7), (7, 7),          (17, 7), (22, 7),
        (2, 12), (7, 12), (12, 12), (17, 12), (22, 12),
    };
    var inserters = new HashSet<(int X, int Z)>
    {
        (4, 4), (8, 4), (14, 4), (18, 4),
        (4, 10), (8, 10), (14, 10), (18, 10),
    };

    var entitiesByCell = new Dictionary<(int X, int Z), FactoryEntity>(state.Width * state.Height);
    AddEntity(FactoryKind.PowerInlet, 10, 6, 0);

    for (int z = 0; z < state.Height; z++)
    for (int x = 0; x < state.Width; x++)
    {
        if (x is 10 or 11 && z is 6 or 7)
            continue;

        FactoryKind kind = powerPoles.Contains((x, z))
            ? FactoryKind.Pole
            : inserters.Contains((x, z))
                ? FactoryKind.Inserter
                : FactoryKind.Belt;
        AddEntity(kind, x, z, BeltCycleDirection(x, z));
    }

    for (int z = 0; z < state.Height; z += 2)
    for (int x = 0; x < state.Width; x += 2)
    {
        FactoryEntity[] loop =
        {
            entitiesByCell[(x, z)],
            entitiesByCell[(x + 1, z)],
            entitiesByCell[(x + 1, z + 1)],
            entitiesByCell[(x, z + 1)],
        };
        if (loop.All(entity => entity.Kind == FactoryKind.Belt))
        {
            loop[0].CargoResource = (Resource)(1 + ((x / 2 + z / 2) % 8));
            loop[0].CargoProgress = 0.25f;
        }
    }

    state.NextEntityId = state.Entities.Count + 1;
    FactorySimulation.ValidateState(state);
    return state;

    void AddEntity(FactoryKind kind, int x, int z, int direction)
    {
        var entity = new FactoryEntity
        {
            Id = state.Entities.Count + 1,
            Kind = kind,
            X = x,
            Z = z,
            Direction = direction,
        };
        state.Entities.Add(entity);

        FactorySpec spec = FactoryCatalog.Get(kind)
            ?? throw new InvalidOperationException($"No factory catalog entry for {kind}.");
        for (int occupiedZ = z; occupiedZ < z + spec.Height; occupiedZ++)
        for (int occupiedX = x; occupiedX < x + spec.Width; occupiedX++)
            entitiesByCell[(occupiedX, occupiedZ)] = entity;
    }
}

static int BeltCycleDirection(int x, int z) => (x & 1, z & 1) switch
{
    (0, 0) => 0,
    (1, 0) => 1,
    (1, 1) => 2,
    _ => 3,
};

static StateObservation ObserveState(FactorySimulation simulation)
{
    FactoryState state = simulation.State;
    long bufferedUnits = 0;
    double progressTotal = 0;
    int poweredEntities = 0;

    foreach (FactoryEntity entity in state.Entities)
    {
        for (int resource = 1; resource < entity.Input.Count; resource++)
            bufferedUnits += entity.Input[resource];
        for (int resource = 1; resource < entity.Output.Count; resource++)
            bufferedUnits += entity.Output[resource];
        if (entity.CargoResource != Resource.Coins)
            bufferedUnits++;
        progressTotal += entity.Progress + entity.CargoProgress;
        if (entity.Powered)
            poweredEntities++;
    }

    long producedUnits = SumInventory(state.Produced);
    long exportedUnits = SumInventory(state.Exported);
    long recoveredUnits = SumInventory(state.Recovered);
    long progressMicrounits = checked((long)Math.Round(progressTotal * 1_000_000d));
    long elapsedMilliseconds = checked((long)Math.Round(state.ElapsedSeconds * 1_000d));
    long checksum = 17;
    Mix(state.Entities.Count);
    Mix(poweredEntities);
    Mix(simulation.MovingItems);
    Mix(bufferedUnits);
    Mix(producedUnits);
    Mix(exportedUnits);
    Mix(recoveredUnits);
    Mix(progressMicrounits);
    Mix(elapsedMilliseconds);

    return new StateObservation(
        state.Entities.Count,
        poweredEntities,
        simulation.MovingItems,
        bufferedUnits,
        producedUnits,
        exportedUnits,
        recoveredUnits,
        progressTotal,
        state.ElapsedSeconds,
        checksum);

    void Mix(long value) => checksum = unchecked(checksum * 31 + value);
}

static long SumInventory(IReadOnlyList<int> inventory)
{
    long total = 0;
    for (int resource = 1; resource < inventory.Count; resource++)
        total += inventory[resource];
    return total;
}

static string FindWorkspaceRoot()
{
    DirectoryInfo directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "Assets", "Scripts", "Core")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find the Riverworks workspace root.");
}

internal sealed record BenchmarkCase(
    string Name,
    string Workload,
    bool OptionalFactoryTechnologyUpgrades,
    Func<FactoryState> CreateState);

internal sealed record BenchmarkReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Runtime,
    int TicksPerScenario,
    float SecondsPerTick,
    IReadOnlyList<BenchmarkResult> Results);

internal sealed record BenchmarkResult(
    string Name,
    string Workload,
    bool OptionalFactoryTechnologyUpgrades,
    int EntityCount,
    int Ticks,
    float SecondsPerTick,
    double ElapsedMilliseconds,
    double MillisecondsPer1000Ticks,
    long AllocatedBytes,
    double AllocatedBytesPer1000Ticks,
    StateObservation FinalState);

internal sealed record StateObservation(
    int EntityCount,
    int PoweredEntities,
    int MovingItems,
    long BufferedUnits,
    long ProducedUnits,
    long ExportedUnits,
    long RecoveredUnits,
    double ProgressTotal,
    float ElapsedSeconds,
    long Checksum);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkReport))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;
