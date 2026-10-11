using Alpaca.Markets;

var keyId =
    Environment.GetEnvironmentVariable("ALPACA_KEY_ID");

var secretKey =
    Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");

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

    Console.WriteLine(
        $"Scanning {symbols.Length} stocks..."
    );

    Console.WriteLine();

    var snapshotRequest =
        new LatestMarketDataListRequest(
            symbols
        )
        {
            Feed = MarketDataFeed.Iex
        };

    var snapshots =
        await dataClient.ListSnapshotsAsync(
            snapshotRequest
        );

    Console.WriteLine(
        "=============================================="
    );

    Console.WriteLine(
        " MARKET SCANNER"
    );

    Console.WriteLine(
        "=============================================="
    );

    foreach (var symbol in symbols)
    {
        if (!snapshots.TryGetValue(
                symbol,
                out var snapshot))
        {
            Console.WriteLine(
                $"{symbol}: No data available."
            );

            continue;
        }

        var bar = snapshot.MinuteBar;

        if (bar is null)
        {
            Console.WriteLine(
                $"{symbol}: No minute bar available."
            );

            continue;
        }

        Console.WriteLine();

        Console.WriteLine(
            $"Symbol: {symbol}"
        );

        Console.WriteLine(
            $"Close:  ${bar.Close}"
        );

        Console.WriteLine(
            $"Volume: {bar.Volume}"
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
        " HISTORICAL DATA TEST"
    );

    Console.WriteLine(
        "=============================================="
    );

    const string analysisSymbol = "AAPL";

    var endDate =
        DateTime.UtcNow;

    var startDate =
        endDate.AddDays(-90);

    var historicalRequest =
        new HistoricalBarsRequest(
            analysisSymbol,
            startDate,
            endDate,
            BarTimeFrame.Day
        )
        {
            Feed = MarketDataFeed.Iex
        };

    var historicalPage =
        await dataClient.ListHistoricalBarsAsync(
            historicalRequest
        );

    var bars =
        historicalPage.Items
            .OrderBy(bar => bar.TimeUtc)
            .ToList();

    Console.WriteLine();

    Console.WriteLine(
        $"Symbol: {analysisSymbol}"
    );

    Console.WriteLine(
        $"Daily bars loaded: {bars.Count}"
    );

    if (bars.Count > 0)
    {
        var firstBar = bars.First();
        var lastBar = bars.Last();

        Console.WriteLine(
            $"First date: {firstBar.TimeUtc:yyyy-MM-dd}"
        );

        Console.WriteLine(
            $"First close: ${firstBar.Close}"
        );

        Console.WriteLine(
            $"Latest date: {lastBar.TimeUtc:yyyy-MM-dd}"
        );

        Console.WriteLine(
            $"Latest close: ${lastBar.Close}"
        );
    }
    else
    {
        Console.WriteLine(
            "No historical bars returned."
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