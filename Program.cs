using Alpaca.Markets;

using Microsoft.Extensions.Configuration;



var configuration =

    new ConfigurationBuilder()

        .AddUserSecrets<Program>()

        .Build();



var keyId =

    configuration["ALPACA_KEY_ID"];



var secretKey =

    configuration["ALPACA_SECRET_KEY"];



if (string.IsNullOrWhiteSpace(keyId) ||

    string.IsNullOrWhiteSpace(secretKey))

{

    Console.WriteLine("Alpaca API keys are missing.");

    return;

}



var credentials =

    new SecretKey(

        keyId,

        secretKey

    );



var tradingClient =

    Environments.Paper.GetAlpacaTradingClient(

        credentials

    );



var dataClient =

    Environments.Paper.GetAlpacaDataClient(

        credentials

    );



try

{

    var account =

        await tradingClient.GetAccountAsync();



    Console.WriteLine(

        "Connected to Alpaca Paper Trading."

    );



    Console.WriteLine(

        $"Account Status: {account.Status}"

    );



    Console.WriteLine();



    const double strategyCapital = 20.00;



    // Maximum amount we are willing to lose

    // on one trade.

    const double riskPerTradePercent = 0.50;



    // Maximum percentage of total capital

    // allowed in one position.

    const double maxPositionPercent = 25.00;



    var symbols = new[]

    {

        "AAPL",

        "MSFT",

        "NVDA",

        "AMZN",

        "GOOGL",

        "META",

        "TSLA"

    };



    var analyses =

        new List<StockAnalysis>();



    Console.WriteLine(

        "=============================================="

    );



    Console.WriteLine(

        " AI TRADING ANALYSIS ENGINE"

    );



    Console.WriteLine(

        "=============================================="

    );



    Console.WriteLine(

        $"Strategy Capital: ${strategyCapital:F2}"

    );



    Console.WriteLine(

        $"Risk Per Trade: {riskPerTradePercent:F2}%"

    );



    Console.WriteLine(

        $"Max Position: {maxPositionPercent:F2}%"

    );



    Console.WriteLine();



    foreach (var symbol in symbols)

    {

        Console.WriteLine(

            $"Analyzing {symbol}..."

        );



        var analysis =

            await AnalyzeStockAsync(

                dataClient,

                symbol

            );



        if (analysis is not null)

        {

            analyses.Add(

                analysis

            );

        }

    }



    var ranked =

        analyses

            .OrderByDescending(

                item => item.NetScore

            )

            .ThenByDescending(

                item => item.OpportunityScore

            )

            .ThenBy(

                item => item.RiskScore

            )

            .ToList();



    Console.WriteLine();



    Console.WriteLine(

        "=============================================="

    );



    Console.WriteLine(

        " ANALYSIS RANKING"

    );



    Console.WriteLine(

        "=============================================="

    );



    for (

        var index = 0;

        index < ranked.Count;

        index++

    )

    {

        var item =

            ranked[index];



        Console.WriteLine();



        Console.WriteLine(

            $"#{index + 1} {item.Symbol}"

        );



        Console.WriteLine(

            $"Price: ${item.Price:F2}"

        );



        Console.WriteLine(

            $"Trend: {item.Trend}"

        );



        Console.WriteLine(

            $"Momentum: {item.Momentum:F2}%"

        );



        Console.WriteLine(

            $"Volatility: {item.Volatility:F2}%"

        );



        Console.WriteLine(

            $"Volume Strength: {item.VolumeStrength}"

        );



        Console.WriteLine(

            $"Opportunity: {item.OpportunityScore}/100"

        );



        Console.WriteLine(

            $"Risk: {item.RiskScore}/100"

        );



        Console.WriteLine(

            $"Net Score: {item.NetScore}"

        );



        Console.WriteLine(

            $"Decision: {item.Decision}"

        );



        Console.WriteLine(

            "----------------------------------------------"

        );

    }



    Console.WriteLine();



    Console.WriteLine(

        "=============================================="

    );



    Console.WriteLine(

        " TRADE CANDIDATE ENGINE"

    );



    Console.WriteLine(

        "=============================================="

    );



    var tradeCandidates =

        new List<TradeCandidate>();



    foreach (

        var analysis in ranked

    )

    {

        var candidate =

            BuildTradeCandidate(

                analysis,

                strategyCapital,

                riskPerTradePercent,

                maxPositionPercent

            );



        tradeCandidates.Add(

            candidate

        );



        Console.WriteLine();



        Console.WriteLine(

            $"[{candidate.Symbol}]"

        );



        Console.WriteLine(

            $"Analysis Decision: {analysis.Decision}"

        );



        Console.WriteLine();



        Console.WriteLine(

            $"Entry:        ${candidate.EntryPrice:F2}"

        );



        Console.WriteLine(

            $"ATR(14):      ${candidate.Atr:F2}"

        );



        Console.WriteLine(

            $"Stop Loss:    ${candidate.StopLoss:F2}"

        );



        Console.WriteLine(

            $"Take Profit:  ${candidate.TakeProfit:F2}"

        );



        Console.WriteLine();



        Console.WriteLine(

            $"Risk/Share:   ${candidate.RiskPerShare:F2}"

        );



        Console.WriteLine(

            $"Reward/Share: ${candidate.RewardPerShare:F2}"

        );



        Console.WriteLine(

            $"Risk/Reward:  1:{candidate.RiskRewardRatio:F2}"

        );



        Console.WriteLine();



        Console.WriteLine(

            $"Max Risk:     ${candidate.MaximumRiskDollars:F2}"

        );



        Console.WriteLine(

            $"Position:     {candidate.Quantity:F4} shares"

        );



        Console.WriteLine(

            $"Position $:   ${candidate.PositionValue:F2}"

        );



        Console.WriteLine(

            $"Max Loss:     ${candidate.ExpectedMaximumLoss:F2}"

        );



        Console.WriteLine();



        Console.WriteLine(

            $"FINAL: {candidate.FinalDecision}"

        );



        Console.WriteLine(

            $"Reason: {candidate.Reason}"

        );



        Console.WriteLine(

            "----------------------------------------------"

        );

    }



    var backtestEngine =

        new BacktestEngine(

            dataClient

    );



    await backtestEngine.RunAsync(

        symbols

    );



    var portfolioBacktestEngine =

        new PortfolioBacktestEngine(

            dataClient

        );



    var portfolioBacktestResult =

        await portfolioBacktestEngine.RunAsync(

         symbols

        );



    var diagnosticsEngine =

        new DiagnosticsEngine();



    diagnosticsEngine.Run(

     portfolioBacktestResult.Development,

     portfolioBacktestResult.OutOfSample,

     portfolioBacktestResult.Overall

    );



    var strategyExperimentEngine =

        new StrategyExperimentEngine(

         portfolioBacktestEngine

     );



    await strategyExperimentEngine.RunAsync(

    symbols

    );



    var walkForwardValidationEngine =

        new WalkForwardValidationEngine(

            portfolioBacktestEngine

        );



    await walkForwardValidationEngine.RunAsync(

        symbols

    );



    var marketRegimeValidationEngine =

        new MarketRegimeValidationEngine(

            portfolioBacktestEngine

        );



    var marketRegimeStudy =
        await marketRegimeValidationEngine.RunAsync(
            symbols
        );

    var currentRegime =
        marketRegimeStudy.BenchmarkRegimes
            .Where(
                snapshot =>
                    snapshot.Date <
                    DateTime.UtcNow.Date
            )
        .OrderByDescending(
            snapshot =>
                snapshot.Date
        )
        .FirstOrDefault();



    var regimeAwareWalkForwardEngine =

        new RegimeAwareWalkForwardEngine(

         portfolioBacktestEngine

        );



    await regimeAwareWalkForwardEngine.RunAsync(

        symbols

    );



    var paperTradingEngine =

        new PaperTradingEngine(

            tradingClient

        );



    var preflightResult =
        await paperTradingEngine.RunPreflightAsync();

    var frozenForwardPolicy =
        FrozenForwardPolicy.V9C;

    frozenForwardPolicy.Print();

    var forwardSignalEngine =
        new ForwardSignalEngine(
            dataClient
        );

    var forwardSignalStudy =
        await forwardSignalEngine.RunAsync(
            symbols,
            preflightResult,
            frozenForwardPolicy
        );

    var forwardSignalStore =
        new ForwardSignalStore();

    if (!preflightResult.MarketOpen)
    {
        await forwardSignalStore.SaveAsync(
            forwardSignalStudy
        );
    }
    else
    {
        var storedForwardSignals =
            await forwardSignalStore.LoadAsync();

        forwardSignalStore.PrintLoadSummary(
            storedForwardSignals
        );
    }    

    var forwardExecutionEngine =
        new ForwardExecutionEngine(
            dataClient,
            forwardSignalStore
        );

    var forwardExecutionPlan =
        await forwardExecutionEngine.RunAsync(
            preflightResult,
            frozenForwardPolicy
        );



    Console.WriteLine();



    Console.WriteLine(

        "NO ORDERS WERE SUBMITTED."

    );



    Console.WriteLine(

        "This run only analyzed and simulated trade candidates."

    );

}

