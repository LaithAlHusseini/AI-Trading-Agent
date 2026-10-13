using Alpaca.Markets;

public sealed partial class PortfolioBacktestEngine
{
    public async Task<WalkForwardStudyResult>
        RunWalkForwardStudyAsync(
            IEnumerable<string> symbols,
            IReadOnlyCollection<StrategyExperimentConfig> configurations)
    {
        var tradingSymbols =
            symbols
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        if (tradingSymbols.Length == 0 ||
            configurations.Count == 0)
        {
            return new WalkForwardStudyResult(
                Array.Empty<WalkForwardFoldResult>()
            );
        }

        var history =
            new Dictionary<string, List<IBar>>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var symbol in tradingSymbols)
        {
            var bars =
                await LoadHistoricalBarsAsync(
                    symbol
                );

            history[symbol] =
                bars;
        }

        var endDate =
            DateTime.UtcNow.Date;

        var overallStart =
            endDate.AddYears(
                -BacktestYears
            );

        var folds =
            BuildWalkForwardFolds(
                overallStart,
                endDate
            );

        var foldResults =
            new List<WalkForwardFoldResult>();

        foreach (var fold in folds)
        {
            var trainingResults =
                new List<StrategyExperimentResult>();

            foreach (var configuration in configurations)
            {
                var trainingSimulation =
                    SimulatePortfolio(
                        $"V6 TRAIN FOLD {fold.Index} - " +
                        $"{configuration.Name}",
                        tradingSymbols,
                        history,
                        fold.TrainingStart,
                        fold.TrainingEnd,
                        configuration
                    );

                trainingResults.Add(
                    BuildStrategyExperimentResult(
                        configuration,
                        trainingSimulation
                    )
                );
            }

            var baseline =
                trainingResults.FirstOrDefault(
                    result =>
                        result.Configuration.Name ==
                        "BASELINE"
                );

            var minimumTrades =
                baseline is null
                    ? 20
                    : Math.Max(
                        20,
                        (int)Math.Ceiling(
                            baseline.Trades *
                            0.30
                        )
                    );

            var selectedTrainingResult =
                trainingResults
                    .Where(
                        result =>
                            result.Trades >=
                            minimumTrades
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
                            result.ProfitFactor
                    )
                    .ThenByDescending(
                        result =>
                            result.AverageR
                    )
                    .ThenBy(
                        result =>
                            result.MaxDrawdownPercent
                    )
                    .FirstOrDefault();

            StrategyExperimentResult?
                validationResult =
                    null;

            if (selectedTrainingResult is not null)
            {
                var validationSimulation =
                    SimulatePortfolio(
                        $"V6 VALIDATION FOLD {fold.Index} - " +
                        $"{selectedTrainingResult.Configuration.Name}",
                        tradingSymbols,
                        history,
                        fold.ValidationStart,
                        fold.ValidationEnd,
                        selectedTrainingResult.Configuration
                    );

                validationResult =
                    BuildStrategyExperimentResult(
                        selectedTrainingResult.Configuration,
                        validationSimulation
                    );
            }

            foldResults.Add(
                new WalkForwardFoldResult(
                    fold,
                    minimumTrades,
                    trainingResults,
                    selectedTrainingResult,
                    validationResult
                )
            );
        }

