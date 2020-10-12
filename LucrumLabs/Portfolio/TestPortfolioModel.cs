using System;
using System.Collections.Generic;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data.UniverseSelection;

namespace LucrumLabs.Portfolio
{
    public class TestPortfolioModel : IPortfolioConstructionModel
    {
        private InsightCollection _insights;
        /*
        protected override bool ShouldCreateTargetForInsight(Insight insight)
        {
            Console.WriteLine("{0} - Portfolio model received insight - {1} {2}", Algorithm.Time, insight.Symbol, insight.Direction);
            return false;
        }

        protected override Dictionary<Insight, double> DetermineTargetPercent(List<Insight> activeInsights)
        {
            var result = new Dictionary<Insight, double>();
            return result;
        }*/

        public TestPortfolioModel()
        {
            _insights = new InsightCollection();
        }

        public void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            
        }

        public IEnumerable<IPortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
        {
            if (insights.Length > 0)
            {
                Console.WriteLine("{0} - Portfolio model received insights", algorithm.Time);
                foreach (var insight in insights)
                {
                    Console.WriteLine(" {0} - {1}", insight.Symbol, insight.Direction);
                    //var security = algorithm.Securities[insight.Symbol];
                    //Console.WriteLine("    O:{0}, C:{1}, H:{2}, L:{3}", security.Open, security.Close, security.High, security.Low);
                    if (insight.Direction == InsightDirection.Up)
                    {
                        yield return new PortfolioTarget(insight.Symbol, 100);
                    } 
                    else if (insight.Direction == InsightDirection.Down)
                    {
                        yield return new PortfolioTarget(insight.Symbol, -100);
                    }
                }
            }
        }
    }
}