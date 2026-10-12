using Alpaca.Markets;

public sealed class BacktestEngine
{
    private readonly IAlpacaDataClient _dataClient;

    private const double ReferenceCapital = 20.00;
    private const double RiskPerTradePercent = 0.50;
    private const double MaxPositionPercent = 25.00;

    private const int MaxHoldingDays = 10;
    private const int BacktestYears = 5;

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

    public BacktestEngine(
        IAlpacaDataClient dataClient)
    {
        _dataClient = dataClient;
    }

    public async Task RunAsync(
        IEnumerable<string> symbols)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " BACKTESTING ENGINE V2"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"History Window: {BacktestYears} years"
        );

        Console.WriteLine(
            $"Reference Capital: ${ReferenceCapital:F2}"
        );

        Console.WriteLine(
            $"Risk Per Trade: {RiskPerTradePercent:F2}%"
        );

        Console.WriteLine(
            $"Max Position: {MaxPositionPercent:F2}%"
        );

        Console.WriteLine(
            $"Max Holding Period: {MaxHoldingDays} trading days"
        );

        Console.WriteLine(
            $"Out-of-Sample Start: {OutOfSampleStart:yyyy-MM-dd}"
        );

        Console.WriteLine();

        var allTrades =
            new List<BacktestTrade>();

        foreach (var symbol in symbols)
        {
            Console.WriteLine(
                $"Loading {symbol} history..."
            );

            try
            {
                var bars =
                    await LoadHistoricalBarsAsync(
                        symbol
                    );

                if (bars.Count < 80)
                {
                    Console.WriteLine(
                        $"  {symbol}: Not enough historical data."
                    );

                    Console.WriteLine(
                        $"  Bars loaded: {bars.Count}"
                    );

                    Console.WriteLine();

                    continue;
                }

                Console.WriteLine(
                    $"  Bars loaded: {bars.Count}"
                );

                Console.WriteLine(
                    $"  From: {bars.First().TimeUtc:yyyy-MM-dd}"
                );

                Console.WriteLine(
                    $"  To:   {bars.Last().TimeUtc:yyyy-MM-dd}"
                );

                var trades =
                    BacktestSymbol(
                        symbol,
                        bars
                    );

                allTrades.AddRange(
                    trades
                );

                PrintSymbolSummary(
                    symbol,
                    trades
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  {symbol}: Backtest failed."
                );

                Console.WriteLine(
                    $"  {ex.Message}"
                );

                Console.WriteLine();
            }
        }

        var developmentTrades =
            allTrades
                .Where(
                    trade =>
                        trade.SignalDate <
                        OutOfSampleStart
                )
                .ToList();

        var outOfSampleTrades =
            allTrades
                .Where(
                    trade =>
                        trade.SignalDate >=
                        OutOfSampleStart
                )
                .ToList();

        PrintPeriodSummary(
            "DEVELOPMENT PERIOD",
            developmentTrades
        );

        PrintPeriodSummary(
            "OUT-OF-SAMPLE PERIOD",
            outOfSampleTrades
        );

        PrintPeriodSummary(
            "OVERALL BACKTEST RESULTS",
            allTrades
        );

        Console.WriteLine();

        Console.WriteLine(
            "Backtest notes:"
        );

        Console.WriteLine(
            "- Strategy rules were kept unchanged between periods."
        );

        Console.WriteLine(
            "- Out-of-sample trades are separated by signal date."
        );

        Console.WriteLine(
            "- Daily bars are used, so if stop and target are hit"
        );

        Console.WriteLine(
            "  on the same candle, the engine assumes STOP first."
        );

        Console.WriteLine(
            "- Max drawdown uses a synthetic fixed-capital equity curve."
        );

        Console.WriteLine(
            "- No commissions, fees, spread, or slippage are modeled yet."
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

        var allBars =
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

            allBars.AddRange(
                page.Items
            );

            chunkStart =
                chunkEnd;
        }

        return allBars
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

    private static List<BacktestTrade>
        BacktestSymbol(
            string symbol,
            IReadOnlyList<IBar> bars)
    {
        var trades =
            new List<BacktestTrade>();

        var index =
            50;

        while (
            index <
            bars.Count - 1)
        {
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
                index++;

                continue;
            }

            var entryIndex =
                index + 1;

            var entryPrice =
                (double)
                    bars[entryIndex]
                        .Open;

            var candidate =
                BuildCandidate(
                    analysis,
                    entryPrice
                );

            if (!candidate.IsValid)
            {
                index++;

                continue;
            }

            var trade =
                SimulateTrade(
                    symbol,
                    bars,
                    index,
                    entryIndex,
                    candidate
                );

            trades.Add(
                trade
            );

            // Do not allow overlapping positions
            // for the same symbol.
            index =
                trade.ExitIndex + 1;
        }

        return trades;
    }

    private static BacktestAnalysis?
        AnalyzeAt(
            string symbol,
            IReadOnlyList<IBar> bars,
            int index)
    {
        if (index < 50)
        {
            return null;
        }

        var availableBars =
            bars
                .Take(
                    index + 1
                )
                .ToList();

        var closes =
            availableBars
                .Select(
                    bar =>
                        (double)
                            bar.Close
                )
                .ToList();

        var volumes =
            availableBars
                .Select(
                    bar =>
                        (double)
                            bar.Volume
                )
                .ToList();

        var currentPrice =
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
                        currentPrice -
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
                .Take(
                    Math.Min(
                        10,
                        index + 1
                    )
                )
                .Min(
                    bar =>
                        (double)
                            bar.Low
                );

        var resistance20 =
            bars
                .Skip(
                    Math.Max(
                        0,
                        index - 19
                    )
                )
                .Take(
                    Math.Min(
                        20,
                        index + 1
                    )
                )
                .Max(
                    bar =>
                        (double)
                            bar.High
                );

        var trend =
            currentPrice > sma20 &&
            sma20 > sma50
                ? "Bullish"

                : currentPrice < sma20 &&
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

        return new BacktestAnalysis(
            symbol,
            bars[index]
                .TimeUtc,
            currentPrice,
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

    private static BacktestCandidate
        BuildCandidate(
            BacktestAnalysis analysis,
            double entryPrice)
    {
        if (analysis.Atr <= 0)
        {
            return
                BacktestCandidate.Invalid(
                    "Invalid ATR."
                );
        }

        var atrStop =
            entryPrice -
            analysis.Atr *
            2.0;

        var supportStop =
            analysis.Support10 -
            analysis.Atr *
            0.25;

        var stopLoss =
            Math.Max(
                atrStop,
                supportStop
            );

        if (stopLoss <= 0 ||
            stopLoss >= entryPrice)
        {
            return
                BacktestCandidate.Invalid(
                    "Invalid stop loss."
                );
        }

        var takeProfit =
            analysis.Resistance20 +
            analysis.Atr *
            1.50;

        if (takeProfit <=
            entryPrice)
        {
            return
                BacktestCandidate.Invalid(
                    "Target is below entry."
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
                BacktestCandidate.Invalid(
                    "Trend not bullish."
                );
        }

        if (analysis.Momentum <= 0)
        {
            return
                BacktestCandidate.Invalid(
                    "Momentum not positive."
                );
        }

        if (analysis.RiskScore > 35)
        {
            return
                BacktestCandidate.Invalid(
                    "Risk score too high."
                );
        }

        if (riskReward < 1.80)
        {
            return
                BacktestCandidate.Invalid(
                    "Risk/reward below 1.80."
                );
        }

        var maximumRisk =
            ReferenceCapital *
            (
                RiskPerTradePercent /
                100.0
            );

        var maximumPosition =
            ReferenceCapital *
            (
                MaxPositionPercent /
                100.0
            );

        var quantityByRisk =
            maximumRisk /
            riskPerShare;

        var quantityByCapital =
            maximumPosition /
            entryPrice;

        var quantity =
            Math.Min(
                quantityByRisk,
                quantityByCapital
            );

        quantity =
            Math.Floor(
                quantity *
                10000
            ) /
            10000;

        if (quantity < 0.001)
        {
            return
                BacktestCandidate.Invalid(
                    "Position too small."
                );
        }

        return new BacktestCandidate(
            true,
            entryPrice,
            stopLoss,
            takeProfit,
            riskPerShare,
            rewardPerShare,
            riskReward,
            quantity,
            string.Empty
        );
    }

    private static BacktestTrade
        SimulateTrade(
            string symbol,
            IReadOnlyList<IBar> bars,
            int signalIndex,
            int entryIndex,
            BacktestCandidate candidate)
    {
        var exitIndex =
            Math.Min(
                entryIndex +
                MaxHoldingDays -
                1,
                bars.Count - 1
            );

        var exitPrice =
            (double)
                bars[exitIndex]
                    .Close;

        var exitReason =
            "TIME_EXIT";

        for (
            var index = entryIndex;
            index <= exitIndex;
            index++)
        {
            var bar =
                bars[index];

            var open =
                (double)
                    bar.Open;

            var high =
                (double)
                    bar.High;

            var low =
                (double)
                    bar.Low;

            if (open <=
                candidate.StopLoss)
            {
                exitPrice =
                    open;

                exitReason =
                    "GAP_STOP";

                exitIndex =
                    index;

                break;
            }

            if (open >=
                candidate.TakeProfit)
            {
                exitPrice =
                    open;

                exitReason =
                    "GAP_TARGET";

                exitIndex =
                    index;

                break;
            }

            var stopHit =
                low <=
                candidate.StopLoss;

            var targetHit =
                high >=
                candidate.TakeProfit;

            if (stopHit &&
                targetHit)
            {
                exitPrice =
                    candidate.StopLoss;

                exitReason =
                    "BOTH_HIT_ASSUME_STOP";

                exitIndex =
                    index;

                break;
            }

            if (stopHit)
            {
                exitPrice =
                    candidate.StopLoss;

                exitReason =
                    "STOP_LOSS";

                exitIndex =
                    index;

                break;
            }

            if (targetHit)
            {
                exitPrice =
                    candidate.TakeProfit;

                exitReason =
                    "TAKE_PROFIT";

                exitIndex =
                    index;

                break;
            }
        }

        var pnlPerShare =
            exitPrice -
            candidate.EntryPrice;

        var pnl =
            pnlPerShare *
            candidate.Quantity;

        var riskDollars =
            candidate.RiskPerShare *
            candidate.Quantity;

        var rMultiple =
            riskDollars > 0
                ? pnl /
                  riskDollars
                : 0;

        var positionValue =
            candidate.EntryPrice *
            candidate.Quantity;

        var returnPercent =
            positionValue > 0
                ? pnl /
                  positionValue *
                  100
                : 0;

        return new BacktestTrade(
            symbol,
            bars[signalIndex]
                .TimeUtc,
            bars[entryIndex]
                .TimeUtc,
            bars[exitIndex]
                .TimeUtc,
            signalIndex,
            entryIndex,
            exitIndex,
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.TakeProfit,
            exitPrice,
            candidate.Quantity,
            pnl,
            returnPercent,
            rMultiple,
            exitReason
        );
    }

    private static void PrintSymbolSummary(
        string symbol,
        IReadOnlyCollection<BacktestTrade> trades)
    {
        Console.WriteLine(
            $"  Trades: {trades.Count}"
        );

        if (trades.Count == 0)
        {
            Console.WriteLine();

            return;
        }

        var stats =
            CalculateStatistics(
                trades
            );

        Console.WriteLine(
            $"  Win Rate: {stats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"  Profit Factor: {FormatNumber(stats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"  Average R: {stats.AverageR:F2}"
        );

        Console.WriteLine(
            $"  P&L: ${stats.TotalPnl:F2}"
        );

        Console.WriteLine();
    }

    private static void PrintPeriodSummary(
        string title,
        IReadOnlyCollection<BacktestTrade> trades)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $" {title}"
        );

        Console.WriteLine(
            "=============================================="
        );

        if (trades.Count == 0)
        {
            Console.WriteLine(
                "No qualifying trades."
            );

            return;
        }

        var stats =
            CalculateStatistics(
                trades
            );

        Console.WriteLine(
            $"Trades: {stats.Trades}"
        );

        Console.WriteLine(
            $"Winning Trades: {stats.Wins}"
        );

        Console.WriteLine(
            $"Losing Trades: {stats.Losses}"
        );

        Console.WriteLine(
            $"Flat Trades: {stats.Flat}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Win Rate: {stats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(stats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"Average R: {stats.AverageR:F2}"
        );

        Console.WriteLine(
            $"Expectancy: {stats.ExpectancyR:F2} R/trade"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Average Win: {stats.AverageWinR:F2} R"
        );

        Console.WriteLine(
            $"Average Loss: {stats.AverageLossR:F2} R"
        );

        Console.WriteLine(
            $"Largest Win: {stats.LargestWinR:F2} R"
        );

        Console.WriteLine(
            $"Largest Loss: {stats.LargestLossR:F2} R"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Average Position Return: {stats.AverageReturn:F2}%"
        );

        Console.WriteLine(
            $"Fixed-Capital Simulated P&L: ${stats.TotalPnl:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Synthetic Max Drawdown: ${stats.MaxDrawdownDollars:F2}"
        );

        Console.WriteLine(
            $"Synthetic Max Drawdown %: {stats.MaxDrawdownPercent:F2}%"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Max Consecutive Wins: {stats.MaxConsecutiveWins}"
        );

        Console.WriteLine(
            $"Max Consecutive Losses: {stats.MaxConsecutiveLosses}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Exit Reasons:"
        );

        foreach (
            var exit in stats.ExitReasons
                .OrderByDescending(
                    item =>
                        item.Value
                ))
        {
            Console.WriteLine(
                $"  {exit.Key}: {exit.Value}"
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Per Symbol:"
        );

        foreach (
            var group in trades
                .GroupBy(
                    trade =>
                        trade.Symbol
                )
                .OrderBy(
                    group =>
                        group.Key
                ))
        {
            var symbolStats =
                CalculateStatistics(
                    group.ToList()
                );

            Console.WriteLine(
                $"  {group.Key} | " +
                $"Trades {symbolStats.Trades} | " +
                $"Win {symbolStats.WinRate:F2}% | " +
                $"PF {FormatNumber(symbolStats.ProfitFactor)} | " +
                $"Avg R {symbolStats.AverageR:F2}"
            );
        }
    }

    private static BacktestStatistics
        CalculateStatistics(
            IReadOnlyCollection<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            return
                BacktestStatistics.Empty;
        }

        var ordered =
            trades
                .OrderBy(
                    trade =>
                        trade.ExitDate
                )
                .ThenBy(
                    trade =>
                        trade.Symbol
                )
                .ToList();

        var winningTrades =
            ordered
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losingTrades =
            ordered
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var flat =
            ordered.Count -
            winningTrades.Count -
            losingTrades.Count;

        var winRate =
            winningTrades.Count /
            (double)
                ordered.Count *
            100;

        var grossProfit =
            winningTrades
                .Sum(
                    trade =>
                        trade.Pnl
                );

        var grossLoss =
            Math.Abs(
                losingTrades
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
            ordered.Average(
                trade =>
                    trade.RMultiple
            );

        var averageWinR =
            winningTrades.Count > 0
                ? winningTrades.Average(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var averageLossR =
            losingTrades.Count > 0
                ? losingTrades.Average(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var winProbability =
            winningTrades.Count /
            (double)
                ordered.Count;

        var lossProbability =
            losingTrades.Count /
            (double)
                ordered.Count;

        var expectancyR =
            (
                winProbability *
                averageWinR
            ) +
            (
                lossProbability *
                averageLossR
            );

        var largestWinR =
            winningTrades.Count > 0
                ? winningTrades.Max(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var largestLossR =
            losingTrades.Count > 0
                ? losingTrades.Min(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var averageReturn =
            ordered.Average(
                trade =>
                    trade.ReturnPercent
            );

        var totalPnl =
            ordered.Sum(
                trade =>
                    trade.Pnl
            );

        var drawdown =
            CalculateSyntheticDrawdown(
                ordered
            );

        var streaks =
            CalculateStreaks(
                ordered
            );

        var exitReasons =
            ordered
                .GroupBy(
                    trade =>
                        trade.ExitReason
                )
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group.Count()
                );

        return new BacktestStatistics(
            ordered.Count,
            winningTrades.Count,
            losingTrades.Count,
            flat,
            winRate,
            profitFactor,
            averageR,
            expectancyR,
            averageWinR,
            averageLossR,
            largestWinR,
            largestLossR,
            averageReturn,
            totalPnl,
            drawdown.Dollars,
            drawdown.Percent,
            streaks.Wins,
            streaks.Losses,
            exitReasons
        );
    }

    private static (
        double Dollars,
        double Percent)
        CalculateSyntheticDrawdown(
            IReadOnlyList<BacktestTrade> trades)
    {
        var equity =
            ReferenceCapital;

        var peak =
            equity;

        var maxDrawdownDollars =
            0.0;

        var maxDrawdownPercent =
            0.0;

        foreach (var trade in trades)
        {
            equity +=
                trade.Pnl;

            if (equity > peak)
            {
                peak =
                    equity;
            }

            var drawdownDollars =
                peak -
                equity;

            var drawdownPercent =
                peak > 0
                    ? drawdownDollars /
                      peak *
                      100
                    : 0;

            if (drawdownDollars >
                maxDrawdownDollars)
            {
                maxDrawdownDollars =
                    drawdownDollars;
            }

            if (drawdownPercent >
                maxDrawdownPercent)
            {
                maxDrawdownPercent =
                    drawdownPercent;
            }
        }

        return (
            maxDrawdownDollars,
            maxDrawdownPercent
        );
    }

    private static (
        int Wins,
        int Losses)
        CalculateStreaks(
            IReadOnlyList<BacktestTrade> trades)
    {
        var currentWins =
            0;

        var currentLosses =
            0;

        var maxWins =
            0;

        var maxLosses =
            0;

        foreach (var trade in trades)
        {
            if (trade.Pnl > 0)
            {
                currentWins++;

                currentLosses =
                    0;

                maxWins =
                    Math.Max(
                        maxWins,
                        currentWins
                    );
            }
            else if (trade.Pnl < 0)
            {
                currentLosses++;

                currentWins =
                    0;

                maxLosses =
                    Math.Max(
                        maxLosses,
                        currentLosses
                    );
            }
            else
            {
                currentWins =
                    0;

                currentLosses =
                    0;
            }
        }

        return (
            maxWins,
            maxLosses
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

        return (
            (
                current -
                previous
            ) /
            previous
        ) *
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

public sealed record BacktestAnalysis(
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

public sealed record BacktestCandidate(
    bool IsValid,
    double EntryPrice,
    double StopLoss,
    double TakeProfit,
    double RiskPerShare,
    double RewardPerShare,
    double RiskRewardRatio,
    double Quantity,
    string RejectionReason)
{
    public static BacktestCandidate Invalid(
        string reason)
    {
        return new BacktestCandidate(
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            reason
        );
    }
}

public sealed record BacktestTrade(
    string Symbol,
    DateTime SignalDate,
    DateTime EntryDate,
    DateTime ExitDate,
    int SignalIndex,
    int EntryIndex,
    int ExitIndex,
    double EntryPrice,
    double StopLoss,
    double TakeProfit,
    double ExitPrice,
    double Quantity,
    double Pnl,
    double ReturnPercent,
    double RMultiple,
    string ExitReason
);

public sealed record BacktestStatistics(
    int Trades,
    int Wins,
    int Losses,
    int Flat,
    double WinRate,
    double ProfitFactor,
    double AverageR,
    double ExpectancyR,
    double AverageWinR,
    double AverageLossR,
    double LargestWinR,
    double LargestLossR,
    double AverageReturn,
    double TotalPnl,
    double MaxDrawdownDollars,
    double MaxDrawdownPercent,
    int MaxConsecutiveWins,
    int MaxConsecutiveLosses,
    IReadOnlyDictionary<string, int> ExitReasons)
{
    public static BacktestStatistics Empty =>
    new(
        0, // Trades
        0, // Wins
        0, // Losses
        0, // Flat
        0, // WinRate
        0, // ProfitFactor
        0, // AverageR
        0, // ExpectancyR
        0, // AverageWinR
        0, // AverageLossR
        0, // LargestWinR
        0, // LargestLossR
        0, // AverageReturn
        0, // TotalPnl
        0, // MaxDrawdownDollars
        0, // MaxDrawdownPercent
        0, // MaxConsecutiveWins
        0, // MaxConsecutiveLosses
        new Dictionary<string, int>()
    );
}