        return new WalkForwardStudyResult(
            foldResults
        );
    }

    private static IReadOnlyList<WalkForwardFoldDefinition>
        BuildWalkForwardFolds(
            DateTime overallStart,
            DateTime endDate)
    {
        var folds =
            new List<WalkForwardFoldDefinition>();

        // Start forward validation only after a substantial
        // expanding training history has accumulated.
        var firstValidationStart =
            new DateTime(
                overallStart.Year + 3,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc
            );

        if (firstValidationStart <=
            overallStart.AddYears(2))
        {
            firstValidationStart =
                overallStart
                    .AddYears(2)
                    .Date;
        }

        var validationStart =
            firstValidationStart;

        var index =
            1;

        while (validationStart <= endDate)
        {
            var validationEnd =
                validationStart
                    .AddMonths(6)
                    .AddDays(-1);

            if (validationEnd > endDate)
            {
                validationEnd =
                    endDate;
            }

            var trainingEnd =
                validationStart
                    .AddDays(-1);

            if (trainingEnd >= overallStart)
            {
                folds.Add(
                    new WalkForwardFoldDefinition(
                        index,
                        overallStart,
                        trainingEnd,
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
}

public sealed class WalkForwardValidationEngine
{
    private readonly PortfolioBacktestEngine
        _portfolioBacktestEngine;

    public WalkForwardValidationEngine(
        PortfolioBacktestEngine portfolioBacktestEngine)
    {
        _portfolioBacktestEngine =
            portfolioBacktestEngine;
    }

    public async Task<WalkForwardStudyResult> RunAsync(
        IEnumerable<string> symbols)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " WALK-FORWARD VALIDATION ENGINE V6"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            "Method: expanding training window + " +
            "6-month forward validation."
        );

        Console.WriteLine(
            "Each fold selects a configuration using " +
            "TRAINING DATA ONLY."
        );

        Console.WriteLine(
            "The selected configuration is then frozen " +
            "for that fold's validation period."
        );

        Console.WriteLine(
            "No orders are submitted."
        );

        Console.WriteLine();

        var configurations =
            BuildConfigurations();

        var study =
            await _portfolioBacktestEngine
                .RunWalkForwardStudyAsync(
                    symbols,
                    configurations
                );

        PrintFoldResults(
            study
        );

        PrintSummary(
            study
        );

        Console.WriteLine();

        Console.WriteLine(
            "Important V6 research note:"
        );

        Console.WriteLine(
            "- The candidate family was informed by earlier diagnostics,"
        );

        Console.WriteLine(
            "  so this is a stability test, not a pristine final holdout."
        );

        Console.WriteLine(
            "- A configuration is never selected using its own " +
            "validation period."
        );

        Console.WriteLine(
            "- Paper trading remains the stronger forward-validation gate"
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

    private static IReadOnlyList<StrategyExperimentConfig>
        BuildConfigurations()
    {
        return new[]
        {
            StrategyExperimentConfig.Baseline,

            new StrategyExperimentConfig(
                "OPPORTUNITY_80",
                MinOpportunityScore: 80
            ),

            new StrategyExperimentConfig(
                "RR_2_5_TO_4",
                MinRiskReward: 2.50,
                MaxRiskReward: 4.00
            ),

            new StrategyExperimentConfig(
                "STOP_1_5_TO_2_ATR",
                MinStopDistanceAtr: 1.50,
                MaxStopDistanceAtr: 2.00
            ),

            new StrategyExperimentConfig(
                "ENTRY_GAP_0_TO_1",
                MinEntryGapPercent: 0.00,
                MaxEntryGapPercent: 1.00
            ),

            new StrategyExperimentConfig(
                "OPP80_RR_2_5_TO_4",
                MinOpportunityScore: 80,
                MinRiskReward: 2.50,
                MaxRiskReward: 4.00
            ),

            new StrategyExperimentConfig(
                "OPP80_RR_2_5_TO_4_STOP_1_5_TO_2",
                MinOpportunityScore: 80,
                MinRiskReward: 2.50,
                MaxRiskReward: 4.00,
                MinStopDistanceAtr: 1.50,
                MaxStopDistanceAtr: 2.00
            )
        };
    }

    private static void PrintFoldResults(
        WalkForwardStudyResult study)
    {
        foreach (var foldResult in study.Folds)
        {
            var fold =
                foldResult.Fold;

            Console.WriteLine(
                "=============================================="
            );

            Console.WriteLine(
                $" FOLD {fold.Index}"
            );

            Console.WriteLine(
                "=============================================="
            );

            Console.WriteLine(
                $"Training:   " +
                $"{fold.TrainingStart:yyyy-MM-dd} -> " +
                $"{fold.TrainingEnd:yyyy-MM-dd}"
            );

            Console.WriteLine(
                $"Validation: " +
                $"{fold.ValidationStart:yyyy-MM-dd} -> " +
                $"{fold.ValidationEnd:yyyy-MM-dd}"
            );

            Console.WriteLine(
                $"Minimum training trades for selection: " +
                $"{foldResult.MinimumTrainingTrades}"
            );

            Console.WriteLine();

            if (foldResult.SelectedTrainingResult is null)
            {
                Console.WriteLine(
                    "Selected configuration: NONE"
                );

                Console.WriteLine(
                    "Reason: no configuration passed the " +
                    "training qualification gate."
                );

                Console.WriteLine(
                    "Validation action: ABSTAIN / NO STRATEGY"
                );

                Console.WriteLine();

                continue;
            }

            var selected =
                foldResult.SelectedTrainingResult;

            var validation =
                foldResult.ValidationResult!;

            Console.WriteLine(
                $"Selected configuration: " +
                $"{selected.Configuration.Name}"
            );

            Console.WriteLine();

            Console.WriteLine(
                "TRAINING RESULT"
            );

            PrintCompactResult(
                selected
            );

            Console.WriteLine();

            Console.WriteLine(
                "FORWARD VALIDATION RESULT"
            );

            PrintCompactResult(
                validation
            );

            Console.WriteLine();

            var validationPositive =
                IsPositiveValidation(
                    validation
                );

            Console.WriteLine(
                $"Fold classification: " +
                $"{(validationPositive ? "POSITIVE" : "NEGATIVE")}"
            );

            Console.WriteLine();
        }
    }

    private static void PrintSummary(
        WalkForwardStudyResult study)
    {
        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " WALK-FORWARD SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        var completed =
            study.Folds
                .Where(
                    fold =>
                        fold.ValidationResult is not null
                )
                .ToList();

        var abstained =
            study.Folds.Count -
            completed.Count;

        var positive =
            completed.Count(
                fold =>
                    IsPositiveValidation(
                        fold.ValidationResult!
                    )
            );

        var negative =
            completed.Count -
            positive;

        var allValidationTrades =
            completed
                .SelectMany(
                    fold =>
                        fold.ValidationResult!
                            .Simulation
                            .Trades
                )
                .OrderBy(
                    trade =>
                        trade.ExitDate
                )
                .ToList();

        Console.WriteLine(
            $"Total folds: {study.Folds.Count}"
        );

        Console.WriteLine(
            $"Folds with qualified strategy: " +
            $"{completed.Count}"
        );

        Console.WriteLine(
            $"Abstained folds: {abstained}"
        );

        Console.WriteLine(
            $"Positive validation folds: {positive}"
        );

        Console.WriteLine(
            $"Negative validation folds: {negative}"
        );

        if (completed.Count > 0)
        {
            Console.WriteLine(
                $"Positive fold rate: " +
                $"{positive / (double)completed.Count * 100:F2}%"
            );
        }

        Console.WriteLine();

        PrintAggregateValidationStats(
            allValidationTrades,
            completed
        );

        Console.WriteLine();

        Console.WriteLine(
            "CONFIGURATION SELECTION FREQUENCY"
        );

        var selectionGroups =
            completed
                .GroupBy(
                    fold =>
                        fold.SelectedTrainingResult!
                            .Configuration
                            .Name
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key
                );

        if (!selectionGroups.Any())
        {
            Console.WriteLine(
                "  No configurations were selected."
            );
        }
        else
        {
            foreach (var group in selectionGroups)
            {
                Console.WriteLine(
                    $"  {group.Key,-36} " +
                    $"{group.Count()}/{completed.Count} folds"
                );
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "V6 INTERPRETATION"
        );

        if (completed.Count < 3)
        {
            Console.WriteLine(
                "INSUFFICIENT FORWARD EVIDENCE: too few " +
                "qualified validation folds."
            );
        }
        else
        {
            var aggregate =
                CalculateAggregateStats(
                    allValidationTrades
                );

            var positiveFoldRate =
                positive /
                (double)completed.Count;

            if (positiveFoldRate >= 0.70 &&
                aggregate.ProfitFactor >= 1.30 &&
                aggregate.AverageR > 0.10)
            {
                Console.WriteLine(
                    "PROMISING WALK-FORWARD STABILITY: " +
                    "candidate family deserves paper validation."
                );
            }
            else if (positiveFoldRate >= 0.50 &&
                     aggregate.ProfitFactor > 1.00 &&
                     aggregate.AverageR > 0)
            {
                Console.WriteLine(
                    "MIXED / WEAK EDGE: positive evidence exists, " +
                    "but robustness is not strong enough yet."
                );
            }
            else
            {
                Console.WriteLine(
                    "NO ROBUST WALK-FORWARD EDGE: do not promote " +
                    "the strategy based on current evidence."
                );
            }
        }
    }

    private static void PrintAggregateValidationStats(
        IReadOnlyCollection<PortfolioTrade> trades,
        IReadOnlyCollection<WalkForwardFoldResult> completedFolds)
    {
        Console.WriteLine(
            "AGGREGATED FORWARD VALIDATION"
        );

        var stats =
            CalculateAggregateStats(
                trades
            );

        Console.WriteLine(
            $"Validation Trades: {stats.Trades}"
        );

        Console.WriteLine(
            $"Win Rate: {stats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(stats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"Average R: {stats.AverageR:F2}"
        );

        var summedPnl =
            completedFolds.Sum(
                fold =>
                    fold.ValidationResult!
                        .NetPnl
            );

        var averageFoldReturn =
            completedFolds.Count > 0
                ? completedFolds.Average(
                    fold =>
                        fold.ValidationResult!
                            .ReturnPercent
                  )
                : 0;

        var worstFoldDrawdown =
            completedFolds.Count > 0
                ? completedFolds.Max(
                    fold =>
                        fold.ValidationResult!
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

        Console.WriteLine(
            "Note: fold P&L is fixed-capital and is not " +
            "a compounded portfolio return."
        );
    }

    private static WalkForwardAggregateStats
        CalculateAggregateStats(
            IReadOnlyCollection<PortfolioTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new WalkForwardAggregateStats(
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

        return new WalkForwardAggregateStats(
            trades.Count,
            winRate,
            profitFactor,
            averageR
        );
    }

    private static bool IsPositiveValidation(
        StrategyExperimentResult result)
    {
        return
            result.Trades > 0 &&
            result.ProfitFactor > 1.0 &&
            result.AverageR > 0 &&
            result.NetPnl > 0;
    }

    private static void PrintCompactResult(
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
            $"  Max Drawdown: " +
            $"{result.MaxDrawdownPercent:F2}%"
        );
    }

    private static string FormatNumber(
        double value)
    {
        return double.IsPositiveInfinity(
            value
        )
            ? "∞"
            : value.ToString(
                "F2"
            );
    }
}

public sealed record WalkForwardFoldDefinition(
    int Index,
    DateTime TrainingStart,
    DateTime TrainingEnd,
    DateTime ValidationStart,
    DateTime ValidationEnd
);

public sealed record WalkForwardFoldResult(
    WalkForwardFoldDefinition Fold,
    int MinimumTrainingTrades,
    IReadOnlyList<StrategyExperimentResult> TrainingResults,
    StrategyExperimentResult? SelectedTrainingResult,
    StrategyExperimentResult? ValidationResult
);

public sealed record WalkForwardStudyResult(
    IReadOnlyList<WalkForwardFoldResult> Folds
);

public sealed record WalkForwardAggregateStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR
);
