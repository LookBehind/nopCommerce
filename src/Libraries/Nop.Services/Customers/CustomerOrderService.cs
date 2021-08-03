using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core.Domain.Orders;
using Nop.Data;

namespace Nop.Services.Customers
{
    public class CustomerOrderService : ICustomerOrderService
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<OrderItem> _orderItemRepository;

        public CustomerOrderService(
            IRepository<Order> orderRepository,
            IRepository<OrderItem> orderItemRepository)
        {
            _orderRepository = orderRepository;
            _orderItemRepository = orderItemRepository;
        }

        public async Task<decimal> GetCoustomerOrdersTotalAmount(int customerId, DateTime fromDate, DateTime toDate)
        {
            var orders = from order in _orderRepository.Table
                         where order.CustomerId == customerId
                                && order.CreatedOnUtc.Between(fromDate,toDate)
                         select order;

            var totalAmount = 0m;
            if (orders.Count() > 0)
            {
                var prices = await (from order in orders
                                     join item in _orderItemRepository.Table
                                     on order.Id equals item.OrderId
                                     select item.PriceInclTax).ToListAsync();

                totalAmount = prices.Sum();
            }

            return totalAmount;
        }
    }
}
