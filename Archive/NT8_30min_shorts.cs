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
    public class NT8ShortOnly : Strategy
    {
        private Order shortOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8ShortOnly";
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
            else if (State == State.Realtime)
            {
                shortOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                Print("=== Strategy entering REALTIME mode (SHORTS) ===");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            // Debug info
            Print($"[{Time[0]}] OnBarUpdate | H={High[0]} L={Low[0]} " +
                  $"Pos={Position.MarketPosition}");

            // 🔹 Flatten if 1:1 R/R reached
            if (Position.MarketPosition == MarketPosition.Short)
            {
                double reward = entryPrice - Close[0];  // ✅ inverse calc for shorts
                if (reward >= riskPerTrade)
                {
                    Print($"[{Time[0]}] [FLATTEN] 1:1 R/R reached (reward={reward}, risk={riskPerTrade}) → closing SHORT");
                    ExitShort("RR_Flatten", "Short1");
                }
                return; // don’t place new orders while in position
            }

            // Skip if not flat
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Only act on green candles (Close > Open)
            if (Close[0] > Open[0])
            {
                entryPrice = Low[0] - TickSize;         // stop entry below the low
                pendingStopPrice = High[0] + TickSize;  // SL above the high
                riskPerTrade = pendingStopPrice - entryPrice;

                // ✅ Attach SL BEFORE entry
                SetStopLoss("Short1", CalculationMode.Price, pendingStopPrice, false);

                // Place new stop-market entry
                shortOrder = EnterShortStopMarket(0, true, 1, entryPrice, "Short1");
                Print($"[{Time[0]}] >>> Submitted new SHORT stop @ {entryPrice}, SL will be @ {pendingStopPrice}");
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null)
                return;

            // Just debug info now — no SetStopLoss here
            if (execution.Order.Name == "Short1" &&
                execution.Order.OrderState == OrderState.Filled &&
                marketPosition == MarketPosition.Short)
            {
                Print($"[{time}] [ENTRY FILLED] Short entry filled @ {price}, SL is already set @ {pendingStopPrice}");
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                Print($"[{time}] Flat → no active SL");
            }
        }
    }
}
