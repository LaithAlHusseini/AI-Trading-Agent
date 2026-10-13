using Alpaca.Markets;

public sealed partial class PortfolioBacktestEngine
{
    public async Task<MarketRegimeStudyResult>
        RunMarketRegimeStudyAsync(
            IEnumerable<string> symbols)
    {
        var tradingSymbols =
            symbols
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        var endDate =
            DateTime.UtcNow.Date;

        var startDate =
            endDate.AddYears(
                -BacktestYears
            );

        var history =
            new Dictionary<string, List<IBar>>(
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
                startDate,
                endDate
            );

        var regimeSnapshots =
            BuildMarketRegimeSnapshots(
                benchmarkBars
            );

        var configurations =
            new[]
            {
                StrategyExperimentConfig.Baseline,

                new StrategyExperimentConfig(
                    "ENTRY_GAP_0_TO_1",
                    MinEntryGapPercent: 0.00,
                    MaxEntryGapPercent: 1.00
                ),

                new StrategyExperimentConfig(
                    "STOP_1_5_TO_2_ATR",
                    MinStopDistanceAtr: 1.50,
                    MaxStopDistanceAtr: 2.00
                )
            };

        var candidateResults =
            new List<MarketRegimeCandidateResult>();

        foreach (var configuration in configurations)
        {
            var simulation =
                SimulatePortfolio(
                    $"V7 REGIME STUDY - {configuration.Name}",
                    tradingSymbols,
                    history,
                    startDate,
                    endDate,
                    configuration
                );

            var taggedTrades =
                TagTradesWithMarketRegime(
                    simulation.Trades,
                    regimeSnapshots
                );

            candidateResults.Add(
                new MarketRegimeCandidateResult(
                    configuration,
                    simulation,
                    taggedTrades
                )
            );
        }

        return new MarketRegimeStudyResult(
            "SPY",
            regimeSnapshots,
            candidateResults
        );
    }

    private async Task<List<IBar>>
        LoadMarketRegimeBenchmarkAsync(
            string symbol,
            DateTime studyStart,
            DateTime studyEnd)
    {
        var warmupStart =
            studyStart
                .AddYears(-1);

        var endDate =
            studyEnd
                .AddDays(1);

        var bars =
            new List<IBar>();

        var chunkStart =
            warmupStart;

        while (chunkStart < endDate)
        {
            var chunkEnd =
                chunkStart
                    .AddYears(1);

            if (chunkEnd > endDate)
            {
                chunkEnd =
                    endDate;
            }

            var request =
                new HistoricalBarsRequest(
                    symbol,
                    chunkStart,
                    chunkEnd,
                    BarTimeFrame.Day
                )
                {
                    Feed =
                        MarketDataFeed.Iex
                };

            var page =
                await _dataClient
                    .ListHistoricalBarsAsync(
                        request
                    );

            bars.AddRange(
                page.Items
            );

            chunkStart =
                chunkEnd;
        }

        return bars
            .GroupBy(
                bar =>
                    bar.TimeUtc
            )
            .Select(
                group =>
                    group.First()
            )
            .OrderBy(
                bar =>
                    bar.TimeUtc
            )
            .ToList();
    }

    private static IReadOnlyList<MarketRegimeSnapshot>
        BuildMarketRegimeSnapshots(
            IReadOnlyList<IBar> bars)
    {
        var snapshots =
            new List<MarketRegimeSnapshot>();

        if (bars.Count < 200)
        {
            return snapshots;
        }

        var closes =
            bars
                .Select(
                    bar =>
                        (double)bar.Close
                )
                .ToArray();

        for (var index = 199;
             index < bars.Count;
             index++)
        {
            var close =
                closes[index];

            var sma50 =
                AverageWindow(
                    closes,
                    index,
                    50
                );

            var sma200 =
                AverageWindow(
                    closes,
                    index,
                    200
                );

            var realizedVolatility =
                CalculateAnnualizedVolatility(
                    closes,
                    index,
                    20
                );

            var trendRegime =
                ClassifyTrendRegime(
                    close,
                    sma50,
                    sma200
                );

            var volatilityRegime =
                ClassifyVolatilityRegime(
                    realizedVolatility
                );

            snapshots.Add(
                new MarketRegimeSnapshot(
                    bars[index]
                        .TimeUtc
                        .Date,
                    close,
                    sma50,
                    sma200,
                    realizedVolatility,
                    trendRegime,
                    volatilityRegime
                )
            );
        }

        return snapshots;
    }

