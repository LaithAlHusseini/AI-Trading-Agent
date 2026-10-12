public sealed class DiagnosticsEngine
{
    public void Run(
        PortfolioSimulationResult development,
        PortfolioSimulationResult outOfSample,
        PortfolioSimulationResult overall)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " DIAGNOSTICS ENGINE V4A"
        );

        Console.WriteLine(
            "=============================================="
        );

        PrintDiagnostics(
            "DEVELOPMENT DIAGNOSTICS",
            development
        );

        PrintDiagnostics(
            "OUT-OF-SAMPLE DIAGNOSTICS",
            outOfSample
        );

        PrintDiagnostics(
            "OVERALL DIAGNOSTICS",
            overall
        );

        PrintStabilityComparison(
            development,
            outOfSample
        );
    }

    private static void PrintDiagnostics(
        string title,
        PortfolioSimulationResult result)
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

        var trades =
            result.Trades
                .OrderBy(
                    trade =>
                        trade.ExitDate
                )
                .ToList();

        if (trades.Count == 0)
        {
            Console.WriteLine(
                "No trades available."
            );

            return;
        }

        PrintCoreStatistics(
            trades
        );

        PrintFrictionAnalysis(
            trades
        );

        PrintBySymbol(
            trades
        );

        PrintByYear(
            trades
        );

        PrintByMonth(
            trades
        );

        PrintByExitReason(
            trades
        );

        PrintByHoldingPeriod(
            trades
        );

        PrintExtremes(
            trades
        );
    }

    private static void PrintCoreStatistics(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var wins =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losses =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var grossProfit =
            wins.Sum(
                trade =>
                    trade.Pnl
            );

        var grossLoss =
            Math.Abs(
                losses.Sum(
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
            trades.Average(
                trade =>
                    trade.RMultiple
            );

        var averageWin =
            wins.Count > 0
                ? wins.Average(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var averageLoss =
            losses.Count > 0
                ? losses.Average(
                    trade =>
                        trade.RMultiple
                  )
                : 0;

        var winRate =
            wins.Count /
            (double)trades.Count *
            100;

        var expectancy =
            averageR;

        var streaks =
            CalculateStreaks(
                trades
                    .OrderBy(
                        trade =>
                            trade.ExitDate
                    )
                    .ToList()
            );

        Console.WriteLine(
            "CORE PERFORMANCE"
        );

        Console.WriteLine(
            $"Trades: {trades.Count}"
        );

        Console.WriteLine(
            $"Win Rate: {winRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(profitFactor)}"
        );

        Console.WriteLine(
            $"Expectancy: {expectancy:F2} R/trade"
        );

        Console.WriteLine(
            $"Average Win: {averageWin:F2} R"
        );

        Console.WriteLine(
            $"Average Loss: {averageLoss:F2} R"
        );

        Console.WriteLine(
            $"Max Consecutive Wins: {streaks.Wins}"
        );

        Console.WriteLine(
            $"Max Consecutive Losses: {streaks.Losses}"
        );

        Console.WriteLine();
    }

    private static void PrintFrictionAnalysis(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var netPnl =
            trades.Sum(
                trade =>
                    trade.Pnl
            );

        var fees =
            trades.Sum(
                trade =>
                    trade.EntryFee +
                    trade.ExitFee
            );

        var slippage =
            trades.Sum(
                trade =>
                    trade.SlippageCost
            );

        var totalFriction =
            fees +
            slippage;

        var estimatedPnlBeforeFriction =
            netPnl +
            totalFriction;

        Console.WriteLine(
            "EXECUTION FRICTION"
        );

        Console.WriteLine(
            $"Net P&L: ${netPnl:F4}"
        );

        Console.WriteLine(
            $"Transaction Costs: ${fees:F4}"
        );

        Console.WriteLine(
            $"Slippage Cost: ${slippage:F4}"
        );

        Console.WriteLine(
            $"Total Friction: ${totalFriction:F4}"
        );

        Console.WriteLine(
            $"Estimated P&L Before Friction: " +
            $"${estimatedPnlBeforeFriction:F4}"
        );

        if (Math.Abs(
                estimatedPnlBeforeFriction) >
            0.000001)
        {
            var frictionPercent =
                totalFriction /
                Math.Abs(
                    estimatedPnlBeforeFriction
                ) *
                100;

            Console.WriteLine(
                $"Friction vs Pre-Friction P&L: " +
                $"{frictionPercent:F2}%"
            );
        }

        Console.WriteLine();
    }

    private static void PrintBySymbol(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "PERFORMANCE BY SYMBOL"
        );

        foreach (
            var group in trades
                .GroupBy(
                    trade =>
                        trade.Symbol
                )
                .OrderByDescending(
                    group =>
                        group.Sum(
                            trade =>
                                trade.Pnl
                        )
                ))
        {
            PrintGroup(
                group.Key,
                group.ToList()
            );
        }

        Console.WriteLine();
    }

    private static void PrintByYear(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "PERFORMANCE BY EXIT YEAR"
        );

        foreach (
            var group in trades
                .GroupBy(
                    trade =>
                        trade.ExitDate.Year
                )
                .OrderBy(
                    group =>
                        group.Key
                ))
        {
            PrintGroup(
                group.Key.ToString(),
                group.ToList()
            );
        }

        Console.WriteLine();
    }

    private static void PrintByMonth(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "PERFORMANCE BY CALENDAR MONTH"
        );

        foreach (
            var group in trades
                .GroupBy(
                    trade =>
                        trade.ExitDate.Month
                )
                .OrderBy(
                    group =>
                        group.Key
                ))
        {
            var monthName =
                new DateTime(
                    2000,
                    group.Key,
                    1
                )
                .ToString(
                    "MMM"
                );

            PrintGroup(
                monthName,
                group.ToList()
            );
        }

        Console.WriteLine();
    }

    private static void PrintByExitReason(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "EXIT REASON DIAGNOSTICS"
        );

        foreach (
            var group in trades
                .GroupBy(
                    trade =>
                        trade.ExitReason
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                ))
        {
            PrintGroup(
                group.Key,
                group.ToList()
            );
        }

        var stopTrades =
            trades
                .Where(
                    trade =>
                        trade.ExitReason
                            .Contains(
                                "STOP",
                                StringComparison.OrdinalIgnoreCase
                            )
                )
                .ToList();

        var targetTrades =
            trades
                .Where(
                    trade =>
                        trade.ExitReason
                            .Contains(
                                "TARGET",
                                StringComparison.OrdinalIgnoreCase
                            ) ||
                        trade.ExitReason ==
                        "TAKE_PROFIT"
                )
                .ToList();

        Console.WriteLine();

        Console.WriteLine(
            $"Stop-related exits: {stopTrades.Count}"
        );

        Console.WriteLine(
            $"Target-related exits: {targetTrades.Count}"
        );

        if (trades.Count > 0)
        {
            Console.WriteLine(
                $"Stop Exit Rate: " +
                $"{stopTrades.Count / (double)trades.Count * 100:F2}%"
            );

            Console.WriteLine(
                $"Target Exit Rate: " +
                $"{targetTrades.Count / (double)trades.Count * 100:F2}%"
            );
        }

        Console.WriteLine();
    }

    private static void PrintByHoldingPeriod(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "PERFORMANCE BY CALENDAR HOLDING PERIOD"
        );

        var groups =
            trades
                .GroupBy(
                    trade =>
                    {
                        var days =
                            Math.Max(
                                0,
                                (
                                    trade.ExitDate.Date -
                                    trade.EntryDate.Date
                                ).Days
                            );

                        return days switch
                        {
                            <= 2 =>
                                "0-2 days",

                            <= 5 =>
                                "3-5 days",

                            <= 10 =>
                                "6-10 days",

                            _ =>
                                "11+ days"
                        };
                    }
                )
                .OrderBy(
                    group =>
                        HoldingBucketOrder(
                            group.Key
                        )
                );

        foreach (var group in groups)
        {
            PrintGroup(
                group.Key,
                group.ToList()
            );
        }

        Console.WriteLine();
    }

    private static void PrintExtremes(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "BEST TRADES"
        );

        foreach (
            var trade in trades
                .OrderByDescending(
                    trade =>
                        trade.RMultiple
                )
                .Take(5))
        {
            PrintTrade(
                trade
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "WORST TRADES"
        );

        foreach (
            var trade in trades
                .OrderBy(
                    trade =>
                        trade.RMultiple
                )
                .Take(5))
        {
            PrintTrade(
                trade
            );
        }

        Console.WriteLine();
    }

    private static void PrintStabilityComparison(
        PortfolioSimulationResult development,
        PortfolioSimulationResult outOfSample)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " STRATEGY STABILITY CHECK"
        );

        Console.WriteLine(
            "=============================================="
        );

        var developmentStats =
            CalculateCompactStats(
                development.Trades
            );

        var outOfSampleStats =
            CalculateCompactStats(
                outOfSample.Trades
            );

        Console.WriteLine(
            "Development:"
        );

        Console.WriteLine(
            $"  Trades: {developmentStats.Trades}"
        );

        Console.WriteLine(
            $"  Win Rate: {developmentStats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"  Profit Factor: " +
            $"{FormatNumber(developmentStats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"  Average R: {developmentStats.AverageR:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Out-of-Sample:"
        );

        Console.WriteLine(
            $"  Trades: {outOfSampleStats.Trades}"
        );

        Console.WriteLine(
            $"  Win Rate: {outOfSampleStats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"  Profit Factor: " +
            $"{FormatNumber(outOfSampleStats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"  Average R: {outOfSampleStats.AverageR:F2}"
        );

        Console.WriteLine();

        var pfDelta =
            outOfSampleStats.ProfitFactor -
            developmentStats.ProfitFactor;

        var rDelta =
            outOfSampleStats.AverageR -
            developmentStats.AverageR;

        Console.WriteLine(
            $"PF Change: {pfDelta:+0.00;-0.00;0.00}"
        );

        Console.WriteLine(
            $"Average R Change: {rDelta:+0.00;-0.00;0.00}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Diagnostic classification:"
        );

        if (developmentStats.ProfitFactor < 1.0 &&
            outOfSampleStats.ProfitFactor < 1.0)
        {
            Console.WriteLine(
                "NO EDGE: both periods are unprofitable."
            );
        }
        else if (
            developmentStats.ProfitFactor >= 1.0 &&
            outOfSampleStats.ProfitFactor >= 1.0)
        {
            Console.WriteLine(
                "POSSIBLE EDGE: positive in both periods."
            );
        }
        else
        {
            Console.WriteLine(
                "UNSTABLE EDGE: performance changes materially between periods."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Important: this classification is diagnostic only."
        );

        Console.WriteLine(
            "It is not a trading recommendation."
        );
    }

    private static void PrintGroup(
        string label,
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var stats =
            CalculateCompactStats(
                trades
            );

        var pnl =
            trades.Sum(
                trade =>
                    trade.Pnl
            );

        Console.WriteLine(
            $"  {label,-18} | " +
            $"Trades {stats.Trades,3} | " +
            $"Win {stats.WinRate,6:F2}% | " +
            $"PF {FormatNumber(stats.ProfitFactor),6} | " +
            $"Avg R {stats.AverageR,6:F2} | " +
            $"P&L ${pnl,7:F2}"
        );
    }

    private static void PrintTrade(
        PortfolioTrade trade)
    {
        var holdingDays =
            Math.Max(
                0,
                (
                    trade.ExitDate.Date -
                    trade.EntryDate.Date
                ).Days
            );

        Console.WriteLine(
            $"  {trade.Symbol} | " +
            $"{trade.EntryDate:yyyy-MM-dd} -> " +
            $"{trade.ExitDate:yyyy-MM-dd} | " +
            $"{holdingDays}d | " +
            $"{trade.RMultiple:F2} R | " +
            $"${trade.Pnl:F2} | " +
            $"{trade.ExitReason}"
        );
    }

    private static CompactDiagnosticsStats
        CalculateCompactStats(
            IReadOnlyCollection<PortfolioTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new CompactDiagnosticsStats(
                0,
                0,
                0,
                0
            );
        }

        var wins =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losses =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var grossProfit =
            wins.Sum(
                trade =>
                    trade.Pnl
            );

        var grossLoss =
            Math.Abs(
                losses.Sum(
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

        var winRate =
            wins.Count /
            (double)trades.Count *
            100;

        var averageR =
            trades.Average(
                trade =>
                    trade.RMultiple
            );

        return new CompactDiagnosticsStats(
            trades.Count,
            winRate,
            profitFactor,
            averageR
        );
    }

    private static (
        int Wins,
        int Losses)
        CalculateStreaks(
            IReadOnlyList<PortfolioTrade> trades)
    {
        var currentWins =
            0;

        var currentLosses =
            0;

        var maximumWins =
            0;

        var maximumLosses =
            0;

        foreach (var trade in trades)
        {
            if (trade.Pnl > 0)
            {
                currentWins++;

                currentLosses =
                    0;

                maximumWins =
                    Math.Max(
                        maximumWins,
                        currentWins
                    );
            }
            else if (trade.Pnl < 0)
            {
                currentLosses++;

                currentWins =
                    0;

                maximumLosses =
                    Math.Max(
                        maximumLosses,
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
            maximumWins,
            maximumLosses
        );
    }

    private static int HoldingBucketOrder(
        string bucket)
    {
        return bucket switch
        {
            "0-2 days" => 1,
            "3-5 days" => 2,
            "6-10 days" => 3,
            _ => 4
        };
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

public sealed record CompactDiagnosticsStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR
);