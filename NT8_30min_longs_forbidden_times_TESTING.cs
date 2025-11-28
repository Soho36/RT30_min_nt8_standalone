#region Using declarations
using System;
using System.Collections.Generic;   // ✅ Needed for List<>
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NT8LongOnlyForbiddenTimes : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;

        // 🕒 Forbidden window parameters
        private TimeSpan forbiddenStart = new TimeSpan(10, 00, 0);  // 10:00
        private TimeSpan forbiddenEnd   = new TimeSpan(11, 00, 0);  // 11:00
        private double cancelDistance; // calculated as 4 ticks by default

<<<<<<< HEAD:NT8_30min_longs_forbidden_times.cs
        // 🔹 Forbidden trading windows (HHmm format)
        private List<Tuple<int,int>> forbiddenWindows;
=======
        // 🧭 Helper: Check if current time is in forbidden window
        private bool InForbiddenWindow()
        {
            TimeSpan now = Times[0][0].TimeOfDay;
            return now >= forbiddenStart && now <= forbiddenEnd;
        }
>>>>>>> 389b5fb6f2336fc578994d41348959c4e7e4c528:NT8_30min_longs_forbidden_times_TESTING.cs

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
                IsUnmanaged = false;   // ✅ managed mode
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
<<<<<<< HEAD:NT8_30min_longs_forbidden_times.cs
            else if (State == State.DataLoaded)
            {
                forbiddenWindows = new List<Tuple<int,int>>();

                // Example: block trades from 10:00–10:30 and 14:00–14:15
                forbiddenWindows.Add(new Tuple<int,int>(1000, 1030));
                forbiddenWindows.Add(new Tuple<int,int>(1400, 1415));
=======
            else if (State == State.Configure)
            {
                cancelDistance = 4 * TickSize; // roughly $1 on MNQ; adjust as needed
>>>>>>> 389b5fb6f2336fc578994d41348959c4e7e4c528:NT8_30min_longs_forbidden_times_TESTING.cs
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

<<<<<<< HEAD:NT8_30min_longs_forbidden_times.cs
            // Check if current bar is in forbidden window
            bool inForbidden = IsInForbiddenWindow(ToTime(Time[0]) / 100); // HHmm

            // 🔹 Release delayed order if we left forbidden window
            if (!inForbidden && delayedEntry > 0 && Position.MarketPosition == MarketPosition.Flat)
=======
            // 🟠 Cancel Buy-stop pending order if in forbidden window & price too close
            if (InForbiddenWindow()
            && longOrder != null
            && longOrder.OrderState == OrderState.Working
            && longOrder.OrderAction == OrderAction.Buy)
>>>>>>> 389b5fb6f2336fc578994d41348959c4e7e4c528:NT8_30min_longs_forbidden_times_TESTING.cs
            {
                double distance = Math.Abs(Close[0] - longOrder.StopPrice);
                if (distance < cancelDistance)
                {
                    Print($"[{Time[0]}] 🚫 Forbidden window active & price {Close[0]} near stop {longOrder.StopPrice} (< {cancelDistance:F2}) → cancelling order");
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
                entryPrice = High[0] + TickSize;       // stop entry above the high
                pendingStopPrice = Low[0] - TickSize;  // SL under the low
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
<<<<<<< HEAD:NT8_30min_longs_forbidden_times.cs

        // 🔹 Helper: check if given time is inside a forbidden window
        private bool IsInForbiddenWindow(int currentTimeHHmm)
        {
            foreach (Tuple<int, int> window in forbiddenWindows)
            {
                int start = window.Item1;
                int end   = window.Item2;

                if (currentTimeHHmm >= start && currentTimeHHmm < end)
                    return true;
            }
            return false;
        }
=======
>>>>>>> 389b5fb6f2336fc578994d41348959c4e7e4c528:NT8_30min_longs_forbidden_times_TESTING.cs
    }
}
