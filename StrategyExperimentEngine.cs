public sealed class StrategyExperimentEngine
{
    private const int AbsoluteMinimumTrades = 30;
    private const double MinimumBaselineTradeRetention = 0.30;

    private readonly PortfolioBacktestEngine _portfolioBacktestEngine;

    public StrategyExperimentEngine(
        PortfolioBacktestEngine portfolioBacktestEngine)
    {
        _portfolioBacktestEngine = portfolioBacktestEngine;
    }

    public async Task<StrategyExperimentStudyResult> RunAsync(
        IEnumerable<string> symbols)
    {
        Console.WriteLine();
        Console.WriteLine(
            "=============================================="
        );
        Console.WriteLine(
            " STRATEGY RESEARCH ENGINE V5"
        );
        Console.WriteLine(
            "=============================================="
        );
        Console.WriteLine(
            "Selection period: DEVELOPMENT DATA ONLY"
        );
        Console.WriteLine(
            "Out-of-sample data is not used for ranking."
        );
        Console.WriteLine(
            "No orders are submitted."
        );
        Console.WriteLine();

        var configurations =
            BuildConfigurations();

        var results =
            await _portfolioBacktestEngine
                .RunDevelopmentExperimentsAsync(
                    symbols,
                    configurations
                );

        var baseline =
            results.FirstOrDefault(
                result =>
                    result.Configuration.Name ==
                    "BASELINE"
            );

        if (baseline is null)
        {
            Console.WriteLine(
                "No baseline result was produced."
            );

            return new StrategyExperimentStudyResult(
                null,
                results,
                AbsoluteMinimumTrades,
                null
            );
        }

        var minimumTradesForRanking =
            Math.Max(
                AbsoluteMinimumTrades,
                (int)Math.Ceiling(
                    baseline.Trades *
                    MinimumBaselineTradeRetention
                )
            );

        Console.WriteLine(
            "DEVELOPMENT EXPERIMENT RESULTS"
        );
        Console.WriteLine(
            "----------------------------------------------"
        );

        foreach (
            var result in results
                .OrderByDescending(
                    item =>
                        item.ProfitFactor
                )
                .ThenByDescending(
                    item =>
                        item.AverageR
                ))
        {
            PrintResult(
                result,
                minimumTradesForRanking
            );
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Minimum trades for ranking: " +
            $"{minimumTradesForRanking}"
        );

        var eligible =
            results
                .Where(
                    result =>
                        result.Configuration.Name !=
                        "BASELINE"
                )
                .Where(
                    result =>
                        result.Trades >=
                        minimumTradesForRanking
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
                .ToList();

        var provisionalCandidate =
            eligible.FirstOrDefault();

        Console.WriteLine();
        Console.WriteLine(
            "RESEARCH DECISION"
        );
        Console.WriteLine(
            "----------------------------------------------"
        );

        if (provisionalCandidate is null)
        {
            Console.WriteLine(
                "No configuration qualifies as a provisional candidate."
            );
            Console.WriteLine(
                "Do not loosen the qualification rules just to force a winner."
            );
        }
        else
        {
            Console.WriteLine(
                $"Provisional candidate: " +
                $"{provisionalCandidate.Configuration.Name}"
            );
            Console.WriteLine(
                $"Trades: {provisionalCandidate.Trades}"
            );
            Console.WriteLine(
                $"Profit Factor: " +
                $"{FormatNumber(provisionalCandidate.ProfitFactor)}"
            );
            Console.WriteLine(
                $"Average R: {provisionalCandidate.AverageR:F2}"
            );
            Console.WriteLine(
                $"Net P&L: ${provisionalCandidate.NetPnl:F2}"
            );
            Console.WriteLine(
                $"Max Drawdown: " +
                $"{provisionalCandidate.MaxDrawdownPercent:F2}%"
            );
            Console.WriteLine();
            Console.WriteLine(
                "This is a research candidate only, not an approved strategy."
            );
        }

        Console.WriteLine();
        Console.WriteLine(
            "Important research note:"
        );
        Console.WriteLine(
            "The existing out-of-sample period has already been inspected"
        );
        Console.WriteLine(
            "during earlier diagnostics, so it is not a pristine final holdout."
        );
        Console.WriteLine(
            "Future walk-forward validation and paper trading will provide"
        );
        Console.WriteLine(
            "the stronger forward validation before any live-trading decision."
        );

        return new StrategyExperimentStudyResult(
            baseline,
            results,
            minimumTradesForRanking,
            provisionalCandidate
        );
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

    private static void PrintResult(
        StrategyExperimentResult result,
        int minimumTradesForRanking)
    {
        var eligible =
            result.Trades >=
            minimumTradesForRanking;

        Console.WriteLine(
            $"{result.Configuration.Name}"
        );
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
        Console.WriteLine(
            $"  Sample Eligible: {(eligible ? "YES" : "NO")}"
        );
        Console.WriteLine(
            "----------------------------------------------"
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

public sealed record StrategyExperimentStudyResult(
    StrategyExperimentResult? Baseline,
    IReadOnlyList<StrategyExperimentResult> Results,
    int MinimumTradesForRanking,
    StrategyExperimentResult? ProvisionalCandidate
);
