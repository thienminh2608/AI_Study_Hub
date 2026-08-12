using AIStudyHub.Application.Interfaces;

using System;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace AIStudyHub.Infrastructure.Services.Email;

public class MailService : IMailService
{
    private readonly string _senderEmail;
    private readonly string _senderPassword;
    private readonly string _displayName;

    public MailService(IConfiguration configuration)
    {
        _senderEmail = configuration["MailSettings:SenderEmail"] ?? throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình MailSettings:SenderEmail trong appsettings.json hoặc biến môi trường!");
        _senderPassword = configuration["MailSettings:SenderPassword"] ?? throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình MailSettings:SenderPassword trong appsettings.json hoặc biến môi trường!");
        _displayName = configuration["MailSettings:DisplayName"] ?? "AI Study Hub";
    }

    private bool SendHtmlEmail(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_displayName, _senderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            // Connect to Gmail SMTP
            client.Connect("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            client.Authenticate(_senderEmail, _senderPassword);
            client.Send(message);
            client.Disconnect(true);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMTP Error: {ex.Message}");
            return false;
        }
    }

    public bool SendOtp(string toEmail, string otp)
    {
        string subject = "Mã xác thực đổi mật khẩu - AI Study Hub";
        string htmlBody = BuildOtpEmailHtml(otp);
        return SendHtmlEmail(toEmail, subject, htmlBody);
    }

    public bool SendPremiumExpiryWarning(string toEmail, string username, int hoursLeft)
    {
        string subject = "Gói Premium sắp hết hạn - AI Study Hub";
        string htmlBody = BuildSimpleNoticeHtml(
            "Gói Premium sắp hết hạn",
            $"Xin chào {username},<br><br>"
            + $"Gói Premium của bạn sẽ hết hạn trong khoảng <strong>{hoursLeft} giờ</strong> tới. "
            + "Số dư trong ví hiện không đủ để hệ thống tự động gia hạn.<br><br>"
            + "Vui lòng nạp thêm Coin vào ví để tiếp tục sử dụng Premium không bị gián đoạn."
        );
        return SendHtmlEmail(toEmail, subject, htmlBody);
    }

    public bool SendPremiumDowngraded(string toEmail, string username)
    {
        string subject = "Gói Premium đã bị huỷ - AI Study Hub";
        string htmlBody = BuildSimpleNoticeHtml(
            "Gói Premium đã bị huỷ",
            $"Xin chào {username},<br><br>"
            + "Gói Premium của bạn đã hết hạn và không thể tự động gia hạn do số dư trong ví không đủ. "
            + "Tài khoản của bạn đã được chuyển về gói Free.<br><br>"
            + "Bạn có thể nạp Coin và nâng cấp lại Premium bất cứ lúc nào."
        );
        return SendHtmlEmail(toEmail, subject, htmlBody);
    }

    private string BuildOtpEmailHtml(string otp)
    {
        string logoUrl = "https://i.postimg.cc/nhWz4dLJ/web-app-manifest-512x512.png";
        return $@"<!DOCTYPE html>
<html>
<head><meta charset=""UTF-8""></head>
<body style=""margin:0; padding:0; background-color:#f4f4f7; font-family:'Segoe UI', Arial, Helvetica, sans-serif;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#f4f4f7; padding:32px 16px;"">
<tr><td align=""center"">
<table role=""presentation"" width=""480"" cellpadding=""0"" cellspacing=""0"" style=""max-width:480px; width:100%; background-color:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 4px 18px rgba(92,60,245,0.12);"">
<tr><td style=""background:linear-gradient(135deg, #5c3cf5 0%, #7c3aed 100%); padding:36px 32px; text-align:center;"">
<div style=""display:inline-block; width:56px; height:56px; background-color:rgba(255,255,255,0.18); border-radius:16px; text-align:center; line-height:62px;"">
<img src=""{logoUrl}"" width=""34"" height=""34"" style=""vertical-align:middle; border:none;"" alt=""AI Study Hub Logo"" />
</div>
<div style=""color:#ffffff; font-size:22px; font-weight:700; letter-spacing:0.3px; margin-top:12px;"">AI Study Hub</div>
</td></tr>
<tr><td style=""padding:36px 32px 8px 32px;"">
<h1 style=""margin:0 0 8px 0; font-size:20px; color:#111827; font-weight:700;"">Mã xác thực của bạn</h1>
<p style=""margin:0 0 24px 0; font-size:14px; line-height:22px; color:#6b7280;"">
Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản AI Study Hub của bạn.
Vui lòng nhập mã bên dưới để tiếp tục:
</p>
</td></tr>
<tr><td style=""padding:0 32px;"">
<div style=""background-color:#f5f3ff; border:1px solid #ddd6fe; border-radius:12px; padding:20px; text-align:center;"">
<span style=""font-size:34px; font-weight:700; letter-spacing:10px; color:#5c3cf5;"">{otp}</span>
</div>
</td></tr>
<tr><td style=""padding:16px 32px 0 32px;"">
<p style=""margin:0; font-size:13px; color:#9ca3af; text-align:center;"">Mã có hiệu lực trong <strong style=""color:#6b7280;"">2 phút</strong> kể từ thời điểm gửi.</p>
</td></tr>
<tr><td style=""padding:28px 32px 0 32px;"">
<hr style=""border:none; border-top:1px solid #eef0f4; margin:0;"">
</td></tr>
<tr><td style=""padding:20px 32px 32px 32px;"">
<p style=""margin:0; font-size:12.5px; line-height:20px; color:#9ca3af;"">
Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.
Tài khoản của bạn vẫn an toàn và không cần thực hiện thêm thao tác nào.
Không chia sẻ mã này cho bất kỳ ai, kể cả nhân viên AI Study Hub.
</p>
</td></tr>
<tr><td style=""background-color:#fafafa; padding:20px 32px; text-align:center;"">
<p style=""margin:0; font-size:12px; color:#b0b3ba;"">Dự án SWP391 &middot; Đại học FPT</p>
</td></tr>
</table>
</td></tr>
</table>
</body>
</html>";
    }

    private string BuildSimpleNoticeHtml(String title, String bodyHtml)
    {
        return $@"<!DOCTYPE html><html><head><meta charset=""UTF-8""></head>
<body style=""margin:0;padding:0;background-color:#f4f4f7;font-family:'Segoe UI',Arial,sans-serif;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""padding:32px 16px;"">
<tr><td align=""center"">
<table role=""presentation"" width=""480"" cellpadding=""0"" cellspacing=""0"" style=""max-width:480px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 4px 18px rgba(92,60,245,0.12);"">
<tr><td style=""background:linear-gradient(135deg,#5c3cf5 0%,#7c3aed 100%);padding:28px 32px;text-align:center;color:#fff;font-size:20px;font-weight:700;"">{title}</td></tr>
<tr><td style=""padding:32px;font-size:14px;line-height:22px;color:#374151;"">{bodyHtml}</td></tr>
<tr><td style=""background:#fafafa;padding:16px 32px;text-align:center;"">
<span style=""font-size:12px;color:#b0b3ba;"">Dự án SWP391 &middot; Đại học FPT</span></td></tr>
</table></td></tr></table></body></html>";
    }
}
