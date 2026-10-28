using api.Configurations;
using api.Telemetry;
using Azure.Core;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace api.Services
{
    public enum EmailTarget
    {
        Fencilla,
    }

    public interface IEmailService
    {
        public Task SendMailAsync(
            string subject,
            string body,
            EmailTarget recipient,
            string installation
        );
        public Task SendFencillaResultEmail(
            string inspectionId,
            float? confidence,
            string installation
        );
    }

    public record AnalysisImageEmailOptions
    {
        public List<string> TargetEmails { get; set; } = [];
        public string EmailGroup { get; set; } = "";
        public string? AnalysedImageBasePath { get; set; } = "";
    }

    public record FencillaOptions
    {
        public Dictionary<string, AnalysisImageEmailOptions> Installations { get; set; } = [];
    }

    public record EmailOptions
    {
        public FencillaOptions Fencilla { get; set; } = new FencillaOptions();
    }

    public class EmailService : IEmailService
    {
        private readonly GraphServiceClient appClient;
        private readonly EmailOptions _emailOptions;

        public EmailService(TokenCredential credential, IOptions<EmailOptions> emailOptions)
        {
            appClient = new GraphServiceClient(
                credential,
                ["https://graph.microsoft.com/.default"]
            );

            _emailOptions = emailOptions.Value;
        }

        public async Task SendMailAsync(
            string subject,
            string body,
            EmailTarget recipient,
            string installation
        )
        {
            var outcome = OperationOutcome.Failure;
            try
            {
                installation = installation.ToUpperInvariant();

                var recipientEmails = recipient switch
                {
                    EmailTarget.Fencilla => _emailOptions
                        .Fencilla
                        .Installations[installation]
                        .TargetEmails,
                    _ => throw new ArgumentException("No emails found for given email targets"),
                };

                var emailGroup = recipient switch
                {
                    EmailTarget.Fencilla => _emailOptions
                        .Fencilla
                        .Installations[installation]
                        .EmailGroup,
                    _ => throw new ArgumentException("No email found for given email group"),
                };

                foreach (var targetEmail in recipientEmails)
                {
                    // Create a new message
                    var message = new Message
                    {
                        Subject = subject,
                        Body = new ItemBody { Content = body, ContentType = BodyType.Text },
                        ToRecipients =
                        [
                            new Recipient
                            {
                                EmailAddress = new EmailAddress { Address = targetEmail },
                            },
                        ],
                    };

                    // Send the message
                    await appClient
                        .Users[emailGroup]
                        .SendMail.PostAsync(
                            new SendMailPostRequestBody
                            {
                                Message = message,
                                SaveToSentItems = true,
                            }
                        );
                }
                outcome =
                    recipientEmails.Count == 0
                        ? OperationOutcome.Skipped
                        : OperationOutcome.Success;
            }
            finally
            {
                // One outcome for the logical send, including partial recipient failures.
                SaraMetrics.RecordDownstreamDelivery("email", "send", outcome);
            }
        }

        public async Task SendFencillaResultEmail(
            string inspectionId,
            float? confidence,
            string installation
        )
        {
            installation = installation.ToUpperInvariant();
            if (!_emailOptions.Fencilla.Installations.ContainsKey(installation))
            {
                SaraMetrics.RecordDownstreamDelivery("email", "send", OperationOutcome.Skipped);
                return;
            }

            var title = "[AI] Potensielt perimeterbrudd";
            var flotillaImageUrl =
                $"{_emailOptions.Fencilla.Installations[installation].AnalysedImageBasePath}{installation}/mission-simple?inspectionId={inspectionId}&focus=analysis";
            var urlMessage = $"\nBildet er tilgjengelig her:\n\n{flotillaImageUrl}";
            var confidenceMessage =
                confidence != null
                    ? $"\nDenne analysen har en estimert treffsikkerhet på {Convert.ToInt32(confidence * 100.0)}%."
                    : "";
            var body =
                $"Et potensielt brudd har blitt detektert langs gjerdet (enten et hull eller et nytt objekt).{confidenceMessage}{urlMessage}";
            await SendMailAsync(title, body, EmailTarget.Fencilla, installation);
        }
    }
}
