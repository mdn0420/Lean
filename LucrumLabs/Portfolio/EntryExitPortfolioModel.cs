using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LucrumLabs.Trades;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Orders;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Portfolio
{
    public class EntryExitPortfolioModel : IPortfolioConstructionModel, IOrderEventHandler
    {
        /// <summary>
        /// Mapping of insights to trades
        /// </summary>
        private ConcurrentDictionary<Insight, CalculatedTrade> _insightTrades = new ConcurrentDictionary<Insight, CalculatedTrade>();

        private Dictionary<Symbol, ATRPriceProvider> _priceProviders = new Dictionary<Symbol, ATRPriceProvider>();

        private Func<DateTime, CalendarInfo> _timeFrame;

        private bool _shouldCleanInsights;

        public EntryExitPortfolioModel(Func<DateTime, CalendarInfo> timeFrame)
        {
            _timeFrame = timeFrame;
        }
        
        // This gets called every tick
        public IEnumerable<IPortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
        {
            if (algorithm.Time.Minute == 0)
            {
                // hack to cleanup every hour, should refactor to schedule based on insight expiration
                _shouldCleanInsights = true;
            }
            
            foreach (var insight in insights)
            {
                SetupOrders(algorithm, insight);
            }

            if (_shouldCleanInsights)
            {
                CleanupInsights(algorithm);
                _shouldCleanInsights = false;
            }
            //var activeTrades = _insightTrades.Count(kvp => kvp.Value.State != ManagedTrade.TradeState.CLOSED);
            //algorithm.Log(string.Format("{0} - {1} open or pending trades.", algorithm.Time, activeTrades));
            return Enumerable.Empty<IPortfolioTarget>();
        }

        private void SetupOrders(QCAlgorithm algorithm, Insight insight)
        {
            OrderDirection direction = insight.Direction == InsightDirection.Down ? OrderDirection.Sell : OrderDirection.Buy;
            Symbol symbol = insight.Symbol;
            OrderType entryType = OrderType.Limit;

            ATRPriceProvider priceProvider = null;
            if (!_priceProviders.TryGetValue(symbol, out priceProvider))
            {
                Console.WriteLine("Could not find price provider for {0}", symbol);
                return;
            }
            
            int qty = 0;
            decimal entryPrice = 0m;
            decimal slPrice = 0m;
            decimal tpPrice = 0m;
            priceProvider.CalculatePrices(out entryPrice, out slPrice, out tpPrice, direction);
            
            Console.WriteLine("{0} - ATR: {1}", algorithm.UtcTime, priceProvider.ATR);
            
            Forex pair = algorithm.Securities[symbol] as Forex;
            decimal pipSize = ForexUtils.GetPipSize(pair);
            var riskPips = Math.Abs(entryPrice - slPrice) / pipSize;
            qty = ForexUtils.CalculatePositionSize(pair, riskPips, algorithm.Portfolio.MarginRemaining, 0.0025m);
            if (direction == OrderDirection.Sell)
            {
                qty *= -1;
            }

            CalculatedTrade trade = new CalculatedTrade(algorithm, insight.Symbol, direction, entryType, entryPrice,
                slPrice, tpPrice, qty);
            _insightTrades[insight] = trade;
            trade.Execute();

            //var numActive = _insightTrades.Count(kvp => kvp.Value.State != ManagedTrade.TradeState.CLOSED);
            //algorithm.Log(string.Format("{0} - {1} active trades.", algorithm.UtcTime, numActive));
        }

        private void CleanupInsights(QCAlgorithm algorithm)
        {
            var remove = new List<Insight>();

            var utcTime = algorithm.UtcTime;
            foreach (var kvp in _insightTrades)
            {
                var insight = kvp.Key;
                var trade = kvp.Value;
                if (trade.State == ManagedTrade.TradeState.CLOSED)
                {
                    remove.Add(insight);
                }
                else if (trade.State == ManagedTrade.TradeState.PENDING && insight.IsExpired(utcTime))
                {
                    trade.Close();
                    remove.Add(insight);
                }
            }

            if (remove.Count > 0)
            {
                //algorithm.Log(string.Format("Cleaning up {0} insights", remove.Count));
            }

            CalculatedTrade removed = null;
            foreach (var insight in remove)
            {
                _insightTrades.TryRemove(insight, out removed);
            }
        }

        public void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var removed in changes.RemovedSecurities)
            {
                ATRPriceProvider provider = null;
                if (_priceProviders.TryGetValue(removed.Symbol, out provider))
                {
                    provider.Dispose();
                    _priceProviders.Remove(removed.Symbol);
                }
            }
            
            var symbols = changes.AddedSecurities.Select(x => x.Symbol);
            foreach (var symbol in symbols)
            {
                if (!_priceProviders.ContainsKey(symbol))
                {
                    //Console.WriteLine("StdDevAlphaModel - adding {0}", symbol);
                    _priceProviders[symbol] = new ATRPriceProvider(algorithm, symbol, _timeFrame, 1.5m, 3m, 14);
                }
                else
                {
                    algorithm.Error(string.Format("StdDevAlphaModel received duplicate add security event for {0}", symbol));
                }
            }
        }

        public void OnOrderEvent(OrderEvent orderEvent)
        {
            foreach (var kvp in _insightTrades)
            {
                var trade = kvp.Value;
                // todo: check for thread saftey
                if (trade.HasOrderId(orderEvent.OrderId))
                {
                    trade.OnOrderEvent(orderEvent);
                }
            }

            _shouldCleanInsights = true;
        }
    }
}
