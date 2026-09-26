using System;
using Nop.Core;

namespace Nop.Plugin.Company.Support.Domain
{
    /// <summary>
    /// One message in a support case's reply thread - either the customer or a staff member
    /// (distinguished by IsStaff, not by a role lookup) writing a follow-up after the case's
    /// initial Subject/Description. Ordered by CreatedOnUtc, oldest first - same shape both
    /// the mobile detail screen and the admin Edit page render.
    /// </summary>
    public partial class SupportCaseMessage : BaseEntity
    {
        /// <summary>
        /// Gets or sets the support case identifier this message belongs to
        /// </summary>
        public int SupportCaseId { get; set; }

        /// <summary>
        /// Gets or sets the customer identifier who wrote this message - the case's own
        /// customer for a customer reply, or the staff member who was logged in for a staff
        /// reply (staff are customers with the admin role, same identity space).
        /// </summary>
        public int AuthorCustomerId { get; set; }

        /// <summary>
        /// Gets or sets whether this message was written by staff (admin Edit page) rather
        /// than the case's own customer (mobile app).
        /// </summary>
        public bool IsStaff { get; set; }

        /// <summary>
        /// Gets or sets the message text
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the message was written
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }
    }
}
