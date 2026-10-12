using Alpaca.Markets;

public sealed class PortfolioBacktestEngine
{
    private readonly IAlpacaDataClient _dataClient;

    private const double StartingCapital = 20.00;

    // Risk controls
    private const double RiskPerTradePercent = 0.50;
    private const double MaxPositionPercent = 25.00;
    private const int MaxOpenPositions = 3;
    private const int MaxHoldingDays = 10;

    // Execution assumptions
    private const double SlippageBps = 5.0;
    private const double TransactionCostBps = 1.0;

    private const int BacktestYears = 5;

    private const string BenchmarkSymbol = "SPY";

    private static readonly DateTime OutOfSampleStart =
        new(
            2025,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc
        );

    public PortfolioBacktestEngine(
        IAlpacaDataClient dataClient)
    {
        _dataClient = dataClient;
    }

    public async Task<PortfolioBacktestRunResult> RunAsync(
        IEnumerable<string> symbols)
    {
        var tradingSymbols =
            symbols
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        var symbolsToLoad =
            tradingSymbols
                .Append(
                    BenchmarkSymbol
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " PORTFOLIO BACKTESTING ENGINE V3"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Starting Capital: ${StartingCapital:F2}"
        );

        Console.WriteLine(
            $"Risk Per Trade: {RiskPerTradePercent:F2}%"
        );

        Console.WriteLine(
            $"Max Position Size: {MaxPositionPercent:F2}%"
        );

        Console.WriteLine(
            $"Max Open Positions: {MaxOpenPositions}"
        );

        Console.WriteLine(
            $"Max Holding Period: {MaxHoldingDays} trading days"
        );

        Console.WriteLine(
            $"Slippage: {SlippageBps:F1} bps per side"
        );

        Console.WriteLine(
            $"Transaction Cost: {TransactionCostBps:F1} bps per side"
        );

        Console.WriteLine();

        var history =
            new Dictionary<string, List<IBar>>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var symbol in symbolsToLoad)
        {
            Console.WriteLine(
                $"Loading {symbol}..."
            );

            var bars =
                await LoadHistoricalBarsAsync(
                    symbol
                );

            history[symbol] =
                bars;

            Console.WriteLine(
                $"  Bars: {bars.Count}"
            );

            if (bars.Count > 0)
            {
                Console.WriteLine(
                    $"  {bars.First().TimeUtc:yyyy-MM-dd}" +
                    $" -> " +
                    $"{bars.Last().TimeUtc:yyyy-MM-dd}"
                );
            }
        }

        var endDate =
            DateTime.UtcNow.Date;

        var overallStart =
            endDate
                .AddYears(
                    -BacktestYears
                );

        var developmentEnd =
            OutOfSampleStart
                .AddDays(-1);

        var development =
            SimulatePortfolio(
                "DEVELOPMENT PORTFOLIO",
                tradingSymbols,
                history,
                overallStart,
                developmentEnd
            );

        var outOfSample =
            SimulatePortfolio(
                "OUT-OF-SAMPLE PORTFOLIO",
                tradingSymbols,
                history,
                OutOfSampleStart,
                endDate
            );

        var overall =
            SimulatePortfolio(
                "OVERALL PORTFOLIO",
                tradingSymbols,
                history,
                overallStart,
                endDate
            );

        PrintResult(
            development,
            history
        );

        PrintResult(
            outOfSample,
            history
        );

        PrintResult(
            overall,
            history
        );

        Console.WriteLine();

        Console.WriteLine(
            "Portfolio V3 notes:"
        );

        Console.WriteLine(
            "- One shared cash balance is used."
        );

        Console.WriteLine(
            "- Fractional shares are simulated."
        );

        Console.WriteLine(
            "- Concurrent positions compete for the same cash."
        );

        Console.WriteLine(
            "- Slippage and synthetic transaction costs are included."
        );

        Console.WriteLine(
            "- If stop and target are both hit in one daily candle,"
        );

        Console.WriteLine(
            "  the engine assumes STOP first."
        );