    private static IReadOnlyList<RegimeTaggedTrade>
        TagTradesWithMarketRegime(
            IReadOnlyCollection<PortfolioTrade> trades,
            IReadOnlyList<MarketRegimeSnapshot> snapshots)
    {
        var orderedSnapshots =
            snapshots
                .OrderBy(
                    snapshot =>
                        snapshot.Date
                )
                .ToList();

        var results =
            new List<RegimeTaggedTrade>();

        foreach (var trade in trades)
        {
            var regime =
                orderedSnapshots
                    .LastOrDefault(
                        snapshot =>
                            snapshot.Date <
                            trade.EntryDate.Date
                    );

            if (regime is null)
            {
                continue;
            }

            results.Add(
                new RegimeTaggedTrade(
                    trade,
                    regime
                )
            );
        }

        return results;
    }

    private static double AverageWindow(
        IReadOnlyList<double> values,
        int endIndex,
        int length)
    {
        var start =
            endIndex -
            length +
            1;

        var sum =
            0.0;

        for (var index = start;
             index <= endIndex;
             index++)
        {
            sum +=
                values[index];
        }

        return
            sum /
            length;
    }

    private static double CalculateAnnualizedVolatility(
        IReadOnlyList<double> closes,
        int endIndex,
        int lookbackReturns)
    {
        var startIndex =
            endIndex -
            lookbackReturns;

        if (startIndex < 0)
        {
            return 0;
        }

        var returns =
            new List<double>();

        for (var index = startIndex + 1;
             index <= endIndex;
             index++)
        {
            var previous =
                closes[index - 1];

            var current =
                closes[index];

            if (previous <= 0)
            {
                continue;
            }

            returns.Add(
                current /
                previous -
                1.0
            );
        }

        if (returns.Count < 2)
        {
            return 0;
        }

        var mean =
            returns.Average();

        var variance =
            returns.Sum(
                value =>
                    Math.Pow(
                        value - mean,
                        2
                    )
            ) /
            (returns.Count - 1);

        return
            Math.Sqrt(
                variance
            ) *
            Math.Sqrt(252) *
            100.0;
    }

    private static string ClassifyTrendRegime(
        double close,
        double sma50,
        double sma200)
    {
        if (close > sma200 &&
            sma50 > sma200)
        {
            return "BULL_TREND";
        }

        if (close < sma200 &&
            sma50 < sma200)
        {
            return "BEAR_TREND";
        }

        return "SIDEWAYS_TRANSITION";
    }

    private static string ClassifyVolatilityRegime(
        double annualizedVolatility)
    {
        if (annualizedVolatility <= 15.0)
        {
            return "LOW_VOL";
        }

        if (annualizedVolatility <= 25.0)
        {
            return "NORMAL_VOL";
        }

        return "HIGH_VOL";
    }
}

public sealed class MarketRegimeValidationEngine
{
    private readonly PortfolioBacktestEngine
        _portfolioBacktestEngine;

    public MarketRegimeValidationEngine(
        PortfolioBacktestEngine portfolioBacktestEngine)
    {
        _portfolioBacktestEngine =
            portfolioBacktestEngine;
    }

