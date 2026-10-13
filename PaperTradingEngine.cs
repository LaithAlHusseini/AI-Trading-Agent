using Alpaca.Markets;

public sealed class PaperTradingEngine
{
    private readonly IAlpacaTradingClient _tradingClient;

    private const decimal StrategyCapital = 20.00M;

    // Hard safety lock for V9A.
    // This phase performs account and execution-environment checks only.
    // There is intentionally no order-submission path yet.
    private const bool OrderSubmissionEnabled = false;

    public PaperTradingEngine(
        IAlpacaTradingClient tradingClient)
    {
        _tradingClient =
            tradingClient;
    }

    public async Task<PaperTradingPreflightResult>
        RunPreflightAsync()
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " PAPER TRADING ENGINE V9A"
        );

        Console.WriteLine(
            " PRE-TRADE SAFETY CHECK"
        );

        Console.WriteLine(
            "=============================================="
        );

        var account =
            await _tradingClient
                .GetAccountAsync();

        var clock =
            await _tradingClient
                .GetClockAsync();

        var positions =
            await _tradingClient
                .ListPositionsAsync();

        var openOrdersRequest =
            new ListOrdersRequest
            {
                OrderStatusFilter =
                    OrderStatusFilter.Open,

                LimitOrderNumber =
                    500
            };

        var openOrders =
            await _tradingClient
                .ListOrdersAsync(
                    openOrdersRequest
                );

        var accountStatusValid =
            account.Status ==
            AccountStatus.Active ||
            account.Status ==
            AccountStatus.PaperOnly;

        var accountNotBlocked =
            !account.IsAccountBlocked &&
            !account.IsTradingBlocked &&
            !account.TradeSuspendedByUser;

        var buyingPower =
            account.BuyingPower ??
            0M;

        var sufficientBuyingPower =
            buyingPower >=
            StrategyCapital;

        var cleanPositions =
            positions.Count ==
            0;

        var cleanOrders =
            openOrders.Count ==
            0;

        var safetyChecksPassed =
            accountStatusValid &&
            accountNotBlocked &&
            sufficientBuyingPower &&
            cleanPositions &&
            cleanOrders;

        var canSubmitOrdersNow =
            safetyChecksPassed &&
            clock.IsOpen &&
            OrderSubmissionEnabled;

        Console.WriteLine(
            $"Account Status: {account.Status}"
        );

        Console.WriteLine(
            $"Account Blocked: {account.IsAccountBlocked}"
        );

        Console.WriteLine(
            $"Trading Blocked: {account.IsTradingBlocked}"
        );

        Console.WriteLine(
            $"Trade Suspended By User: " +
            $"{account.TradeSuspendedByUser}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Account Equity: " +
            $"${(account.Equity ?? 0M):F2}"
        );

        Console.WriteLine(
            $"Buying Power: " +
            $"${buyingPower:F2}"
        );

        Console.WriteLine(
            $"V9 Strategy Capital Cap: " +
            $"${StrategyCapital:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Market Open: {clock.IsOpen}"
        );

        Console.WriteLine(
            $"Broker Time (UTC): " +
            $"{clock.TimestampUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine(
            $"Next Open (UTC): " +
            $"{clock.NextOpenUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine(
            $"Next Close (UTC): " +
            $"{clock.NextCloseUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Open Positions: {positions.Count}"
        );

        Console.WriteLine(
            $"Open Orders: {openOrders.Count}"
        );

        Console.WriteLine();

        PrintCheck(
            "Account status valid",
            accountStatusValid
        );

        PrintCheck(
            "Account trading not blocked",
            accountNotBlocked
        );

        PrintCheck(
            "Buying power >= strategy capital",
            sufficientBuyingPower
        );

        PrintCheck(
            "No pre-existing open positions",
            cleanPositions
        );

        PrintCheck(
            "No pre-existing open orders",
            cleanOrders
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Safety Checks Passed: " +
            $"{safetyChecksPassed}"
        );

        Console.WriteLine(
            $"Order Submission Enabled: " +
            $"{OrderSubmissionEnabled}"
        );

        Console.WriteLine(
            $"Can Submit Orders Now: " +
            $"{canSubmitOrdersNow}"
        );

        Console.WriteLine();

        if (!safetyChecksPassed)
        {
            Console.WriteLine(
                "V9A STATUS: BLOCKED"
            );

            Console.WriteLine(
                "Paper execution cannot proceed until all " +
                "required safety checks pass."
            );
        }
        else
        {
            Console.WriteLine(
                "V9A STATUS: PREFLIGHT PASSED"
            );

            Console.WriteLine(
                "Account and execution environment are ready " +
                "for the next dry-run phase."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "IMPORTANT: V9A DOES NOT SUBMIT ORDERS."
        );

        return new PaperTradingPreflightResult(
            account.Status,
            account.IsAccountBlocked,
            account.IsTradingBlocked,
            account.TradeSuspendedByUser,
            account.Equity ?? 0M,
            buyingPower,
            StrategyCapital,
            clock.IsOpen,
            clock.TimestampUtc,
            clock.NextOpenUtc,
            clock.NextCloseUtc,
            positions.Count,
            openOrders.Count,
            safetyChecksPassed,
            OrderSubmissionEnabled,
            canSubmitOrdersNow
        );
    }

    private static void PrintCheck(
        string name,
        bool passed)
    {
        Console.WriteLine(
            $"[{(passed ? "PASS" : "FAIL")}] {name}"
        );
    }
}

public sealed record PaperTradingPreflightResult(
    AccountStatus AccountStatus,
    bool IsAccountBlocked,
    bool IsTradingBlocked,
    bool TradeSuspendedByUser,
    decimal Equity,
    decimal BuyingPower,
    decimal StrategyCapital,
    bool MarketOpen,
    DateTime BrokerTimestampUtc,
    DateTime NextOpenUtc,
    DateTime NextCloseUtc,
    int OpenPositions,
    int OpenOrders,
    bool SafetyChecksPassed,
    bool OrderSubmissionEnabled,
    bool CanSubmitOrdersNow
);
