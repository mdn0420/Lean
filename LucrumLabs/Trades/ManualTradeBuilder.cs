using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QuantConnect;
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Statistics;

namespace LucrumLabs.Trades
{
    public class ManualTradeBuilder : ITradeBuilder
    {
        /// <summary>
        /// Helper class to manage pending trades and market price updates for a symbol
        /// </summary>
        private class Position
        {
            public AdvancedTrade Trade = null;
            public int TradeId = -1;
            public int EntryOrderId = -1;
            public int ExitOrderId = -1;

            internal decimal TotalFees { get; set; }
            internal decimal MaxPrice { get; set; }
            internal decimal MinPrice { get; set; }

            public Position()
            {
                
            }
        }

        private class PendingFillData
        {
            public OrderEvent Fill;
            public decimal securityConversionRate;
            public decimal feeInAccountCurrency;
            public decimal multiplier = 1.0m;
        }

        private Dictionary<Symbol, List<Position>> _openPositions = new Dictionary<Symbol, List<Position>>();

        private List<Position> _pendingPositions = new List<Position>();
        private List<PendingFillData> _pendingFills = new List<PendingFillData>();
        
        private readonly List<Trade> _closedTrades = new List<Trade>();

        private int _nextTradeId = 0;
        public List<Trade> ClosedTrades {
            get
            {
                lock (_closedTrades)
                {
                    return new List<Trade>(_closedTrades);
                }
            }        
        }
        
        public void SetLiveMode(bool live)
        {
            if (live)
            {
                throw new NotSupportedException("Live mode not supported");
            }
        }
        public bool HasOpenPosition(Symbol symbol)
        {
            List<Position> positions = null;
            _openPositions.TryGetValue(symbol, out positions);
            return positions != null && positions.Count > 0;
        }

        public void SetMarketPrice(Symbol symbol, decimal price)
        {
            List<Position> positions = null;
            if (!_openPositions.TryGetValue(symbol, out positions)) return;

            foreach (var position in positions)
            {
                if (price > position.MaxPrice)
                    position.MaxPrice = price;
                else if (price < position.MinPrice)
                    position.MinPrice = price;
            }
        }

        public void ProcessFill(
            OrderEvent fill,
            decimal securityConversionRate,
            decimal feeInAccountCurrency,
            decimal multiplier = 1.0m
            )
        {
            var data = new PendingFillData()
            {
                Fill = fill,
                securityConversionRate = securityConversionRate,
                feeInAccountCurrency = feeInAccountCurrency,
                multiplier = multiplier
            };
            _pendingFills.Add(data);
            MatchFills();
        }

        public int OpenTrade(DateTime openTimeUtc, decimal slPrice, decimal tpPrice)
        {
            var position = new Position();
            var trade = position.Trade = new AdvancedTrade();
            trade.OpenTime = openTimeUtc;
            trade.StopLossPrice = slPrice;
            trade.TakeProfitPrice = tpPrice;
            position.TradeId = _nextTradeId++;
            
            _pendingPositions.Add(position);
            Console.WriteLine("Opening trade {0}", position.TradeId);
            return position.TradeId;
        }

        public void CancelTrade(int tradeId)
        {
            var index = _pendingPositions.FindIndex(p => p.TradeId == tradeId);
            if (index != -1)
            {
                _pendingPositions.RemoveAt(index);
            }
        }

        public void RegisterTradeEntry(int tradeId, int orderId)
        {
            var position = _pendingPositions.FirstOrDefault(p => p.TradeId == tradeId);
            if (position != null)
            {
                position.EntryOrderId = orderId;
            }
            else
            {
                Console.WriteLine("Error: RegisterTradeEntry - Could not find trade id {0}", tradeId);
            }
            
            MatchFills();
        }

