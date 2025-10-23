#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NT8LongOnlyAllowedTimes : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;
        private double cancelDistance;

        // 🕐 Define allowed trading windows (add as many as you like)
        private List<(TimeSpan Start, TimeSpan End)> allowedWindows = new List<(TimeSpan, TimeSpan)>
        {
            (new TimeSpan(8, 30, 0), new TimeSpan(10, 0, 0)),   // 08:30–10:00
            (new TimeSpan(14, 0, 0), new TimeSpan(16, 0, 0))    // 14:00–16:00
        };

        // 🧭 Helper: Are we in any allowed window?
        private bool InAllowedWindow()
        {
            TimeSpan now = Times[0][0].TimeOfDay;
            foreach (var (start, end) in allowedWindows)
            {
                if (now >= start && now <= end)
                    return true;
            }
            return false;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8LongOnlyAllowedTimes";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;   // ✅ managed mode
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.Configure)
            {
                cancelDistance = 4 * TickSize; // roughly $1 on MNQ
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            // Debug info
            Print($"[{Time[0]}] OnBarUpdate | H={High[0]} L={Low[0]} Pos={Position.MarketPosition}");

            // 🟠 Cancel only pending BUY entry orders when outside allowed window
            if (!InAllowedWindow()
                && longOrder != null
                && longOrder.OrderState == OrderState.Working
                && longOrder.OrderAction == OrderAction.Buy)
            {
                double distance = Math.Abs(Close[0] - longOrder.StopPrice);
                if (distance < cancelDistance)
                {
                    Print($"[{Time[0]}] 🚫 Outside allowed window & price {Close[0]} near BUY stop {longOrder.StopPrice} (< {cancelDistance:F2}) → cancelling entry order");
                    CancelOrder(longOrder);
                }
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
                return; // don’t place new orders while in position
            }

            // Skip if not flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Only act on red candles (Close < Open)
            if (Close[0] < Open[0])
            {
                entryPrice = High[0] + TickSize;      // stop entry above high
                pendingStopPrice = Low[0] - TickSize; // SL under low
                riskPerTrade = entryPrice - pendingStopPrice;

                // ✅ Attach SL BEFORE entry (fixes reuse bug)
                SetStopLoss("Long1", CalculationMode.Price, pendingStopPrice, false);

                // ⚙️ Define stop/limit price
                double stopPrice = entryPrice;

                // ✅ Submit Buy Stop Limit (normal case)
                longOrder = EnterLongStopLimit(0, true, 1, stopPrice, stopPrice, "Long1");

                Print($"[{Time[0]}] >>> Submitted new LONG stop-limit @ {entryPrice}, SL @ {pendingStopPrice}");
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null)
                return;

            if (execution.Order.Name == "Long1" &&
                execution.Order.OrderState == OrderState.Filled &&
                marketPosition == MarketPosition.Long)
            {
                Print($"[{time}] [ENTRY FILLED] Long entry filled @ {price}, SL already set @ {pendingStopPrice}");
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                Print($"[{time}] Flat → no active SL");
            }
        }
    }
}
