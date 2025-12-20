using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace Bulky.Utility
{
    public class IdentityUiEmailSenderAdapter : IEmailSender
    {
        private readonly IEmailSender<IdentityUser> _inner;

        public IdentityUiEmailSenderAdapter(IEmailSender<IdentityUser> inner)
        {
            _inner = inner;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // Logic to send email
            return Task.CompletedTask;
            //// Identity UI فقط htmlMessage را پاس می‌دهد
            //// که معمولاً لینک تأیید یا ریست پسورد است
            //return _inner.SendConfirmationLinkAsync(
            //    user: null!,
            //    email: email,
            //    confirmationLink: htmlMessage);
        }
    }
}
