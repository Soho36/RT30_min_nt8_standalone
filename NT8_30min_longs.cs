#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MyCustomStrategyLongOnly : Strategy
    {
        private Order longOrder1;
        private Order slOrder;
        private Dictionary<string, int> orderCreationCandle = new Dictionary<string, int>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PatternLongOnly";
                Calculate = Calculate.OnBarClose;   // only act on closed candles
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 10;
            }
        }

        protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			// Cancel orders older than 5 candles
			CancelOldOrders(CurrentBar, 5);

			// --- Pattern detection ---
			// If the last closed bar (index 1) is red
			if (Close[1] < Open[1] && Position.MarketPosition == MarketPosition.Flat)
			{
				double entryPrice = High[1] + TickSize;   // Buy stop 1 tick above last red high
				double stopPrice  = Low[1];               // SL at last red low

				if (entryPrice > GetCurrentAsk())
				{
					if (longOrder1 == null || longOrder1.OrderState != OrderState.Working)
					{
						longOrder1 = EnterLongStopMarket(0, true, 1, entryPrice, "Long1");

						if (longOrder1 != null && !string.IsNullOrEmpty(longOrder1.OrderId))
							orderCreationCandle[longOrder1.OrderId] = CurrentBar;

						Print($"[{Time[0]}] LONG stop placed @ {entryPrice}, SL: {stopPrice}");
					}
				}
				else
				{
					Print($"[{Time[0]}] ❌ Skipped order. Entry {entryPrice} not above ask {GetCurrentAsk()}");
				}
			}
		}


        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            // When long is filled → place SL at last red candle low
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled && marketPosition == MarketPosition.Long)
            {
                double slPrice = GetLastRedCandleLow();
                if (slOrder != null && slOrder.OrderState == OrderState.Working)
                    CancelOrder(slOrder);

                slOrder = ExitLongStopMarket(Position.Quantity, slPrice, "SL_LastRed", "");
                Print($"[{Time[0]}] [SL SET] Stop-loss placed @ {slPrice}");
            }
        }

        private double GetLastRedCandleLow(int lookbackBars = 10)
        {
            for (int i = 1; i <= lookbackBars; i++)
            {
                if (Close[i] < Open[i])
                {
                    Print($"[{Time[0]}] Found red candle at bar {i}, low: {Low[i]}");
                    return Low[i];
                }
            }
            return Low[1]; // fallback
        }

        private void CancelOldOrders(int currentCandleIndex, int maxCandleAge)
        {
            try
            {
                List<string> ordersToCancel = new List<string>();

                foreach (Order order in Account.Orders)
                {
                    if (order == null || string.IsNullOrEmpty(order.OrderId))
                        continue;

                    if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted)
                    {
                        if (orderCreationCandle.TryGetValue(order.OrderId, out int orderCandleIndex))
                        {
                            int candleAge = currentCandleIndex - orderCandleIndex;
                            if (candleAge > maxCandleAge)
                            {
                                ordersToCancel.Add(order.OrderId);
                                Print($"Order {order.Name} is {candleAge} candles old → canceling.");
                            }
                        }
                    }
                }

                foreach (string orderId in ordersToCancel)
                {
                    Order orderToCancel = Account.Orders.FirstOrDefault(o => o.OrderId == orderId);
                    if (orderToCancel != null)
                    {
                        CancelOrder(orderToCancel);
                        Print($"Cancelled old order: {orderToCancel.Name}");
                        orderCreationCandle.Remove(orderId);
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error in CancelOldOrders: {ex.Message}");
            }
        }
    }
}
