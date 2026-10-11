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

var credentials = new SecretKey(
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

    var request =
        new LatestMarketDataListRequest(symbols)
        {
            Feed = MarketDataFeed.Iex
        };

    var snapshots =
        await dataClient.ListSnapshotsAsync(
            request
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
            $"Open:   ${bar.Open}"
        );

        Console.WriteLine(
            $"High:   ${bar.High}"
        );

        Console.WriteLine(
            $"Low:    ${bar.Low}"
        );

        Console.WriteLine(
            $"Close:  ${bar.Close}"
        );

        Console.WriteLine(
            $"Volume: {bar.Volume}"
        );

        Console.WriteLine(
            $"Time:   {bar.TimeUtc:O}"
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