using Alpaca.Markets;

public sealed class ForwardSignalEngine
{
    private const string BenchmarkSymbol =
        "SPY";

    private const int RequiredAnalysisBars =
        50;

    private const int RequiredBenchmarkBars =
        200;

    private const int HistoryCalendarDays =
        450;

    private readonly IAlpacaDataClient
        _dataClient;

    public ForwardSignalEngine(
        IAlpacaDataClient dataClient)
    {
        _dataClient =
            dataClient;
    }

    public async Task<ForwardSignalStudyResult> RunAsync(
        IEnumerable<string> symbols,
        PaperTradingPreflightResult preflight,
        FrozenForwardPolicy policy)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " FORWARD SIGNAL ENGINE V9D"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Policy Version: {policy.PolicyVersion}"
        );

        Console.WriteLine(
            $"Strategy: {policy.StrategyName}"
        );

        Console.WriteLine(
            $"Regime Gate: {policy.RegimeGate}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Signal model:"
        );

        Console.WriteLine(
            "- Analyze the latest completed daily bar."
        );

        Console.WriteLine(
            "- Freeze the signal before the next market session."
        );

        Console.WriteLine(
            "- Entry-price-dependent checks are deferred until execution."
        );

        Console.WriteLine(
            "- No orders are submitted."
        );

        var configuration =
            new StrategyExperimentConfig(
                policy.StrategyName,
                MinRiskReward:
                    policy.MinimumRiskRewardRatio,
                MinStopDistanceAtr:
                    policy.MinimumStopDistanceAtr,
                MaxStopDistanceAtr:
                    policy.MaximumStopDistanceAtr
            );

        var tradingSymbols =
            symbols
                .Where(
                    symbol =>
                        !string.IsNullOrWhiteSpace(
                            symbol
                        )
                )
                .Select(
                    symbol =>
                        symbol.Trim()
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        if (preflight.MarketOpen)
        {
            Console.WriteLine();

            Console.WriteLine(
                "FORWARD SIGNAL STATUS: BLOCKED"
            );

            Console.WriteLine(
                "Reason: market is currently open. " +
                "Forward signals must be frozen before the execution session."
            );

            Console.WriteLine(
                "No pending signals were generated."
            );

            return new ForwardSignalStudyResult(
                policy.PolicyVersion,
                null,
                Array.Empty<ForwardPendingSignal>(),
                tradingSymbols.Length,
                0,
                tradingSymbols.Length,
                false,
                "Market is open; forward-signal generation is blocked."
            );
        }        

        var benchmarkBars =
            await LoadRecentDailyBarsAsync(
                BenchmarkSymbol,
                preflight.BrokerTimestampUtc
            );

        var completedBenchmarkBars =
            SelectCompletedBars(
                benchmarkBars,
                preflight
            );

        if (completedBenchmarkBars.Count <
            RequiredBenchmarkBars)
        {
            Console.WriteLine();

            Console.WriteLine(
                "FORWARD SIGNAL STATUS: BLOCKED"
            );

            Console.WriteLine(
                $"Reason: benchmark history contains only " +
                $"{completedBenchmarkBars.Count} completed bars; " +
                $"{RequiredBenchmarkBars} are required."
            );

            return new ForwardSignalStudyResult(
                policy.PolicyVersion,
                null,
                Array.Empty<ForwardPendingSignal>(),
                tradingSymbols.Length,
                0,
                tradingSymbols.Length,
                false,
                "Insufficient completed benchmark history."
            );
        }

        var currentRegime =
            BuildLatestRegime(
                completedBenchmarkBars
            );

        if (currentRegime is null)
        {
            Console.WriteLine();

            Console.WriteLine(
                "FORWARD SIGNAL STATUS: BLOCKED"
            );

            Console.WriteLine(
                "Reason: current benchmark regime could not be calculated."
            );

            return new ForwardSignalStudyResult(
                policy.PolicyVersion,
                null,
                Array.Empty<ForwardPendingSignal>(),
                tradingSymbols.Length,
                0,
                tradingSymbols.Length,
                false,
                "Benchmark regime unavailable."
            );
        }

        var signalDate =
            currentRegime.Date.Date;

        var plannedExecutionOpenUtc =
            preflight.NextOpenUtc;

        var benchmarkAgeDays =
            Math.Max(
                0,
                (
                    preflight.BrokerTimestampUtc.Date -
                    signalDate
                ).Days
            );

        Console.WriteLine();

        Console.WriteLine(
            "BENCHMARK / REGIME"
        );

        Console.WriteLine(
            $"Benchmark: {BenchmarkSymbol}"
        );

        Console.WriteLine(
            $"Completed Bar Date: " +
            $"{signalDate:yyyy-MM-dd}"
        );

        Console.WriteLine(
            $"Benchmark Age: " +
            $"{benchmarkAgeDays} calendar day(s)"
        );

        Console.WriteLine(
            $"Trend Regime: " +
            $"{currentRegime.TrendRegime}"
        );

        Console.WriteLine(
            $"Volatility Regime: " +
            $"{currentRegime.VolatilityRegime}"
        );

        Console.WriteLine(
            $"SPY Close: " +
            $"${currentRegime.Close:F2}"
        );

        Console.WriteLine(
            $"Next Planned Execution Open UTC: " +
            $"{plannedExecutionOpenUtc:yyyy-MM-dd HH:mm:ss}"
        );

        var regimeAllowed =
            policy.AllowsRegime(
                currentRegime.TrendRegime,
                currentRegime.VolatilityRegime
            );

        Console.WriteLine(
            $"Frozen Regime Gate Passed: " +
            $"{regimeAllowed}"
        );

        var pendingSignals =
            new List<ForwardPendingSignal>();

        var rejectedCount =
            0;

        foreach (var symbol in tradingSymbols)
        {
            Console.WriteLine();

            Console.WriteLine(
                "----------------------------------------------"
            );

            Console.WriteLine(
                $"FORWARD SIGNAL: {symbol}"
            );

            if (!regimeAllowed)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    $"Reason: frozen regime gate " +
                    $"{policy.RegimeGate} does not allow " +
                    $"{currentRegime.TrendRegime} | " +
                    $"{currentRegime.VolatilityRegime}."
                );

                rejectedCount++;

                continue;
            }

            var bars =
                await LoadRecentDailyBarsAsync(
                    symbol,
                    preflight.BrokerTimestampUtc
                );

            var completedBars =
                SelectCompletedBars(
                    bars,
                    preflight
                );

            if (completedBars.Count <
                RequiredAnalysisBars)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    $"Reason: only {completedBars.Count} " +
                    $"completed daily bars are available."
                );

                rejectedCount++;

                continue;
            }

            var signalBarIndex =
                FindBarIndexForDate(
                    completedBars,
                    signalDate
                );

            if (signalBarIndex < 0)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    $"Reason: no completed {symbol} bar exists " +
                    $"for benchmark signal date " +
                    $"{signalDate:yyyy-MM-dd}."
                );

                rejectedCount++;

                continue;
            }

            if (signalBarIndex <
                RequiredAnalysisBars)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    "Reason: not enough pre-signal history " +
                    "for analysis."
                );

                rejectedCount++;

                continue;
            }

            var analysis =
                AnalyzeAt(
                    symbol,
                    completedBars,
                    signalBarIndex
                );

            if (analysis is null)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    "Reason: signal analysis could not be calculated."
                );

                rejectedCount++;

                continue;
            }

            PrintAnalysis(
                analysis
            );

            var preliminaryDecision =
                EvaluateSignalOnlyRules(
                    analysis,
                    configuration
                );

            if (!preliminaryDecision.Accepted)
            {
                Console.WriteLine(
                    "Status: REJECTED"
                );

                Console.WriteLine(
                    $"Reason: {preliminaryDecision.Reason}"
                );

                rejectedCount++;

                continue;
            }

            var pendingSignal =
                new ForwardPendingSignal(
                    Symbol:
                        symbol,

                    PolicyVersion:
                        policy.PolicyVersion,

                    StrategyName:
                        policy.StrategyName,

                    SignalDate:
                        signalDate,

                    PlannedExecutionOpenUtc:
                        plannedExecutionOpenUtc,

                    SignalClose:
                        analysis.Price,

                    Sma20:
                        analysis.Sma20,

                    Sma50:
                        analysis.Sma50,

                    Trend:
                        analysis.Trend,

                    Momentum:
                        analysis.Momentum,

                    Volatility:
                        analysis.Volatility,

                    VolumeRatio:
                        analysis.VolumeRatio,

                    Drawdown:
                        analysis.Drawdown,

                    Atr:
                        analysis.Atr,

                    Support10:
                        analysis.Support10,

                    Resistance20:
                        analysis.Resistance20,

                    OpportunityScore:
                        analysis.OpportunityScore,

                    RiskScore:
                        analysis.RiskScore,

                    NetScore:
                        analysis.OpportunityScore -
                        analysis.RiskScore,

                    RegimeDate:
                        currentRegime.Date,

                    TrendRegime:
                        currentRegime.TrendRegime,

                    VolatilityRegime:
                        currentRegime.VolatilityRegime,

                    Status:
                        "PENDING_NEXT_SESSION_EXECUTION",

                    Reason:
                        "Signal-day rules and frozen regime gate passed. " +
                        "Entry-price-dependent stop, target, risk/reward, " +
                        "position sizing, and stop-distance checks are " +
                        "deferred until the next session execution price."
                );

            pendingSignals.Add(
                pendingSignal
            );

            Console.WriteLine(
                "Status: PENDING_NEXT_SESSION_EXECUTION"
            );

            Console.WriteLine(
                $"Planned Entry Session: " +
                $"{plannedExecutionOpenUtc:yyyy-MM-dd HH:mm:ss} UTC"
            );

            Console.WriteLine(
                "Reason: signal-day rules passed; execution-price " +
                "rules are intentionally deferred."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " V9D FORWARD SIGNAL SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Signal Date: {signalDate:yyyy-MM-dd}"
        );

        Console.WriteLine(
            $"Symbols Evaluated: {tradingSymbols.Length}"
        );

        Console.WriteLine(
            $"Pending Signals: {pendingSignals.Count}"
        );

        Console.WriteLine(
            $"Rejected: {rejectedCount}"
        );

        Console.WriteLine(
            $"Regime Gate Passed: {regimeAllowed}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "ENTRY-PRICE-DEPENDENT RULES NOT EVALUATED YET:"
        );

        Console.WriteLine(
            "- next-session execution price"
        );

        Console.WriteLine(
            "- simulated buy slippage"
        );

        Console.WriteLine(
            "- stop-loss geometry"
        );

        Console.WriteLine(
            "- 1.50-2.00 ATR stop-distance filter"
        );

        Console.WriteLine(
            "- take-profit geometry"
        );

        Console.WriteLine(
            "- minimum 1:1.80 risk/reward"
        );

        Console.WriteLine(
            "- final quantity and capital allocation"
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return new ForwardSignalStudyResult(
            policy.PolicyVersion,
            currentRegime,
            pendingSignals,
            tradingSymbols.Length,
            pendingSignals.Count,
            rejectedCount,
            regimeAllowed,
            pendingSignals.Count > 0
                ? "Pending signals generated."
                : "No pending signals qualified."
        );
    }

    private async Task<List<IBar>>
        LoadRecentDailyBarsAsync(
            string symbol,
            DateTime brokerTimestampUtc)
    {
        var startDate =
            brokerTimestampUtc
                .AddDays(
                    -HistoryCalendarDays
                );

        var request =
            new HistoricalBarsRequest(
                symbol,
                startDate,
                brokerTimestampUtc,
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

        return page.Items
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

    private static List<IBar> SelectCompletedBars(
        IReadOnlyCollection<IBar> bars,
        PaperTradingPreflightResult preflight)
    {
        var brokerDate =
            preflight.BrokerTimestampUtc.Date;

        return bars
            .Where(
                bar =>
                    preflight.MarketOpen
                        ? bar.TimeUtc.Date <
                          brokerDate

                        : bar.TimeUtc.Date <=
                          brokerDate
            )
            .OrderBy(
                bar =>
                    bar.TimeUtc
            )
            .ToList();
    }

    private static int FindBarIndexForDate(
        IReadOnlyList<IBar> bars,
        DateTime date)
    {
        for (
            var index = bars.Count - 1;
            index >= 0;
            index--)
        {
            if (bars[index]
                    .TimeUtc
                    .Date ==
                date.Date)
            {
                return index;
            }
        }

        return -1;
    }

    private static PortfolioAnalysis? AnalyzeAt(
        string symbol,
        IReadOnlyList<IBar> bars,
        int index)
    {
        if (index < 50)
        {
            return null;
        }

        var closes =
            bars
                .Take(
                    index + 1
                )
                .Select(
                    bar =>
                        (double)bar.Close
                )
                .ToList();

        var volumes =
            bars
                .Take(
                    index + 1
                )
                .Select(
                    bar =>
                        (double)bar.Volume
                )
                .ToList();

        var price =
            closes[^1];

        var sma20 =
            closes
                .TakeLast(20)
                .Average();

        var sma50 =
            closes
                .TakeLast(50)
                .Average();

        var momentum =
            CalculateMomentum(
                closes,
                20
            );

        var volatility =
            CalculateAnnualizedVolatility(
                closes,
                20
            );

        var averageVolume5 =
            volumes
                .TakeLast(5)
                .Average();

        var averageVolume20 =
            volumes
                .TakeLast(20)
                .Average();

        var volumeRatio =
            averageVolume20 > 0
                ? averageVolume5 /
                  averageVolume20
                : 0;

        var highestClose20 =
            closes
                .TakeLast(20)
                .Max();

        var drawdown =
            highestClose20 > 0
                ? (
                    (
                        price -
                        highestClose20
                    ) /
                    highestClose20
                  ) *
                  100
                : 0;

        var atr =
            CalculateAtr(
                bars,
                index,
                14
            );

        var support10 =
            bars
                .Skip(
                    Math.Max(
                        0,
                        index - 9
                    )
                )
                .Take(10)
                .Min(
                    bar =>
                        (double)bar.Low
                );

        var resistance20 =
            bars
                .Skip(
                    Math.Max(
                        0,
                        index - 19
                    )
                )
                .Take(20)
                .Max(
                    bar =>
                        (double)bar.High
                );

        var trend =
            price > sma20 &&
            sma20 > sma50
                ? "Bullish"

                : price < sma20 &&
                  sma20 < sma50
                    ? "Bearish"

                    : "Neutral";

        var trendScore =
            trend switch
            {
                "Bullish" => 35,
                "Neutral" => 18,
                _ => 5
            };

        var momentumScore =
            momentum switch
            {
                >= 10 => 25,
                >= 5 => 22,
                >= 2 => 18,
                >= 0 => 12,
                >= -5 => 6,
                _ => 0
            };

        var volumeScore =
            volumeRatio switch
            {
                >= 1.50 => 20,
                >= 1.20 => 17,
                >= 1.00 => 14,
                >= 0.80 => 10,
                _ => 5
            };

        var volatilityScore =
            volatility switch
            {
                <= 20 => 20,
                <= 30 => 16,
                <= 40 => 12,
                <= 55 => 7,
                _ => 2
            };

        var opportunityScore =
            Math.Clamp(
                trendScore +
                momentumScore +
                volumeScore +
                volatilityScore,
                0,
                100
            );

        var volatilityRisk =
            volatility switch
            {
                <= 20 => 10,
                <= 30 => 20,
                <= 40 => 35,
                <= 55 => 55,
                _ => 75
            };

        var drawdownRisk =
            drawdown switch
            {
                >= -2 => 5,
                >= -5 => 15,
                >= -10 => 30,
                >= -20 => 50,
                _ => 70
            };

        var trendRisk =
            trend switch
            {
                "Bullish" => 5,
                "Neutral" => 20,
                _ => 35
            };

        var riskScore =
            Math.Clamp(
                (int)Math.Round(
                    volatilityRisk *
                    0.50 +

                    drawdownRisk *
                    0.30 +

                    trendRisk *
                    0.20
                ),
                0,
                100
            );

        var decision =
            opportunityScore >= 75 &&
            riskScore <= 35
                ? "CONSIDER"

                : opportunityScore >= 55 &&
                  riskScore <= 55
                    ? "WATCH"

                    : "REJECT";

        return new PortfolioAnalysis(
            symbol,
            bars[index]
                .TimeUtc,
            price,
            sma20,
            sma50,
            trend,
            momentum,
            volatility,
            volumeRatio,
            drawdown,
            atr,
            support10,
            resistance20,
            opportunityScore,
            riskScore,
            decision
        );
    }

    private static ForwardSignalDecision
        EvaluateSignalOnlyRules(
            PortfolioAnalysis analysis,
            StrategyExperimentConfig configuration)
    {
        if (analysis.OpportunityScore <
            configuration.MinOpportunityScore)
        {
            return Reject(
                $"Opportunity score " +
                $"{analysis.OpportunityScore} is below " +
                $"{configuration.MinOpportunityScore}."
            );
        }

        if (analysis.RiskScore >
            configuration.MaxRiskScore)
        {
            return Reject(
                $"Risk score {analysis.RiskScore} exceeds " +
                $"{configuration.MaxRiskScore}."
            );
        }

        if (analysis.Trend !=
            "Bullish")
        {
            return Reject(
                "Long strategy requires a bullish signal-day trend."
            );
        }

        if (analysis.Momentum <= 0)
        {
            return Reject(
                "Signal-day momentum is not positive."
            );
        }

        if (analysis.Atr <= 0)
        {
            return Reject(
                "ATR is unavailable or invalid."
            );
        }

        return new ForwardSignalDecision(
            true,
            "Signal-day rules passed."
        );
    }

    private static ForwardSignalDecision Reject(
        string reason)
    {
        return new ForwardSignalDecision(
            false,
            reason
        );
    }

    private static void PrintAnalysis(
        PortfolioAnalysis analysis)
    {
        Console.WriteLine(
            $"Signal Bar: " +
            $"{analysis.SignalDate:yyyy-MM-dd}"
        );

        Console.WriteLine(
            $"Signal Close: " +
            $"${analysis.Price:F2}"
        );

        Console.WriteLine(
            $"Trend: {analysis.Trend}"
        );

        Console.WriteLine(
            $"Momentum: {analysis.Momentum:F2}%"
        );

        Console.WriteLine(
            $"Volatility: {analysis.Volatility:F2}%"
        );

        Console.WriteLine(
            $"ATR(14): ${analysis.Atr:F2}"
        );

        Console.WriteLine(
            $"Opportunity: " +
            $"{analysis.OpportunityScore}/100"
        );

        Console.WriteLine(
            $"Risk: {analysis.RiskScore}/100"
        );

        Console.WriteLine(
            $"Net Score: " +
            $"{analysis.OpportunityScore - analysis.RiskScore}"
        );
    }

    private static MarketRegimeSnapshot?
        BuildLatestRegime(
            IReadOnlyList<IBar> bars)
    {
        if (bars.Count <
            RequiredBenchmarkBars)
        {
            return null;
        }

        var index =
            bars.Count - 1;

        var closes =
            bars
                .Select(
                    bar =>
                        (double)bar.Close
                )
                .ToArray();

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

        var annualizedVolatility =
            CalculateAnnualizedVolatility(
                closes,
                index,
                20
            );

        var trendRegime =
            close > sma200 &&
            sma50 > sma200
                ? "BULL_TREND"

                : close < sma200 &&
                  sma50 < sma200
                    ? "BEAR_TREND"

                    : "SIDEWAYS_TRANSITION";

        var volatilityRegime =
            annualizedVolatility <= 15.0
                ? "LOW_VOL"

                : annualizedVolatility <= 25.0
                    ? "NORMAL_VOL"

                    : "HIGH_VOL";

        return new MarketRegimeSnapshot(
            bars[index]
                .TimeUtc
                .Date,
            close,
            sma50,
            sma200,
            annualizedVolatility,
            trendRegime,
            volatilityRegime
        );
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

        for (
            var index = start;
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

    private static double CalculateMomentum(
        IReadOnlyList<double> closes,
        int periods)
    {
        if (closes.Count <=
            periods)
        {
            return 0;
        }

        var current =
            closes[^1];

        var previous =
            closes[
                ^(periods + 1)
            ];

        if (previous == 0)
        {
            return 0;
        }

        return
            (
                current -
                previous
            ) /
            previous *
            100.0;
    }

    private static double CalculateAnnualizedVolatility(
        IReadOnlyList<double> closes,
        int periods)
    {
        if (closes.Count <=
            periods)
        {
            return 0;
        }

        var recent =
            closes
                .TakeLast(
                    periods + 1
                )
                .ToList();

        var returns =
            new List<double>();

        for (
            var index = 1;
            index < recent.Count;
            index++)
        {
            var previous =
                recent[
                    index - 1
                ];

            var current =
                recent[index];

            if (previous == 0)
            {
                continue;
            }

            returns.Add(
                (
                    current -
                    previous
                ) /
                previous
            );
        }

        if (returns.Count <
            2)
        {
            return 0;
        }

        var average =
            returns.Average();

        var variance =
            returns.Sum(
                value =>
                    Math.Pow(
                        value -
                        average,
                        2
                    )
            ) /
            (
                returns.Count -
                1
            );

        return
            Math.Sqrt(
                variance
            ) *
            Math.Sqrt(
                252
            ) *
            100.0;
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

        for (
            var index =
                startIndex + 1;
            index <= endIndex;
            index++)
        {
            var previous =
                closes[
                    index - 1
                ];

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

        if (returns.Count <
            2)
        {
            return 0;
        }

        var mean =
            returns.Average();

        var variance =
            returns.Sum(
                value =>
                    Math.Pow(
                        value -
                        mean,
                        2
                    )
            ) /
            (
                returns.Count -
                1
            );

        return
            Math.Sqrt(
                variance
            ) *
            Math.Sqrt(
                252
            ) *
            100.0;
    }

    private static double CalculateAtr(
        IReadOnlyList<IBar> bars,
        int endIndex,
        int periods)
    {
        if (endIndex <
            periods)
        {
            return 0;
        }

        var trueRanges =
            new List<double>();

        var startIndex =
            Math.Max(
                1,
                endIndex -
                periods +
                1
            );

        for (
            var index = startIndex;
            index <= endIndex;
            index++)
        {
            var high =
                (double)
                    bars[index]
                        .High;

            var low =
                (double)
                    bars[index]
                        .Low;

            var previousClose =
                (double)
                    bars[
                        index - 1
                    ]
                    .Close;

            var trueRange =
                Math.Max(
                    high -
                    low,

                    Math.Max(
                        Math.Abs(
                            high -
                            previousClose
                        ),

                        Math.Abs(
                            low -
                            previousClose
                        )
                    )
                );

            trueRanges.Add(
                trueRange
            );
        }

        return
            trueRanges.Count > 0
                ? trueRanges.Average()
                : 0;
    }
}

public sealed record ForwardSignalDecision(
    bool Accepted,
    string Reason
);

public sealed record ForwardPendingSignal(
    string Symbol,
    string PolicyVersion,
    string StrategyName,
    DateTime SignalDate,
    DateTime PlannedExecutionOpenUtc,
    double SignalClose,
    double Sma20,
    double Sma50,
    string Trend,
    double Momentum,
    double Volatility,
    double VolumeRatio,
    double Drawdown,
    double Atr,
    double Support10,
    double Resistance20,
    int OpportunityScore,
    int RiskScore,
    int NetScore,
    DateTime RegimeDate,
    string TrendRegime,
    string VolatilityRegime,
    string Status,
    string Reason
);

public sealed record ForwardSignalStudyResult(
    string PolicyVersion,
    MarketRegimeSnapshot? Regime,
    IReadOnlyList<ForwardPendingSignal> PendingSignals,
    int SymbolsEvaluated,
    int PendingSignalCount,
    int RejectedCount,
    bool RegimeGatePassed,
    string Status
);
