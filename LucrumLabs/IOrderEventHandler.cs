using QuantConnect.Orders;

namespace LucrumLabs
{
    public interface IOrderEventHandler
    {
        void OnOrderEvent(OrderEvent orderEvent);
    }
}
