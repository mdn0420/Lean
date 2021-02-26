using System.Collections.Generic;
using QuantConnect.Orders;

namespace LucrumLabs.Orders
{
    public class OCOOrderTickets
    {
        private ICollection<OrderTicket> _tickets;

        public OCOOrderTickets(ICollection<OrderTicket> tickets)
        {
            _tickets = tickets;
        }

        public void Filled()
        {
            // Cancel all the outstanding tickets.
            foreach (var orderTicket in _tickets)
            {
                if (!orderTicket.Status.IsClosed())
                {
                    orderTicket.Cancel();
                }
            }
        }
    }
}
