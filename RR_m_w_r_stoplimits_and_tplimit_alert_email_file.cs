#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
using System.IO;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RTLongTimeWinStopLimitTPlimitGAP : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;
        private DateTime lastFlattenDate = Core.Globals.MinDate;
        private bool lastWindowState = false;
        private bool priceConnectionAlertActive = false;
        private bool orderConnectionAlertActive = false;
        private bool pendingPriceConnectionEmail = false;
        private bool pendingOrderConnectionEmail = false;
        private DateTime lastPriceConnectionEmail = Core.Globals.MinDate;
        private DateTime lastOrderConnectionEmail = Core.Globals.MinDate;

        // ===== CONNECTION ALERT FILE QUEUE =====
        [NinjaScriptProperty]
        [Display(Name = "Enable File Alerts", Order = 0, GroupName = "Connection Alert File Queue")]
        public bool EnableFileAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Queue File Path", Order = 1, GroupName = "Connection Alert File Queue")]
        public string AlertQueueFilePath { get; set; }

        // ===== CONNECTION ALERTS =====
        [NinjaScriptProperty]
        [Display(Name = "Enable Connection Emails", Order = 0, GroupName = "Connection Alerts")]
        public bool EnableConnectionEmails { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Email To", Order = 1, GroupName = "Connection Alerts")]
        public string AlertEmailTo { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Email Cooldown Minutes", Order = 2, GroupName = "Connection Alerts")]
        public int ConnectionEmailCooldownMinutes { get; set; }

        // ===== RISK REWARD RATIO =====
        [NinjaScriptProperty]
        [Display(Name = "Risk/Reward Ratio", Order = 0, GroupName = "Risk Management")]
        public double RiskRewardRatio { get; set; }

        // ===== TIME WINDOW INPUTS =====
        [NinjaScriptProperty]
        [Display(Name = "Use Trade Window", Order = 0, GroupName = "Trade Windows")]
        public bool UseTradeWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "00:00-01:00", Order = 1, GroupName = "Trade Windows")]
        public bool W00 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "01:00-02:00", Order = 2, GroupName = "Trade Windows")]
        public bool W01 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "02:00-03:00", Order = 3, GroupName = "Trade Windows")]
        public bool W02 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "03:00-04:00", Order = 4, GroupName = "Trade Windows")]
        public bool W03 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "04:00-05:00", Order = 5, GroupName = "Trade Windows")]
        public bool W04 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "05:00-06:00", Order = 6, GroupName = "Trade Windows")]
        public bool W05 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "06:00-07:00", Order = 7, GroupName = "Trade Windows")]
        public bool W06 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "07:00-08:00", Order = 8, GroupName = "Trade Windows")]
        public bool W07 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "08:00-09:00", Order = 9, GroupName = "Trade Windows")]
        public bool W08 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "09:00-10:00", Order = 10, GroupName = "Trade Windows")]
        public bool W09 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "10:00-11:00", Order = 11, GroupName = "Trade Windows")]
        public bool W10 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "11:00-12:00", Order = 12, GroupName = "Trade Windows")]
        public bool W11 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "12:00-13:00", Order = 13, GroupName = "Trade Windows")]
        public bool W12 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "13:00-14:00", Order = 14, GroupName = "Trade Windows")]
        public bool W13 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "14:00-15:00", Order = 15, GroupName = "Trade Windows")]
        public bool W14 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "15:00-16:00", Order = 16, GroupName = "Trade Windows")]
        public bool W15 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "16:00-17:00", Order = 17, GroupName = "Trade Windows")]
        public bool W16 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "17:00-18:00", Order = 18, GroupName = "Trade Windows")]
        public bool W17 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "18:00-19:00", Order = 19, GroupName = "Trade Windows")]
        public bool W18 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "19:00-20:00", Order = 20, GroupName = "Trade Windows")]
        public bool W19 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "20:00-21:00", Order = 21, GroupName = "Trade Windows")]
        public bool W20 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "21:00-22:00", Order = 22, GroupName = "Trade Windows")]
        public bool W21 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "22:00-23:00", Order = 23, GroupName = "Trade Windows")]
        public bool W22 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "23:00-00:00", Order = 24, GroupName = "Trade Windows")]
        public bool W23 { get; set; }

        private bool[] tradeWindows;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RTLongTimeWinStopLimitTPlimitGAP";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                RiskRewardRatio = 1.0;
                UseTradeWindow = true;
                EnableFileAlerts = true;
                AlertQueueFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8",
                    "connection_alerts",
                    "connection_alert_queue.tsv");
                EnableConnectionEmails = true;
                AlertEmailTo = "";
                ConnectionEmailCooldownMinutes = 5;
            }
            else if (State == State.DataLoaded)
            {
                tradeWindows = new bool[]
                {
                    W00, W01, W02, W03, W04, W05,
                    W06, W07, W08, W09, W10, W11,
                    W12, W13, W14, W15, W16, W17,
                    W18, W19, W20, W21, W22, W23
                };
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                priceConnectionAlertActive = false;
                orderConnectionAlertActive = false;
                pendingPriceConnectionEmail = false;
                pendingOrderConnectionEmail = false;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
        {
            if (State != State.Realtime)
                return;

            if (connectionStatusUpdate.PriceStatus == ConnectionStatus.ConnectionLost)
            {
                if (!priceConnectionAlertActive)
                {
                    pendingPriceConnectionEmail = true;
                    WriteConnectionAlertToFile("PRICE", connectionStatusUpdate);
                    SendConnectionLostEmail("PRICE", connectionStatusUpdate, false);
                    priceConnectionAlertActive = true;
                }
            }
            else if (connectionStatusUpdate.PriceStatus == ConnectionStatus.Connected)
            {
                priceConnectionAlertActive = false;

                if (pendingPriceConnectionEmail)
                    SendConnectionLostEmail("PRICE", connectionStatusUpdate, true);
            }

            if (connectionStatusUpdate.Status == ConnectionStatus.ConnectionLost)
            {
                if (!orderConnectionAlertActive)
                {
                    pendingOrderConnectionEmail = true;
                    WriteConnectionAlertToFile("ORDER", connectionStatusUpdate);
                    SendConnectionLostEmail("ORDER", connectionStatusUpdate, false);
                    orderConnectionAlertActive = true;
                }
            }
            else if (connectionStatusUpdate.Status == ConnectionStatus.Connected)
            {
                orderConnectionAlertActive = false;

                if (pendingOrderConnectionEmail)
                    SendConnectionLostEmail("ORDER", connectionStatusUpdate, true);
            }
        }

        private void WriteConnectionAlertToFile(string connectionType, ConnectionStatusEventArgs connectionStatusUpdate)
        {
            DateTime now = DateTime.Now;

            if (!EnableFileAlerts || string.IsNullOrWhiteSpace(AlertQueueFilePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(AlertQueueFilePath);

                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(AlertQueueFilePath))
                {
                    File.AppendAllText(
                        AlertQueueFilePath,
                        "id\tcreated_at\tconnection_type\tstrategy\tinstrument\taccount\tconnection\torder_status\tprevious_order_status\tprice_status\tprevious_price_status\tposition\tposition_quantity\ttracked_order\tnative_error" + Environment.NewLine);
                }

                string connectionName = connectionStatusUpdate.Connection != null && connectionStatusUpdate.Connection.Options != null
                    ? connectionStatusUpdate.Connection.Options.Name
                    : "Unknown";

                string accountName = Account != null ? Account.Name : "Unknown";
                string instrumentName = Instrument != null ? Instrument.FullName : "Unknown";
                string alertId = string.Format("{0}-{1}-{2}-{3}",
                    now.ToString("yyyyMMddHHmmssfff"),
                    Name,
                    Instrument != null ? Instrument.FullName : "UnknownInstrument",
                    connectionType);

                string[] fields = new string[]
                {
                    alertId,
                    now.ToString("yyyy-MM-dd HH:mm:ss"),
                    connectionType,
                    Name,
                    instrumentName,
                    accountName,
                    connectionName,
                    connectionStatusUpdate.Status.ToString(),
                    connectionStatusUpdate.PreviousStatus.ToString(),
                    connectionStatusUpdate.PriceStatus.ToString(),
                    connectionStatusUpdate.PreviousPriceStatus.ToString(),
                    Position.MarketPosition.ToString(),
                    Position.Quantity.ToString(),
                    GetLongOrderContext(),
                    connectionStatusUpdate.NativeError
                };

                File.AppendAllText(AlertQueueFilePath, string.Join("\t", EscapeAlertFields(fields)) + Environment.NewLine);
                Print($"[{now}] {connectionType} connection alert written to file queue: {AlertQueueFilePath}");
            }
            catch (Exception ex)
            {
                Print($"[{now}] Failed to write {connectionType} connection alert to file queue. Error: {ex.Message}");
            }
        }

        private string[] EscapeAlertFields(string[] fields)
        {
            string[] escaped = new string[fields.Length];

            for (int i = 0; i < fields.Length; i++)
            {
                escaped[i] = (fields[i] ?? string.Empty)
                    .Replace("\\", "\\\\")
                    .Replace("\t", "\\t")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
            }

            return escaped;
        }

        private void SendConnectionLostEmail(string connectionType, ConnectionStatusEventArgs connectionStatusUpdate, bool clearPendingOnSuccess)
        {
            DateTime now = DateTime.Now;

            if (!EnableConnectionEmails || string.IsNullOrWhiteSpace(AlertEmailTo))
            {
                Print($"[{now}] {connectionType} connection lost. Email not sent because connection email alerts are disabled or Alert Email To is blank.");
                return;
            }

            DateTime lastEmailTime = connectionType == "PRICE" ? lastPriceConnectionEmail : lastOrderConnectionEmail;

            if (!clearPendingOnSuccess && ConnectionEmailCooldownMinutes > 0 && lastEmailTime != Core.Globals.MinDate &&
                now < lastEmailTime.AddMinutes(ConnectionEmailCooldownMinutes))
            {
                Print($"[{now}] {connectionType} connection lost. Email suppressed by cooldown.");
                return;
            }

            string connectionName = connectionStatusUpdate.Connection != null && connectionStatusUpdate.Connection.Options != null
                ? connectionStatusUpdate.Connection.Options.Name
                : "Unknown";

            string accountName = Account != null ? Account.Name : "Unknown";
            string instrumentName = Instrument != null ? Instrument.FullName : "Unknown";
            string orderContext = GetLongOrderContext();

            string subject = $"{Name}: {connectionType} connection lost";
            string body =
                $"Strategy: {Name}{Environment.NewLine}" +
                $"Instrument: {instrumentName}{Environment.NewLine}" +
                $"Account: {accountName}{Environment.NewLine}" +
                $"Connection: {connectionName}{Environment.NewLine}" +
                $"Event time: {now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"Order status: {connectionStatusUpdate.Status}{Environment.NewLine}" +
                $"Previous order status: {connectionStatusUpdate.PreviousStatus}{Environment.NewLine}" +
                $"Price status: {connectionStatusUpdate.PriceStatus}{Environment.NewLine}" +
                $"Previous price status: {connectionStatusUpdate.PreviousPriceStatus}{Environment.NewLine}" +
                $"Position: {Position.MarketPosition}, Quantity: {Position.Quantity}{Environment.NewLine}" +
                $"Tracked Long1 order: {orderContext}{Environment.NewLine}" +
                $"Native error: {connectionStatusUpdate.NativeError}";

            try
            {
                SendMail(AlertEmailTo, subject, body);
            }
            catch (Exception ex)
            {
                if (connectionType == "PRICE")
                    pendingPriceConnectionEmail = true;
                else
                    pendingOrderConnectionEmail = true;

                Print($"[{now}] {connectionType} connection lost email failed and will be retried after reconnect. Error: {ex.Message}");
                return;
            }

            if (connectionType == "PRICE")
            {
                lastPriceConnectionEmail = now;

                if (clearPendingOnSuccess)
                    pendingPriceConnectionEmail = false;
            }
            else
            {
                lastOrderConnectionEmail = now;

                if (clearPendingOnSuccess)
                    pendingOrderConnectionEmail = false;
            }

            Print($"[{now}] {connectionType} connection lost email sent to {AlertEmailTo}");
        }

        private string GetLongOrderContext()
        {
            if (longOrder == null)
                return "None";

            return string.Format(
                "Name={0}, State={1}, Action={2}, Type={3}, Quantity={4}, Filled={5}, StopPrice={6}, LimitPrice={7}, AverageFillPrice={8}, OrderId={9}",
                longOrder.Name,
                longOrder.OrderState,
                longOrder.OrderAction,
                longOrder.OrderType,
                longOrder.Quantity,
                longOrder.Filled,
                longOrder.StopPrice,
                longOrder.LimitPrice,
                longOrder.AverageFillPrice,
                longOrder.OrderId);
        }

        private bool IsTradeWindow(DateTime time)
        {
            if (!UseTradeWindow)
                return true;

            int slot = time.Hour;

            if (slot < 0 || slot > 23)
                return false;

            return tradeWindows[slot];
        }

        protected override void OnBarUpdate()
        {
            if (State != State.Realtime)
                return;

            if (ToTime(Time[0]) >= 235700 && ToTime(Time[0]) < 235800)
            {
                if (lastFlattenDate.Date != Time[0].Date)
                {
                    lastFlattenDate = Time[0];
                    Print($"[{Time[0]}] End of session FLATTEN -> all positions & orders cleared");

                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("DailyFlatten", "Long1");

                    if (longOrder != null &&
                        (longOrder.OrderState == OrderState.Working ||
                         longOrder.OrderState == OrderState.Accepted))
                        CancelOrder(longOrder);
                }
                return;
            }

            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double targetPrice = entryPrice + (riskPerTrade * RiskRewardRatio);

                if (Close[0] >= targetPrice)
                {
                    Print($"[{Time[0]}] 1R reached: Bar Close={Close[0]}, Target={targetPrice}");
                    ExitLongLimit(0, true, Position.Quantity, targetPrice, "RR_Limit", "Long1");
                    Print($"[{Time[0]}] Limit order submitted @ {targetPrice}");
                }
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool inWindow = IsTradeWindow(Time[0]);

            if (inWindow != lastWindowState)
            {
                Print($"[{Time[0]}] Trade window state changed -> {(inWindow ? "INSIDE" : "OUTSIDE")}");
                lastWindowState = inWindow;
            }

            if (!inWindow)
            {
                if (longOrder != null &&
                    (longOrder.OrderState == OrderState.Working || longOrder.OrderState == OrderState.Accepted))
                {
                    Print($"[{Time[0]}] Outside window -> cancelling pending order @ {longOrder.StopPrice}");
                    CancelOrder(longOrder);
                }
                return;
            }

            if (Close[0] < Open[0])
            {
                entryPrice = High[0];
                pendingStopPrice = Low[0];
                riskPerTrade = entryPrice - pendingStopPrice;

                Print($"[{Time[0]}] Red candle detected -> evaluating entry");
                Print($"[{Time[0]}] Entry={entryPrice} SL={pendingStopPrice} Risk={riskPerTrade}");

                double ask = GetCurrentAsk();

                if (ask >= entryPrice)
                {
                    Print($"[{Time[0]}] Gap above entry -> skipping stop placement");
                    return;
                }

                longOrder = EnterLongStopLimit(0, true, DefaultQuantity, entryPrice, entryPrice, "Long1");
                Print($"[{Time[0]}] Submitted BUY STOP-LIMIT @ {entryPrice}");
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.Name == "Long1")
            {
                if (execution.Order.OrderState == OrderState.Filled)
                {
                    entryPrice = execution.Order.AverageFillPrice;

                    if (pendingStopPrice > 0)
                    {
                        riskPerTrade = entryPrice - pendingStopPrice;
                        double limitPrice = pendingStopPrice - TickSize;

                        Print($"[{time}] Entry FILLED at {entryPrice} - Submitting STOP-LIMIT immediately");
                        Print($"[{time}]    Stop={pendingStopPrice}, Limit={limitPrice}, Risk={riskPerTrade}");

                        ExitLongStopLimit(0, true, execution.Order.Filled, pendingStopPrice, pendingStopPrice, "StopLimit", "Long1");
                    }
                }
            }

            if (execution.Order != null && execution.Order.Name == "Long1" &&
                execution.Order.OrderState == OrderState.Filled)
            {
                longOrder = null;
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string nativeError)
        {
            if (order.Name == "Long1")
            {
                longOrder = order;
            }
        }
    }
}