    public async Task<MarketRegimeStudyResult> RunAsync(
        IEnumerable<string> symbols)
    {
        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine(" MARKET REGIME VALIDATION ENGINE V7");
        Console.WriteLine("==============================================");
        Console.WriteLine("Benchmark: SPY");
        Console.WriteLine("Trend regime:");
        Console.WriteLine("  BULL_TREND          = SPY close > SMA200 and SMA50 > SMA200");
        Console.WriteLine("  BEAR_TREND          = SPY close < SMA200 and SMA50 < SMA200");
        Console.WriteLine("  SIDEWAYS_TRANSITION = all other states");
        Console.WriteLine("Volatility regime:");
        Console.WriteLine("  LOW_VOL    <= 15%");
        Console.WriteLine("  NORMAL_VOL > 15% and <= 25%");
        Console.WriteLine("  HIGH_VOL   > 25%");
        Console.WriteLine();
        Console.WriteLine("Regime is measured using the latest SPY daily bar");
        Console.WriteLine("strictly BEFORE each trade entry date.");
        Console.WriteLine("No same-day benchmark information is used.");
        Console.WriteLine();

        var study =
            await _portfolioBacktestEngine
                .RunMarketRegimeStudyAsync(
                    symbols
                );

        foreach (var candidate in study.Candidates)
        {
            PrintCandidate(
                candidate
            );
        }

        PrintCrossCandidateSummary(
            study
        );

        Console.WriteLine();
        Console.WriteLine("V7 research note:");
        Console.WriteLine("- This is regime attribution and robustness research.");
        Console.WriteLine("- Do not promote a regime filter merely because one bucket");
        Console.WriteLine("  performed well historically.");
        Console.WriteLine("- Small regime samples must be treated as inconclusive.");
        Console.WriteLine("- Paper trading remains the forward-validation gate.");
        Console.WriteLine();
        Console.WriteLine("NO ORDERS WERE SUBMITTED.");

        return study;
    }

    private static void PrintCandidate(
        MarketRegimeCandidateResult candidate)
    {
        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine($" {candidate.Configuration.Name}");
        Console.WriteLine("==============================================");
        Console.WriteLine(
            $"Trades with regime classification: " +
            $"{candidate.TaggedTrades.Count}"
        );
        Console.WriteLine();
        Console.WriteLine("BY TREND REGIME");

        foreach (
            var group in candidate.TaggedTrades
                .GroupBy(
                    item =>
                        item.Regime.TrendRegime
                )
                .OrderBy(
                    group =>
                        TrendOrder(
                            group.Key
                        )
                ))
        {
            PrintGroup(
                group.Key,
                group.Select(
                    item =>
                        item.Trade
                )
                .ToList()
            );
        }

        Console.WriteLine();
        Console.WriteLine("BY VOLATILITY REGIME");

        foreach (
            var group in candidate.TaggedTrades
                .GroupBy(
                    item =>
                        item.Regime.VolatilityRegime
                )
                .OrderBy(
                    group =>
                        VolatilityOrder(
                            group.Key
                        )
                ))
        {
            PrintGroup(
                group.Key,
                group.Select(
                    item =>
                        item.Trade
                )
                .ToList()
            );
        }

        Console.WriteLine();
        Console.WriteLine("BY COMBINED REGIME");

        foreach (
            var group in candidate.TaggedTrades
                .GroupBy(
                    item =>
                        $"{item.Regime.TrendRegime} | " +
                        $"{item.Regime.VolatilityRegime}"
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                ))
        {
            PrintGroup(
                group.Key,
                group.Select(
                    item =>
                        item.Trade
                )
                .ToList()
            );
        }
    }

