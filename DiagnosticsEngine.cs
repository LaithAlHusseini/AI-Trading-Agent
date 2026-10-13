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
            " DIAGNOSTICS ENGINE V4B"
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

        Console.WriteLine();

        Console.WriteLine(
            "V4B notes:"
        );

        Console.WriteLine(
            "- MAE/MFE use conservative daily-bar sequencing on exit candles."
        );

        Console.WriteLine(
            "- Post-stop recovery looks only at bars AFTER the exit date."
        );

        Console.WriteLine(
            "- Recovery diagnostics are descriptive only and are not used"
        );

        Console.WriteLine(
            "  by the strategy or execution rules."
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

        PrintSignalTelemetry(
            trades
        );

        PrintWinnerLoserTelemetry(
            trades
        );

        PrintExcursionAnalysis(
            trades
        );

        PrintStopRecoveryAnalysis(
            trades
        );

        PrintExtremes(
            trades
        );
    }

    private static void PrintCoreStatistics(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var stats =
            CalculateCompactStats(
                trades
            );

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
            $"Trades: {stats.Trades}"
        );

        Console.WriteLine(
            $"Win Rate: {stats.WinRate:F2}%"
        );

        Console.WriteLine(
            $"Profit Factor: {FormatNumber(stats.ProfitFactor)}"
        );

        Console.WriteLine(
            $"Expectancy: {stats.AverageR:F2} R/trade"
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
                        IsStopRelated(
                            trade.ExitReason
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

        Console.WriteLine(
            $"Stop Exit Rate: " +
            $"{stopTrades.Count / (double)trades.Count * 100:F2}%"
        );

        Console.WriteLine(
            $"Target Exit Rate: " +
            $"{targetTrades.Count / (double)trades.Count * 100:F2}%"
        );

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

    private static void PrintSignalTelemetry(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        Console.WriteLine(
            "SIGNAL TELEMETRY"
        );

        PrintBucketSection(
            "Opportunity Score",
            trades,
            trade =>
                trade.OpportunityScore switch
                {
                    <= 79 => "75-79",
                    <= 84 => "80-84",
                    <= 89 => "85-89",
                    _ => "90+"
                }
        );

        PrintBucketSection(
            "Risk Score",
            trades,
            trade =>
                trade.RiskScore switch
                {
                    <= 10 => "0-10",
                    <= 20 => "11-20",
                    <= 30 => "21-30",
                    _ => "31-35"
                }
        );

        PrintBucketSection(
            "Net Score",
            trades,
            trade =>
                trade.NetScore switch
                {
                    < 50 => "<50",
                    < 60 => "50-59",
                    < 70 => "60-69",
                    _ => "70+"
                }
        );

        PrintBucketSection(
            "Momentum",
            trades,
            trade =>
                trade.Momentum switch
                {
                    < 2 => "0-2%",
                    < 5 => "2-5%",
                    < 10 => "5-10%",
                    _ => "10%+"
                }
        );

        PrintBucketSection(
            "Annualized Volatility",
            trades,
            trade =>
                trade.Volatility switch
                {
                    <= 20 => "<=20%",
                    <= 30 => "20-30%",
                    <= 40 => "30-40%",
                    _ => "40%+"
                }
        );

        PrintBucketSection(
            "ATR as % of Entry",
            trades,
            trade =>
                trade.AtrPercent switch
                {
                    < 2 => "<2%",
                    < 3 => "2-3%",
                    < 5 => "3-5%",
                    _ => "5%+"
                }
        );

        PrintBucketSection(
            "Distance Above SMA20",
            trades,
            trade =>
                trade.DistanceFromSma20Percent switch
                {
                    < 2 => "<2%",
                    < 5 => "2-5%",
                    < 10 => "5-10%",
                    _ => "10%+"
                }
        );

        PrintBucketSection(
            "Distance Above SMA50",
            trades,
            trade =>
                trade.DistanceFromSma50Percent switch
                {
                    < 5 => "<5%",
                    < 10 => "5-10%",
                    < 20 => "10-20%",
                    _ => "20%+"
                }
        );

        PrintBucketSection(
            "Entry Gap",
            trades,
            trade =>
                trade.EntryGapPercent switch
                {
                    < -1 => "<-1%",
                    < 0 => "-1 to 0%",
                    <= 1 => "0 to 1%",
                    <= 2 => "1 to 2%",
                    _ => ">2%"
                }
        );

        PrintBucketSection(
            "Stop Distance in ATR",
            trades,
            trade =>
                trade.StopDistanceAtr switch
                {
                    < 1.5 => "<1.5 ATR",
                    < 2.0 => "1.5-2 ATR",
                    < 2.5 => "2-2.5 ATR",
                    _ => "2.5+ ATR"
                }
        );

        PrintBucketSection(
            "Risk / Reward",
            trades,
            trade =>
                trade.RiskRewardRatio switch
                {
                    < 2.5 => "1.8-2.5",
                    < 4.0 => "2.5-4",
                    < 6.0 => "4-6",
                    _ => "6+"
                }
        );

        Console.WriteLine();
    }

    private static void PrintBucketSection(
        string title,
        IReadOnlyCollection<PortfolioTrade> trades,
        Func<PortfolioTrade, string> selector)
    {
        Console.WriteLine(
            $"  {title}:"
        );

        foreach (
            var group in trades
                .GroupBy(
                    selector
                )
                .OrderBy(
                    group =>
                        group.Key
                ))
        {
            PrintGroup(
                group.Key,
                group.ToList(),
                "    "
            );
        }
    }

    private static void PrintWinnerLoserTelemetry(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var winners =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losers =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        Console.WriteLine(
            "WINNERS VS LOSERS TELEMETRY"
        );

        PrintAverageMetric(
            "Opportunity Score",
            winners,
            losers,
            trade =>
                trade.OpportunityScore
        );

        PrintAverageMetric(
            "Risk Score",
            winners,
            losers,
            trade =>
                trade.RiskScore
        );

        PrintAverageMetric(
            "Net Score",
            winners,
            losers,
            trade =>
                trade.NetScore
        );

        PrintAverageMetric(
            "Momentum %",
            winners,
            losers,
            trade =>
                trade.Momentum
        );

        PrintAverageMetric(
            "Volatility %",
            winners,
            losers,
            trade =>
                trade.Volatility
        );

        PrintAverageMetric(
            "ATR %",
            winners,
            losers,
            trade =>
                trade.AtrPercent
        );

        PrintAverageMetric(
            "SMA20 Distance %",
            winners,
            losers,
            trade =>
                trade.DistanceFromSma20Percent
        );

        PrintAverageMetric(
            "Entry Gap %",
            winners,
            losers,
            trade =>
                trade.EntryGapPercent
        );

        PrintAverageMetric(
            "Stop Distance ATR",
            winners,
            losers,
            trade =>
                trade.StopDistanceAtr
        );

        PrintAverageMetric(
            "Risk/Reward",
            winners,
            losers,
            trade =>
                trade.RiskRewardRatio
        );

        Console.WriteLine();
    }

    private static void PrintAverageMetric(
        string label,
        IReadOnlyCollection<PortfolioTrade> winners,
        IReadOnlyCollection<PortfolioTrade> losers,
        Func<PortfolioTrade, double> selector)
    {
        var winnerAverage =
            winners.Count > 0
                ? winners.Average(
                    selector
                  )
                : 0;

        var loserAverage =
            losers.Count > 0
                ? losers.Average(
                    selector
                  )
                : 0;

        Console.WriteLine(
            $"  {label,-22} | " +
            $"Winners {winnerAverage,8:F2} | " +
            $"Losers {loserAverage,8:F2}"
        );
    }

    private static void PrintExcursionAnalysis(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var winners =
            trades
                .Where(
                    trade =>
                        trade.Pnl > 0
                )
                .ToList();

        var losers =
            trades
                .Where(
                    trade =>
                        trade.Pnl < 0
                )
                .ToList();

        var stopTrades =
            trades
                .Where(
                    trade =>
                        IsStopRelated(
                            trade.ExitReason
                        )
                )
                .ToList();

        Console.WriteLine(
            "MAE / MFE ANALYSIS"
        );

        Console.WriteLine(
            $"  All Trades | Avg MAE {Average(trades, t => t.MaeR):F2} R" +
            $" | Avg MFE {Average(trades, t => t.MfeR):F2} R"
        );

        Console.WriteLine(
            $"  Winners    | Avg MAE {Average(winners, t => t.MaeR):F2} R" +
            $" | Avg MFE {Average(winners, t => t.MfeR):F2} R"
        );

        Console.WriteLine(
            $"  Losers     | Avg MAE {Average(losers, t => t.MaeR):F2} R" +
            $" | Avg MFE {Average(losers, t => t.MfeR):F2} R"
        );

        var losingReachedHalfR =
            losers.Count(
                trade =>
                    trade.MfeR >= 0.50
            );

        var losingReachedOneR =
            losers.Count(
                trade =>
                    trade.MfeR >= 1.00
            );

        var stoppedReachedHalfR =
            stopTrades.Count(
                trade =>
                    trade.MfeR >= 0.50
            );

        var stoppedReachedOneR =
            stopTrades.Count(
                trade =>
                    trade.MfeR >= 1.00
            );

        Console.WriteLine();

        Console.WriteLine(
            $"  Losing trades that reached +0.5R first: " +
            $"{losingReachedHalfR}/{losers.Count} " +
            $"({Percent(losingReachedHalfR, losers.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Losing trades that reached +1.0R first: " +
            $"{losingReachedOneR}/{losers.Count} " +
            $"({Percent(losingReachedOneR, losers.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Stop exits with >= +0.5R observed before exit: " +
            $"{stoppedReachedHalfR}/{stopTrades.Count} " +
            $"({Percent(stoppedReachedHalfR, stopTrades.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Stop exits with >= +1.0R observed before exit: " +
            $"{stoppedReachedOneR}/{stopTrades.Count} " +
            $"({Percent(stoppedReachedOneR, stopTrades.Count):F2}%)"
        );

        Console.WriteLine();
    }

    private static void PrintStopRecoveryAnalysis(
        IReadOnlyCollection<PortfolioTrade> trades)
    {
        var stopTrades =
            trades
                .Where(
                    trade =>
                        IsStopRelated(
                            trade.ExitReason
                        )
                )
                .ToList();

        Console.WriteLine(
            "POST-STOP RECOVERY ANALYSIS"
        );

        Console.WriteLine(
            $"  Stop exits: {stopTrades.Count}"
        );

        if (stopTrades.Count == 0)
        {
            Console.WriteLine();

            return;
        }

        var withFive =
            stopTrades
                .Where(
                    trade =>
                        trade.PostExitBestR5.HasValue
                )
                .ToList();

        var withTen =
            stopTrades
                .Where(
                    trade =>
                        trade.PostExitBestR10.HasValue
                )
                .ToList();

        var recoveredEntry5 =
            withFive.Count(
                trade =>
                    trade.RecoveredEntryWithin5Bars ==
                    true
            );

        var recoveredEntry10 =
            withTen.Count(
                trade =>
                    trade.RecoveredEntryWithin10Bars ==
                    true
            );

        var reachedPlusOneR5 =
            withFive.Count(
                trade =>
                    trade.PostExitBestR5 >=
                    1.0
            );

        var reachedPlusOneR10 =
            withTen.Count(
                trade =>
                    trade.PostExitBestR10 >=
                    1.0
            );

        Console.WriteLine(
            $"  5-bar recovery samples: {withFive.Count}"
        );

        Console.WriteLine(
            $"  Recovered original entry within 5 bars: " +
            $"{recoveredEntry5}/{withFive.Count} " +
            $"({Percent(recoveredEntry5, withFive.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Reached +1R from original entry within 5 bars: " +
            $"{reachedPlusOneR5}/{withFive.Count} " +
            $"({Percent(reachedPlusOneR5, withFive.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Average best post-stop R within 5 bars: " +
            $"{AverageNullable(withFive, t => t.PostExitBestR5):F2} R"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"  10-bar recovery samples: {withTen.Count}"
        );

        Console.WriteLine(
            $"  Recovered original entry within 10 bars: " +
            $"{recoveredEntry10}/{withTen.Count} " +
            $"({Percent(recoveredEntry10, withTen.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Reached +1R from original entry within 10 bars: " +
            $"{reachedPlusOneR10}/{withTen.Count} " +
            $"({Percent(reachedPlusOneR10, withTen.Count):F2}%)"
        );

        Console.WriteLine(
            $"  Average best post-stop R within 10 bars: " +
            $"{AverageNullable(withTen, t => t.PostExitBestR10):F2} R"
        );

        Console.WriteLine();

        if (withTen.Count >= 10)
        {
            var recoveryRate =
                recoveredEntry10 /
                (double)withTen.Count *
                100.0;

            var plusOneRate =
                reachedPlusOneR10 /
                (double)withTen.Count *
                100.0;

            Console.WriteLine(
                "  Diagnostic hint:"
            );

            if (recoveryRate >= 50 ||
                plusOneRate >= 30)
            {
                Console.WriteLine(
                    "  A meaningful share of stopped trades recovered after exit."
                );

                Console.WriteLine(
                    "  Stop placement and entry timing deserve deeper testing."
                );
            }
            else
            {
                Console.WriteLine(
                    "  Most stopped trades did not recover quickly."
                );

                Console.WriteLine(
                    "  Entry/signal quality may be a larger issue than stop width."
                );
            }
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
        IReadOnlyCollection<PortfolioTrade> trades,
        string prefix = "  ")
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
            $"{prefix}{label,-18} | " +
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
            $"MAE {trade.MaeR:F2} R | " +
            $"MFE {trade.MfeR:F2} R | " +
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

    private static bool IsStopRelated(
        string exitReason)
    {
        return
            exitReason.Contains(
                "STOP",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static double Average(
        IReadOnlyCollection<PortfolioTrade> trades,
        Func<PortfolioTrade, double> selector)
    {
        return
            trades.Count > 0
                ? trades.Average(
                    selector
                  )
                : 0;
    }

    private static double AverageNullable(
        IReadOnlyCollection<PortfolioTrade> trades,
        Func<PortfolioTrade, double?> selector)
    {
        var values =
            trades
                .Select(
                    selector
                )
                .Where(
                    value =>
                        value.HasValue
                )
                .Select(
                    value =>
                        value!.Value
                )
                .ToList();

        return
            values.Count > 0
                ? values.Average()
                : 0;
    }

    private static double Percent(
        int numerator,
        int denominator)
    {
        return
            denominator > 0
                ? numerator /
                  (double)denominator *
                  100.0
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

public sealed record CompactDiagnosticsStats(
    int Trades,
    double WinRate,
    double ProfitFactor,
    double AverageR
);
