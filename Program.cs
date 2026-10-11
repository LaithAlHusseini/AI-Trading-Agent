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

    const string symbol = "AAPL";

    Console.WriteLine(
        $"Reading market data for {symbol}..."
    );

    var request =
        new LatestMarketDataRequest(symbol)
        {
            Feed = MarketDataFeed.Iex
        };

    var snapshot =
        await dataClient.GetSnapshotAsync(
            request
        );

    var bar = snapshot.MinuteBar;

    Console.WriteLine();
    Console.WriteLine($"Symbol: {symbol}");
    Console.WriteLine($"Open:   ${bar.Open}");
    Console.WriteLine($"High:   ${bar.High}");
    Console.WriteLine($"Low:    ${bar.Low}");
    Console.WriteLine($"Close:  ${bar.Close}");
    Console.WriteLine($"Volume: {bar.Volume}");
    Console.WriteLine($"Time:   {bar.TimeUtc:O}");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Request failed:");
    Console.WriteLine(ex.Message);
}