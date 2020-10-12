using QuantConnect.Algorithm.Framework.Portfolio;

namespace LucrumLabs.Portfolio
{
    /// <summary>
    /// Portfolio target that also specifies price levels
    /// </summary>
    public interface IPortfolioTargetPrice : IPortfolioTarget
    {
        decimal EntryPrice { get; }
        decimal StopLossPrice { get; }
        decimal TakeProfitPrice { get; }
    }
}