catch (Exception ex)

{

    Console.WriteLine();



    Console.WriteLine(

        "Request failed:"

    );



    Console.WriteLine(

        ex.Message

    );

}



static async Task<StockAnalysis?> AnalyzeStockAsync(

    IAlpacaDataClient dataClient,

    string symbol

)

{

    try

    {

        var endDate =

            DateTime.UtcNow;



        var startDate =

            endDate.AddDays(-120);



        var request =

            new HistoricalBarsRequest(

                symbol,

                startDate,

                endDate,

                BarTimeFrame.Day

            )

            {

                Feed = MarketDataFeed.Iex

            };



        var page =

            await dataClient.ListHistoricalBarsAsync(

                request

            );



        var bars =

            page.Items

                .OrderBy(

                    bar => bar.TimeUtc

                )

                .ToList();



        if (bars.Count < 50)

        {

            Console.WriteLine(

                $"{symbol}: Not enough historical data."

            );



            return null;

        }



        var closes =

            bars

                .Select(

                    bar => (double)bar.Close

                )

                .ToList();



        var volumes =

            bars

                .Select(

                    bar => (double)bar.Volume

                )

                .ToList();



        var currentPrice =

            closes[^1];



        var sma20 =

            AverageLast(

                closes,

                20

            );



        var sma50 =

            AverageLast(

                closes,

                50

            );



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

            AverageLast(

                volumes,

                5

            );



        var averageVolume20 =

            AverageLast(

                volumes,

                20

            );



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

                14

            );



        var support10 =

            bars

                .TakeLast(10)

                .Min(

                    bar =>

                        (double)bar.Low

                );



        var resistance20 =

            bars

                .TakeLast(20)

                .Max(

                    bar =>

                        (double)bar.High

                );



        var trend =

            currentPrice > sma20 &&

            sma20 > sma50

                ? "Bullish"



                : currentPrice < sma20 &&

                  sma20 < sma50

                    ? "Bearish"



                    : "Neutral";



        var volumeStrength =

            volumeRatio >= 1.20

                ? "Strong"



                : volumeRatio >= 0.80

                    ? "Normal"



                    : "Weak";



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

                    volatilityRisk * 0.50 +

                    drawdownRisk * 0.30 +

                    trendRisk * 0.20

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



        var netScore =

            opportunityScore -

            riskScore;



        return new StockAnalysis(

            symbol,

            currentPrice,

            sma20,

            sma50,

            trend,

            momentum,

            volatility,

            volumeStrength,

            volumeRatio,

            drawdown,

            atr,

            support10,

            resistance20,

            opportunityScore,

            riskScore,

            netScore,

            decision

        );

    }

    catch (Exception ex)

    {

        Console.WriteLine(

            $"{symbol}: Analysis failed - {ex.Message}"

        );



        return null;

    }

}



