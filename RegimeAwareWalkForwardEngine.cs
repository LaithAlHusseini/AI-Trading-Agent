public sealed partial class PortfolioBacktestEngine
{
    public async Task<RegimeAwareWalkForwardStudyResult>
        RunRegimeAwareWalkForwardStudyAsync(
            IEnumerable<string> symbols,
            IReadOnlyCollection<RegimeAwareCandidateDefinition> candidates)
    {
        var tradingSymbols =
            symbols
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        if (tradingSymbols.Length == 0 ||
            candidates.Count == 0)
        {
            return new RegimeAwareWalkForwardStudyResult(
                Array.Empty<RegimeAwareOuterFoldResult>()
            );
        }

        var endDate =
            DateTime.UtcNow.Date;

        var overallStart =
            endDate.AddYears(
                -BacktestYears
            );

        var history =
            new Dictionary<string, List<Alpaca.Markets.IBar>>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var symbol in tradingSymbols)
        {
            history[symbol] =
                await LoadHistoricalBarsAsync(
                    symbol
                );
        }

        var benchmarkBars =
            await LoadMarketRegimeBenchmarkAsync(
                "SPY",
                overallStart,
                endDate
            );

        var regimeSnapshots =
            BuildMarketRegimeSnapshots(
                benchmarkBars
            );

        var outerFolds =
            BuildWalkForwardFolds(
                overallStart,
                endDate
            );

        var outerResults =
            new List<RegimeAwareOuterFoldResult>();

        foreach (var outerFold in outerFolds)
        {
            var innerFolds =
                BuildInnerWalkForwardFolds(
                    outerFold.TrainingStart,
                    outerFold.TrainingEnd
                );

            var candidateSelections =
                new List<RegimeAwareCandidateSelectionResult>();

            foreach (var candidate in candidates)
            {
                var innerValidationResults =
                    new List<RegimeAwareInnerValidationResult>();

                foreach (var innerFold in innerFolds)
                {
                    var simulation =
                        SimulatePortfolio(
                            $"V8 INNER {outerFold.Index}.{innerFold.Index} - " +
                            $"{candidate.Name}",
                            tradingSymbols,
                            history,
                            innerFold.ValidationStart,
                            innerFold.ValidationEnd,
                            candidate.Configuration,
                            signal =>
                                PassesRegimeGate(
                                    signal,
                                    candidate.Gate,
                                    regimeSnapshots
                                )
                        );

                    var result =
                        BuildStrategyExperimentResult(
                            candidate.Configuration,
                            simulation
                        );

                    innerValidationResults.Add(
                        new RegimeAwareInnerValidationResult(
                            innerFold,
                            result
                        )
                    );
                }

                candidateSelections.Add(
                    BuildCandidateSelectionResult(
                        candidate,
                        innerValidationResults
                    )
                );
            }

            var qualifiedCandidates =
                candidateSelections
                    .Where(
                        result =>
                            result.InnerValidationFolds >= 2
                    )
                    .Where(
                        result =>
                            result.Trades >= 12
                    )
                    .Where(
                        result =>
                            result.PositiveFoldRate >= 0.50
                    )
                    .Where(
                        result =>
                            result.ProfitFactor > 1.0
                    )
                    .Where(
                        result =>
                            result.AverageR > 0
                    )
                    .Where(
                        result =>
                            result.NetPnl > 0
                    )
                    .OrderByDescending(
                        result =>
                            result.PositiveFoldRate
                    )
                    .ThenByDescending(
                        result =>
                            result.ProfitFactor
                    )
                    .ThenByDescending(
                        result =>
                            result.AverageR
                    )
                    .ThenBy(
                        result =>
                            result.WorstFoldDrawdownPercent
                    )
                    .ToList();

            var selectedCandidate =
                qualifiedCandidates
                    .FirstOrDefault();

            StrategyExperimentResult?
                outerValidationResult =
                    null;

            if (selectedCandidate is not null)
            {
                var validationSimulation =
                    SimulatePortfolio(
                        $"V8 OUTER VALIDATION {outerFold.Index} - " +
                        $"{selectedCandidate.Candidate.Name}",
                        tradingSymbols,
                        history,
                        outerFold.ValidationStart,
                        outerFold.ValidationEnd,
                        selectedCandidate.Candidate.Configuration,
                        signal =>
                            PassesRegimeGate(
                                signal,
                                selectedCandidate.Candidate.Gate,
                                regimeSnapshots
                            )
                    );

                outerValidationResult =
                    BuildStrategyExperimentResult(
                        selectedCandidate.Candidate.Configuration,
                        validationSimulation
                    );
            }

            outerResults.Add(
                new RegimeAwareOuterFoldResult(
                    outerFold,
                    innerFolds,
                    candidateSelections,
                    selectedCandidate,
                    outerValidationResult
                )
            );
        }

        return new RegimeAwareWalkForwardStudyResult(
            outerResults
        );
    }

    private static IReadOnlyList<RegimeAwareInnerFoldDefinition>
        BuildInnerWalkForwardFolds(
            DateTime trainingStart,
            DateTime trainingEnd)
    {
        var folds =
            new List<RegimeAwareInnerFoldDefinition>();

        var firstValidationStart =
            trainingStart
                .AddYears(1)
                .Date;

        // Align the first validation period to the next month boundary.
        firstValidationStart =
            new DateTime(
                firstValidationStart.Year,
                firstValidationStart.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc
            )
            .AddMonths(1);

        var validationStart =
            firstValidationStart;

        var index =
            1;

        while (validationStart <= trainingEnd.Date)
        {
            var validationEnd =
                validationStart
                    .AddMonths(6)
                    .AddDays(-1);

            if (validationEnd > trainingEnd.Date)
            {
                validationEnd =
                    trainingEnd.Date;
            }

            var durationDays =
                (
                    validationEnd -
                    validationStart
                ).Days +
                1;

            // Ignore very short partial windows.
            if (durationDays >= 90)
            {
                folds.Add(
                    new RegimeAwareInnerFoldDefinition(
                        index,
                        trainingStart.Date,
                        validationStart.AddDays(-1),
                        validationStart,
                        validationEnd
                    )
                );

                index++;
            }

            validationStart =
                validationStart
                    .AddMonths(6);
        }

        return folds;
    }

    private static bool PassesRegimeGate(
        PortfolioSignal signal,
        RegimeGateMode gate,
        IReadOnlyList<MarketRegimeSnapshot> snapshots)
    {
        if (gate == RegimeGateMode.None)
        {
            return true;
        }

        var regime =
            snapshots
                .LastOrDefault(
                    snapshot =>
                        snapshot.Date <
                        signal.EntryDate.Date
                );

        if (regime is null)
        {
            return false;
        }

        return gate switch
        {
            RegimeGateMode.BullOnly =>
                regime.TrendRegime ==
                "BULL_TREND",

            RegimeGateMode.LowVolOnly =>
                regime.VolatilityRegime ==
                "LOW_VOL",

            RegimeGateMode.BullAndLowVol =>
                regime.TrendRegime ==
                "BULL_TREND" &&
                regime.VolatilityRegime ==
                "LOW_VOL",

            _ =>
                true
        };
    }

    private static RegimeAwareCandidateSelectionResult
        BuildCandidateSelectionResult(
            RegimeAwareCandidateDefinition candidate,
            IReadOnlyList<RegimeAwareInnerValidationResult> innerResults)
    {
        var completed =
            innerResults
                .Where(
                    item =>
                        item.Result is not null
                )
                .ToList();

        var trades =
            completed
                .SelectMany(
                    item =>
                        item.Result
                            .Simulation
                            .Trades
                )
                .ToList();

        var stats =
            CalculateRegimeAwareAggregateStats(
                trades
            );

        var positiveFolds =
            completed.Count(
                item =>
                    IsPositiveRegimeAwareResult(
                        item.Result
                    )
            );

        var positiveFoldRate =
            completed.Count > 0
                ? positiveFolds /
                  (double)completed.Count
                : 0;

        var netPnl =
            completed.Sum(
                item =>
                    item.Result.NetPnl
            );

        var worstFoldDrawdown =
            completed.Count > 0
                ? completed.Max(
                    item =>
                        item.Result
                            .MaxDrawdownPercent
                  )
                : 0;

        return new RegimeAwareCandidateSelectionResult(
            candidate,
            completed.Count,
            positiveFolds,
            positiveFoldRate,
            stats.Trades,
            stats.WinRate,
            stats.ProfitFactor,
            stats.AverageR,
            netPnl,
            worstFoldDrawdown,
            innerResults
        );
    }

    private static RegimeAwareAggregateStats
        CalculateRegimeAwareAggregateStats(
            IReadOnlyCollection<PortfolioTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new RegimeAwareAggregateStats(
                0,
                0,
                0,
                0
            );
        }

        var wins =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losses =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var grossProfit =
            wins.Sum(
                trade =>
                    trade.Pnl
            );

        var grossLoss =
            Math.Abs(
                losses.Sum(
                    trade =>
                        trade.Pnl
                )
            );

        var profitFactor =
            grossLoss > 0
                ? grossProfit /
                  grossLoss
                : grossProfit > 0
                    ? double.PositiveInfinity
                    : 0;

        var winRate =
            wins.Count /
            (double)trades.Count *
            100.0;

        var averageR =
            trades.Average(
                trade =>
                    trade.RMultiple
            );

        return new RegimeAwareAggregateStats(
            trades.Count,
            winRate,
            profitFactor,
            averageR
        );
    }

    private static bool IsPositiveRegimeAwareResult(
        StrategyExperimentResult result)
    {
        return
            result.Trades > 0 &&
            result.ProfitFactor > 1.0 &&
            result.AverageR > 0 &&
            result.NetPnl > 0;
    }
}

