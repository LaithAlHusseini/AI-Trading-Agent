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
        " MULTI-STOCK ANALYSIS ENGINE"
    );

    Console.WriteLine(
        "=============================================="
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

    Console.WriteLine();
    Console.WriteLine(
        "=============================================="
    );

    Console.WriteLine(
        " RANKING"
    );

    Console.WriteLine(
        "=============================================="
    );

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
            $"Volume Ratio: {item.VolumeRatio:F2}x"
        );

        Console.WriteLine(
            $"Drawdown: {item.Drawdown:F2}%"
        );

        Console.WriteLine(
            $"Opportunity Score: {item.OpportunityScore}/100"
        );

        Console.WriteLine(
            $"Risk Score: {item.RiskScore}/100"
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
    int OpportunityScore,
    int RiskScore,
    int NetScore,
    string Decision
);