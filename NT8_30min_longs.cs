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

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8LongOnly";
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
                longOrder = null;
                pendingStopPrice = 0;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            // Debug info for every bar
            Print($"[{Time[0]}] OnBarUpdate | O={Open[0]} H={High[0]} L={Low[0]} C={Close[0]} " +
                  $"Ask={GetCurrentAsk()} Bid={GetCurrentBid()} Pos={Position.MarketPosition}");

            // Skip if we are already in a trade
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                Print($"[{Time[0]}] Skipping — already in position ({Position.MarketPosition})");
                return;
            }

            // Only act on red candles (Close < Open)
            if (Close[0] < Open[0])
            {
                double entryPrice = High[0] + TickSize;     // stop entry above the high
                pendingStopPrice = Low[0] - TickSize;       // SL under the low

                // Replace only if price changed
                if (longOrder != null && longOrder.OrderState == OrderState.Working)
                {
                    if (Math.Abs(longOrder.StopPrice - entryPrice) > TickSize / 2)
                    {
                        CancelOrder(longOrder);
                        Print($"[{Time[0]}] Cancelled old LONG stop @ {longOrder.StopPrice} → new {entryPrice}");
                    }
                    else
                    {
                        Print($"[{Time[0]}] Existing LONG stop already correct @ {entryPrice} → keep it");
                        return;
                    }
                }

                // Place new stop-market entry
                longOrder = EnterLongStopMarket(0, true, 1, entryPrice, "Long1");
                Print($"[{Time[0]}] >>> Submitted new LONG stop @ {entryPrice}, SL candidate: {pendingStopPrice}");
            }
            else
            {
                Print($"[{Time[0]}] Green candle → keep working LONG stop alive (if any)");
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null)
                return;

            // Long entry filled → attach SL
            if (execution.Order.Name == "Long1"
                && execution.Order.OrderState == OrderState.Filled
                && marketPosition == MarketPosition.Long)
            {
                double slPrice = pendingStopPrice;
                Print($"[{time}] [SL SET] Stop-loss placed @ {slPrice}");

                // Managed-style stop loss
                SetStopLoss("Long1", CalculationMode.Price, slPrice, false);
            }

            // Flat cleanup (mostly just debug info in managed mode)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                Print($"[{time}] Flat → no active SL");
            }
        }
    }
}