public sealed class RegimeAwareWalkForwardEngine
{
    private readonly PortfolioBacktestEngine
        _portfolioBacktestEngine;

    public RegimeAwareWalkForwardEngine(
        PortfolioBacktestEngine portfolioBacktestEngine)
    {
        _portfolioBacktestEngine =
            portfolioBacktestEngine;
    }

    public async Task<RegimeAwareWalkForwardStudyResult>
        RunAsync(
            IEnumerable<string> symbols)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " REGIME-AWARE WALK-FORWARD ENGINE V8"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            "Outer validation: 6-month walk-forward periods."
        );

        Console.WriteLine(
            "Candidate selection uses INNER historical walk-forward"
        );

        Console.WriteLine(
            "validation inside each outer training period."
        );

        Console.WriteLine(
            "The outer validation period is never used to choose"
        );

        Console.WriteLine(
            "its own strategy or regime gate."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Candidate family:"
        );

        Console.WriteLine(
            "  ENTRY_GAP_0_TO_1"
        );

        Console.WriteLine(
            "  STOP_1_5_TO_2_ATR"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Regime gates:"
        );

        Console.WriteLine(
            "  NONE"
        );

        Console.WriteLine(
            "  BULL_ONLY"
        );

        Console.WriteLine(
            "  LOW_VOL_ONLY"
        );

        Console.WriteLine(
            "  BULL_AND_LOW_VOL"
        );

        Console.WriteLine();

        var candidates =
            BuildCandidates();

        var study =
            await _portfolioBacktestEngine
                .RunRegimeAwareWalkForwardStudyAsync(
                    symbols,
                    candidates
                );

        PrintOuterFolds(
            study
        );

        PrintSummary(
            study
        );

        Console.WriteLine();

        Console.WriteLine(
            "Important V8 research note:"
        );

        Console.WriteLine(
            "- The candidate family and regime definitions were informed"
        );

        Console.WriteLine(
            "  by earlier historical diagnostics."
        );

        Console.WriteLine(
            "- V8 reduces selection leakage with inner walk-forward"
        );

        Console.WriteLine(
            "  selection, but it is still not a pristine untouched holdout."
        );

        Console.WriteLine(
            "- Paper trading remains the required forward-validation gate"
        );

        Console.WriteLine(
            "  before any live-trading decision."
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return study;
    }

    private static IReadOnlyList<RegimeAwareCandidateDefinition>
        BuildCandidates()
    {
        var entryGap =
            new StrategyExperimentConfig(
                "ENTRY_GAP_0_TO_1",
                MinEntryGapPercent: 0.00,
                MaxEntryGapPercent: 1.00
            );

        var stopDistance =
            new StrategyExperimentConfig(
                "STOP_1_5_TO_2_ATR",
                MinStopDistanceAtr: 1.50,
                MaxStopDistanceAtr: 2.00
            );

        var strategies =
            new[]
            {
                entryGap,
                stopDistance
            };

        var gates =
            new[]
            {
                RegimeGateMode.None,
                RegimeGateMode.BullOnly,
                RegimeGateMode.LowVolOnly,
                RegimeGateMode.BullAndLowVol
            };

        var candidates =
            new List<RegimeAwareCandidateDefinition>();

        foreach (var strategy in strategies)
        {
            foreach (var gate in gates)
            {
                candidates.Add(
                    new RegimeAwareCandidateDefinition(
                        $"{strategy.Name}__{GateName(gate)}",
                        strategy,
                        gate
                    )
                );
            }
        }

        return candidates;
    }

    private static void PrintOuterFolds(
        RegimeAwareWalkForwardStudyResult study)
    {
        foreach (var fold in study.Folds)
        {
            Console.WriteLine();

            Console.WriteLine(
                "=============================================="
            );

            Console.WriteLine(
                $" OUTER FOLD {fold.Fold.Index}"
            );

            Console.WriteLine(
                "=============================================="
            );

            Console.WriteLine(
                $"Training:   " +
                $"{fold.Fold.TrainingStart:yyyy-MM-dd} -> " +
                $"{fold.Fold.TrainingEnd:yyyy-MM-dd}"
            );

            Console.WriteLine(
                $"Validation: " +
                $"{fold.Fold.ValidationStart:yyyy-MM-dd} -> " +
                $"{fold.Fold.ValidationEnd:yyyy-MM-dd}"
            );

            Console.WriteLine(
                $"Inner forward-validation windows: " +
                $"{fold.InnerFolds.Count}"
            );

            Console.WriteLine();

            Console.WriteLine(
                "INNER SELECTION RANKING"
            );

            foreach (
                var candidate in fold.CandidateSelections
                    .OrderByDescending(
                        result =>
                            result.PositiveFoldRate
                    )
                    .ThenByDescending(
                        result =>
                            result.ProfitFactor
                    )
                    .ThenByDescending(
                        result =>
                            result.AverageR
                    ))
            {
                var eligible =
                    IsSelectionEligible(
                        candidate
                    );

                Console.WriteLine(
                    $"  {candidate.Candidate.Name,-48} | " +
                    $"Inner {candidate.InnerValidationFolds,2} | " +
                    $"Pos {candidate.PositiveFolds,2} " +
                    $"({candidate.PositiveFoldRate * 100,6:F2}%) | " +
                    $"Trades {candidate.Trades,3} | " +
                    $"PF {FormatNumber(candidate.ProfitFactor),6} | " +
                    $"Avg R {candidate.AverageR,6:F2} | " +
                    $"P&L ${candidate.NetPnl,7:F2} | " +
                    $"Eligible {(eligible ? "YES" : "NO")}"
                );
            }

            Console.WriteLine();

            if (fold.SelectedCandidate is null)
            {
                Console.WriteLine(
                    "Selected candidate: NONE"
                );

                Console.WriteLine(
                    "Outer validation action: ABSTAIN"
                );

                continue;
            }

            Console.WriteLine(
                $"Selected candidate: " +
                $"{fold.SelectedCandidate.Candidate.Name}"
            );

            Console.WriteLine();

            Console.WriteLine(
                "OUTER FORWARD VALIDATION"
            );

            PrintStrategyResult(
                fold.OuterValidationResult!
            );

            Console.WriteLine(
                $"Fold classification: " +
                $"{(IsPositiveOuterResult(fold.OuterValidationResult!) ? "POSITIVE" : "NEGATIVE")}"
            );
        }
    }

    private static void PrintSummary(
        RegimeAwareWalkForwardStudyResult study)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " V8 OUTER WALK-FORWARD SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        var completed =
            study.Folds
                .Where(
                    fold =>
                        fold.OuterValidationResult is not null
                )
                .ToList();

        var abstained =
            study.Folds.Count -
            completed.Count;

        var positive =
            completed.Count(
                fold =>
                    IsPositiveOuterResult(
                        fold.OuterValidationResult!
                    )
            );

        var negative =
            completed.Count -
            positive;

        var trades =
            completed
                .SelectMany(
                    fold =>
                        fold.OuterValidationResult!
                            .Simulation
                            .Trades
                )
                .OrderBy(
                    trade =>
                        trade.ExitDate
                )
                .ToList();

        var aggregate =
            CalculateSummaryStats(
                trades
            );

        Console.WriteLine(
            $"Total outer folds: {study.Folds.Count}"
        );

        Console.WriteLine(
            $"Completed outer validations: {completed.Count}"
        );

        Console.WriteLine(
            $"Abstained folds: {abstained}"
        );

        Console.WriteLine(
            $"Positive folds: {positive}"
        );

        Console.WriteLine(
            $"Negative folds: {negative}"
        );

        if (completed.Count > 0)
        {
            Console.WriteLine(
                $"Positive fold rate: " +
                $"{positive / (double)completed.Count * 100:F2}%"
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "AGGREGATED OUTER VALIDATION"
        );

        Console.WriteLine(
            $"Trades: {aggregate.Trades}"
        );

        Console.WriteLine(
            $"Win Rate: {aggregate.WinRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(aggregate.ProfitFactor)}"
        );

        Console.WriteLine(
            $"Average R: {aggregate.AverageR:F2}"
        );

        var summedPnl =
            completed.Sum(
                fold =>
                    fold.OuterValidationResult!
                        .NetPnl
            );

        var averageFoldReturn =
            completed.Count > 0
                ? completed.Average(
                    fold =>
                        fold.OuterValidationResult!
                            .ReturnPercent
                  )
                : 0;

        var worstFoldDrawdown =
            completed.Count > 0
                ? completed.Max(
                    fold =>
                        fold.OuterValidationResult!
                            .MaxDrawdownPercent
                  )
                : 0;

        Console.WriteLine(
            $"Sum of Fold Fixed-Capital P&L: " +
            $"${summedPnl:F2}"
        );

        Console.WriteLine(
            $"Average Fold Return: {averageFoldReturn:F2}%"
        );

        Console.WriteLine(
            $"Worst Fold Max Drawdown: " +
            $"{worstFoldDrawdown:F2}%"
        );

        Console.WriteLine();

        Console.WriteLine(
            "SELECTION FREQUENCY"
        );

        var selectionGroups =
            completed
                .GroupBy(
                    fold =>
                        fold.SelectedCandidate!
                            .Candidate
                            .Name
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key
                )
                .ToList();

        if (selectionGroups.Count == 0)
        {
            Console.WriteLine(
                "  No candidate was selected."
            );
        }
        else
        {
            foreach (var group in selectionGroups)
            {
                Console.WriteLine(
                    $"  {group.Key,-48} " +
                    $"{group.Count()}/{completed.Count} folds"
                );
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "V8 INTERPRETATION"
        );

        if (completed.Count < 3)
        {
            Console.WriteLine(
                "INSUFFICIENT OUTER EVIDENCE: too few completed folds."
            );

            return;
        }

        var positiveFoldRate =
            positive /
            (double)completed.Count;

        if (positiveFoldRate >= 0.70 &&
            aggregate.ProfitFactor >= 1.30 &&
            aggregate.AverageR > 0.10 &&
            aggregate.Trades >= 30)
        {
            Console.WriteLine(
                "PROMISING REGIME-AWARE ROBUSTNESS: " +
                "eligible for disciplined paper validation."
            );
        }
        else if (
            positiveFoldRate >= 0.50 &&
            aggregate.ProfitFactor > 1.00 &&
            aggregate.AverageR > 0)
        {
            Console.WriteLine(
                "MIXED / WEAK REGIME-AWARE EDGE: " +
                "continue research; do not promote yet."
            );
        }
        else
        {
            Console.WriteLine(
                "NO ROBUST REGIME-AWARE EDGE: " +
                "do not promote the strategy."
            );
        }
    }

    private static RegimeAwareSummaryStats
        CalculateSummaryStats(
            IReadOnlyCollection<PortfolioTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new RegimeAwareSummaryStats(
                0,
                0,
                0,
                0
            );
        }

        var wins =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losses =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var grossProfit =
            wins.Sum(
                trade =>
                    trade.Pnl
            );

        var grossLoss =
            Math.Abs(
                losses.Sum(
                    trade =>
                        trade.Pnl
                )
            );

        var profitFactor =
            grossLoss > 0
                ? grossProfit /
                  grossLoss
                : grossProfit > 0
                    ? double.PositiveInfinity
                    : 0;

        var winRate =
            wins.Count /
            (double)trades.Count *
            100.0;

        var averageR =
            trades.Average(
                trade =>
                    trade.RMultiple
            );

        return new RegimeAwareSummaryStats(
            trades.Count,
            winRate,
            profitFactor,
            averageR
        );
    }

    private static bool IsSelectionEligible(
        RegimeAwareCandidateSelectionResult result)
    {
        return
            result.InnerValidationFolds >= 2 &&
            result.Trades >= 12 &&
            result.PositiveFoldRate >= 0.50 &&
            result.ProfitFactor > 1.0 &&
            result.AverageR > 0 &&
            result.NetPnl > 0;
    }

    private static bool IsPositiveOuterResult(
        StrategyExperimentResult result)
    {
        return
            result.Trades > 0 &&
            result.ProfitFactor > 1.0 &&
            result.AverageR > 0 &&
            result.NetPnl > 0;
    }

    private static void PrintStrategyResult(
        StrategyExperimentResult result)
    {
        Console.WriteLine(
            $"  Trades: {result.Trades}"
        );

        Console.WriteLine(
            $"  Win Rate: {result.WinRate:F2}%"
        );

        Console.WriteLine(
            $"  Profit Factor: " +
            $"{FormatNumber(result.ProfitFactor)}"
        );

        Console.WriteLine(
            $"  Average R: {result.AverageR:F2}"
        );

        Console.WriteLine(
            $"  Net P&L: ${result.NetPnl:F2}"
        );

        Console.WriteLine(
            $"  Return: {result.ReturnPercent:F2}%"
        );

        Console.WriteLine(
            $"  Max Drawdown: {result.MaxDrawdownPercent:F2}%"
        );
    }

    private static string GateName(
        RegimeGateMode gate)
    {
        return gate switch
        {
            RegimeGateMode.None =>
                "NO_GATE",

            RegimeGateMode.BullOnly =>
                "BULL_ONLY",

            RegimeGateMode.LowVolOnly =>
                "LOW_VOL_ONLY",

            RegimeGateMode.BullAndLowVol =>
                "BULL_LOW_VOL",

            _ =>
                "UNKNOWN"
        };
    }

    private static string FormatNumber(
        double value)
    {
        return
            double.IsPositiveInfinity(
                value
            )
                ? "∞"
                : value.ToString(
                    "F2"
                );
    }
}

