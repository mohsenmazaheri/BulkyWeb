using Bulky.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bulky.DataAccess.Repository.IRepository
{
    public interface IOrderHeaderRepository : IRepository<OrderHeader>
    {
        void Update(OrderHeader obj);
        void UpdateStatus(int id, string orderStatus, string? paymentStatus = null);
        void UpdateStripePaymentId(int id, string sessionId, string paymentIntentId);

        /// <summary>
        /// Records a successful Stripe payment. Returns false when the order does not exist or is already paid,
        /// so the same payment can safely be reported more than once (browser redirect, webhook, webhook retries).
        /// </summary>
        bool MarkPaid(int id, string sessionId, string? paymentIntentId);
    }
}
