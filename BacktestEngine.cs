using Alpaca.Markets;

public sealed class BacktestEngine
{
    private readonly IAlpacaDataClient _dataClient;

    private const double ReferenceCapital = 20.00;
    private const double RiskPerTradePercent = 0.50;
    private const double MaxPositionPercent = 25.00;
    private const int MaxHoldingDays = 10;

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
            "==============================================");

        Console.WriteLine(
            " BACKTESTING ENGINE V1");

        Console.WriteLine(
            "==============================================");

        Console.WriteLine(
            $"Reference Capital: ${ReferenceCapital:F2}");

        Console.WriteLine(
            $"Risk Per Trade: {RiskPerTradePercent:F2}%");

        Console.WriteLine(
            $"Max Position: {MaxPositionPercent:F2}%");

        Console.WriteLine(
            $"Max Holding Period: {MaxHoldingDays} trading days");

        Console.WriteLine();

        var allTrades =
            new List<BacktestTrade>();

        foreach (var symbol in symbols)
        {
            Console.WriteLine(
                $"Backtesting {symbol}...");

            var trades =
                await BacktestSymbolAsync(
                    symbol);

            allTrades.AddRange(
                trades);

            PrintSymbolSummary(
                symbol,
                trades);
        }

        PrintOverallSummary(
            allTrades);
    }

    private async Task<List<BacktestTrade>>
        BacktestSymbolAsync(
            string symbol)
    {
        var endDate =
            DateTime.UtcNow;

        var startDate =
            endDate.AddYears(-2);

        var request =
            new HistoricalBarsRequest(
                symbol,
                startDate,
                endDate,
                BarTimeFrame.Day)
            {
                Feed = MarketDataFeed.Iex
            };

        var page =
            await _dataClient
                .ListHistoricalBarsAsync(
                    request);

        var bars =
            page.Items
                .OrderBy(
                    bar => bar.TimeUtc)
                .ToList();

        var trades =
            new List<BacktestTrade>();

        if (bars.Count < 80)
        {
            Console.WriteLine(
                $"{symbol}: Not enough history.");

            return trades;
        }

        var index = 50;

        while (index < bars.Count - 1)
        {
            var analysis =
                AnalyzeAt(
                    symbol,
                    bars,
                    index);

            if (analysis is null ||
                analysis.Decision != "CONSIDER")
            {
                index++;
                continue;
            }

            var entryIndex =
                index + 1;

            var entryBar =
                bars[entryIndex];

            var entryPrice =
                (double)entryBar.Open;

            var candidate =
                BuildCandidate(
                    analysis,
                    entryPrice);

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
                    candidate);

            trades.Add(
                trade);

            // Prevent overlapping trades
            // for the same symbol.
            index =
                trade.ExitIndex + 1;
        }

        return trades;
    }

    private static BacktestAnalysis? AnalyzeAt(
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
                .Take(index + 1)
                .Select(
                    bar =>
                        (double)bar.Close)
                .ToList();

        var volumes =
            bars
                .Take(index + 1)
                .Select(
                    bar =>
                        (double)bar.Volume)
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

        var momentum20 =
            CalculateMomentum(
                closes,
                20);

        var volatility =
            CalculateAnnualizedVolatility(
                closes,
                20);

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
                    (currentPrice -
                     highestClose20) /
                    highestClose20
                  ) * 100
                : 0;

        var atr =
            CalculateAtr(
                bars,
                index,
                14);

        var support10 =
            bars
                .Skip(
                    Math.Max(
                        0,
                        index - 9))
                .Take(
                    Math.Min(
                        10,
                        index + 1))
                .Min(
                    bar =>
                        (double)bar.Low);

        var resistance20 =
            bars
                .Skip(
                    Math.Max(
                        0,
                        index - 19))
                .Take(
                    Math.Min(
                        20,
                        index + 1))
                .Max(
                    bar =>
                        (double)bar.High);

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
            momentum20 switch
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
                100);

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
                    volatilityRisk * 0.50 +
                    drawdownRisk * 0.30 +
                    trendRisk * 0.20),
                0,
                100);

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
            bars[index].TimeUtc,
            currentPrice,
            trend,
            momentum20,
            volatility,
            volumeRatio,
            drawdown,
            atr,
            support10,
            resistance20,
            opportunityScore,
            riskScore,
            decision);
    }

    private static BacktestCandidate BuildCandidate(
        BacktestAnalysis analysis,
        double entryPrice)
    {
        if (analysis.Atr <= 0)
        {
            return BacktestCandidate.Invalid(
                "Invalid ATR.");
        }

        var atrStop =
            entryPrice -
            analysis.Atr * 2.0;

        var supportStop =
            analysis.Support10 -
            analysis.Atr * 0.25;

        var stopLoss =
            Math.Max(
                atrStop,
                supportStop);

        if (stopLoss <= 0 ||
            stopLoss >= entryPrice)
        {
            return BacktestCandidate.Invalid(
                "Invalid stop loss.");
        }

        var takeProfit =
            analysis.Resistance20 +
            analysis.Atr * 1.50;

        if (takeProfit <= entryPrice)
        {
            return BacktestCandidate.Invalid(
                "Target is below entry.");
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

        if (analysis.Trend != "Bullish")
        {
            return BacktestCandidate.Invalid(
                "Trend not bullish.");
        }

        if (analysis.Momentum <= 0)
        {
            return BacktestCandidate.Invalid(
                "Momentum not positive.");
        }

        if (analysis.RiskScore > 35)
        {
            return BacktestCandidate.Invalid(
                "Risk score too high.");
        }

        if (riskReward < 1.80)
        {
            return BacktestCandidate.Invalid(
                "Risk/reward below 1.80.");
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
                quantityByCapital);

        quantity =
            Math.Floor(
                quantity * 10000) /
            10000;

        if (quantity < 0.001)
        {
            return BacktestCandidate.Invalid(
                "Position too small.");
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
            string.Empty);
    }

    private static BacktestTrade SimulateTrade(
        string symbol,
        IReadOnlyList<IBar> bars,
        int signalIndex,
        int entryIndex,
        BacktestCandidate candidate)
    {
        var exitIndex =
            Math.Min(
                entryIndex + MaxHoldingDays - 1,
                bars.Count - 1);

        var exitPrice =
            (double)bars[exitIndex].Close;

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
                (double)bar.Open;

            var high =
                (double)bar.High;

            var low =
                (double)bar.Low;

            // Gap below stop.
            if (open <= candidate.StopLoss)
            {
                exitPrice =
                    open;

                exitReason =
                    "GAP_STOP";

                exitIndex =
                    index;

                break;
            }

            // Gap above target.
            if (open >= candidate.TakeProfit)
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

            // With daily candles we cannot know
            // which happened first.
            // Assume stop first to avoid optimistic bias.
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
                ? pnl / riskDollars
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
            bars[signalIndex].TimeUtc,
            bars[entryIndex].TimeUtc,
            bars[exitIndex].TimeUtc,
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
            exitReason);
    }

    private static void PrintSymbolSummary(
        string symbol,
        IReadOnlyCollection<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            Console.WriteLine(
                $"  {symbol}: 0 trades");

            return;
        }

        var wins =
            trades.Count(
                trade =>
                    trade.Pnl > 0);

        var losses =
            trades.Count(
                trade =>
                    trade.Pnl < 0);

        var winRate =
            trades.Count > 0
                ? wins /
                  (double)trades.Count *
                  100
                : 0;

        var grossProfit =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0)
                .Sum(
                    trade =>
                        trade.Pnl);

        var grossLoss =
            Math.Abs(
                trades
                    .Where(
                        trade =>
                            trade.Pnl < 0)
                    .Sum(
                        trade =>
                            trade.Pnl));

        var profitFactor =
            grossLoss > 0
                ? grossProfit /
                  grossLoss
                : grossProfit > 0
                    ? double.PositiveInfinity
                    : 0;

        var averageR =
            trades.Average(
                trade =>
                    trade.RMultiple);

        Console.WriteLine(
            $"  Trades: {trades.Count}");

        Console.WriteLine(
            $"  Wins: {wins}");

        Console.WriteLine(
            $"  Losses: {losses}");

        Console.WriteLine(
            $"  Win Rate: {winRate:F2}%");

        Console.WriteLine(
            $"  Profit Factor: {FormatNumber(profitFactor)}");

        Console.WriteLine(
            $"  Average R: {averageR:F2}");

        Console.WriteLine();
    }

    private static void PrintOverallSummary(
        IReadOnlyCollection<BacktestTrade> trades)
    {
        Console.WriteLine();
        Console.WriteLine(
            "==============================================");

        Console.WriteLine(
            " OVERALL BACKTEST RESULTS");

        Console.WriteLine(
            "==============================================");

        if (trades.Count == 0)
        {
            Console.WriteLine(
                "No qualifying historical trades.");

            return;
        }

        var wins =
            trades.Count(
                trade =>
                    trade.Pnl > 0);

        var losses =
            trades.Count(
                trade =>
                    trade.Pnl < 0);

        var flat =
            trades.Count -
            wins -
            losses;

        var winRate =
            wins /
            (double)trades.Count *
            100;

        var grossProfit =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0)
                .Sum(
                    trade =>
                        trade.Pnl);

        var grossLoss =
            Math.Abs(
                trades
                    .Where(
                        trade =>
                            trade.Pnl < 0)
                    .Sum(
                        trade =>
                            trade.Pnl));

        var profitFactor =
            grossLoss > 0
                ? grossProfit /
                  grossLoss
                : grossProfit > 0
                    ? double.PositiveInfinity
                    : 0;

        var averageR =
            trades.Average(
                trade =>
                    trade.RMultiple);

        var averageReturn =
            trades.Average(
                trade =>
                    trade.ReturnPercent);

        var totalSimulatedPnl =
            trades.Sum(
                trade =>
                    trade.Pnl);

        Console.WriteLine(
            $"Trades: {trades.Count}");

        Console.WriteLine(
            $"Winning Trades: {wins}");

        Console.WriteLine(
            $"Losing Trades: {losses}");

        Console.WriteLine(
            $"Flat Trades: {flat}");

        Console.WriteLine();

        Console.WriteLine(
            $"Win Rate: {winRate:F2}%");

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(profitFactor)}");

        Console.WriteLine(
            $"Average R: {averageR:F2}");

        Console.WriteLine(
            $"Average Position Return: {averageReturn:F2}%");

        Console.WriteLine(
            $"Fixed-Capital Simulated P&L: ${totalSimulatedPnl:F2}");

        Console.WriteLine();

        Console.WriteLine(
            "Important: this is not a real portfolio return.");

        Console.WriteLine(
            "Each trade uses the same $20 reference-capital sizing.");

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED.");
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
            closes[^(periods + 1)];

        if (previous == 0)
        {
            return 0;
        }

        return (
            (current - previous) /
            previous
        ) * 100;
    }

    private static double CalculateAnnualizedVolatility(
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
                    periods + 1)
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
                (current - previous) /
                previous);
        }

        if (returns.Count < 2)
        {
            return 0;
        }

        var average =
            returns.Average();

        var variance =
            returns.Sum(
                value =>
                    Math.Pow(
                        value - average,
                        2)) /
            (returns.Count - 1);

        return
            Math.Sqrt(
                variance) *
            Math.Sqrt(
                252) *
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
                1);

        for (
            var index = startIndex;
            index <= endIndex;
            index++)
        {
            var high =
                (double)bars[index].High;

            var low =
                (double)bars[index].Low;

            var previousClose =
                (double)bars[index - 1].Close;

            var trueRange =
                Math.Max(
                    high - low,
                    Math.Max(
                        Math.Abs(
                            high -
                            previousClose),
                        Math.Abs(
                            low -
                            previousClose)));

            trueRanges.Add(
                trueRange);
        }

        return trueRanges.Count > 0
            ? trueRanges.Average()
            : 0;
    }

    private static string FormatNumber(
        double value)
    {
        return double.IsPositiveInfinity(
            value)
            ? "∞"
            : value.ToString("F2");
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
            reason);
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