    private static void PrintCrossCandidateSummary(
        MarketRegimeStudyResult study)
    {
        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine(" CROSS-CANDIDATE REGIME SUMMARY");
        Console.WriteLine("==============================================");

        var allTrendRegimes =
            study.Candidates
                .SelectMany(
                    candidate =>
                        candidate.TaggedTrades
                )
                .Select(
                    item =>
                        item.Regime.TrendRegime
                )
                .Distinct()
                .OrderBy(
                    TrendOrder
                )
                .ToList();

        foreach (var trendRegime in allTrendRegimes)
        {
            Console.WriteLine();
            Console.WriteLine(
                trendRegime
            );

            foreach (var candidate in study.Candidates)
            {
                var trades =
                    candidate.TaggedTrades
                        .Where(
                            item =>
                                item.Regime.TrendRegime ==
                                trendRegime
                        )
                        .Select(
                            item =>
                                item.Trade
                        )
                        .ToList();

                var stats =
                    CalculateStats(
                        trades
                    );

                Console.WriteLine(
                    $"  {candidate.Configuration.Name,-24} | " +
                    $"Trades {stats.Trades,3} | " +
                    $"Win {stats.WinRate,6:F2}% | " +
                    $"PF {FormatNumber(stats.ProfitFactor),6} | " +
                    $"Avg R {stats.AverageR,6:F2} | " +
                    $"P&L ${stats.NetPnl,7:F2}"
                );
            }
        }

        Console.WriteLine();
        Console.WriteLine("REGIME STABILITY FLAGS");

        foreach (var candidate in study.Candidates)
        {
            var regimeGroups =
                candidate.TaggedTrades
                    .GroupBy(
                        item =>
                            item.Regime.TrendRegime
                    )
                    .Select(
                        group =>
                            CalculateStats(
                                group.Select(
                                    item =>
                                        item.Trade
                                )
                                .ToList()
                            )
                    )
                    .Where(
                        stats =>
                            stats.Trades >= 10
                    )
                    .ToList();

            var positiveGroups =
                regimeGroups.Count(
                    stats =>
                        stats.ProfitFactor > 1.0 &&
                        stats.AverageR > 0 &&
                        stats.NetPnl > 0
                );

            var negativeGroups =
                regimeGroups.Count -
                positiveGroups;

            Console.WriteLine(
                $"  {candidate.Configuration.Name,-24} | " +
                $"Qualified regimes {regimeGroups.Count} | " +
                $"Positive {positiveGroups} | " +
                $"Negative {negativeGroups}"
            );
        }
    }

    private static void PrintGroup(
        string label,
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var stats =
            CalculateStats(
                trades
            );

        Console.WriteLine(
            $"  {label,-34} | " +
            $"Trades {stats.Trades,3} | " +
            $"Win {stats.WinRate,6:F2}% | " +
            $"PF {FormatNumber(stats.ProfitFactor),6} | " +
            $"Avg R {stats.AverageR,6:F2} | " +
            $"P&L ${stats.NetPnl,7:F2}"
        );
    }

    private static MarketRegimePerformanceStats
        CalculateStats(
            IReadOnlyCollection<PortfolioTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new MarketRegimePerformanceStats(
                0,
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

        var netPnl =
            trades.Sum(
                trade =>
                    trade.Pnl
            );

        return new MarketRegimePerformanceStats(
            trades.Count,
            winRate,
            profitFactor,
            averageR,
            netPnl
        );
    }

    private static int TrendOrder(
        string regime)
    {
        return regime switch
        {
            "BULL_TREND" => 1,
            "SIDEWAYS_TRANSITION" => 2,
            "BEAR_TREND" => 3,
            _ => 4
        };
    }

    private static int VolatilityOrder(
        string regime)
    {
        return regime switch
        {
            "LOW_VOL" => 1,
            "NORMAL_VOL" => 2,
            "HIGH_VOL" => 3,
            _ => 4
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

public sealed record MarketRegimeSnapshot(
    DateTime Date,
    double Close,
    double Sma50,
    double Sma200,
    double AnnualizedVolatility,
    string TrendRegime,
    string VolatilityRegime
);

public sealed record RegimeTaggedTrade(
    PortfolioTrade Trade,
    MarketRegimeSnapshot Regime
);

public sealed record MarketRegimeCandidateResult(
    StrategyExperimentConfig Configuration,
    PortfolioSimulationResult Simulation,
    IReadOnlyList<RegimeTaggedTrade> TaggedTrades
);

public sealed record MarketRegimeStudyResult(
    string BenchmarkSymbol,
    IReadOnlyList<MarketRegimeSnapshot> BenchmarkRegimes,
    IReadOnlyList<MarketRegimeCandidateResult> Candidates
);

public sealed record MarketRegimePerformanceStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR,
    double NetPnl
);
