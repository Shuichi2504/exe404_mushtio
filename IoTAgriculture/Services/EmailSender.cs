using System.Net;
using System.Net.Mail;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task SendVerificationCodeAsync(
            string email,
            string code,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            var host = _configuration["Email:Smtp:Host"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var from = _configuration["Email:Smtp:From"];
            if (string.IsNullOrWhiteSpace(from))
            {
                from = username;
            }

            var port = int.TryParse(_configuration["Email:Smtp:Port"], out var parsedPort)
                ? parsedPort
                : 587;

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(from) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                if (_environment.IsDevelopment())
                {
                    _logger.LogWarning("Email verification code for {Email}/{Purpose}: {Code}", email, purpose, code);
                    return;
                }

                throw new InvalidOperationException(
                    "Email SMTP configuration is missing. Configure Email:Smtp:Host, Username, Password and optionally From. For Gmail SMTP, use an App Password or OAuth refresh token flow; a Google OAuth client secret alone cannot send email.");
            }

            using var message = new MailMessage(
                from,
                email,
                "Mã xác thực Mushtio",
                $"Mã xác thực của bạn là: {code}. Mã có hiệu lực trong 10 phút.");

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = bool.TryParse(_configuration["Email:Smtp:EnableSsl"], out var ssl)
                    ? ssl
                    : true
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message, cancellationToken);
        }
    }
}
