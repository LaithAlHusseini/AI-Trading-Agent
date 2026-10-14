using System.Text.Json;

public sealed class ForwardSignalStore
{
    private const int CurrentSchemaVersion =
        1;

    private const string FileName =
        "pending-forward-signals.json";

    private readonly string
        _storeDirectory;

    private readonly string
        _storePath;

    private readonly JsonSerializerOptions
        _jsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented =
                    true,

                PropertyNameCaseInsensitive =
                    true
            };

    public ForwardSignalStore(
        string? storeDirectory = null)
    {
        _storeDirectory =
            string.IsNullOrWhiteSpace(
                storeDirectory
            )
                ? BuildDefaultStoreDirectory()
                : Path.GetFullPath(
                    storeDirectory
                );

        _storePath =
            Path.Combine(
                _storeDirectory,
                FileName
            );
    }

    public string StorePath =>
        _storePath;

    public async Task<ForwardSignalStoreSnapshot>
        SaveAsync(
            ForwardSignalStudyResult study,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            study
        );

        ValidateStudy(
            study
        );

        Directory.CreateDirectory(
            _storeDirectory
        );

        var pendingSignals =
            study.PendingSignals
                .OrderBy(
                    signal =>
                        signal.Symbol
                )
                .ToArray();

        var signalDate =
            pendingSignals.Length > 0
                ? pendingSignals
                    .Select(
                        signal =>
                            signal.SignalDate.Date
                    )
                    .Distinct()
                    .Single()

                : study.Regime?
                    .Date
                    .Date;

        var plannedExecutionOpenUtc =
            pendingSignals.Length > 0
                ? pendingSignals
                    .Select(
                        signal =>
                            EnsureUtc(
                                signal.PlannedExecutionOpenUtc
                            )
                    )
                    .Distinct()
                    .Single()

                : (DateTime?)null;

        if (File.Exists(
                _storePath
            ))
        {
            var existingSnapshot =
                await LoadAsync(
                    cancellationToken
                );

            if (existingSnapshot is not null &&
                string.Equals(
                    existingSnapshot.PolicyVersion,
                    study.PolicyVersion,
                    StringComparison.Ordinal
                ) &&
                existingSnapshot
                    .PlannedExecutionOpenUtc
                    .HasValue &&
                plannedExecutionOpenUtc
                    .HasValue &&
                EnsureUtc(
                    existingSnapshot
                        .PlannedExecutionOpenUtc
                        .Value
                ) ==
                EnsureUtc(
                    plannedExecutionOpenUtc
                        .Value
                ))
            {
                PrintImmutableFreezeSummary(
                    existingSnapshot
                );

                return existingSnapshot;
            }
        }        

        var snapshot =
            new ForwardSignalStoreSnapshot(
                SchemaVersion:
                    CurrentSchemaVersion,

                SavedAtUtc:
                    DateTime.UtcNow,

                PolicyVersion:
                    study.PolicyVersion,

                SignalDate:
                    signalDate,

                PlannedExecutionOpenUtc:
                    plannedExecutionOpenUtc,

                Regime:
                    study.Regime,

                PendingSignals:
                    pendingSignals
            );

        var json =
            JsonSerializer.Serialize(
                snapshot,
                _jsonOptions
            );

        var temporaryPath =
            _storePath +
            ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken
            );

            File.Move(
                temporaryPath,
                _storePath,
                true
            );
        }
        finally
        {
            if (File.Exists(
                    temporaryPath
                ))
            {
                File.Delete(
                    temporaryPath
                );
            }
        }

        PrintSavedSummary(
            snapshot
        );

        return snapshot;
    }

    public async Task<ForwardSignalStoreSnapshot?>
        LoadAsync(
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(
                _storePath
            ))
        {
            return null;
        }

        string json;

        try
        {
            json =
                await File.ReadAllTextAsync(
                    _storePath,
                    cancellationToken
                );
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Unable to read forward-signal store: " +
                $"{_storePath}",
                ex
            );
        }

        ForwardSignalStoreSnapshot? snapshot;

        try
        {
            snapshot =
                JsonSerializer.Deserialize<
                    ForwardSignalStoreSnapshot
                >(
                    json,
                    _jsonOptions
                );
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Forward-signal store contains invalid JSON. " +
                "The file was not overwritten.",
                ex
            );
        }

        if (snapshot is null)
        {
            throw new InvalidDataException(
                "Forward-signal store is empty or unreadable."
            );
        }

        ValidateSnapshot(
            snapshot
        );

        return snapshot;
    }

    public async Task<
        IReadOnlyList<ForwardPendingSignal>
    >
        LoadPendingForExecutionAsync(
            string policyVersion,
            DateTime executionOpenUtc,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                policyVersion
            ))
        {
            throw new ArgumentException(
                "Policy version is required.",
                nameof(
                    policyVersion
                )
            );
        }

        var snapshot =
            await LoadAsync(
                cancellationToken
            );

        if (snapshot is null)
        {
            return Array.Empty<
                ForwardPendingSignal
            >();
        }

        var normalizedExecutionOpenUtc =
            EnsureUtc(
                executionOpenUtc
            );

        if (!string.Equals(
                snapshot.PolicyVersion,
                policyVersion,
                StringComparison.Ordinal
            ))
        {
            return Array.Empty<
                ForwardPendingSignal
            >();
        }

        if (!snapshot
                .PlannedExecutionOpenUtc
                .HasValue)
        {
            return Array.Empty<
                ForwardPendingSignal
            >();
        }

        if (EnsureUtc(
                snapshot
                    .PlannedExecutionOpenUtc
                    .Value
            ) !=
            normalizedExecutionOpenUtc)
        {
            return Array.Empty<
                ForwardPendingSignal
            >();
        }

        return snapshot.PendingSignals;
    }

    public void PrintLoadSummary(
        ForwardSignalStoreSnapshot? snapshot)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " FORWARD SIGNAL STORE V9E"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Store Path: {_storePath}"
        );

        if (snapshot is null)
        {
            Console.WriteLine(
                "Stored Signal Set: NONE"
            );

            return;
        }

        Console.WriteLine(
            $"Schema Version: " +
            $"{snapshot.SchemaVersion}"
        );

        Console.WriteLine(
            $"Policy Version: " +
            $"{snapshot.PolicyVersion}"
        );

        Console.WriteLine(
            $"Saved At UTC: " +
            $"{snapshot.SavedAtUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine(
            $"Signal Date: " +
            $"{FormatDate(snapshot.SignalDate)}"
        );

        Console.WriteLine(
            $"Planned Execution Open UTC: " +
            $"{FormatDateTime(snapshot.PlannedExecutionOpenUtc)}"
        );

        Console.WriteLine(
            $"Pending Signals: " +
            $"{snapshot.PendingSignals.Count}"
        );

        foreach (
            var signal in snapshot
                .PendingSignals
                .OrderBy(
                    signal =>
                        signal.Symbol
                ))
        {
            Console.WriteLine(
                $"  {signal.Symbol} | " +
                $"{signal.Status}"
            );
        }
    }
    
    private void PrintImmutableFreezeSummary(
    ForwardSignalStoreSnapshot snapshot)
{
    Console.WriteLine();

    Console.WriteLine(
        "=============================================="
    );

    Console.WriteLine(
        " FORWARD SIGNAL STORE V9E.1"
    );

    Console.WriteLine(
        " IMMUTABLE SESSION FREEZE"
    );

    Console.WriteLine(
        "=============================================="
    );

    Console.WriteLine(
        $"Store Path: {_storePath}"
    );

    Console.WriteLine(
        $"Policy Version: " +
        $"{snapshot.PolicyVersion}"
    );

    Console.WriteLine(
        $"Signal Date: " +
        $"{FormatDate(snapshot.SignalDate)}"
    );

    Console.WriteLine(
        $"Planned Execution Open UTC: " +
        $"{FormatDateTime(snapshot.PlannedExecutionOpenUtc)}"
    );

    Console.WriteLine(
        $"Frozen Pending Signals: " +
        $"{snapshot.PendingSignals.Count}"
    );

    Console.WriteLine();

    Console.WriteLine(
        "SESSION ALREADY FROZEN."
    );

    Console.WriteLine(
        "Existing snapshot was preserved."
    );

    Console.WriteLine(
        "Newly generated signals were NOT allowed " +
        "to overwrite this execution session."
    );

    Console.WriteLine();

    Console.WriteLine(
        "NO ORDERS WERE SUBMITTED."
    );
}

    private void PrintSavedSummary(
        ForwardSignalStoreSnapshot snapshot)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " FORWARD SIGNAL STORE V9E"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Store Path: {_storePath}"
        );

        Console.WriteLine(
            $"Policy Version: " +
            $"{snapshot.PolicyVersion}"
        );

        Console.WriteLine(
            $"Signal Date: " +
            $"{FormatDate(snapshot.SignalDate)}"
        );

        Console.WriteLine(
            $"Planned Execution Open UTC: " +
            $"{FormatDateTime(snapshot.PlannedExecutionOpenUtc)}"
        );

        Console.WriteLine(
            $"Pending Signals Saved: " +
            $"{snapshot.PendingSignals.Count}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Storage Mode: ATOMIC REPLACE"
        );

        Console.WriteLine(
            "Existing store is replaced only after the new JSON " +
            "file is written successfully."
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );
    }

    private static void ValidateStudy(
        ForwardSignalStudyResult study)
    {
        if (string.IsNullOrWhiteSpace(
                study.PolicyVersion
            ))
        {
            throw new InvalidDataException(
                "Forward signal study has no policy version."
            );
        }

        var duplicateSymbols =
            study.PendingSignals
                .GroupBy(
                    signal =>
                        signal.Symbol,
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(
                    group =>
                        group.Count() > 1
                )
                .Select(
                    group =>
                        group.Key
                )
                .ToArray();

        if (duplicateSymbols.Length > 0)
        {
            throw new InvalidDataException(
                "Forward signal study contains duplicate symbols: " +
                string.Join(
                    ", ",
                    duplicateSymbols
                )
            );
        }

        foreach (
            var signal in study.PendingSignals
        )
        {
            if (!string.Equals(
                    signal.PolicyVersion,
                    study.PolicyVersion,
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidDataException(
                    $"Signal {signal.Symbol} uses policy " +
                    $"{signal.PolicyVersion}, but the study uses " +
                    $"{study.PolicyVersion}."
                );
            }

            if (!string.Equals(
                    signal.Status,
                    "PENDING_NEXT_SESSION_EXECUTION",
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidDataException(
                    $"Signal {signal.Symbol} has unexpected status " +
                    $"{signal.Status}."
                );
            }

            if (EnsureUtc(
                    signal.PlannedExecutionOpenUtc
                ) <=
                signal.SignalDate.Date)
            {
                throw new InvalidDataException(
                    $"Signal {signal.Symbol} has an invalid " +
                    "planned execution time."
                );
            }
        }
    }

    private static void ValidateSnapshot(
        ForwardSignalStoreSnapshot snapshot)
    {
        if (snapshot.SchemaVersion !=
            CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported forward-signal store schema " +
                $"{snapshot.SchemaVersion}. " +
                $"Expected {CurrentSchemaVersion}."
            );
        }

        if (string.IsNullOrWhiteSpace(
                snapshot.PolicyVersion
            ))
        {
            throw new InvalidDataException(
                "Stored forward-signal policy version is missing."
            );
        }

        var duplicateSymbols =
            snapshot.PendingSignals
                .GroupBy(
                    signal =>
                        signal.Symbol,
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(
                    group =>
                        group.Count() > 1
                )
                .Select(
                    group =>
                        group.Key
                )
                .ToArray();

        if (duplicateSymbols.Length > 0)
        {
            throw new InvalidDataException(
                "Stored forward signals contain duplicate symbols: " +
                string.Join(
                    ", ",
                    duplicateSymbols
                )
            );
        }

        foreach (
            var signal in snapshot.PendingSignals
        )
        {
            if (!string.Equals(
                    signal.PolicyVersion,
                    snapshot.PolicyVersion,
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidDataException(
                    $"Stored signal {signal.Symbol} has a policy " +
                    "version mismatch."
                );
            }
        }
    }

    private static string BuildDefaultStoreDirectory()
    {
        var localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .LocalApplicationData
            );

        if (string.IsNullOrWhiteSpace(
                localApplicationData
            ))
        {
            throw new InvalidOperationException(
                "Local application-data directory is unavailable."
            );
        }

        return Path.Combine(
            localApplicationData,
            "AI-Trading-Agent",
            "paper-state"
        );
    }

    private static DateTime EnsureUtc(
        DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc =>
                value,

            DateTimeKind.Local =>
                value.ToUniversalTime(),

            _ =>
                DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc
                )
        };
    }

    private static string FormatDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value
                .ToString(
                    "yyyy-MM-dd"
                )
            : "N/A";
    }

    private static string FormatDateTime(
        DateTime? value)
    {
        return value.HasValue
            ? EnsureUtc(
                value.Value
              )
              .ToString(
                  "yyyy-MM-dd HH:mm:ss"
              ) +
              " UTC"
            : "N/A";
    }
}

public sealed record ForwardSignalStoreSnapshot(
    int SchemaVersion,
    DateTime SavedAtUtc,
    string PolicyVersion,
    DateTime? SignalDate,
    DateTime? PlannedExecutionOpenUtc,
    MarketRegimeSnapshot? Regime,
    IReadOnlyList<ForwardPendingSignal> PendingSignals
);
