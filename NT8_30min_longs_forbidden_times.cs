#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NT8LongOnly : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;

        // 🔹 For delayed order storage
        private double delayedEntry = 0;
        private double delayedStop = 0;

        // 🔹 Define forbidden windows (HHMMSS format using ToTime)
        // Example: 10:00–10:30 and 14:00–14:15
        private (int start, int end)[] forbiddenWindows = new (int, int)[]
        {
            (100000, 103000),
            (140000, 141500)
        };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8LongOnlyForbiddenTimes";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                delayedEntry = 0;
                delayedStop = 0;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            // Debug info
            Print($"[{Time[0]}] OnBarUpdate | H={High[0]} L={Low[0]} Pos={Position.MarketPosition}");

            // Check if current bar is in forbidden window
            bool inForbidden = IsInForbiddenWindow(ToTime(Time[0]));

            // 🔹 Release delayed order if we left forbidden window
            if (!inForbidden && delayedEntry > 0 && Position.MarketPosition == MarketPosition.Flat)
            {
                SetStopLoss("Long1", CalculationMode.Price, delayedStop, false);
                longOrder = EnterLongStopMarket(0, true, 1, delayedEntry, "Long1");
                Print($"[{Time[0]}] 🟢 Released delayed order @ {delayedEntry}, SL={delayedStop}");
                delayedEntry = 0;
                delayedStop = 0;
            }

            // 🔹 Flatten if 1:1 R/R reached
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double reward = Close[0] - entryPrice;
                if (reward >= riskPerTrade)
                {
                    Print($"[{Time[0]}] [FLATTEN] 1:1 R/R reached (reward={reward}, risk={riskPerTrade}) → closing position");
                    ExitLong("RR_Flatten", "Long1");
                }
                return;
            }

            // Skip if not flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Only act on red candles (Close < Open)
            if (Close[0] < Open[0])
            {
                entryPrice = High[0] + TickSize;     // stop entry above the high
                pendingStopPrice = Low[0] - TickSize; // SL under the low
                riskPerTrade = entryPrice - pendingStopPrice;

                if (inForbidden)
                {
                    // Store for later release
                    delayedEntry = entryPrice;
                    delayedStop = pendingStopPrice;
                    Print($"[{Time[0]}] ⏸ Delayed order stored (forbidden window). Entry={delayedEntry}, SL={delayedStop}");
                }
                else
                {
                    // Normal order placement
                    SetStopLoss("Long1", CalculationMode.Price, pendingStopPrice, false);
                    longOrder = EnterLongStopMarket(0, true, 1, entryPrice, "Long1");
                    Print($"[{Time[0]}] >>> Submitted new LONG stop @ {entryPrice}, SL={pendingStopPrice}");
                }
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;

            if (execution.Order.Name == "Long1" &&
                execution.Order.OrderState == OrderState.Filled &&
                marketPosition == MarketPosition.Long)
            {
                Print($"[{time}] [ENTRY FILLED] Long entry filled @ {price}, SL is already set @ {pendingStopPrice}");
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                Print($"[{time}] Flat → no active SL");
            }
        }

        // 🔹 Helper: check if given time is inside a forbidden window
        private bool IsInForbiddenWindow(int currentTime)
        {
            foreach (var (start, end) in forbiddenWindows)
            {
                if (currentTime >= start && currentTime < end)
                    return true;
            }
            return false;
        }
    }
}