        Console.WriteLine(
            "- SPY is used only as a benchmark."
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return new PortfolioBacktestRunResult(
            development,
            outOfSample,
             overall
        );
    }

    private async Task<List<IBar>>
        LoadHistoricalBarsAsync(
            string symbol)
    {
        var endDate =
            DateTime.UtcNow;

        var startDate =
            endDate.AddYears(
                -BacktestYears
            );

        var bars =
            new List<IBar>();

        var chunkStart =
            startDate;

        while (chunkStart < endDate)
        {
            var chunkEnd =
                chunkStart.AddYears(1);

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

    private static PortfolioSimulationResult
        SimulatePortfolio(
            string name,
            IReadOnlyCollection<string> symbols,
            IReadOnlyDictionary<string, List<IBar>> history,
            DateTime startDate,
            DateTime endDate)
    {
        var barMaps =
            new Dictionary<
                string,
                Dictionary<DateTime, IBar>
            >(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var symbol in symbols)
        {
            if (!history.TryGetValue(
                    symbol,
                    out var bars))
            {
                continue;
            }

            barMaps[symbol] =
                bars
                    .Where(
                        bar =>
                            bar.TimeUtc.Date >=
                            startDate.Date &&
                            bar.TimeUtc.Date <=
                            endDate.Date
                    )
                    .GroupBy(
                        bar =>
                            bar.TimeUtc.Date
                    )
                    .ToDictionary(
                        group =>
                            group.Key,

                        group =>
                            group.First()
                    );
        }

        var signals =
            BuildSignals(
                symbols,
                history,
                startDate,
                endDate
            );

        var signalsByDate =
            signals
                .GroupBy(
                    signal =>
                        signal.EntryDate.Date
                )
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group
                            .OrderByDescending(
                                signal =>
                                    signal.NetScore
                            )
                            .ThenByDescending(
                                signal =>
                                    signal.RiskRewardRatio
                            )
                            .ToList()
                );

        var tradingDates =
            barMaps
                .Values
                .SelectMany(
                    map =>
                        map.Keys
                )
                .Distinct()
                .OrderBy(
                    date =>
                        date
                )
                .ToList();

        var cash =
            StartingCapital;

        var openPositions =
            new Dictionary<
                string,
                PortfolioPosition
            >(
                StringComparer.OrdinalIgnoreCase
            );

        var trades =
            new List<PortfolioTrade>();

        var equityCurve =
            new List<PortfolioEquityPoint>();

        var rejectedForCapital =
            0;

        var rejectedForPositionLimit =
            0;

        var rejectedForExistingPosition =
            0;

        var rejectedByRiskRules =
            0;

        var maximumOpenPositionsObserved =
            0;

        foreach (var date in tradingDates)
        {
            // 1. Process positions that were already open
            // before today's market open.
            foreach (
                var position in
                openPositions
                    .Values
                    .ToList())
            {
                if (!barMaps.TryGetValue(
                        position.Symbol,
                        out var symbolBars))
                {
                    continue;
                }

                if (!symbolBars.TryGetValue(
                        date,
                        out var bar))
                {
                    continue;
                }

                position.HoldingDays++;

                var exit =
                    EvaluateExit(
                        position,
                        bar,
                        false
                    );

                if (exit is not null)
                {
                    ClosePosition(
                        position,
                        exit.RawExitPrice,
                        date,
                        exit.Reason,
                        ref cash,
                        trades
                    );

                    openPositions.Remove(
                        position.Symbol
                    );
                }
                else if (
                    position.HoldingDays >=
                    MaxHoldingDays)
                {
                    ClosePosition(
                        position,
                        (double)bar.Close,
                        date,
                        "TIME_EXIT",
                        ref cash,
                        trades
                    );

                    openPositions.Remove(
                        position.Symbol
                    );
                }
                else
                {
                    position.LastMarkPrice =
                        (double)bar.Close;
                }
            }

            // 2. Open new positions at today's open.
            if (signalsByDate.TryGetValue(
                    date,
                    out var todaysSignals))
            {
                foreach (var signal in todaysSignals)
                {
                    if (openPositions.ContainsKey(
                            signal.Symbol))
                    {
                        rejectedForExistingPosition++;

                        continue;
                    }

                    if (openPositions.Count >=
                        MaxOpenPositions)
                    {
                        rejectedForPositionLimit++;

                        continue;
                    }

                    if (!barMaps.TryGetValue(
                            signal.Symbol,
                            out var symbolBars))
                    {
                        continue;
                    }

                    if (!symbolBars.TryGetValue(
                            date,
                            out var entryBar))
                    {
                        continue;
                    }

                    var rawOpen =
                        (double)entryBar.Open;

                    var executionPrice =
                        ApplyBuySlippage(
                            rawOpen
                        );

                    var candidate =
                        BuildCandidate(
                            signal.Analysis,
                            executionPrice
                        );

                    if (!candidate.IsValid)
                    {
                        rejectedByRiskRules++;

                        continue;
                    }

                    var equityAtOpen =
                        CalculatePortfolioValue(
                            cash,
                            openPositions,
                            barMaps,
                            date,
                            true
                        );

                    var maximumRiskDollars =
                        equityAtOpen *
                        (
                            RiskPerTradePercent /
                            100.0
                        );

                    var maximumPositionDollars =
                        equityAtOpen *
                        (
                            MaxPositionPercent /
                            100.0
                        );

                    var quantityByRisk =
                        maximumRiskDollars /
                        candidate.RiskPerShare;

                    var quantityByPosition =
                        maximumPositionDollars /
                        executionPrice;

                    var feeRate =
                        TransactionCostBps /
                        10000.0;

                    var quantityByCash =
                        cash /
                        (
                            executionPrice *
                            (
                                1.0 +
                                feeRate
                            )
                        );

                    var quantity =
                        Math.Min(
                            quantityByRisk,
                            Math.Min(
                                quantityByPosition,
                                quantityByCash
                            )
                        );

                    quantity =
                        Math.Floor(
                            quantity *
                            10000
                        ) /
                        10000;

                    if (quantity < 0.001)
                    {
                        rejectedForCapital++;

                        continue;
                    }

                    var entryValue =
                        executionPrice *
                        quantity;

                    var entryFee =
                        entryValue *
                        feeRate;

                    var totalEntryCost =
                        entryValue +
                        entryFee;

                    if (totalEntryCost > cash)
                    {
                        rejectedForCapital++;

                        continue;
                    }

                    cash -=
                        totalEntryCost;

                    var initialRiskDollars =
                        candidate.RiskPerShare *
                        quantity;

                    var entrySlippageCost =
                        (
                            executionPrice -
                            rawOpen
                        ) *
                        quantity;

                    var position =
                        new PortfolioPosition(
                            signal.Symbol,
                            date,
                            executionPrice,
                            quantity,
                            candidate.StopLoss,
                            candidate.TakeProfit,
                            initialRiskDollars,
                            entryValue,
                            entryFee,
                            entrySlippageCost,
                            1,
                            (double)entryBar.Close
                        );

                    openPositions[
                        signal.Symbol
                    ] =
                        position;

                    maximumOpenPositionsObserved =
                        Math.Max(
                            maximumOpenPositionsObserved,
                            openPositions.Count
                        );

                    // Position was entered at today's open,
                    // so we can evaluate today's high/low,
                    // but not a gap before entry.
                    var sameDayExit =
                        EvaluateExit(
                            position,
                            entryBar,
                            true
                        );

                    if (sameDayExit is not null)
                    {
                        ClosePosition(
                            position,
                            sameDayExit.RawExitPrice,
                            date,
                            sameDayExit.Reason,
                            ref cash,
                            trades
                        );

                        openPositions.Remove(
                            signal.Symbol
                        );
                    }
                }
            }

            // 3. Mark remaining positions to market at close.
            foreach (
                var position in
                openPositions.Values)
            {
                if (!barMaps.TryGetValue(
                        position.Symbol,
                        out var symbolBars))
                {
                    continue;
                }

                if (symbolBars.TryGetValue(
                        date,
                        out var bar))
                {
                    position.LastMarkPrice =
                        (double)bar.Close;
                }
            }

            var equity =
                cash +
                openPositions
                    .Values
                    .Sum(
                        position =>
                            position.Quantity *
                            position.LastMarkPrice
                    );

            equityCurve.Add(
                new PortfolioEquityPoint(
                    date,
                    cash,
                    equity,
                    openPositions.Count
                )
            );
        }

        // Force-close anything still open at the end
        // of the requested simulation window.
        foreach (
            var position in
            openPositions
                .Values
                .ToList())
        {
            if (!history.TryGetValue(
                    position.Symbol,
                    out var bars))
            {
                continue;
            }

            var lastBar =
                bars
                    .Where(
                        bar =>
                            bar.TimeUtc.Date <=
                            endDate.Date
                    )
                    .OrderByDescending(
                        bar =>
                            bar.TimeUtc
                    )
                    .FirstOrDefault();

            if (lastBar is null)
            {
                continue;
            }

            ClosePosition(
                position,
                (double)lastBar.Close,
                lastBar.TimeUtc.Date,
                "PERIOD_END",
                ref cash,
                trades
            );
        }

        var finalEquity =
            cash;

        if (equityCurve.Count > 0)
        {
            equityCurve.Add(
                new PortfolioEquityPoint(
                    equityCurve[^1].Date,
                    cash,
                    finalEquity,
                    0
                )
            );
        }

        return new PortfolioSimulationResult(
            name,
            startDate,
            endDate,
            StartingCapital,
            finalEquity,
            trades,
            equityCurve,
            rejectedForCapital,
            rejectedForPositionLimit,
            rejectedForExistingPosition,
            rejectedByRiskRules,
            maximumOpenPositionsObserved
        );
    }

    private static List<PortfolioSignal>
        BuildSignals(
            IEnumerable<string> symbols,
            IReadOnlyDictionary<string, List<IBar>> history,
            DateTime startDate,
            DateTime endDate)
    {
        var signals =
            new List<PortfolioSignal>();

        foreach (var symbol in symbols)
        {
            if (!history.TryGetValue(
                    symbol,
                    out var bars))
            {
                continue;
            }

            for (
                var index = 50;
                index < bars.Count - 1;
                index++)
            {
                var signalDate =
                    bars[index]
                        .TimeUtc
                        .Date;

                if (signalDate <
                    startDate.Date)
                {
                    continue;
                }

                if (signalDate >
                    endDate.Date)
                {
                    break;
                }

                var entryDate =
                    bars[index + 1]
                        .TimeUtc
                        .Date;

                if (entryDate >
                    endDate.Date)
                {
                    continue;
                }

                var analysis =
                    AnalyzeAt(
                        symbol,
                        bars,
                        index
                    );

                if (analysis is null ||
                    analysis.Decision !=
                    "CONSIDER")
                {
                    continue;
                }

                var rawEntryOpen =
                    (double)
                        bars[index + 1]
                            .Open;

                var previewEntry =
                    ApplyBuySlippage(
                        rawEntryOpen
                    );

                var candidate =
                    BuildCandidate(
                        analysis,
                        previewEntry
                    );

                if (!candidate.IsValid)
                {
                    continue;
                }

                signals.Add(
                    new PortfolioSignal(
                        symbol,
                        signalDate,
                        entryDate,
                        rawEntryOpen,
                        analysis.OpportunityScore -
                        analysis.RiskScore,
                        candidate.RiskRewardRatio,
                        analysis
                    )
                );
            }
        }

        return signals;
    }

    private static PortfolioAnalysis?
        AnalyzeAt(
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
            bars[index].TimeUtc,
            price,
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

    private static PortfolioCandidate
        BuildCandidate(
            PortfolioAnalysis analysis,
            double entryPrice)
    {
        if (analysis.Atr <= 0)
        {
            return
                PortfolioCandidate.Invalid(
                    "Invalid ATR."
                );
        }

        var atrStop =
            entryPrice -
            (
                analysis.Atr *
                2.0
            );

        var supportStop =
            analysis.Support10 -
            (
                analysis.Atr *
                0.25
            );

        var stopLoss =
            Math.Max(
                atrStop,
                supportStop
            );

        if (stopLoss <= 0 ||
            stopLoss >= entryPrice)
        {
            return
                PortfolioCandidate.Invalid(
                    "Invalid stop loss."
                );
        }

        var takeProfit =
            analysis.Resistance20 +
            (
                analysis.Atr *
                1.50
            );

        if (takeProfit <=
            entryPrice)
        {
            return
                PortfolioCandidate.Invalid(
                    "Target below entry."
                );
        }

        var riskPerShare =
            entryPrice -
            stopLoss;

        var rewardPerShare =
            takeProfit -
            entryPrice;

        var riskReward =
            rewardPerShare /
            riskPerShare;

        if (analysis.Trend !=
            "Bullish")
        {
            return
                PortfolioCandidate.Invalid(
                    "Trend is not bullish."
                );
        }

        if (analysis.Momentum <= 0)
        {
            return
                PortfolioCandidate.Invalid(
                    "Momentum is not positive."
                );
        }

        if (analysis.RiskScore > 35)
        {
            return
                PortfolioCandidate.Invalid(
                    "Risk score too high."
                );
        }

        if (riskReward < 1.80)
        {
            return
                PortfolioCandidate.Invalid(
                    "Risk/reward below 1.80."
                );
        }

        return new PortfolioCandidate(
            true,
            stopLoss,
            takeProfit,
            riskPerShare,
            rewardPerShare,
            riskReward,
            string.Empty
        );
    }

    private static PortfolioExitDecision?
        EvaluateExit(
            PortfolioPosition position,
            IBar bar,
            bool isEntryDay)
    {
        var open =
            (double)bar.Open;

        var high =
            (double)bar.High;

        var low =
            (double)bar.Low;

        if (!isEntryDay)
        {
            if (open <=
                position.StopLoss)
            {
                return new PortfolioExitDecision(
                    open,
                    "GAP_STOP"
                );
            }

            if (open >=
                position.TakeProfit)
            {
                return new PortfolioExitDecision(
                    open,
                    "GAP_TARGET"
                );
            }
        }

        var stopHit =
            low <=
            position.StopLoss;

        var targetHit =
            high >=
            position.TakeProfit;

        if (stopHit &&
            targetHit)
        {
            return new PortfolioExitDecision(
                position.StopLoss,
                "BOTH_HIT_ASSUME_STOP"
            );
        }

        if (stopHit)
        {
            return new PortfolioExitDecision(
                position.StopLoss,
                "STOP_LOSS"
            );
        }

        if (targetHit)
        {
            return new PortfolioExitDecision(
                position.TakeProfit,
                "TAKE_PROFIT"
            );
        }

        return null;
    }

    private static void ClosePosition(
        PortfolioPosition position,
        double rawExitPrice,
        DateTime exitDate,
        string exitReason,
        ref double cash,
        ICollection<PortfolioTrade> trades)
    {
        var exitPrice =
            ApplySellSlippage(
                rawExitPrice
            );

        var proceeds =
            exitPrice *
            position.Quantity;

        var exitFee =
            proceeds *
            (
                TransactionCostBps /
                10000.0
            );

        cash +=
            proceeds -
            exitFee;

        var pnl =
            (
                proceeds -
                exitFee
            ) -
            (
                position.EntryValue +
                position.EntryFee
            );

        var rMultiple =
            position.InitialRiskDollars > 0
                ? pnl /
                  position.InitialRiskDollars
                : 0;

        var returnPercent =
            (
                position.EntryValue +
                position.EntryFee
            ) > 0
                ? pnl /
                  (
                      position.EntryValue +
                      position.EntryFee
                  ) *
                  100
                : 0;

        var exitSlippageCost =
            (
                rawExitPrice -
                exitPrice
            ) *
            position.Quantity;

        trades.Add(
            new PortfolioTrade(
                position.Symbol,
                position.EntryDate,
                exitDate,
                position.EntryPrice,
                exitPrice,
                position.Quantity,
                position.EntryValue,
                position.EntryFee,
                exitFee,
                position.EntrySlippageCost +
                exitSlippageCost,
                pnl,
                returnPercent,
                rMultiple,
                exitReason
            )
        );
    }

    private static double CalculatePortfolioValue(
        double cash,
        IReadOnlyDictionary<
            string,
            PortfolioPosition
        > positions,
        IReadOnlyDictionary<
            string,
            Dictionary<DateTime, IBar>
        > barMaps,
        DateTime date,
        bool useOpen)
    {
        var equity =
            cash;

        foreach (var position in positions.Values)
        {
            var price =
                position.LastMarkPrice;

            if (barMaps.TryGetValue(
                    position.Symbol,
                    out var bars) &&
                bars.TryGetValue(
                    date,
                    out var bar))
            {
                price =
                    useOpen
                        ? (double)bar.Open
                        : (double)bar.Close;
            }

            equity +=
                position.Quantity *
                price;
        }

        return equity;
    }

    private static void PrintResult(
        PortfolioSimulationResult result,
        IReadOnlyDictionary<string, List<IBar>> history)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $" {result.Name}"
        );

        Console.WriteLine(
            "=============================================="
        );

        var pnl =
            result.FinalEquity -
            result.StartingCapital;

        var returnPercent =
            result.StartingCapital > 0
                ? pnl /
                  result.StartingCapital *
                  100
                : 0;

        var wins =
            result.Trades.Count(
                trade =>
                    trade.Pnl > 0
            );

        var losses =
            result.Trades.Count(
                trade =>
                    trade.Pnl < 0
            );

        var winRate =
            result.Trades.Count > 0
                ? wins /
                  (double)result.Trades.Count *
                  100
                : 0;

        var grossProfit =
            result.Trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .Sum(
                    trade =>
                        trade.Pnl
                );

        var grossLoss =
            Math.Abs(
                result.Trades
                    .Where(
                        trade =>
                            trade.Pnl < 0
                    )
                    .Sum(
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

        var averageR =
            result.Trades.Count > 0
                ? result.Trades.Average(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var totalFees =
            result.Trades.Sum(
                trade =>
                    trade.EntryFee +
                    trade.ExitFee
            );

        var totalSlippage =
            result.Trades.Sum(
                trade =>
                    trade.SlippageCost
            );

        var drawdown =
            CalculateDrawdown(
                result.EquityCurve
            );

        var averageOpenPositions =
            result.EquityCurve.Count > 0
                ? result.EquityCurve.Average(
                    point =>
                        point.OpenPositions
                  )
                : 0;

        var benchmarkReturn =
            CalculateBenchmarkReturn(
                history,
                result.StartDate,
                result.EndDate
            );

        Console.WriteLine(
            $"Period: {result.StartDate:yyyy-MM-dd}" +
            $" -> " +
            $"{result.EndDate:yyyy-MM-dd}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Starting Capital: ${result.StartingCapital:F2}"
        );

        Console.WriteLine(
            $"Ending Equity:    ${result.FinalEquity:F2}"
        );

        Console.WriteLine(
            $"Net P&L:          ${pnl:F2}"
        );

        Console.WriteLine(
            $"Portfolio Return: {returnPercent:F2}%"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Trades: {result.Trades.Count}"
        );

        Console.WriteLine(
            $"Wins: {wins}"
        );

        Console.WriteLine(
            $"Losses: {losses}"
        );

        Console.WriteLine(
            $"Win Rate: {winRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(profitFactor)}"
        );

        Console.WriteLine(
            $"Average R: {averageR:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Max Drawdown: ${drawdown.Dollars:F2}"
        );

        Console.WriteLine(
            $"Max Drawdown %: {drawdown.Percent:F2}%"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Total Transaction Costs: ${totalFees:F4}"
        );

        Console.WriteLine(
            $"Estimated Slippage Cost: ${totalSlippage:F4}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Max Simultaneous Positions: " +
            $"{result.MaximumOpenPositionsObserved}"
        );

        Console.WriteLine(
            $"Average Open Positions: " +
            $"{averageOpenPositions:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Rejected Entries:"
        );

        Console.WriteLine(
            $"  Not enough capital: " +
            $"{result.RejectedForCapital}"
        );

        Console.WriteLine(
            $"  Position limit: " +
            $"{result.RejectedForPositionLimit}"
        );

        Console.WriteLine(
            $"  Existing symbol position: " +
            $"{result.RejectedForExistingPosition}"
        );

        Console.WriteLine(
            $"  Risk rules: " +
            $"{result.RejectedByRiskRules}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"SPY Buy & Hold Benchmark: " +
            $"{benchmarkReturn:F2}%"
        );

        Console.WriteLine(
            $"Strategy vs SPY: " +
            $"{returnPercent - benchmarkReturn:F2} percentage points"
        );

        if (result.Trades.Count > 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                "Exit Reasons:"
            );

            foreach (
                var group in result.Trades
                    .GroupBy(
                        trade =>
                            trade.ExitReason
                    )
                    .OrderByDescending(
                        group =>
                            group.Count()
                    ))
            {
                Console.WriteLine(
                    $"  {group.Key}: {group.Count()}"
                );
            }

            Console.WriteLine();

            Console.WriteLine(
                "Per Symbol:"
            );

            foreach (
                var group in result.Trades
                    .GroupBy(
                        trade =>
                            trade.Symbol
                    )
                    .OrderBy(
                        group =>
                            group.Key
                    ))
            {
                var symbolWins =
                    group.Count(
                        trade =>
                            trade.Pnl > 0
                    );

                var symbolPnl =
                    group.Sum(
                        trade =>
                            trade.Pnl
                    );

                var symbolWinRate =
                    symbolWins /
                    (double)group.Count() *
                    100;

                Console.WriteLine(
                    $"  {group.Key} | " +
                    $"Trades {group.Count()} | " +
                    $"Win {symbolWinRate:F2}% | " +
                    $"P&L ${symbolPnl:F2}"
                );
            }
        }
    }

    private static (
        double Dollars,
        double Percent)
        CalculateDrawdown(
            IReadOnlyList<PortfolioEquityPoint> curve)
    {
        if (curve.Count == 0)
        {
            return (
                0,
                0
            );
        }

        var peak =
            curve[0].Equity;

        var maximumDollars =
            0.0;

        var maximumPercent =
            0.0;

        foreach (var point in curve)
        {
            if (point.Equity >
                peak)
            {
                peak =
                    point.Equity;
            }

            var dollars =
                peak -
                point.Equity;

            var percent =
                peak > 0
                    ? dollars /
                      peak *
                      100
                    : 0;

            maximumDollars =
                Math.Max(
                    maximumDollars,
                    dollars
                );

            maximumPercent =
                Math.Max(
                    maximumPercent,
                    percent
                );
        }

        return (
            maximumDollars,
            maximumPercent
        );
    }

    private static double CalculateBenchmarkReturn(
        IReadOnlyDictionary<string, List<IBar>> history,
        DateTime startDate,
        DateTime endDate)
    {
        if (!history.TryGetValue(
                BenchmarkSymbol,
                out var bars))
        {
            return 0;
        }

        var periodBars =
            bars
                .Where(
                    bar =>
                        bar.TimeUtc.Date >=
                        startDate.Date &&
                        bar.TimeUtc.Date <=
                        endDate.Date
                )
                .OrderBy(
                    bar =>
                        bar.TimeUtc
                )
                .ToList();

        if (periodBars.Count < 2)
        {
            return 0;
        }

        var entry =
            (double)
                periodBars.First()
                    .Open;

        var exit =
            (double)
                periodBars.Last()
                    .Close;

        if (entry <= 0)
        {
            return 0;
        }

        return
            (
                exit -
                entry
            ) /
            entry *
            100;
    }

    private static double ApplyBuySlippage(
        double price)
    {
        return
            price *
            (
                1.0 +
                SlippageBps /
                10000.0
            );
    }

    private static double ApplySellSlippage(
        double price)
    {
        return
            price *
            (
                1.0 -
                SlippageBps /
                10000.0
            );
    }

    private static double CalculateMomentum(
        IReadOnlyList<double> closes,
        int periods)
    {
        if (closes.Count <= periods)
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
            100;
    }

    private static double
        CalculateAnnualizedVolatility(
            IReadOnlyList<double> closes,
            int periods)
    {
        if (closes.Count <= periods)
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
                recent[index - 1];

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

        if (returns.Count < 2)
        {
            return 0;
        }

        var average =
            returns.Average();

        var variance =
            returns
                .Sum(
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
            100;
    }

    private static double CalculateAtr(
        IReadOnlyList<IBar> bars,
        int endIndex,
        int periods)
    {
        if (endIndex < periods)
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
                    bars[index - 1]
                        .Close;

            var trueRange =
                Math.Max(
                    high - low,

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

public sealed record PortfolioAnalysis(
    string Symbol,
    DateTime SignalDate,
    double Price,
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
    string Decision
);

public sealed record PortfolioSignal(
    string Symbol,
    DateTime SignalDate,
    DateTime EntryDate,
    double RawEntryOpen,
    int NetScore,
    double RiskRewardRatio,
    PortfolioAnalysis Analysis
);

public sealed record PortfolioCandidate(
    bool IsValid,
    double StopLoss,
    double TakeProfit,
    double RiskPerShare,
    double RewardPerShare,
    double RiskRewardRatio,
    string RejectionReason)
{
    public static PortfolioCandidate Invalid(
        string reason)
    {
        return new PortfolioCandidate(
            false,
            0,
            0,
            0,
            0,
            0,
            reason
        );
    }
}

public sealed class PortfolioPosition
{
    public PortfolioPosition(
        string symbol,
        DateTime entryDate,
        double entryPrice,
        double quantity,
        double stopLoss,
        double takeProfit,
        double initialRiskDollars,
        double entryValue,
        double entryFee,
        double entrySlippageCost,
        int holdingDays,
        double lastMarkPrice)
    {
        Symbol = symbol;
        EntryDate = entryDate;
        EntryPrice = entryPrice;
        Quantity = quantity;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        InitialRiskDollars = initialRiskDollars;
        EntryValue = entryValue;
        EntryFee = entryFee;
        EntrySlippageCost = entrySlippageCost;
        HoldingDays = holdingDays;
        LastMarkPrice = lastMarkPrice;
    }

    public string Symbol { get; }

    public DateTime EntryDate { get; }

    public double EntryPrice { get; }

    public double Quantity { get; }

    public double StopLoss { get; }

    public double TakeProfit { get; }

    public double InitialRiskDollars { get; }

    public double EntryValue { get; }

    public double EntryFee { get; }

    public double EntrySlippageCost { get; }

    public int HoldingDays { get; set; }

    public double LastMarkPrice { get; set; }
}

public sealed record PortfolioExitDecision(
    double RawExitPrice,
    string Reason
);

public sealed record PortfolioTrade(
    string Symbol,
    DateTime EntryDate,
    DateTime ExitDate,
    double EntryPrice,
    double ExitPrice,
    double Quantity,
    double EntryValue,
    double EntryFee,
    double ExitFee,
    double SlippageCost,
    double Pnl,
    double ReturnPercent,
    double RMultiple,
    string ExitReason
);

public sealed record PortfolioEquityPoint(
    DateTime Date,
    double Cash,
    double Equity,
    int OpenPositions
);

public sealed record PortfolioSimulationResult(
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    double StartingCapital,
    double FinalEquity,
    IReadOnlyList<PortfolioTrade> Trades,
    IReadOnlyList<PortfolioEquityPoint> EquityCurve,
    int RejectedForCapital,
    int RejectedForPositionLimit,
    int RejectedForExistingPosition,
    int RejectedByRiskRules,
    int MaximumOpenPositionsObserved
);

public sealed record PortfolioBacktestRunResult(
    PortfolioSimulationResult Development,
    PortfolioSimulationResult OutOfSample,
    PortfolioSimulationResult Overall
);