public enum RegimeGateMode
{
    None = 0,
    BullOnly = 1,
    LowVolOnly = 2,
    BullAndLowVol = 3
}

public sealed record RegimeAwareCandidateDefinition(
    string Name,
    StrategyExperimentConfig Configuration,
    RegimeGateMode Gate
);

public sealed record RegimeAwareInnerFoldDefinition(
    int Index,
    DateTime TrainingStart,
    DateTime TrainingEnd,
    DateTime ValidationStart,
    DateTime ValidationEnd
);

public sealed record RegimeAwareInnerValidationResult(
    RegimeAwareInnerFoldDefinition Fold,
    StrategyExperimentResult Result
);

public sealed record RegimeAwareCandidateSelectionResult(
    RegimeAwareCandidateDefinition Candidate,
    int InnerValidationFolds,
    int PositiveFolds,
    double PositiveFoldRate,
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR,
    double NetPnl,
    double WorstFoldDrawdownPercent,
    IReadOnlyList<RegimeAwareInnerValidationResult> InnerResults
);

public sealed record RegimeAwareOuterFoldResult(
    WalkForwardFoldDefinition Fold,
    IReadOnlyList<RegimeAwareInnerFoldDefinition> InnerFolds,
    IReadOnlyList<RegimeAwareCandidateSelectionResult> CandidateSelections,
    RegimeAwareCandidateSelectionResult? SelectedCandidate,
    StrategyExperimentResult? OuterValidationResult
);

public sealed record RegimeAwareWalkForwardStudyResult(
    IReadOnlyList<RegimeAwareOuterFoldResult> Folds
);

public sealed record RegimeAwareAggregateStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR
);

public sealed record RegimeAwareSummaryStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR
);