static TradeCandidate BuildTradeCandidate(

    StockAnalysis analysis,

    double strategyCapital,

    double riskPerTradePercent,

    double maxPositionPercent

)

{

    var entry =

        analysis.Price;



    var atr =

        analysis.Atr;



    if (atr <= 0)

    {

        return RejectedCandidate(

            analysis,

            "ATR is unavailable or invalid."

        );

    }



    // Volatility-based protective stop.

    var atrStop =

        entry -

        (atr * 2.0);



    // Technical support-based stop.

    var supportStop =

        analysis.Support10 -

        (atr * 0.25);



    // Use the tighter valid stop.

    var stopLoss =

        Math.Max(

            atrStop,

            supportStop

        );



    if (stopLoss <= 0 ||

        stopLoss >= entry)

    {

        return RejectedCandidate(

            analysis,

            "No valid stop-loss level could be calculated."

        );

    }



    // Target above recent resistance.

    var takeProfit =

        analysis.Resistance20 +

        (atr * 1.50);



    if (takeProfit <= entry)

    {

        return RejectedCandidate(

            analysis,

            "Calculated target is not above the entry price."

        );

    }



    var riskPerShare =

        entry -

        stopLoss;



    var rewardPerShare =

        takeProfit -

        entry;



    var riskRewardRatio =

        rewardPerShare /

        riskPerShare;



    var maximumRiskDollars =

        strategyCapital *

        (

            riskPerTradePercent /

            100.0

        );



    var maximumPositionDollars =

        strategyCapital *

        (

            maxPositionPercent /

            100.0

        );



    var quantityByRisk =

        maximumRiskDollars /

        riskPerShare;



    var quantityByCapital =

        maximumPositionDollars /

        entry;



    var quantity =

        Math.Min(

            quantityByRisk,

            quantityByCapital

        );



    quantity =

        Math.Floor(

            quantity * 10000

        ) /

        10000;



    var positionValue =

        quantity *

        entry;



    var expectedMaximumLoss =

        quantity *

        riskPerShare;



    var finalDecision =

        "PAPER TRADE CANDIDATE";



    var reason =

        "Passed analysis and risk-management rules.";



    if (analysis.Decision != "CONSIDER")

    {

        finalDecision =

            "REJECTED";



        reason =

            "Analysis engine did not classify the stock as CONSIDER.";

    }

    else if (analysis.Trend != "Bullish")

    {

        finalDecision =

            "REJECTED";



        reason =

            "Long candidate requires a bullish trend.";

    }

    else if (analysis.Momentum <= 0)

    {

        finalDecision =

            "REJECTED";



        reason =

            "Momentum is not positive.";

    }

    else if (analysis.RiskScore > 35)

    {

        finalDecision =

            "REJECTED";



        reason =

            "Risk score exceeds the maximum allowed threshold.";

    }

    else if (riskRewardRatio < 1.80)

    {

        finalDecision =

            "REJECTED";



        reason =

            $"Risk/reward ratio {riskRewardRatio:F2} is below 1.80.";

    }

    else if (quantity < 0.001)

    {

        finalDecision =

            "REJECTED";



        reason =

            "Position size is too small for this strategy capital.";

    }



    return new TradeCandidate(

        analysis.Symbol,

        entry,

        atr,

        stopLoss,

        takeProfit,

        riskPerShare,

        rewardPerShare,

        riskRewardRatio,

        maximumRiskDollars,

        quantity,

        positionValue,

        expectedMaximumLoss,

        finalDecision,

        reason

    );

}



