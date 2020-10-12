using System;
using System.Collections.Generic;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;

namespace LucrumLabs.Risk
{
    public class TestRiskModel : RiskManagementModel
    {
        private PortfolioTargetCollection _targets;

        public TestRiskModel()
        {
            _targets = new PortfolioTargetCollection();
        }
        
        public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
        {
            // this gets run on every time step
            // target gets reset after insight expires?
            //Console.WriteLine("ManageRisk {0}", algorithm.Time);
            if (targets.Length > 0)
            {
                Console.WriteLine("{0} - TestRiskModel - Targets: {1}", algorithm.Time, targets.Length);
                foreach (var target in targets)
                {
                    Console.WriteLine(" {0} - {1}", target.Symbol, target.Quantity);
                }
            }

            yield break;
        }
    }
}