        public void RegisterTradeExit(int tradeId, int orderId)
        {
            var position = _pendingPositions.FirstOrDefault(p => p.TradeId == tradeId);
            if (position == null)
            {
                foreach (var kvp in _openPositions)
                {
                    var openPos = kvp.Value;
                    if (openPos != null)
                    {
                        position = openPos.FirstOrDefault(p => p.TradeId == tradeId);
                        if (position != null)
                        {
                            break;
                        }
                    }
                }
            }
            if (position != null)
            {
                position.ExitOrderId = orderId;
            }
            else
            {
                Console.WriteLine("Error: RegisterTradeExit - Could not find trade id {0}", tradeId);
            }
            
            MatchFills();
        }

        private void MatchFills()
        {
            var remove = new List<PendingFillData>();
            foreach (var data in _pendingFills)
            {
                var fill = data.Fill;
                var symbol = fill.Symbol;
                var orderId = fill.OrderId;
                
                // Check if this is an entry order
                var entryPosition = _pendingPositions.FirstOrDefault(p => p.EntryOrderId == orderId);
                if (entryPosition != null)
                {
                    var trade = entryPosition.Trade;
                    trade.Symbol = symbol;
                    trade.EntryTime = fill.UtcTime;
                    trade.EntryPrice = fill.FillPrice;
                    trade.Direction = fill.FillQuantity > 0 ? TradeDirection.Long : TradeDirection.Short;
                    trade.Quantity = fill.AbsoluteFillQuantity;
                    trade.TotalFees = data.feeInAccountCurrency;

                    _pendingPositions.Remove(entryPosition);

                    List<Position> openPositions = null;
                    if (!_openPositions.TryGetValue(symbol, out openPositions))
                    {
                        _openPositions[symbol] = openPositions = new List<Position>();
                    }
                    openPositions.Add(entryPosition);
                    
                    SetMarketPrice(symbol, fill.FillPrice);
                    remove.Add(data);
                    Console.WriteLine("Entry order matched for OrderId:{0} to TradeId:{1}", orderId, entryPosition.TradeId);
                }
                else
                {
                    // Otherwise, check if this is an exit order
                    SetMarketPrice(symbol, fill.FillPrice);
                    
                    List<Position> positions = null;
                    if (_openPositions.TryGetValue(symbol, out positions))
                    {
                        var exitPosition = positions.FirstOrDefault(p => p.ExitOrderId == orderId);
                        if (exitPosition != null)
                        {
                            Console.WriteLine("Exit order matched for OrderId:{0} to TradeId:{1}", orderId, exitPosition.TradeId);
                            
                            var trade = exitPosition.Trade;
                            trade.ExitTime = fill.UtcTime;
                            trade.ExitPrice = fill.FillPrice;
                            trade.ProfitLoss =
                                Math.Round(
                                    (trade.ExitPrice - trade.EntryPrice) * trade.Quantity *
                                    (trade.Direction == TradeDirection.Long ? +1 : -1) * data.securityConversionRate *
                                    data.multiplier, 2);
                            trade.TotalFees += data.feeInAccountCurrency;
                            trade.MAE = Math.Round(
                                (trade.Direction == TradeDirection.Long
                                    ? exitPosition.MinPrice - trade.EntryPrice
                                    : trade.EntryPrice - exitPosition.MaxPrice) * trade.Quantity *
                                data.securityConversionRate * data.multiplier, 2);
                            trade.MFE = Math.Round(
                                (trade.Direction == TradeDirection.Long
                                    ? exitPosition.MaxPrice - trade.EntryPrice
                                    : trade.EntryPrice - exitPosition.MinPrice) * trade.Quantity *
                                data.securityConversionRate * data.multiplier, 2);
                            
                            // Close this out
                            positions.Remove(exitPosition);
                            remove.Add(data);
                            AddNewTrade(exitPosition.Trade);
                        }
                    }
                }
            }

            foreach (var r in remove)
            {
                _pendingFills.Remove(r);
            }
            
            Console.WriteLine("Pending fills: {0}, Pending positions: {1}", _pendingFills.Count, _pendingPositions.Count);
        }
        
        /// <summary>
        /// Adds a trade to the list of closed trades, capping the total number only in live mode
        /// </summary>
        private void AddNewTrade(Trade trade)
        {
            lock (_closedTrades)
            {
                _closedTrades.Add(trade);
            }
        }
    }
}