static TradeCandidate RejectedCandidate(

    StockAnalysis analysis,

    string reason

)

{

    return new TradeCandidate(

        analysis.Symbol,

        analysis.Price,

        analysis.Atr,

        0,

        0,

        0,

        0,

        0,

        0,

        0,

        0,

        0,

        "REJECTED",

        reason

    );

}



static double AverageLast(

    List<double> values,

    int count

)

{

    return values

        .TakeLast(count)

        .Average();

}



static double CalculateMomentum(

    List<double> closes,

    int periods

)

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



static double CalculateAnnualizedVolatility(

    List<double> closes,

    int periods

)

{

    if (closes.Count <= periods)

    {

        return 0;

    }



    var recent =

        closes

            .TakeLast(periods + 1)

            .ToList();



    var returns =

        new List<double>();



    for (

        var index = 1;

        index < recent.Count;

        index++

    )

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

                        value - average,

                        2

                    )

            ) /

        (returns.Count - 1);



    var dailyVolatility =

        Math.Sqrt(

            variance

        );



    return

        dailyVolatility *

        Math.Sqrt(252) *

        100;

}



static double CalculateAtr(

    IReadOnlyList<IBar> bars,

    int periods

)

{

    if (bars.Count <= periods)

    {

        return 0;

    }



    var trueRanges =

        new List<double>();



    for (

        var index = 1;

        index < bars.Count;

        index++

    )

    {

        var currentHigh =

            (double)bars[index].High;



        var currentLow =

            (double)bars[index].Low;



        var previousClose =

            (double)bars[index - 1].Close;



        var highLow =

            currentHigh -

            currentLow;



        var highPreviousClose =

            Math.Abs(

                currentHigh -

                previousClose

            );



        var lowPreviousClose =

            Math.Abs(

                currentLow -

                previousClose

            );



        var trueRange =

            Math.Max(

                highLow,

                Math.Max(

                    highPreviousClose,

                    lowPreviousClose

                )

            );



        trueRanges.Add(

            trueRange

        );

    }



    return trueRanges

        .TakeLast(periods)

        .Average();

}



record StockAnalysis(

    string Symbol,

    double Price,

    double Sma20,

    double Sma50,

    string Trend,

    double Momentum,

    double Volatility,

    string VolumeStrength,

    double VolumeRatio,

    double Drawdown,

    double Atr,

    double Support10,

    double Resistance20,

    int OpportunityScore,

    int RiskScore,

    int NetScore,

    string Decision

);



record TradeCandidate(

    string Symbol,

    double EntryPrice,

    double Atr,

    double StopLoss,

    double TakeProfit,

    double RiskPerShare,

    double RewardPerShare,

    double RiskRewardRatio,

    double MaximumRiskDollars,

    double Quantity,

    double PositionValue,

    double ExpectedMaximumLoss,

    string FinalDecision,

    string Reason

);