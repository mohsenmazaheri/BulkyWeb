using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Bulky.Utility
{
    public class EmailSender : IEmailSender<IdentityUser>
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task SendConfirmationLinkAsync(
            IdentityUser user,
            string email,
            string confirmationLink)
        {
            return SendAsync(
                email,
                "Confirm your email",
                $"Please confirm your account by clicking <a href='{confirmationLink}'>here</a>.");
        }

        public Task SendPasswordResetLinkAsync(
            IdentityUser user,
            string email,
            string resetLink)
        {
            return SendAsync(
                email,
                "Reset your password",
                $"Reset your password by clicking <a href='{resetLink}'>here</a>.");
        }

        public Task SendPasswordResetCodeAsync(
            IdentityUser user,
            string email,
            string resetCode)
        {
            return SendAsync(
                email,
                "Password reset code",
                $"Your password reset code is: <strong>{resetCode}</strong>");
        }

        private async Task SendAsync(string email, string subject, string htmlMessage)
        {
            var smtp = _configuration.GetSection("Email:Smtp");

            var mailMessage = new MailMessage
            {
                From = new MailAddress(smtp["From"]),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };

            mailMessage.To.Add(email);

            using var client = new SmtpClient
            {
                Host = smtp["Host"],
                Port = int.Parse(smtp["Port"]),
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    smtp["Username"],
                    smtp["Password"])
            };

            await client.SendMailAsync(mailMessage);
        }
    }
}
